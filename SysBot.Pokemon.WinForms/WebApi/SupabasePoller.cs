using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon;
using System;
using System.Collections.Generic;
using System.Linq;
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
        foreach (var set in sets)
        {
            var result = TradeApiHandler.TryGeneratePKM(set, _runner);
            if (result != null) validPkm.Add(result.Value.pkm);
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

    public void Dispose() => _timer?.Dispose();
}
