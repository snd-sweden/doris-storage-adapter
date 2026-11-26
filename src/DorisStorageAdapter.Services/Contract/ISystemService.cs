using DorisStorageAdapter.Services.Contract.Models;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Contract;

public interface ISystemService
{
    SystemInformation GetSystemInformation();

    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
