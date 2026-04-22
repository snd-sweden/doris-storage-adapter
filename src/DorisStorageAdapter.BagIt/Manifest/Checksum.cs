using System;
using System.Collections.Generic;
using System.Linq;

namespace DorisStorageAdapter.BagIt.Manifest;

public sealed class Checksum : IEquatable<Checksum>
{
    private readonly byte[] _bytes;
    private readonly string _hexString;

    public Checksum(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _bytes = bytes;
        _hexString = Convert.ToHexStringLower(bytes);
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public string HexString => _hexString;

    public static Checksum ParseHexString(string hex) =>
        new(Convert.FromHexString(hex));

    public bool Equals(Checksum? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return _bytes.AsSpan().SequenceEqual(other._bytes);
    }

    public override bool Equals(object? obj) => Equals(obj as Checksum);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    public static bool operator ==(Checksum? left, Checksum? right) =>
        EqualityComparer<Checksum>.Default.Equals(left, right);

    public static bool operator !=(Checksum? left, Checksum? right) =>
        !(left == right);
}
