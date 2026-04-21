using AsyncKeyedLock;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Locking.InProcess;

internal sealed class InProcessLockProvider : ILockProvider, IDisposable
{
    private readonly AsyncKeyedLocker<string> _locker = new();

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        var releaser = await _locker.LockAsync(name, cancellationToken).ConfigureAwait(false);
        return new DisposableAsAsyncDisposable(releaser);
    }

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(
        string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        var releaser = await _locker.LockOrNullAsync(name, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        return releaser is null ? null : new DisposableAsAsyncDisposable(releaser);
    }

    public void Dispose()
    {
        _locker.Dispose();
    }
}
