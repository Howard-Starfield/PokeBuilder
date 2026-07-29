using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SysBot.Pokemon;

public sealed record TradePlanRequestItem(
    string ClientItemId,
    string ShowdownSet);

public sealed record CreateTradePlanCommand(
    string OwnerId,
    ProgramMode GameMode,
    string AccessJson,
    TradePlanPolicies Policies,
    IReadOnlyList<TradePlanRequestItem> Items,
    string IdempotencyKey);

public sealed record TradePlanStructuralValidation(
    bool IsValid,
    IReadOnlyList<TradeControlError> Errors);

public interface ITradeControlClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ITradeControlIdGenerator
{
    string NewPlanId();

    string NewItemId();
}

public sealed class SystemTradeControlClock : ITradeControlClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class Uuid7TradeControlIdGenerator : ITradeControlIdGenerator
{
    public string NewPlanId() => $"plan_{Guid.CreateVersion7():N}";

    public string NewItemId() => $"item_{Guid.CreateVersion7():N}";
}

public sealed class TradePlanValidationException : InvalidOperationException
{
    public TradePlanValidationException(IReadOnlyList<TradeControlError> errors)
        : base("Trade plan validation failed.")
    {
        Errors = errors;
    }

    public IReadOnlyList<TradeControlError> Errors { get; }
}

/// <summary>
/// Pure application boundary for validating and creating durable plan drafts.
/// Pokémon legality preparation remains a later runtime-adapter responsibility.
/// </summary>
public sealed partial class TradePlanApplicationService
{
    private readonly ITradePlanStore _store;
    private readonly ITradeControlClock _clock;
    private readonly ITradeControlIdGenerator _ids;
    private readonly ITradeEvolutionCapabilityRegistry _evolutionCapabilities;

    public TradePlanApplicationService(
        ITradePlanStore store,
        ITradeControlClock clock,
        ITradeControlIdGenerator ids,
        ITradeEvolutionCapabilityRegistry? evolutionCapabilities = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        _evolutionCapabilities = evolutionCapabilities ??
            new TradeEvolutionCapabilityRegistry();
    }

    public TradePlanStructuralValidation Validate(CreateTradePlanCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new List<TradeControlError>();

        if (string.IsNullOrWhiteSpace(command.OwnerId) || command.OwnerId.Length > 128)
        {
            errors.Add(Invalid(
                "owner_id",
                "Owner ID is required and cannot exceed 128 characters."));
        }

        if (command.GameMode is ProgramMode.None ||
            !Enum.IsDefined(command.GameMode))
        {
            errors.Add(Invalid("game_mode", "A supported Nintendo Switch game mode is required."));
        }

        ValidateAccess(command.GameMode, command.AccessJson, errors);

        if (command.Policies is null)
        {
            errors.Add(Invalid("policies", "Trade plan policies are required."));
        }
        else
        {
            foreach (var policyError in command.Policies.Validate())
                errors.Add(Invalid("policies", policyError));

            if (!_evolutionCapabilities.Supports(
                command.GameMode,
                command.Policies.Evolution))
            {
                var capability = _evolutionCapabilities.Get(command.GameMode);
                errors.Add(new(
                    TradeControlErrorCodes.EvolutionRequiresAttention,
                    "Automatic or manual trade-evolution handling is not enabled for this game.",
                    new Dictionary<string, object?>
                    {
                        ["field"] = "policies.evolution",
                        ["requested_policy"] = command.Policies.Evolution.ToString(),
                        ["ordinary_preconfirm_detection"] =
                            capability.OrdinaryPreConfirmDetection,
                        ["batch_preconfirm_detection"] =
                            capability.BatchPreConfirmDetection,
                        ["animation_handled"] =
                            capability.EvolutionAnimationHandled,
                        ["move_learning_handled"] =
                            capability.MoveLearningHandled,
                        ["native_switch_validated"] =
                            capability.NativeSwitchValidated,
                        ["evidence"] = capability.Evidence,
                    }));
            }
        }

        if (command.Items is null || command.Items.Count is < 1 or > 100)
        {
            errors.Add(Invalid("items", "A trade plan must contain between 1 and 100 items."));
        }
        else
        {
            var clientIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < command.Items.Count; i++)
            {
                var item = command.Items[i];
                if (item is null)
                {
                    errors.Add(Invalid($"items[{i}]", "Trade plan items cannot be null."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.ClientItemId) ||
                    item.ClientItemId.Length > 80)
                {
                    errors.Add(Invalid(
                        $"items[{i}].client_item_id",
                        "Client item ID is required and cannot exceed 80 characters."));
                }
                else if (!clientIds.Add(item.ClientItemId))
                {
                    errors.Add(Invalid(
                        $"items[{i}].client_item_id",
                        "Client item IDs must be unique within a plan."));
                }

                if (string.IsNullOrWhiteSpace(item.ShowdownSet) ||
                    item.ShowdownSet.Length > 8192)
                {
                    errors.Add(Invalid(
                        $"items[{i}].showdown_set",
                        "Showdown set is required and cannot exceed 8192 characters."));
                }
            }
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            command.IdempotencyKey.Length is < 8 or > 128)
        {
            errors.Add(Invalid(
                "idempotency_key",
                "Idempotency key must contain between 8 and 128 characters."));
        }

        return new(errors.Count == 0, errors);
    }

