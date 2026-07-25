using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

internal sealed class LatestStatusUpdate
{
    internal const int MaxAttempts = 2;
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _activeApply;
    private string? _desiredStatus;

    public void SetDesired(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        CancellationTokenSource? activeApply;
        lock (_sync)
        {
            if (_desiredStatus == status)
                return;

            _desiredStatus = status;
            activeApply = _activeApply;
        }

        try
        {
            activeApply?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The previous apply completed between taking the snapshot and canceling it.
        }
    }

    public async Task<bool> ApplyAsync(
        Func<string, int, CancellationToken, Task<bool>> apply,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(apply);
        delay ??= Task.Delay;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            while (true)
            {
                string? status;
                CancellationTokenSource applyCancellation;
                lock (_sync)
                {
                    status = _desiredStatus;

                    if (status is null)
                        return true;

                    applyCancellation = new CancellationTokenSource();
                    _activeApply = applyCancellation;
                }

                bool applied = false;
                try
                {
                    for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                    {
                        try
                        {
                            applied = await apply(status, attempt, applyCancellation.Token).ConfigureAwait(false);
                            if (applied)
                                break;

                            if (attempt < MaxAttempts)
                                await delay(RetryDelay, applyCancellation.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (applyCancellation.IsCancellationRequested)
                        {
                            // The desired status changed while Discord was waiting.
                            break;
                        }
                    }
                }
                finally
                {
                    lock (_sync)
                    {
                        if (ReferenceEquals(_activeApply, applyCancellation))
                            _activeApply = null;
                    }
                    applyCancellation.Dispose();
                }

                lock (_sync)
                {
                    if (status == _desiredStatus)
                        return applied;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
