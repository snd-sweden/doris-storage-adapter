using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.BagIt;

internal sealed class BagItDeclaration : IBagItElement<BagItDeclaration>
{
    private static readonly byte[] contents = Encoding.UTF8.GetBytes("BagIt-Version: 1.0\nTag-File-Character-Encoding: UTF-8\n");
    public static readonly BagItDeclaration Instance = new();

    private BagItDeclaration() { }

    public static string FileName => "bagit.txt";

    public static Task<BagItDeclaration> ParseAsync(Stream stream, CancellationToken cancellationToken) => Task.FromResult(Instance);

    public bool HasValues() => true;

    public byte[] Serialize() => contents;
}
