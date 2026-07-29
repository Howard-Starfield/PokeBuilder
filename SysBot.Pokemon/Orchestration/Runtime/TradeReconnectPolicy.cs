using System;

namespace SysBot.Pokemon;

public static class TradeReconnectPolicy
{
    private static readonly int[] StagedDelaysMs =
        [0, 250, 1_000, 5_000, 30_000];

    public static int GetDelayBeforeAttempt(
        int zeroBasedAttempt,
        int configuredExtraDelayMs)
    {
        if (zeroBasedAttempt < 0)
            throw new ArgumentOutOfRangeException(nameof(zeroBasedAttempt));

        if (zeroBasedAttempt == 0)
            return 0;

        var staged = StagedDelaysMs[
            Math.Min(zeroBasedAttempt, StagedDelaysMs.Length - 1)];
        return checked(staged + Math.Max(0, configuredExtraDelayMs));
    }
}
