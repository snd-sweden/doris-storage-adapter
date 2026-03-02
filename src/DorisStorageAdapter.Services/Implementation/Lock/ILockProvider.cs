using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Lock;

internal interface ILockProvider
{
    ValueTask<IAsyncDisposable> AcquireAsync(string name, CancellationToken cancellationToken);

    ValueTask<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken);
}
