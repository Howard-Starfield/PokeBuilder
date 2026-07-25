using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;

namespace SysBot.Pokemon.Discord;

public sealed record TradeRequestValidationResult<T>(
    T? Pokemon,
    string? Error,
    bool DeleteResponseAfterDelay,
    bool HandlerNormalized = false)
    where T : PKM, new()
{
    public bool IsValid => Pokemon is not null && string.IsNullOrEmpty(Error);

    public static TradeRequestValidationResult<T> Valid(T pokemon, bool handlerNormalized = false) =>
        new(pokemon, null, false, handlerNormalized);

    public static TradeRequestValidationResult<T> Invalid(string error, bool deleteResponseAfterDelay = false) =>
        new(null, error, deleteResponseAfterDelay, false);
}

/// <summary>
/// Applies the common queue-entry safety policy to both single-file and batch-file requests.
/// </summary>
public static class TradeRequestValidator<T> where T : PKM, new()
{
    public static TradeRequestValidationResult<T> Validate(T? pk, bool isNonNative = false)
    {
        if (pk is null)
            return TradeRequestValidationResult<T>.Invalid("Attachment provided is not compatible with this module!");

        var config = SysCord<T>.Runner.Hub.Config;
        var legality = new LegalityAnalysis(pk);
        bool handlerNormalized = false;

        if (!legality.Valid && TryNormalizeCurrentHandler(pk, legality, out var normalized))
        {
            pk = normalized;
            legality = new LegalityAnalysis(pk);
            handlerNormalized = true;

            string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
            LogUtil.LogInfo(
                $"Normalized a stale current-handler flag for a {speciesName} attachment.",
                nameof(TradeRequestValidator<T>));
        }

        if (!pk.CanBeTraded(legality.EncounterOriginal))
            return TradeRequestValidationResult<T>.Invalid("Provided Pokémon content is blocked from trading!", true);

        if (TradeExtensions<T>.IsItemBlocked(pk))
        {
            var itemName = pk.HeldItem > 0 ? GameInfo.GetStrings("en").Item[pk.HeldItem] : "(none)";
            return TradeRequestValidationResult<T>.Invalid($"Trade blocked: The held item '{itemName}' cannot be traded.", true);
        }

        if (!legality.Valid)
        {
            string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
            string error = pk.IsEgg
                ? $"Invalid Showdown Set for the {speciesName} egg. Please review your information and try again.\n\nLegality Report:\n```\n{legality.Report()}\n```"
                : $"{speciesName} attachment is not legal, and cannot be traded!\n\nLegality Report:\n```\n{legality.Report()}\n```";
            return TradeRequestValidationResult<T>.Invalid(error, true);
        }

        if (config.Legality.DisallowNonNatives && isNonNative)
        {
            string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
            return TradeRequestValidationResult<T>.Invalid(
                $"This **{speciesName}** is not native to this game, and cannot be traded! Trade with the correct bot, then trade to HOME.");
        }

        if (config.Legality.DisallowTracked && pk is IHomeTrack { HasTracker: true })
        {
            string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
            return TradeRequestValidationResult<T>.Invalid($"This {speciesName} file is tracked by HOME, and cannot be traded!");
        }

        return TradeRequestValidationResult<T>.Valid(pk, handlerNormalized);
    }

    internal static bool TryNormalizeCurrentHandler(T pk, LegalityAnalysis legality, out T normalized) =>
        TryNormalizeCurrentHandler(
            pk,
            legality.HasResult(LegalityCheckResultCode.TransferHandlerFlagRequired),
            candidate => new LegalityAnalysis(candidate).Valid,
            out normalized);

    internal static bool TryNormalizeCurrentHandler(
        T pk,
        bool handlerFlagRequired,
        Func<T, bool> isLegal,
        out T normalized)
    {
        ArgumentNullException.ThrowIfNull(pk);
        ArgumentNullException.ThrowIfNull(isLegal);
        normalized = pk;

        if (!handlerFlagRequired ||
            pk.CurrentHandler != 0 ||
            pk.IsEgg ||
            pk.IsUntraded ||
            string.IsNullOrWhiteSpace(pk.HandlingTrainerName))
        {
            return false;
        }

        var clone = (T)pk.Clone();
        clone.CurrentHandler = 1;
        clone.RefreshChecksum();

        if (!isLegal(clone))
            return false;

        normalized = clone;
        return true;
    }
}
