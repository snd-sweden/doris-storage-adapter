using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt;

public sealed class BagItDeclaration : IBagItElement
{
    private static readonly byte[] _contents = Encoding.UTF8.GetBytes(
        "BagIt-Version: 1.0\nTag-File-Character-Encoding: UTF-8\n");

    public const string FileName = "bagit.txt";

    private BagItDeclaration() { }

    public static BagItDeclaration Instance { get; } = new();

    public bool HasValues() => true;

    public byte[] Serialize() => _contents;
}
