using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

internal sealed class LatestStatusUpdate
{
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

    public async Task ApplyAsync(Func<string, CancellationToken, Task> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

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
                        return;

                    applyCancellation = new CancellationTokenSource();
                    _activeApply = applyCancellation;
                }

                try
                {
                    await apply(status, applyCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (applyCancellation.IsCancellationRequested)
                {
                    // The desired status changed while Discord was waiting to apply this one.
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
                        return;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
