using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon;

internal static class PokePortalRecovery
{
    internal const int ConfirmationSettleDelayMilliseconds = 500;
    internal const int MaxAttempts = 2;
    internal const int PromptSettleDelayMilliseconds = 1_500;
    internal const int TransitionPollCount = 16;
    internal const int TransitionPollIntervalMilliseconds = 250;

    internal static async Task<bool> TryExitAsync(
        Func<CancellationToken, Task<bool>> isInPortal,
        Func<int, CancellationToken, Task> pressBack,
        Func<int, CancellationToken, Task> confirmExit,
        Func<int, CancellationToken, Task> delay,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(isInPortal);
        ArgumentNullException.ThrowIfNull(pressBack);
        ArgumentNullException.ThrowIfNull(confirmExit);
        ArgumentNullException.ThrowIfNull(delay);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (!await isInPortal(token).ConfigureAwait(false))
                return true;

            // Give the cancel-search prompt time to become interactive before confirming Yes.
            await pressBack(PromptSettleDelayMilliseconds, token).ConfigureAwait(false);
            if (!await isInPortal(token).ConfigureAwait(false))
                return true;

            await confirmExit(ConfirmationSettleDelayMilliseconds, token).ConfigureAwait(false);

            // SV keeps the portal flag set while the confirmation and exit transition finish.
            // Do not send another B during this window, or it can undo the recovery.
            if (await WaitForPortalExitAsync(isInPortal, delay, token).ConfigureAwait(false))
                return true;
        }

        return !await isInPortal(token).ConfigureAwait(false);
    }

    private static async Task<bool> WaitForPortalExitAsync(
        Func<CancellationToken, Task<bool>> isInPortal,
        Func<int, CancellationToken, Task> delay,
        CancellationToken token)
    {
        for (var poll = 0; poll < TransitionPollCount; poll++)
        {
            if (!await isInPortal(token).ConfigureAwait(false))
                return true;

            await delay(TransitionPollIntervalMilliseconds, token).ConfigureAwait(false);
        }

        return !await isInPortal(token).ConfigureAwait(false);
    }
}
