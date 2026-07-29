using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.WinForms.WebApi;

/// <summary>
/// Background service that polls Supabase for pending trade requests.
/// Active only when WebTradeMode is Supabase.
/// </summary>
public sealed class SupabasePoller : IDisposable
{
    private static readonly Random _rng = new();
    private Timer? _timer;
    private readonly SupabaseClient _client;
    private readonly IPokeBotRunner _runner;
    private readonly int _pollIntervalMs;
    private readonly int _ttlMinutes;
    private bool _polling;

    public SupabasePoller(SupabaseClient client, IPokeBotRunner runner, WebTradeSettings settings)
    {
        _client = client;
        _runner = runner;
        _pollIntervalMs = settings.SupabasePollIntervalSeconds * 1000;
        _ttlMinutes = settings.TradeRequestTtlMinutes;
    }

    public void Start()
    {
        LogUtil.LogInfo("SupabasePoller", $"Starting — polling every {_pollIntervalMs / 1000}s, TTL {_ttlMinutes}min");
        _timer = new Timer(PollCallback, null, 0, _pollIntervalMs);
    }

    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        LogUtil.LogInfo("SupabasePoller", "Stopped");
    }

    private async void PollCallback(object? state)
    {
        if (_polling) return;
        _polling = true;

        try
        {
            if (!_runner.IsRunning) return;

            // Expire trades stuck in active states (PokeBot crash/disconnect scenario)
            var stuck = await _client.GetStuckActiveRequests(_ttlMinutes);
            foreach (var req in stuck)
            {
                await _client.UpdateTradeRequest(req.Id, new
                {
                    status = "expired",
                    result_message = "Trade request timed out — PokeBot may have disconnected. Please try again.",
                });
                LogUtil.LogInfo("SupabasePoller", $"Expired stuck active request {req.Id} (status was {req.Status})");
            }

            var pending = await _client.GetPendingRequests();
            foreach (var req in pending)
            {
                // TTL check
                if (DateTime.UtcNow - req.CreatedAt > TimeSpan.FromMinutes(_ttlMinutes))
                {
                    await _client.UpdateTradeRequest(req.Id, new { status = "expired", result_message = "Request timed out" });
                    LogUtil.LogInfo("SupabasePoller", $"Expired request {req.Id}");
                    continue;
                }

                await ProcessRequest(req);
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogInfo("SupabasePoller", $"Poll error: {ex.Message}");
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task ProcessRequest(TradeRequestRow req)
    {
        // Mark as processing to prevent double-pickup
        await _client.UpdateTradeRequest(req.Id, new { status = "processing" });
        LogUtil.LogInfo("SupabasePoller", $"Processing request {req.Id}");

        // Branch: batch trade
        if (req.BatchTotal > 1)
        {
            await ProcessBatchRequest(req);
            return;
        }

        // Generate PKM using the shared internal method from TradeApiHandler
        var result = TradeApiHandler.TryGeneratePKM(req.ShowdownSet, _runner);
        if (result == null)
        {
            await _client.UpdateTradeRequest(req.Id, new
            {
                status = "failed",
                result_message = "Could not legalize Showdown set. Check the set and try again.",
            });
            return;
        }

        var (pkm, mode) = result.Value;
        var tradeCode = mode is ProgramMode.SV or ProgramMode.LZA
            ? _rng.Next(0, 100_000_000)
            : _rng.Next(0, 10000);

        if (await TryProcessWithControlPlane(
            req,
            [req.ShowdownSet],
            tradeCode,
            batchTotal: 0).ConfigureAwait(false))
        {
            return;
        }

        int queuePos = EnqueueWebTrade(pkm, mode, req, tradeCode);
        if (queuePos < 0)
        {
            await _client.UpdateTradeRequest(req.Id, new
            {
                status = "failed",
                result_message = "Could not enqueue trade — bot may be offline or queue full.",
            });
            return;
        }

        await _client.UpdateTradeRequest(req.Id, new
        {
            status = "queued",
            link_code = tradeCode,
            queue_position = queuePos,
        });
    }

    private int EnqueueWebTrade(PKM pkm, ProgramMode mode, TradeRequestRow req, int tradeCode)
    {
        try
        {
            return mode switch
            {
                ProgramMode.SV   => EnqueueTyped((PK9)pkm, req, tradeCode),
                ProgramMode.SWSH => EnqueueTyped((PK8)pkm, req, tradeCode),
                ProgramMode.BDSP => EnqueueTyped((PB8)pkm, req, tradeCode),
                ProgramMode.LA   => EnqueueTyped((PA8)pkm, req, tradeCode),
                ProgramMode.LZA  => EnqueueTyped((PA9)pkm, req, tradeCode),
                _ => -1,
            };
        }
        catch { return -1; }
    }

    private int EnqueueTyped<T>(T pkm, TradeRequestRow req, int tradeCode) where T : PKM, new()
    {
        if (_runner is not PokeBotRunner<T> typedRunner) return -1;

        var hub = typedRunner.Hub;
        ulong webUserId = (ulong)Math.Abs(req.UserId.GetHashCode());
        var trainerInfo = new PokeTradeTrainerInfo("WebTrader", webUserId);
        var notifier = new WebTradeNotifier<T>(_client, req.Id, req.UserId, req.ShowdownSet, req.GameVersion);

        var detail = new PokeTradeDetail<T>(pkm, trainerInfo, notifier, PokeTradeType.Specific, tradeCode, false);
        var entry = new TradeEntry<T>(detail, webUserId, PokeRoutineType.LinkTrade, "WebTrader");

        notifier.OnFinish = _ =>
        {
            hub.Queues.Info.Remove(entry);
            WebTradeRegistry.CancelActions.TryRemove(req.Id, out var _);
        };

        WebTradeRegistry.CancelActions[req.Id] = () =>
        {
            detail.IsCanceled = true;
            hub.Queues.Info.Remove(entry);
        };

        var addResult = hub.Queues.Info.AddToTradeQueue(entry, webUserId);
        if (addResult != QueueResultAdd.Added)
        {
            WebTradeRegistry.CancelActions.TryRemove(req.Id, out var _);
            return -1;
        }

        return hub.Queues.Info.CheckPosition(webUserId, 0, PokeRoutineType.LinkTrade).Position;
    }

    private async Task ProcessBatchRequest(TradeRequestRow req)
    {
        var sets = req.ShowdownSet.Split("---",
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var mode = _runner.Config.Distribution.CurrentMode;
        var validPkm = new List<PKM>();
        var validSets = new List<string>();
        foreach (var set in sets)
        {
            var result = TradeApiHandler.TryGeneratePKM(set, _runner);
            if (result != null)
            {
                validPkm.Add(result.Value.pkm);
                validSets.Add(set);
            }
        }

        if (validPkm.Count == 0)
        {
            await _client.UpdateTradeRequest(req.Id, new
            {
                status = "failed",
                result_message = "None of the batch sets could be legalized. Check your sets and try again.",
            });
            return;
        }

        var tradeCode = mode is ProgramMode.SV or ProgramMode.LZA
            ? _rng.Next(0, 100_000_000)
            : _rng.Next(0, 10000);

        // Only one valid set — fall back to single trade
        if (validPkm.Count == 1)
        {
            if (await TryProcessWithControlPlane(
                req,
                validSets,
                tradeCode,
                batchTotal: 0).ConfigureAwait(false))
            {
                return;
            }

            int singlePos = EnqueueWebTrade(validPkm[0], mode, req, tradeCode);
            if (singlePos < 0)
            {
                await _client.UpdateTradeRequest(req.Id, new
                {
                    status = "failed",
                    result_message = "Could not enqueue trade — bot may be offline or queue full.",
                });
                return;
            }
            await _client.UpdateTradeRequest(req.Id, new
            {
                status = "queued",
                link_code = tradeCode,
                queue_position = singlePos,
            });
            return;
        }

        if (await TryProcessWithControlPlane(
            req,
            validSets,
            tradeCode,
            validPkm.Count).ConfigureAwait(false))
        {
            return;
        }

        int queuePos = EnqueueWebBatch(validPkm, mode, req, tradeCode);
        if (queuePos < 0)
        {
            await _client.UpdateTradeRequest(req.Id, new
            {
                status = "failed",
                result_message = "Could not enqueue batch trade — bot may be offline or queue full.",
            });
            return;
        }

        await _client.UpdateTradeRequest(req.Id, new
        {
            status = "queued",
            link_code = tradeCode,
            queue_position = queuePos,
            batch_current = 0,
        });
    }

    private int EnqueueWebBatch(List<PKM> pkms, ProgramMode mode, TradeRequestRow req, int tradeCode)
    {
        try
        {
            return mode switch
            {
                ProgramMode.SV   => EnqueueWebBatchTyped(pkms.Cast<PK9>().ToList(), req, tradeCode),
                ProgramMode.SWSH => EnqueueWebBatchTyped(pkms.Cast<PK8>().ToList(), req, tradeCode),
                ProgramMode.BDSP => EnqueueWebBatchTyped(pkms.Cast<PB8>().ToList(), req, tradeCode),
                ProgramMode.LA   => EnqueueWebBatchTyped(pkms.Cast<PA8>().ToList(), req, tradeCode),
                ProgramMode.LZA  => EnqueueWebBatchTyped(pkms.Cast<PA9>().ToList(), req, tradeCode),
                _ => -1,
            };
        }
        catch { return -1; }
    }

    private int EnqueueWebBatchTyped<T>(List<T> pkms, TradeRequestRow req, int tradeCode) where T : PKM, new()
    {
        if (_runner is not PokeBotRunner<T> typedRunner) return -1;

        var hub = typedRunner.Hub;
        ulong webUserId = (ulong)Math.Abs(req.UserId.GetHashCode());
        var trainerInfo = new PokeTradeTrainerInfo("WebTrader", webUserId);
        var notifier = new WebTradeNotifier<T>(_client, req.Id, req.UserId, req.ShowdownSet, req.GameVersion);
        var uniqueId = req.Id.GetHashCode() & 0x7FFFFFFF;

        var detail = new PokeTradeDetail<T>(
            pkms[0], trainerInfo, notifier,
            PokeTradeType.Batch, tradeCode,
            favored: false, lgcode: null,
            batchTradeNumber: 1,
            totalBatchTrades: pkms.Count,
            isMysteryEgg: false,
            uniqueTradeID: uniqueId)
        {
            BatchTrades = pkms,
        };

        var entry = new TradeEntry<T>(detail, webUserId, PokeRoutineType.Batch, "WebTrader", uniqueId);

        notifier.OnFinish = routine =>
        {
            hub.Queues.Info.Remove(entry);
            WebTradeRegistry.CancelActions.TryRemove(req.Id, out var _);
        };

        WebTradeRegistry.CancelActions[req.Id] = () =>
        {
            detail.IsCanceled = true;
            hub.Queues.Info.Remove(entry);
        };

        var addResult = hub.Queues.Info.AddToTradeQueue(entry, webUserId);
        if (addResult != QueueResultAdd.Added)
        {
            WebTradeRegistry.CancelActions.TryRemove(req.Id, out var _);
            return -1;
        }

        return hub.Queues.Info.CheckPosition(webUserId, 0, PokeRoutineType.Batch).Position;
    }

    private async Task<bool> TryProcessWithControlPlane(
        TradeRequestRow request,
        IReadOnlyList<string> showdownSets,
        int tradeCode,
        int batchTotal)
    {
        if (McpControlPlaneService.CurrentOrchestrator is not { } orchestrator)
            return false;

        var trainerId = StableWebsiteTrainerId(request.UserId);
        var submission =
            await ControlPlaneHttpTradeBridge.SubmitQueueAsync(
                orchestrator,
                _runner,
                trainerId,
                "WebTrader",
                showdownSets,
                tradeCode,
                isFavored: false,
                rateLimitReservationId: null,
                requestedIdempotencyKey: $"supabase-{request.Id}",
                requestIdentity: request.Id).ConfigureAwait(false);
        if (!submission.IsSuccess)
        {
            await _client.UpdateTradeRequest(request.Id, new
            {
                status = "failed",
                result_message =
                    submission.Error?.Message ??
                    "Could not enqueue durable trade operation.",
            }).ConfigureAwait(false);
            return true;
        }

        WebTradeRegistry.CancelActions[request.Id] = () =>
            ControlPlaneHttpTradeBridge.Cancel(
                orchestrator,
                submission.OwnerId!,
                submission.OperationId!,
                request.Id);
        _ = Task.Run(() => MirrorControlPlaneOperation(
            orchestrator,
            submission.OwnerId!,
            submission.OperationId!,
            request,
            batchTotal));

        await _client.UpdateTradeRequest(request.Id, new
        {
            status = "queued",
            link_code = tradeCode,
            queue_position = submission.Admission!.QueuePosition,
            batch_current = batchTotal > 0 ? 0 : (int?)null,
        }).ConfigureAwait(false);
        return true;
    }

    private async Task MirrorControlPlaneOperation(
        TradeOrchestrator orchestrator,
        string ownerId,
        string operationId,
        TradeRequestRow request,
        int batchTotal)
    {
        string? previousFingerprint = null;
        try
        {
            while (true)
            {
                var operationResponse =
                    orchestrator.GetTradeOperation(ownerId, operationId);
                if (!operationResponse.Success)
                    throw new InvalidOperationException(
                        operationResponse.Error?.Message ??
                        "Trade operation is unavailable.");
                var operation = operationResponse.Data!;
                var planResponse =
                    orchestrator.GetTradePlan(ownerId, operation.PlanId);
                if (!planResponse.Success)
                    throw new InvalidOperationException(
                        planResponse.Error?.Message ??
                        "Trade plan is unavailable.");

                var plan = planResponse.Data!;
                var current = operation.CurrentItemId is null
                    ? plan.Items.OrderBy(z => z.Position).FirstOrDefault(
                        z => !IsFinished(z.State))
                    : plan.Items.FirstOrDefault(
                        z => z.ItemId == operation.CurrentItemId);
                var status = MapSupabaseStatus(operation.State, current?.State);
                var batchCurrent = batchTotal > 0
                    ? operation.State == TradeOperationState.Completed
                        ? batchTotal
                        : current?.Position + 1 ??
                            plan.Items.Count(z => IsFinished(z.State))
                    : 0;
                var message = OperationMessage(operation.State);
                var fingerprint = $"{status}:{batchCurrent}:{message}";
                if (!string.Equals(
                    fingerprint,
                    previousFingerprint,
                    StringComparison.Ordinal))
                {
                    await _client.UpdateTradeRequest(request.Id, new
                    {
                        status,
                        result_message = message,
                        batch_current =
                            batchTotal > 0 ? batchCurrent : (int?)null,
                    }).ConfigureAwait(false);
                    previousFingerprint = fingerprint;
                }

                if (operation.State is
                    TradeOperationState.Completed or
                    TradeOperationState.Failed or
                    TradeOperationState.Cancelled)
                {
                    await _client.InsertTradeHistory(new()
                    {
                        Source = "web",
                        UserId = request.UserId,
                        ShowdownSet = request.ShowdownSet,
                        GameVersion = request.GameVersion,
                        Status = status,
                        ResultMessage = message,
                    }).ConfigureAwait(false);
                    return;
                }

                await Task.Delay(250).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogInfo(
                "SupabasePoller",
                $"Durable status mirror failed for {request.Id}: {ex.Message}");
            try
            {
                await _client.UpdateTradeRequest(request.Id, new
                {
                    status = "failed",
                    result_message =
                        "Trade status synchronization failed; an operator should inspect the durable operation.",
                }).ConfigureAwait(false);
            }
            catch (Exception updateException)
            {
                LogUtil.LogInfo(
                    "SupabasePoller",
                    $"Could not report mirror failure for {request.Id}: {updateException.Message}");
            }
        }
        finally
        {
            WebTradeRegistry.CancelActions.TryRemove(request.Id, out _);
        }
    }

    private static string MapSupabaseStatus(
        TradeOperationState operation,
        TradePlanItemState? item) =>
        operation switch
        {
            TradeOperationState.Completed => "completed",
            TradeOperationState.Cancelled => "canceled",
            TradeOperationState.Failed or
                TradeOperationState.Paused or
                TradeOperationState.NeedsAttention => "failed",
            TradeOperationState.Queued => "queued",
            _ => item == TradePlanItemState.Searching
                ? "searching"
                : item is
                    TradePlanItemState.PartnerFound or
                    TradePlanItemState.Offered or
                    TradePlanItemState.Confirming or
                    TradePlanItemState.Settling
                    ? "inprogress"
                    : "queued",
        };

    private static string? OperationMessage(TradeOperationState state) =>
        state switch
        {
            TradeOperationState.Completed => "Trade completed.",
            TradeOperationState.Cancelled => "Trade canceled.",
            TradeOperationState.Failed => "Trade failed.",
            TradeOperationState.Paused =>
                "Trade paused; operator action is required.",
            TradeOperationState.NeedsAttention =>
                "Trade settlement is uncertain and requires operator attention.",
            _ => null,
        };

    private static bool IsFinished(TradePlanItemState state) =>
        state is
            TradePlanItemState.Completed or
            TradePlanItemState.Skipped or
            TradePlanItemState.Failed;

    private static ulong StableWebsiteTrainerId(string userId)
    {
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(userId ?? string.Empty));
        var trainerId = BitConverter.ToUInt64(hash, 0);
        return trainerId == 0 ? 1UL : trainerId;
    }

    public void Dispose() => _timer?.Dispose();
}
