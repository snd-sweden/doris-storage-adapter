using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal interface IStorageLockProvider
{
    ValueTask<IAsyncDisposable> AcquireAsync(string name, CancellationToken cancellationToken);
}
