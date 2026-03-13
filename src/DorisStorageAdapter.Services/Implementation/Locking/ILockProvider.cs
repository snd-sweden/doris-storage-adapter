using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Locking;

internal interface ILockProvider
{
    ValueTask<IAsyncDisposable> AcquireAsync(string name, CancellationToken cancellationToken);

    ValueTask<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken);
}
