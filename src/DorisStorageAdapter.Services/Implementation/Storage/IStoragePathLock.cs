using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal interface IStoragePathLock
{
    ValueTask<IAsyncDisposable> LockPath(string path, CancellationToken cancellationToken);
}
