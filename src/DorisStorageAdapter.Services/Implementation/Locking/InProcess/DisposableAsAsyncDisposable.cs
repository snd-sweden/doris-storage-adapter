using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Locking.InProcess;

internal sealed class DisposableAsAsyncDisposable : IAsyncDisposable, IDisposable
{
    private IDisposable? _inner;

    public DisposableAsAsyncDisposable(IDisposable inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _inner, null)?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
