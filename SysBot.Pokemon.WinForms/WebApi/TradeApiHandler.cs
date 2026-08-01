using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SysBot.Pokemon.WinForms.WebApi;

public static class TradeApiHandler
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Returns (statusCode, jsonString, "application/json") — matches BotServer's switch tuple.
    public static async Task<(int, object?, string)> HandlePostTrade(HttpListenerRequest request, IPokeBotRunner? runner)
    {
        string body;
        using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
            body = await reader.ReadToEndAsync();

        TradeRequest? req;
        try { req = JsonSerializer.Deserialize<TradeRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return Error(400, "Invalid JSON body"); }

        if (req == null || string.IsNullOrWhiteSpace(req.ShowdownSet))
            return Error(400, "Missing showdown_set");

        if (string.IsNullOrWhiteSpace(req.DiscordId))
            return Error(400, "Missing discord_id");

        if (!ulong.TryParse(req.DiscordId, out var discordUserId))
            return Error(400, "Invalid discord_id");

        if (runner == null || !runner.IsRunning)
            return Error(503, "No bot is currently running");

        var result = TryGeneratePKM(req.ShowdownSet, runner);
        if (result == null)
            return Error(400, "Could not legalize Showdown set. Check the set and try again.");

        var (pkm, mode) = result.Value;
        bool isFavored = IsFavoredRequest(runner, discordUserId, req.DiscordRoleIds, req.DiscordRoles);
        var limitDecision = TryReserveTradeLimitForApi(runner, discordUserId, 1, isFavored);
        if (limitDecision is { Allowed: false })
            return TradeLimitError(limitDecision);

        var record = new HttpTradeRecord
        {
            DiscordId = req.DiscordId,
            TradeCode = GetTradeCodeForUser(runner, discordUserId),
        };

        if (McpControlPlaneService.CurrentOrchestrator is { } orchestrator)
        {
            var submission = await ControlPlaneHttpTradeBridge.SubmitAsync(
                orchestrator,
                runner,
                discordUserId,
                req.DiscordUsername,
                [req.ShowdownSet],
                record,
                isFavored,
                limitDecision?.ReservationId,
                request.Headers["Idempotency-Key"]);
            if (!submission.IsSuccess)
                return ControlPlaneError(submission.Error!);

            record.QueuePosition = submission.Admission!.QueuePosition;
            return Ok(new
            {
                success = true,
                trade_id = record.TradeId,
                trade_code = record.TradeCode,
                queue_position = submission.Admission.QueuePosition,
                queue_count = submission.Admission.QueueCount,
                estimated_wait_minutes = submission.Admission.EstimatedWaitMinutes,
                is_favored = isFavored,
                bypassed_count = submission.Admission.BypassedCount,
            });
        }

        var enqueueResult = EnqueueTrade(pkm, mode, record, req, runner, isFavored, limitDecision?.ReservationId);
        if (enqueueResult == null)
            return Error(503, "Could not enqueue trade — bot may be offline or user already in queue");

        record.QueuePosition = enqueueResult.QueuePosition;

        return Ok(new
        {
            success = true,
            trade_id = record.TradeId,
            trade_code = record.TradeCode,
            queue_position = enqueueResult.QueuePosition,
            queue_count = enqueueResult.QueueCount,
            estimated_wait_minutes = enqueueResult.EstimatedWaitMinutes,
            is_favored = isFavored,
            bypassed_count = enqueueResult.BypassedCount,
        });
    }

    public static async Task<(int, object?, string)> HandlePostBatchTrade(
        HttpListenerRequest request, IPokeBotRunner? runner)
    {
        string body;
        using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
            body = await reader.ReadToEndAsync();

        BatchTradeRequest? req;
        try { req = JsonSerializer.Deserialize<BatchTradeRequest>(body,
                  new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return Error(400, "Invalid JSON body"); }

        if (req == null || string.IsNullOrWhiteSpace(req.DiscordId))
            return Error(400, "Missing discord_id");

        if (!ulong.TryParse(req.DiscordId, out var discordUserId))
            return Error(400, "Invalid discord_id");

        if (req.ShowdownSets == null || req.ShowdownSets.Length < 2)
            return Error(400, "Provide at least 2 showdown_sets for a batch trade");

        if (runner == null || !runner.IsRunning)
            return Error(503, "No bot is currently running");

        // Determine favored status before applying the shared policy. Standard users stop at five.
        // Favored users are unlimited when the operator cap is unset and otherwise use that cap.
        bool isFavored = IsFavoredRequest(runner, discordUserId, req.DiscordRoleIds, req.DiscordRoles);
        var batchConfig = runner.Config.Legality;
        int maxTradesAllowed = TradeBatchLimits.GetEffectiveMax(batchConfig.MaxPkmsPerTrade, isFavored);

        if (!batchConfig.AllowBatchTrades || maxTradesAllowed < 2)
            return Error(400, "Batch trades are disabled on the bot right now");

        if (req.ShowdownSets.Length > maxTradesAllowed)
        {
            string suffix = !isFavored && batchConfig.MaxPkmsPerTrade <= 0
                ? " (favored/VIP/owner users have no bot-side limit)"
                : !isFavored && batchConfig.MaxPkmsPerTrade > TradeBatchLimits.DefaultMaxStandard
                    ? $" (favored/VIP/owner users: up to {batchConfig.MaxPkmsPerTrade})"
                    : "";
            return Error(400, $"Maximum {maxTradesAllowed} Pokémon per batch{suffix}");
        }

        var mode = runner.Config.Distribution.CurrentMode;

        // Legalize each set; collect valid and skipped
        var validPkm = new List<(PKM pkm, int idx)>();
        var skipped  = new List<int>();

        for (int i = 0; i < req.ShowdownSets.Length; i++)
        {
            var result = TryGeneratePKM(req.ShowdownSets[i], runner);
            if (result == null) skipped.Add(i);
            else validPkm.Add((result.Value.pkm, i));
        }

        if (validPkm.Count == 0)
            return Error(400, "None of the provided sets could be legalized");

        // isFavored already computed above to gate the role-tiered size cap; reuse it here.
        int requestedCount = validPkm.Count == 1 ? 1 : validPkm.Count;
        var limitDecision = TryReserveTradeLimitForApi(runner, discordUserId, requestedCount, isFavored);
        if (limitDecision is { Allowed: false })
            return TradeLimitError(limitDecision);

        var record = new HttpTradeRecord
        {
            DiscordId = req.DiscordId,
            TradeCode = GetTradeCodeForUser(runner, discordUserId),
        };

        // Single valid set → enqueue as normal trade
        if (validPkm.Count == 1)
        {
            if (McpControlPlaneService.CurrentOrchestrator is { } orchestrator)
            {
                var submission = await ControlPlaneHttpTradeBridge.SubmitAsync(
                    orchestrator,
                    runner,
                    discordUserId,
                    req.DiscordUsername,
                    [req.ShowdownSets[validPkm[0].idx]],
                    record,
                    isFavored,
                    limitDecision?.ReservationId,
                    request.Headers["Idempotency-Key"]);
                if (!submission.IsSuccess)
                    return ControlPlaneError(submission.Error!);

                record.QueuePosition = submission.Admission!.QueuePosition;
                return Ok(new
                {
                    success = true,
                    trade_id = record.TradeId,
                    trade_code = record.TradeCode,
                    queue_position = submission.Admission.QueuePosition,
                    queue_count = submission.Admission.QueueCount,
                    estimated_wait_minutes = submission.Admission.EstimatedWaitMinutes,
                    total = 1,
                    skipped,
                    is_favored = isFavored,
                    bypassed_count = submission.Admission.BypassedCount,
                });
            }

            var enqueueResult = EnqueueTrade(validPkm[0].pkm, mode, record,
                new TradeRequest(req.DiscordId, req.DiscordUsername, req.ShowdownSets[validPkm[0].idx], req.DiscordRoleIds, req.DiscordRoles),
                runner, isFavored, limitDecision?.ReservationId);
            if (enqueueResult == null)
                return Error(503, "Could not enqueue trade");
            record.QueuePosition = enqueueResult.QueuePosition;
            return Ok(new
            {
                success        = true,
                trade_id       = record.TradeId,
                trade_code     = record.TradeCode,
                queue_position = enqueueResult.QueuePosition,
                queue_count    = enqueueResult.QueueCount,
                estimated_wait_minutes = enqueueResult.EstimatedWaitMinutes,
                total          = 1,
                skipped,
                is_favored     = isFavored,
                bypassed_count = enqueueResult.BypassedCount,
            });
        }

        // Multiple valid sets → batch enqueue
        record.BatchTotal   = validPkm.Count;
        record.BatchCurrent = 0;
        record.BatchSpecies = validPkm
            .Select(v => ((Species)v.pkm.Species).ToString())
            .ToList();

        if (McpControlPlaneService.CurrentOrchestrator is { } batchOrchestrator)
        {
            var submission = await ControlPlaneHttpTradeBridge.SubmitAsync(
                batchOrchestrator,
                runner,
                discordUserId,
                req.DiscordUsername,
                validPkm.Select(v => req.ShowdownSets[v.idx]).ToArray(),
                record,
                isFavored,
                limitDecision?.ReservationId,
                request.Headers["Idempotency-Key"]);
            if (!submission.IsSuccess)
                return ControlPlaneError(submission.Error!);

            record.QueuePosition = submission.Admission!.QueuePosition;
            return Ok(new
            {
                success = true,
                trade_id = record.TradeId,
                trade_code = record.TradeCode,
                queue_position = submission.Admission.QueuePosition,
                queue_count = submission.Admission.QueueCount,
                estimated_wait_minutes = submission.Admission.EstimatedWaitMinutes,
                total = validPkm.Count,
                skipped,
                is_favored = isFavored,
                bypassed_count = submission.Admission.BypassedCount,
            });
        }

        var batchEnqueueResult = EnqueueBatchTrade(
            validPkm.Select(v => v.pkm).ToList(),
            mode, record, req, runner, isFavored, limitDecision?.ReservationId);

        if (batchEnqueueResult == null)
            return Error(503, "Could not enqueue batch trade");

        record.QueuePosition = batchEnqueueResult.QueuePosition;

        return Ok(new
        {
            success        = true,
            trade_id       = record.TradeId,
            trade_code     = record.TradeCode,
            queue_position = batchEnqueueResult.QueuePosition,
            queue_count    = batchEnqueueResult.QueueCount,
            estimated_wait_minutes = batchEnqueueResult.EstimatedWaitMinutes,
            total          = validPkm.Count,
            skipped,
            is_favored     = isFavored,
            bypassed_count = batchEnqueueResult.BypassedCount,
        });
    }

    public static (int, object?, string) HandleGetStatus(IPokeBotRunner? runner)
    {
        if (runner == null)
            return Ok(new
            {
                running = false,
                game_mode = "Unknown",
                queue_count = 0,
                queue_open = false,
            });

        var queue = GetLiveQueueStatus(runner);

        return Ok(new
        {
            running = runner.IsRunning,
            game_mode = runner.Config.Distribution.CurrentMode.ToString(),
            queue_count = queue.Count,
            queue_open = queue.Open,
        });
    }

    public static (int, object?, string) HandleGetQueueStatus(string tradeId)
    {
        if (!HttpTradeRegistry.ActiveTrades.TryGetValue(tradeId, out var record))
            return (404, Json(new { error = "Trade not found" }), "application/json");

        // Build batch_items from BatchSpecies + BatchCurrent
        object? batchItems = null;
        if (record.BatchTotal > 0 && record.BatchSpecies.Count > 0)
        {
            batchItems = record.BatchSpecies.Select((species, i) => new
            {
                index   = i,
                species,
                status  = i < record.BatchCurrent - 1 ? "completed"
                        : i == record.BatchCurrent - 1 ? "inprogress"
                        : "waiting",
            }).ToArray();
        }

        return Ok(new
        {
            trade_id       = record.TradeId,
            status         = record.Status.ToString().ToLower(),
            queue_position = record.QueuePosition,
            trade_code     = record.TradeCode,
            result_message = record.ResultMessage,
            batch_total    = record.BatchTotal > 0 ? (int?)record.BatchTotal   : null,
            batch_current  = record.BatchTotal > 0 ? (int?)record.BatchCurrent : null,
            batch_items    = batchItems,
        });
    }

    public static (int, object?, string) HandleDeleteQueue(string tradeId)
    {
        // Local/Discord trade
        if (HttpTradeRegistry.ActiveTrades.TryGetValue(tradeId, out var record))
        {
            if (record.Status is HttpTradeStatus.Completed or HttpTradeStatus.Canceled or HttpTradeStatus.Failed)
            {
                HttpTradeRegistry.ActiveTrades.TryRemove(tradeId, out _);
                return (404, Json(new { error = "Trade already finished" }), "application/json");
            }
            record.CancelAction?.Invoke();
            record.Status = HttpTradeStatus.Canceled;
            record.ResultMessage = "Canceled by user";
            HttpTradeRegistry.ActiveTrades.TryRemove(tradeId, out _);
            return Ok(new { success = true });
        }

        // Supabase trade (keyed by Supabase UUID)
        if (WebTradeRegistry.CancelActions.TryGetValue(tradeId, out var cancelAction))
        {
            cancelAction();
            WebTradeRegistry.CancelActions.TryRemove(tradeId, out _);
            return Ok(new { success = true });
        }

        return (404, Json(new { error = "Trade not found" }), "application/json");
    }

    // --- Helpers ---

    private record TradeRequest(
        string DiscordId,
        string DiscordUsername,
        string ShowdownSet,
        ulong[]? DiscordRoleIds = null,
        string[]? DiscordRoles = null);

    private record BatchTradeRequest(
        string DiscordId,
        string DiscordUsername,
        string[] ShowdownSets,
        ulong[]? DiscordRoleIds = null,
        string[]? DiscordRoles = null
    );

    private sealed record EnqueueResult(
        int QueuePosition,
        int BypassedCount,
        int QueueCount,
        float EstimatedWaitMinutes);

    private static (int Count, bool Open) GetLiveQueueStatus(
        IPokeBotRunner runner) =>
        runner.Config.Distribution.CurrentMode switch
        {
            ProgramMode.SWSH when runner is PokeBotRunner<PK8> typed =>
                (typed.Hub.Queues.Info.Count, typed.Hub.Queues.Info.GetCanQueue()),
            ProgramMode.BDSP when runner is PokeBotRunner<PB8> typed =>
                (typed.Hub.Queues.Info.Count, typed.Hub.Queues.Info.GetCanQueue()),
            ProgramMode.LA when runner is PokeBotRunner<PA8> typed =>
                (typed.Hub.Queues.Info.Count, typed.Hub.Queues.Info.GetCanQueue()),
            ProgramMode.SV when runner is PokeBotRunner<PK9> typed =>
                (typed.Hub.Queues.Info.Count, typed.Hub.Queues.Info.GetCanQueue()),
            ProgramMode.LGPE when runner is PokeBotRunner<PB7> typed =>
                (typed.Hub.Queues.Info.Count, typed.Hub.Queues.Info.GetCanQueue()),
            ProgramMode.LZA when runner is PokeBotRunner<PA9> typed =>
                (typed.Hub.Queues.Info.Count, typed.Hub.Queues.Info.GetCanQueue()),
            _ => (0, false),
        };

    private static string Json(object obj) => JsonSerializer.Serialize(obj, _jsonOpts);

    private static (int, object?, string) Ok(object payload) => (200, Json(payload), "application/json");

    private static (int, object?, string) Error(int code, string message) =>
        (code, Json(new { success = false, error = message }), "application/json");

    private static (int, object?, string) ControlPlaneError(
        TradeControlError error)
    {
        var status = error.Code switch
        {
            TradeControlErrorCodes.InvalidRequest or
                TradeControlErrorCodes.LegalityFailed or
                TradeControlErrorCodes.ItemBlocked or
                TradeControlErrorCodes.EvolutionBlocked or
                TradeControlErrorCodes.EvolutionRequiresAttention => 400,
            TradeControlErrorCodes.PlanConflict => 409,
            TradeControlErrorCodes.RateLimited => 429,
            _ => 503,
        };
        return (
            status,
            Json(new
            {
                success = false,
                error = error.Message,
                error_code = error.Code.ToLowerInvariant(),
            }),
            "application/json");
    }

    private static (int, object?, string) TradeLimitError(TradeRateLimitDecision decision)
    {
        string error = decision.FailureReason switch
        {
            "request_exceeds_limit" => $"This request needs {decision.RequestedCount} trade slot(s), but the free limit is {decision.Limit} per {decision.WindowMinutes} minutes.",
            "active_reservations" => "You already have active queued trades using your free hourly slots.",
            _ => "Hourly trade limit reached.",
        };

        return (429, Json(new
        {
            success = false,
            error,
            error_code = "trade_limit_reached",
            limit = decision.Limit,
            used = decision.UsedCount,
            pending = decision.PendingCount,
            requested = decision.RequestedCount,
            window_minutes = decision.WindowMinutes,
            retry_at = decision.RetryAtUnixSeconds,
        }), "application/json");
    }

    private static int GetTradeCodeForUser(IPokeBotRunner runner, ulong trainerId)
    {
        if (runner.Config.Trade.TradeConfiguration.StoreTradeCodes)
            return new TradeCodeStorage().GetTradeCode(trainerId);

        return runner.Config.Trade.GetRandomTradeCode();
    }

    private static TradeRateLimitDecision? TryReserveTradeLimitForApi(IPokeBotRunner runner, ulong userId, int requestedCount, bool isFavored)
    {
        var cfg = runner.Config.Trade.TradeConfiguration;
        if (!cfg.EnableHourlyTradeLimit || isFavored)
            return null;

        int limit = Math.Max(1, cfg.FreeTradeLimitPerHour);
        int windowMinutes = Math.Max(1, cfg.TradeLimitWindowMinutes);
        return TradeRateLimitService.Instance.TryReserve(userId, requestedCount, limit, windowMinutes);
    }

    private static bool IsFavoredRequest(IPokeBotRunner runner, ulong discordUserId, IEnumerable<ulong>? roleIds, IEnumerable<string>? roleNames)
    {
        var favoredRoles = runner.Config.Discord.RoleFavored;
        bool idMatch = roleIds?.Any(favoredRoles.Contains) ?? false;
        bool nameMatch = roleNames?.Any(favoredRoles.Contains) ?? false;

        if (roleIds != null || roleNames != null)
        {
            var ids = roleIds is null ? "(none)" : string.Join(", ", roleIds);
            var names = roleNames is null ? "(none)" : string.Join(", ", roleNames);
            var configuredIds = string.Join(", ", favoredRoles.List.Select(z => z.ID));
            LogUtil.LogInfo(nameof(TradeApiHandler),
                $"Favored role match={idMatch || nameMatch} for DiscordId={discordUserId}. IncomingRoleIds=[{ids}] IncomingRoleNames=[{names}] ConfiguredFavoredRoleIds=[{configuredIds}]");
        }

        return idMatch || nameMatch;
    }

    internal static (PKM pkm, ProgramMode mode)? TryGeneratePKM(string set, IPokeBotRunner runner)
    {
        var mode = runner.Config.Distribution.CurrentMode;
        try
        {
            var template = AutoLegalityWrapper.GetTemplate(new ShowdownSet(set));
            var sav = ModeToTrainerInfo(mode);
            if (sav == null) return null;

            // Dispatch egg requests to ALM's GenerateEgg, matching what the Discord !trade path does
            // in Helpers.ProcessShowdownSetAsync. Without this, "Egg (Species)" sets produce a normal
            // Pokemon nicknamed "Egg" via GetLegal instead of an actual egg.
            // TradeExtensions<T>.IsEggCheck is a pure string check (doesn't use T), so any T works.
            var pkm = TradeExtensions<PK9>.IsEggCheck(set)
                ? sav.GenerateEgg(template, out _)
                : sav.GetLegal(template, out _);

            if (pkm == null || !new LegalityAnalysis(pkm).Valid)
                return null;

            return (pkm, mode);
        }
        catch { return null; }
    }


    internal static ITrainerInfo? ModeToTrainerInfo(ProgramMode mode) => mode switch
    {
        ProgramMode.SWSH => AutoLegalityWrapper.GetTrainerInfo<PK8>(),
        ProgramMode.BDSP => AutoLegalityWrapper.GetTrainerInfo<PB8>(),
        ProgramMode.LA   => AutoLegalityWrapper.GetTrainerInfo<PA8>(),
        ProgramMode.SV   => AutoLegalityWrapper.GetTrainerInfo<PK9>(),
        ProgramMode.LGPE => AutoLegalityWrapper.GetTrainerInfo<PB7>(),
        ProgramMode.LZA  => AutoLegalityWrapper.GetTrainerInfo<PA9>(),
        _ => null,
    };

    private static EnqueueResult? EnqueueTrade(PKM pkm, ProgramMode mode, HttpTradeRecord record, TradeRequest req, IPokeBotRunner runner, bool isFavored, string? reservationId)
    {
        try
        {
            return mode switch
            {
                ProgramMode.SV   => EnqueueTyped((PK9)pkm, record, req, runner, isFavored, reservationId),
                ProgramMode.SWSH => EnqueueTyped((PK8)pkm, record, req, runner, isFavored, reservationId),
                ProgramMode.BDSP => EnqueueTyped((PB8)pkm, record, req, runner, isFavored, reservationId),
                ProgramMode.LA   => EnqueueTyped((PA8)pkm, record, req, runner, isFavored, reservationId),
                ProgramMode.LGPE => EnqueueTyped((PB7)pkm, record, req, runner, isFavored, reservationId),
                ProgramMode.LZA  => EnqueueTyped((PA9)pkm, record, req, runner, isFavored, reservationId),
                _ => null,
            };
        }
        catch
        {
            if (reservationId is not null)
                TradeRateLimitService.Instance.ReleaseReservation(reservationId);
            return null;
        }
    }

    private static EnqueueResult? EnqueueTyped<T>(T pkm, HttpTradeRecord record, TradeRequest req, IPokeBotRunner runner, bool isFavored, string? reservationId)
        where T : PKM, new()
    {
        if (runner is not PokeBotRunner<T> typedRunner)
        {
            if (reservationId is not null)
                TradeRateLimitService.Instance.ReleaseReservation(reservationId);
            return null;
        }

        var hub = typedRunner.Hub;
        var userId = ulong.Parse(req.DiscordId);
        var trainerInfo = new PokeTradeTrainerInfo(req.DiscordUsername, userId);
        var baseNotifier = new HttpTradeNotifier<T>(record);
        IPokeTradeNotifier<T> notifier = reservationId is null
            ? baseNotifier
            : new RateLimitedTradeNotifier<T>(baseNotifier, reservationId);
        int preAddEntryCount = hub.Queues.Info.GetTotalEntryCount();

        var lgcode = typeof(T) == typeof(PB7)
            ? new List<Pictocodes>
            {
                runner.Config.Distribution.LGPECode1,
                runner.Config.Distribution.LGPECode2,
                runner.Config.Distribution.LGPECode3,
            }
            : null;
        var detail = new PokeTradeDetail<T>(
            pkm,
            trainerInfo,
            notifier,
            PokeTradeType.Specific,
            record.TradeCode,
            isFavored,
            lgcode);
        if (reservationId is not null)
            TradeRateLimitService.Instance.AttachReservation(detail, reservationId);
        var entry = new TradeEntry<T>(detail, userId, PokeRoutineType.LinkTrade, req.DiscordUsername);

        notifier.OnFinish = _ => hub.Queues.Info.Remove(entry);

        record.CancelAction = () =>
        {
            detail.IsCanceled = true;
            hub.Queues.Info.Remove(entry);
        };

        var addResult = hub.Queues.Info.AddToTradeQueue(entry, userId);
        if (addResult != QueueResultAdd.Added)
        {
            if (reservationId is not null)
                TradeRateLimitService.Instance.ReleaseReservation(reservationId);
            return null;
        }

        int queuePosition = hub.Queues.Info.CheckPosition(userId, detail.UniqueTradeID, PokeRoutineType.LinkTrade).Position;
        int bypassedCount = isFavored
            ? Math.Max(0, (preAddEntryCount + 1) - hub.Queues.Info.GetEntryPosition(userId, detail.UniqueTradeID))
            : 0;
        return CreateEnqueueResult(hub, queuePosition, bypassedCount);
    }

    private static EnqueueResult? EnqueueBatchTrade(
        List<PKM> pkms, ProgramMode mode, HttpTradeRecord record,
        BatchTradeRequest req, IPokeBotRunner runner, bool isFavored, string? reservationId)
    {
        try
        {
            return mode switch
            {
                ProgramMode.SV   => EnqueueBatchTyped(pkms.Cast<PK9>().ToList(), record, req, runner, isFavored, reservationId),
                ProgramMode.SWSH => EnqueueBatchTyped(pkms.Cast<PK8>().ToList(), record, req, runner, isFavored, reservationId),
                ProgramMode.BDSP => EnqueueBatchTyped(pkms.Cast<PB8>().ToList(), record, req, runner, isFavored, reservationId),
                ProgramMode.LA   => EnqueueBatchTyped(pkms.Cast<PA8>().ToList(), record, req, runner, isFavored, reservationId),
                ProgramMode.LGPE => EnqueueBatchTyped(pkms.Cast<PB7>().ToList(), record, req, runner, isFavored, reservationId),
                ProgramMode.LZA  => EnqueueBatchTyped(pkms.Cast<PA9>().ToList(), record, req, runner, isFavored, reservationId),
                _ => null,
            };
        }
        catch
        {
            if (reservationId is not null)
                TradeRateLimitService.Instance.ReleaseReservation(reservationId);
            return null;
        }
    }

    private static EnqueueResult? EnqueueBatchTyped<T>(
        List<T> pkms, HttpTradeRecord record, BatchTradeRequest req, IPokeBotRunner runner, bool isFavored, string? reservationId)
        where T : PKM, new()
    {
        if (runner is not PokeBotRunner<T> typedRunner)
        {
            if (reservationId is not null)
                TradeRateLimitService.Instance.ReleaseReservation(reservationId);
            return null;
        }

        var hub         = typedRunner.Hub;
        var userId      = ulong.Parse(req.DiscordId);
        var trainerInfo = new PokeTradeTrainerInfo(req.DiscordUsername, userId);
        var baseNotifier = new HttpTradeNotifier<T>(record);
        IPokeTradeNotifier<T> notifier = reservationId is null
            ? baseNotifier
            : new RateLimitedTradeNotifier<T>(baseNotifier, reservationId);
        var uniqueId    = record.TradeId.GetHashCode() & 0x7FFFFFFF;
        int preAddEntryCount = hub.Queues.Info.GetTotalEntryCount();

        var detail = new PokeTradeDetail<T>(
            pkms[0], trainerInfo, notifier,
            PokeTradeType.Batch, record.TradeCode,
            favored: isFavored,
            lgcode: typeof(T) == typeof(PB7)
                ? new List<Pictocodes>
                {
                    runner.Config.Distribution.LGPECode1,
                    runner.Config.Distribution.LGPECode2,
                    runner.Config.Distribution.LGPECode3,
                }
                : null,
            batchTradeNumber: 1,
            totalBatchTrades: pkms.Count,
            isMysteryEgg: false,
            uniqueTradeID: uniqueId)
        {
            BatchTrades = pkms,
        };
        if (reservationId is not null)
            TradeRateLimitService.Instance.AttachReservation(detail, reservationId);

        var entry = new TradeEntry<T>(
            detail, userId, PokeRoutineType.Batch, req.DiscordUsername, uniqueId);

        notifier.OnFinish = _ => hub.Queues.Info.Remove(entry);
        record.CancelAction = () =>
        {
            detail.IsCanceled = true;
            hub.Queues.Info.Remove(entry);
        };

        var addResult = hub.Queues.Info.AddToTradeQueue(entry, userId, allowMultiple: false);
        if (addResult != QueueResultAdd.Added)
        {
            if (reservationId is not null)
                TradeRateLimitService.Instance.ReleaseReservation(reservationId);
            return null;
        }

        int queuePosition = hub.Queues.Info.CheckPosition(userId, uniqueId, PokeRoutineType.Batch).Position;
        int bypassedCount = isFavored
            ? Math.Max(0, (preAddEntryCount + 1) - hub.Queues.Info.GetEntryPosition(userId, uniqueId))
            : 0;
        return CreateEnqueueResult(hub, queuePosition, bypassedCount);
    }

    private static EnqueueResult CreateEnqueueResult<T>(
        PokeTradeHub<T> hub,
        int queuePosition,
        int bypassedCount)
        where T : PKM, new()
    {
        var queueCount = hub.Queues.Info.Count;
        var botCount = Math.Max(1, hub.Bots.Count);
        var estimatedWaitMinutes = queuePosition > botCount
            ? hub.Config.Queues.EstimateDelay(queuePosition, botCount)
            : 0;
        return new(
            queuePosition,
            bypassedCount,
            queueCount,
            estimatedWaitMinutes);
    }
}
