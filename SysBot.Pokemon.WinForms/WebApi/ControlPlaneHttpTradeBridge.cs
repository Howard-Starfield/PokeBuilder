using SysBot.Base;
using SysBot.Pokemon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SysBot.Pokemon.WinForms.WebApi;

internal sealed record ControlPlaneHttpSubmission(
    TradeQueueAdmission? Admission,
    TradeControlError? Error)
{
    public bool IsSuccess => Admission is not null && Error is null;
}

internal sealed record ControlPlaneQueueSubmission(
    string? OwnerId,
    string? OperationId,
    TradeQueueAdmission? Admission,
    TradeControlError? Error)
{
    public bool IsSuccess =>
        OwnerId is not null &&
        OperationId is not null &&
        Admission is not null &&
        Error is null;
}

/// <summary>
/// Website adapter over the durable trade-control application boundary.
/// It preserves the public HTTP response model while sharing preparation,
/// queueing, retries, recovery, evolution policy, and settlement handling.
/// </summary>
internal static class ControlPlaneHttpTradeBridge
{
    private static readonly TimeSpan AdmissionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static async Task<ControlPlaneHttpSubmission> SubmitAsync(
        TradeOrchestrator orchestrator,
        IPokeBotRunner runner,
        ulong trainerId,
        string trainerName,
        IReadOnlyList<string> showdownSets,
        HttpTradeRecord record,
        bool isFavored,
        string? rateLimitReservationId,
        string? requestedIdempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(record);
        var submission = await SubmitQueueAsync(
            orchestrator,
            runner,
            trainerId,
            trainerName,
            showdownSets,
            record.TradeCode,
            isFavored,
            rateLimitReservationId,
            requestedIdempotencyKey,
            record.TradeId).ConfigureAwait(false);
        if (!submission.IsSuccess)
            return new(null, submission.Error);

        HttpTradeRegistry.ActiveTrades[record.TradeId] = record;
        record.CancelAction = () => Cancel(
            orchestrator,
            submission.OwnerId!,
            submission.OperationId!,
            record.TradeId);
        _ = Task.Run(() => MirrorOperationAsync(
            orchestrator,
            submission.OwnerId!,
            submission.OperationId!,
            record));
        return new(submission.Admission, null);
    }

    public static async Task<ControlPlaneQueueSubmission> SubmitQueueAsync(
        TradeOrchestrator orchestrator,
        IPokeBotRunner runner,
        ulong trainerId,
        string trainerName,
        IReadOnlyList<string> showdownSets,
        int tradeCode,
        bool isFavored,
        string? rateLimitReservationId,
        string? requestedIdempotencyKey,
        string requestIdentity)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(showdownSets);

        var safeIdentity = NormalizeRequestIdentity(requestIdentity);
        var ownerId =
            $"website:{trainerId}:{(isFavored ? "favored" : "standard")}";
        var idempotencyKey = NormalizeIdempotencyKey(
            requestedIdempotencyKey,
            safeIdentity);
        var command = new CreateTradePlanCommand(
            ownerId,
            runner.Config.Distribution.CurrentMode,
            CreateAccessJson(runner, tradeCode),
            new TradePlanPolicies
            {
                Evolution = TradeEvolutionPolicy.Block,
            },
            showdownSets.Select((set, index) =>
                new TradePlanRequestItem(
                    $"website-{safeIdentity}-{index + 1:D3}",
                    set)).ToArray(),
            idempotencyKey);

        var created = orchestrator.CreateTradePlan(command);
        if (!created.Success)
        {
            ReleaseReservation(rateLimitReservationId);
            return new(null, null, null, created.Error);
        }

        var enqueued = orchestrator.EnqueueTradePlanWithQueueHints(
            ownerId,
            created.Data!.PlanId,
            idempotencyKey,
            new(
                trainerId,
                NormalizeTrainerName(trainerName),
                isFavored,
                rateLimitReservationId));
        if (!enqueued.Success)
        {
            ReleaseReservation(rateLimitReservationId);
            return new(null, null, null, enqueued.Error);
        }

