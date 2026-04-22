using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt;

public sealed class BagItDeclaration : IBagItElement<BagItDeclaration>
{
    private static readonly byte[] _contents = Encoding.UTF8.GetBytes(
        "BagIt-Version: 1.0\nTag-File-Character-Encoding: UTF-8\n");

    private static readonly BagItDeclaration _instance = new();

    private BagItDeclaration() { }

    public static BagItDeclaration CreateEmpty() => _instance;

    public static string FileName => "bagit.txt";

    public static Task<BagItDeclaration> ParseAsync(Stream stream, CancellationToken cancellationToken) => 
        Task.FromResult(_instance);

    public bool HasValues() => true;

    public byte[] Serialize() => _contents;
}
