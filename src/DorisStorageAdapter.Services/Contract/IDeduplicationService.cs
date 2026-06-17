using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Contract;

public interface IDeduplicationService
{
    Task DeduplicateAsync(CancellationToken cancellationToken);
}