        var operationId = enqueued.Data!.OperationId;
        var deadline = DateTimeOffset.UtcNow.Add(AdmissionTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var admission = orchestrator.GetQueueAdmission(ownerId, operationId);
            if (admission.Success)
                return new(ownerId, operationId, admission.Data, null);

            var operation = orchestrator.GetTradeOperation(ownerId, operationId);
            if (!operation.Success ||
                operation.Data!.State is
                    TradeOperationState.Paused or
                    TradeOperationState.NeedsAttention or
                    TradeOperationState.Failed or
                    TradeOperationState.Cancelled)
            {
                var operationError = operation.Error ??
                    ReadAdmissionFailure(
                        orchestrator,
                        ownerId,
                        operationId,
                        operation.Data?.State);
                Cancel(orchestrator, ownerId, operationId, safeIdentity);
                return new(
                    null,
                    null,
                    null,
                    operationError ?? new(
                        TradeControlErrorCodes.BotBusy,
                        $"The trade operation could not enter the queue ({operation.Data?.State})."));
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        Cancel(orchestrator, ownerId, operationId, safeIdentity);
        return new(
            null,
            null,
            null,
            new(
                TradeControlErrorCodes.BotBusy,
                "The durable trade operation did not enter the live queue in time."));
    }

    private static async Task MirrorOperationAsync(
        TradeOrchestrator orchestrator,
        string ownerId,
        string operationId,
        HttpTradeRecord record)
    {
        try
        {
            while (true)
            {
                var operationResponse =
                    orchestrator.GetTradeOperation(ownerId, operationId);
                if (!operationResponse.Success)
                {
                    record.Status = HttpTradeStatus.Failed;
                    record.ResultMessage =
                        operationResponse.Error?.Message ??
                        "The durable trade operation is unavailable.";
                    break;
                }

                var operation = operationResponse.Data!;
                var planResponse =
                    orchestrator.GetTradePlan(ownerId, operation.PlanId);
                if (!planResponse.Success)
                {
                    record.Status = HttpTradeStatus.Failed;
                    record.ResultMessage =
                        planResponse.Error?.Message ??
                        "The durable trade plan is unavailable.";
                    break;
                }

                ApplySnapshot(record, operation, planResponse.Data!);
                if (operation.State is
                    TradeOperationState.Completed or
                    TradeOperationState.Failed or
                    TradeOperationState.Cancelled)
                {
                    break;
                }

                await Task.Delay(PollInterval).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            record.Status = HttpTradeStatus.Failed;
            record.ResultMessage =
                $"Trade status synchronization failed ({ex.GetType().Name}).";
            LogUtil.LogError(
                $"Website trade bridge failed for {operationId}: {ex.Message}",
                nameof(ControlPlaneHttpTradeBridge));
        }
        finally
        {
            CleanupLater(record.TradeId);
        }
    }

    private static void ApplySnapshot(
        HttpTradeRecord record,
        TradeOperationSnapshot operation,
        TradePlanSnapshot plan)
    {
        var ordered = plan.Items.OrderBy(z => z.Position).ToArray();
        var current = operation.CurrentItemId is null
            ? ordered.FirstOrDefault(z => !IsFinished(z.State))
            : ordered.FirstOrDefault(z => z.ItemId == operation.CurrentItemId);

        if (record.BatchTotal > 0)
        {
            record.BatchCurrent = operation.State == TradeOperationState.Completed
                ? record.BatchTotal
                : current is null
                    ? ordered.Count(z => IsFinished(z.State))
                    : current.Position + 1;
        }

        record.Status = operation.State switch
        {
            TradeOperationState.Completed => HttpTradeStatus.Completed,
            TradeOperationState.Cancelled => HttpTradeStatus.Canceled,
            TradeOperationState.Failed or
                TradeOperationState.Paused or
                TradeOperationState.NeedsAttention => HttpTradeStatus.Failed,
            TradeOperationState.Queued => HttpTradeStatus.Queued,
            _ => current?.State switch
            {
                TradePlanItemState.Searching => HttpTradeStatus.Searching,
                TradePlanItemState.PartnerFound or
                    TradePlanItemState.Offered or
                    TradePlanItemState.Confirming or
                    TradePlanItemState.Settling => HttpTradeStatus.InProgress,
                _ => HttpTradeStatus.Queued,
            },
        };

        record.ResultMessage = operation.State switch
        {
            TradeOperationState.Completed => "Trade completed.",
            TradeOperationState.Cancelled => "Trade canceled.",
            TradeOperationState.Failed => "Trade failed.",
            TradeOperationState.Paused =>
                "Trade paused; operator action is required before it can continue.",
            TradeOperationState.NeedsAttention =>
                "Trade settlement is uncertain and requires operator attention.",
            _ => record.ResultMessage,
        };
    }

    private static bool IsFinished(TradePlanItemState state) =>
        state is
            TradePlanItemState.Completed or
            TradePlanItemState.Skipped or
            TradePlanItemState.Failed;

    private static TradeControlError? ReadAdmissionFailure(
        TradeOrchestrator orchestrator,
        string ownerId,
        string operationId,
        TradeOperationState? state)
    {
        var response = orchestrator.ListTradeEvents(
            ownerId,
            operationId,
            afterSequence: 0,
            limit: 200);
        if (!response.Success)
            return response.Error;

        var failure = response.Data!
            .LastOrDefault(evt =>
                evt.EventType.Contains("paused", StringComparison.Ordinal) ||
                evt.EventType.Contains("failed", StringComparison.Ordinal));
        if (failure is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(failure.DetailsJson);
            var root = document.RootElement;
            var code = TryGetString(root, "code") ??
                TradeControlErrorCodes.BotBusy;
            var message = AdmissionFailureMessage(failure.EventType, code, state);

            if (TryGetProperty(root, "error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    code = TryGetString(error, "code") ?? code;
                    message = TryGetString(error, "message") ?? message;
                }
                else if (error.ValueKind == JsonValueKind.String)
                {
                    message = error.GetString() ?? message;
                }
            }

            return new(code, message);
        }
        catch (JsonException)
        {
            return new(
                TradeControlErrorCodes.BotBusy,
                AdmissionFailureMessage(
                    failure.EventType,
                    TradeControlErrorCodes.BotBusy,
                    state));
        }
    }

    private static string AdmissionFailureMessage(
        string eventType,
        string code,
        TradeOperationState? state) => eventType switch
        {
            "dispatch_paused_runtime_unavailable" when
                code == TradeControlErrorCodes.ModeMismatch =>
                "The running bot changed to a different game mode before queue admission.",
            "dispatch_paused_runtime_unavailable" or
                "dispatch_paused_no_bot" =>
                "No running trade bot is available for this request.",
            "dispatch_paused_bot_busy" =>
                "All running trade bots are busy admitting another request. Please try again shortly.",
            "operation_paused_preparation_failed" =>
                "The Pokemon could not be prepared for the current game.",
            "operation_paused_queue_rejected" =>
                "The live trade queue rejected this request.",
            _ => $"The trade operation could not enter the queue ({state}).",
        };

    private static string? TryGetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    internal static void Cancel(
        TradeOrchestrator orchestrator,
        string ownerId,
        string operationId,
        string tradeId)
    {
        var safeTradeId = NormalizeRequestIdentity(tradeId);
        orchestrator.CancelTradeOperation(
            ownerId,
            operationId,
            $"website-cancel-{safeTradeId}",
            confirm: true,
            "Canceled through the website queue API.");
    }

    private static string CreateAccessJson(
        IPokeBotRunner runner,
        int tradeCode)
    {
        var distribution = runner.Config.Distribution;
        return distribution.CurrentMode == ProgramMode.LGPE
            ? JsonSerializer.Serialize(new
            {
                pictocodes = new[]
                {
                    distribution.LGPECode1.ToString(),
                    distribution.LGPECode2.ToString(),
                    distribution.LGPECode3.ToString(),
                },
            })
            : JsonSerializer.Serialize(new
            {
                link_code = tradeCode.ToString("D8"),
            });
    }

    private static string NormalizeIdempotencyKey(
        string? requested,
        string tradeId) =>
        !string.IsNullOrWhiteSpace(requested) &&
        requested.Trim().Length is >= 8 and <= 128
            ? requested.Trim()
            : $"website-plan-{tradeId}";

    private static string NormalizeTrainerName(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "Website trade"
            : value.Trim();
        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    private static string NormalizeRequestIdentity(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is >= 1 and <= 48 &&
            normalized.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_'))
        {
            return normalized;
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant()[..32];
    }

    private static void ReleaseReservation(string? reservationId)
    {
        if (reservationId is not null)
            TradeRateLimitService.Instance.ReleaseReservation(reservationId);
    }

    private static void CleanupLater(string tradeId)
    {
        _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(
            completedTask =>
                HttpTradeRegistry.ActiveTrades.TryRemove(tradeId, out var _),
            TaskScheduler.Default);
    }
}
