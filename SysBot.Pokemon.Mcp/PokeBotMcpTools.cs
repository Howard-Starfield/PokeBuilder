using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysBot.Pokemon.Mcp;

public sealed class TradeAccessToolInput
{
    [Description("Eight-digit link code for games that use numeric codes.")]
    public string? link_code { get; init; }

    [Description("Exactly three recognized pictocode names for LGPE.")]
    public IReadOnlyList<string>? pictocodes { get; init; }
}

public sealed class TradePlanItemToolInput
{
    [Description("Caller-stable identifier used to correlate item results.")]
    public required string client_item_id { get; init; }

    [Description("Complete Showdown-format Pokémon request.")]
    public required string showdown_set { get; init; }
}

public sealed class TradePlanPoliciesToolInput
{
    [Description("Evolution behavior: block, allow_manual, or allow_and_handle.")]
    public string evolution { get; init; } = "block";

    [Description("Maximum partner re-search attempts for the current item.")]
    public int partner_disconnect_max_attempts { get; init; } = 3;

    [Description("Ordered transport reconnect delays in milliseconds.")]
    public IReadOnlyList<int> transport_reconnect_delays_ms { get; init; } =
        [0, 250, 1_000, 5_000, 30_000];

    [Description("Action after retries: pause, skip_item, or cancel_plan.")]
    public string on_retry_exhausted { get; init; } = "pause";

    [Description("Fail-closed settlement policy; must be needs_attention.")]
    public string on_uncertain_settlement { get; init; } = "needs_attention";
}

public sealed class TradePlanToolInput
{
    [Description("Switch game mode: swsh, bdsp, la, sv, lgpe, or lza.")]
    public required string game_mode { get; init; }

    [Description("Numeric link code or LGPE pictocodes.")]
    public required TradeAccessToolInput access { get; init; }

    [Description("Ordered Pokémon requests, from 1 through 100 items.")]
    public required IReadOnlyList<TradePlanItemToolInput> items { get; init; }

    [Description("Bounded retry, settlement, and evolution policies.")]
    public TradePlanPoliciesToolInput? policies { get; init; }
}