    public TradeStoreIdempotencyResult<TradePlanSnapshot> CreateDraft(
        CreateTradePlanCommand command)
    {
        var validation = Validate(command);
        if (!validation.IsValid)
            throw new TradePlanValidationException(validation.Errors);

        var normalizedAccess = NormalizeJsonObject(command.AccessJson);
        var draft = new TradePlanDraft(
            _ids.NewPlanId(),
            command.OwnerId,
            command.GameMode,
            normalizedAccess,
            command.Policies,
            command.Items.Select((item, index) =>
                new TradePlanItemDraft(
                    _ids.NewItemId(),
                    item.ClientItemId,
                    index,
                    item.ShowdownSet)).ToArray(),
            _clock.UtcNow);

        return _store.CreatePlan(
            draft,
            $"owner:{command.OwnerId}",
            command.IdempotencyKey,
            ComputeRequestHash(command, normalizedAccess));
    }

    private static void ValidateAccess(
        ProgramMode gameMode,
        string accessJson,
        List<TradeControlError> errors)
    {
        if (!TryParseJsonObject(accessJson, out var document))
        {
            errors.Add(Invalid("access", "Access must be a valid JSON object."));
            return;
        }

        using (document)
        {
            var access = document.RootElement;
            if (gameMode is ProgramMode.LGPE)
            {
                AddUnsupportedAccessProperties(access, "pictocodes", errors);
                if (!access.TryGetProperty("pictocodes", out var pictocodes) ||
                    pictocodes.ValueKind is not JsonValueKind.Array ||
                    pictocodes.GetArrayLength() != 3 ||
                    pictocodes.EnumerateArray().Any(code =>
                        code.ValueKind is not JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(code.GetString()) ||
                        code.GetString()!.Length > 32))
                {
                    errors.Add(Invalid(
                        "access.pictocodes",
                        "LGPE requires exactly three non-empty pictocode names."));
                }

                if (access.TryGetProperty("link_code", out _))
                {
                    errors.Add(Invalid(
                        "access.link_code",
                        "LGPE uses pictocodes and cannot use a numeric link code."));
                }
            }
            else if (gameMode is not ProgramMode.None)
            {
                AddUnsupportedAccessProperties(access, "link_code", errors);
                if (!access.TryGetProperty("link_code", out var linkCode) ||
                    linkCode.ValueKind is not JsonValueKind.String ||
                    !LinkCode().IsMatch(linkCode.GetString() ?? string.Empty))
                {
                    errors.Add(Invalid(
                        "access.link_code",
                        "This game requires an eight-digit numeric link code."));
                }

                if (access.TryGetProperty("pictocodes", out _))
                {
                    errors.Add(Invalid(
                        "access.pictocodes",
                        "Pictocodes are only valid for LGPE."));
                }
            }
        }
    }

    private static void AddUnsupportedAccessProperties(
        JsonElement access,
        string allowedProperty,
        List<TradeControlError> errors)
    {
        foreach (var property in access.EnumerateObject())
        {
            if (!string.Equals(property.Name, allowedProperty, StringComparison.Ordinal))
            {
                errors.Add(Invalid(
                    $"access.{property.Name}",
                    $"Access property '{property.Name}' is not valid for this game."));
            }
        }
    }

    private static TradeControlError Invalid(string field, string message) =>
        new(
            TradeControlErrorCodes.InvalidRequest,
            message,
            new Dictionary<string, object?> { ["field"] = field });

    private static bool TryParseJsonObject(
        string json,
        out JsonDocument document)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is JsonValueKind.Object)
                return true;
            document.Dispose();
            document = null!;
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeJsonObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string ComputeRequestHash(
        CreateTradePlanCommand command,
        string normalizedAccess)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            owner_id = command.OwnerId,
            game_mode = command.GameMode.ToString(),
            access = JsonSerializer.Deserialize<JsonElement>(normalizedAccess),
            policies = new
            {
                evolution = command.Policies.Evolution.ToString(),
                partner_disconnect_max_attempts =
                    command.Policies.PartnerDisconnectMaxAttempts,
                transport_reconnect_delays_ms =
                    command.Policies.TransportReconnectDelaysMs,
                on_retry_exhausted = command.Policies.OnRetryExhausted.ToString(),
                on_uncertain_settlement =
                    command.Policies.OnUncertainSettlement.ToString(),
            },
            items = command.Items.Select(item => new
            {
                client_item_id = item.ClientItemId,
                showdown_set = item.ShowdownSet,
            }),
        });
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    [GeneratedRegex("^[0-9]{8}$")]
    private static partial Regex LinkCode();
}
