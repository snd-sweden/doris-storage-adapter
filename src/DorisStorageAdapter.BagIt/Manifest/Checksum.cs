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

        if (bytes.Length != 32)
        {
            throw new ArgumentException(
                $"SHA-256 checksum must be 32 bytes, but was {bytes.Length}.");
        }

        _bytes = bytes;
        _hexString = Convert.ToHexStringLower(bytes);
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public string HexString => _hexString;

    public static Checksum ParseHexString(string hex)
    {
        ArgumentException.ThrowIfNullOrEmpty(hex);

        byte[] bytes;

        try
        {
            bytes = Convert.FromHexString(hex);
        }
        catch (FormatException ex)
        {
            throw new FormatException("Invalid checksum hex string.", ex);
        }

        return new Checksum(bytes);
    }

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
