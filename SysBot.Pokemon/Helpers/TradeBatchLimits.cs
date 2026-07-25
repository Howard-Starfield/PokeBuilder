using System;

namespace SysBot.Pokemon.Helpers;

/// <summary>
/// Shared batch-trade size policy so the HTTP API and Discord trade commands cannot drift.
///
/// Standard users have a hard ceiling. Favored, VIP/sudo, and owner users have no bot-side numeric
/// ceiling unless the operator configures a positive <c>LegalitySettings.MaxPkmsPerTrade</c>.
/// </summary>
public static class TradeBatchLimits
{
    /// <summary>Hard ceiling for non-favored users.</summary>
    public const int DefaultMaxStandard = 5;

    /// <summary>Sentinel used by integer-based callers to represent no numeric ceiling.</summary>
    public const int Unlimited = int.MaxValue;

    /// <summary>Role-tier ceiling without considering operator config.</summary>
    public static int GetCeiling(bool isFavored) => isFavored ? Unlimited : DefaultMaxStandard;

    /// <summary>
    /// Gets the effective per-batch maximum. A value less than or equal to zero means that no
    /// operator cap is configured. Standard users still stop at <see cref="DefaultMaxStandard"/>;
    /// elevated users receive <see cref="Unlimited"/>. A positive value caps elevated users and
    /// may tighten, but never relax, the standard ceiling.
    /// </summary>
    public static int GetEffectiveMax(int configValue, bool isFavored)
    {
        if (isFavored)
            return configValue > 0 ? configValue : Unlimited;

        return configValue > 0 ? Math.Min(DefaultMaxStandard, configValue) : DefaultMaxStandard;
    }

    /// <summary>Returns whether an effective maximum represents an unlimited elevated tier.</summary>
    public static bool IsUnlimited(int effectiveMax) => effectiveMax == Unlimited;
}
