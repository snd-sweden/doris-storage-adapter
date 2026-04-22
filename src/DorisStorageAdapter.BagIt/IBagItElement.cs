using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt;

public interface IBagItElement<T> where T : IBagItElement<T>
{
    static abstract T CreateEmpty();

    static abstract string FileName { get; }

    bool HasValues();

    static abstract Task<T> ParseAsync(Stream stream, CancellationToken cancellationToken);

    byte[] Serialize();
}