[McpServerToolType]
public sealed class PokeBotMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly ITradeControlApi _api;
    private readonly IHttpContextAccessor _httpContext;
    private readonly McpRequestRateLimiter _rateLimiter;

    public PokeBotMcpTools(
        ITradeControlApi api,
        IHttpContextAccessor httpContext,
        McpRequestRateLimiter rateLimiter)
    {
        _api = api;
        _httpContext = httpContext;
        _rateLimiter = rateLimiter;
    }

    [McpServerTool(Name = "list_bot_instances")]
    [Description("List current PokeBot instances, game mode, connection state, queue availability, and runtime generation.")]
    public JsonElement ListBotInstances(
        [Description("Include configured instances that are not currently running.")] bool include_offline = true) =>
        Result(_api.ListBotInstances(OwnerId(), include_offline));

    [McpServerTool(Name = "validate_trade_plan")]
    [Description("Validate a complete multi-Pokémon trade plan without persisting or enqueueing it.")]
    public JsonElement ValidateTradePlan(
        [Description("Switch game mode.")] string game_mode,
        [Description("Numeric link code or LGPE pictocodes.")] TradeAccessToolInput access,
        [Description("Ordered Pokémon requests.")] IReadOnlyList<TradePlanItemToolInput> items,
        [Description("Bounded retry and evolution policies.")] TradePlanPoliciesToolInput? policies = null)
    {
        var mapped = MapCommand(
            OwnerId(),
            "validate-only",
            new TradePlanToolInput
            {
                game_mode = game_mode,
                access = access,
                items = items,
                policies = policies,
            });
        return mapped.Error is null
            ? Result(_api.ValidateTradePlan(mapped.Command!))
            : Error(mapped.Error);
    }

    [McpServerTool(Name = "create_trade_plan")]
    [Description("Create a durable draft trade plan with caller-stable idempotency.")]
    public JsonElement CreateTradePlan(
        [Description("Caller-stable replay key, 8 through 128 characters.")] string idempotency_key,
        [Description("Complete ordered trade plan.")] TradePlanToolInput plan)
    {
        var limited = MutationLimit();
        if (limited is not null)
            return Error(limited);
        var mapped = MapCommand(OwnerId(), idempotency_key, plan);
        return mapped.Error is null
            ? Result(_api.CreateTradePlan(mapped.Command!))
            : Error(mapped.Error);
    }

    [McpServerTool(Name = "get_trade_plan")]
    [Description("Retrieve an authenticated owner's durable trade plan and ordered item progress.")]
    public JsonElement GetTradePlan(
        [Description("Durable plan identifier returned by create_trade_plan.")] string plan_id) =>
        Result(_api.GetTradePlan(OwnerId(), plan_id));

    [McpServerTool(Name = "enqueue_trade_plan")]
    [Description("Validate, prepare, and enqueue a durable trade plan as a long-running operation.")]
    public JsonElement EnqueueTradePlan(
        [Description("Durable plan identifier.")] string plan_id,
        [Description("Caller-stable replay key, 8 through 128 characters.")] string idempotency_key)
    {
        var limited = MutationLimit();
        return limited is null
            ? Result(_api.EnqueueTradePlan(OwnerId(), plan_id, idempotency_key))
            : Error(limited);
    }

    [McpServerTool(Name = "get_trade_operation")]
    [Description("Retrieve current state for a long-running trade operation.")]
    public JsonElement GetTradeOperation(
        [Description("Operation identifier returned by enqueue_trade_plan.")] string operation_id) =>
        Result(_api.GetTradeOperation(OwnerId(), operation_id));

    [McpServerTool(Name = "list_trade_events")]
    [Description("List a bounded page of ordered, redacted trade-operation events.")]
    public JsonElement ListTradeEvents(
        [Description("Durable operation identifier.")] string operation_id,
        [Description("Return events after this exclusive sequence number.")] long after_sequence = 0,
        [Description("Maximum events to return, from 1 through 200.")] int limit = 50) =>
        Result(_api.ListTradeEvents(
            OwnerId(),
            operation_id,
            after_sequence,
            Math.Clamp(limit, 1, 200)));

    [McpServerTool(Name = "pause_trade_operation")]
    [Description("Pause a trade operation at its next safe boundary without interrupting settlement.")]
    public JsonElement PauseTradeOperation(
        [Description("Durable operation identifier.")] string operation_id,
        [Description("Caller-stable replay key.")] string idempotency_key,
        [Description("Concise operator-visible audit reason.")] string reason)
    {
        var limited = MutationLimit();
        return limited is null
            ? Result(_api.PauseTradeOperation(
                OwnerId(),
                operation_id,
                idempotency_key,
                reason))
            : Error(limited);
    }

    [McpServerTool(Name = "resume_trade_operation")]
    [Description("Resume a paused trade operation after revalidating its runtime and lease.")]
    public JsonElement ResumeTradeOperation(
        [Description("Durable operation identifier.")] string operation_id,
        [Description("Caller-stable replay key.")] string idempotency_key)
    {
        var limited = MutationLimit();
        return limited is null
            ? Result(_api.ResumeTradeOperation(
                OwnerId(),
                operation_id,
                idempotency_key))
            : Error(limited);
    }

    [McpServerTool(Name = "cancel_trade_operation")]
    [Description("Cancel a trade operation at its next safe boundary after explicit confirmation.")]
    public JsonElement CancelTradeOperation(
        [Description("Durable operation identifier.")] string operation_id,
        [Description("Caller-stable replay key.")] string idempotency_key,
        [Description("Must be true to authorize cancellation.")] bool confirm,
        [Description("Concise operator-visible audit reason.")] string reason)
    {
        var limited = MutationLimit();
        return limited is null
            ? Result(_api.CancelTradeOperation(
                OwnerId(),
                operation_id,
                idempotency_key,
                confirm,
                reason))
            : Error(limited);
    }

    [McpServerTool(Name = "resolve_trade_attention")]
    [Description("Resolve one uncertain trade item explicitly after reviewing settlement evidence.")]
    public JsonElement ResolveTradeAttention(
        [Description("Durable operation identifier.")] string operation_id,
        [Description("Caller-stable replay key.")] string idempotency_key,
        [Description("Item identifier currently needing attention.")] string item_id,
        [Description("mark_completed, retry_current, skip_item, or fail_plan.")] string resolution,
        [Description("Must be true to authorize this resolution.")] bool confirm,
        [Description("Audit reason supporting the resolution.")] string reason)
    {
        var limited = MutationLimit();
        if (limited is not null)
            return Error(limited);
        if (!TryParseResolution(resolution, out var parsed))
        {
            return Error(new(
                TradeControlErrorCodes.InvalidRequest,
                "Resolution must be mark_completed, retry_current, skip_item, or fail_plan.",
                new Dictionary<string, object?> { ["field"] = "resolution" }));
        }
        return Result(_api.ResolveTradeAttention(
            OwnerId(),
            operation_id,
            idempotency_key,
            item_id,
            parsed,
            confirm,
            reason));
    }

    private string OwnerId() =>
        _httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("An authenticated MCP principal is required.");

    private TradeControlError? MutationLimit() =>
        _rateLimiter.TryAcquireMutation(OwnerId(), DateTimeOffset.UtcNow)
            ? null
            : new(
                TradeControlErrorCodes.RateLimited,
                "The authenticated principal exceeded the MCP mutation rate limit.",
                new Dictionary<string, object?> { ["retry_after_seconds"] = 60 });

    private static JsonElement Result<T>(TradeControlResponse<T> response) =>
        JsonSerializer.SerializeToElement(response, JsonOptions);

    private static JsonElement Error(TradeControlError error) =>
        Result(TradeControlResponse<object>.Fail(error));

    private static (CreateTradePlanCommand? Command, TradeControlError? Error)
        MapCommand(
            string ownerId,
            string idempotencyKey,
            TradePlanToolInput? plan)
    {
        if (plan is null)
        {
            return (null, new(
                TradeControlErrorCodes.InvalidRequest,
                "A complete trade plan is required.",
                new Dictionary<string, object?> { ["field"] = "plan" }));
        }
        if (!TryParseMode(plan.game_mode, out var mode))
        {
            return (null, new(
                TradeControlErrorCodes.InvalidRequest,
                "game_mode must be swsh, bdsp, la, sv, lgpe, or lza.",
                new Dictionary<string, object?> { ["field"] = "game_mode" }));
        }
        if (plan.access is null)
        {
            return (null, new(
                TradeControlErrorCodes.InvalidRequest,
                "Trade access is required.",
                new Dictionary<string, object?> { ["field"] = "access" }));
        }
        if (plan.items is null || plan.items.Any(item => item is null))
        {
            return (null, new(
                TradeControlErrorCodes.InvalidRequest,
                "Trade plan items are required and cannot contain null entries.",
                new Dictionary<string, object?> { ["field"] = "items" }));
        }

        var policy = plan.policies ?? new();
        if (!TryParsePolicies(policy, out var policies, out var policyError))
            return (null, policyError);

        var accessJson = JsonSerializer.Serialize(plan.access, JsonOptions);
        var items = plan.items.Select(item =>
            new TradePlanRequestItem(
                item.client_item_id,
                item.showdown_set)).ToArray();
        return (new(
            ownerId,
            mode,
            accessJson,
            policies,
            items,
            idempotencyKey), null);
    }

    private static bool TryParseMode(string value, out ProgramMode mode)
    {
        mode = value?.Trim().ToLowerInvariant() switch
        {
            "swsh" => ProgramMode.SWSH,
            "bdsp" => ProgramMode.BDSP,
            "la" => ProgramMode.LA,
            "sv" => ProgramMode.SV,
            "lgpe" => ProgramMode.LGPE,
            "lza" => ProgramMode.LZA,
            _ => ProgramMode.None,
        };
        return mode != ProgramMode.None;
    }

    private static bool TryParsePolicies(
        TradePlanPoliciesToolInput input,
        out TradePlanPolicies policies,
        out TradeControlError? error)
    {
        error = null;
        if (!TryParseEvolution(input.evolution, out var evolution) ||
            !TryParseRetryPolicy(input.on_retry_exhausted, out var retry) ||
            !string.Equals(
                input.on_uncertain_settlement,
                "needs_attention",
                StringComparison.OrdinalIgnoreCase))
        {
            policies = new();
            error = new(
                TradeControlErrorCodes.InvalidRequest,
                "One or more policy values are invalid.",
                new Dictionary<string, object?> { ["field"] = "policies" });
            return false;
        }

        policies = new()
        {
            Evolution = evolution,
            PartnerDisconnectMaxAttempts = input.partner_disconnect_max_attempts,
            TransportReconnectDelaysMs = input.transport_reconnect_delays_ms,
            OnRetryExhausted = retry,
        };
        return true;
    }

    private static bool TryParseEvolution(
        string value,
        out TradeEvolutionPolicy result)
    {
        result = value?.Trim().ToLowerInvariant() switch
        {
            "block" => TradeEvolutionPolicy.Block,
            "allow_manual" => TradeEvolutionPolicy.AllowManual,
            "allow_and_handle" => TradeEvolutionPolicy.AllowAndHandle,
            _ => (TradeEvolutionPolicy)(-1),
        };
        return Enum.IsDefined(result);
    }

    private static bool TryParseRetryPolicy(
        string value,
        out TradeRetryExhaustedPolicy result)
    {
        result = value?.Trim().ToLowerInvariant() switch
        {
            "pause" => TradeRetryExhaustedPolicy.Pause,
            "skip_item" => TradeRetryExhaustedPolicy.SkipItem,
            "cancel_plan" => TradeRetryExhaustedPolicy.CancelPlan,
            _ => (TradeRetryExhaustedPolicy)(-1),
        };
        return Enum.IsDefined(result);
    }

    private static bool TryParseResolution(
        string value,
        out TradeAttentionResolution result)
    {
        result = value?.Trim().ToLowerInvariant() switch
        {
            "mark_completed" => TradeAttentionResolution.MarkCompleted,
            "retry_current" => TradeAttentionResolution.RetryCurrent,
            "skip_item" => TradeAttentionResolution.SkipItem,
            "fail_plan" => TradeAttentionResolution.FailPlan,
            _ => (TradeAttentionResolution)(-1),
        };
        return Enum.IsDefined(result);
    }
}
