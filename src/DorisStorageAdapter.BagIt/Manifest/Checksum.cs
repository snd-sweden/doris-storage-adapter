using System;
using System.Collections.Generic;
using System.Linq;

namespace DorisStorageAdapter.BagIt.Manifest;

public sealed class Checksum : IEquatable<Checksum>
{
    private readonly byte[] _bytes;

    public Checksum(ChecksumAlgorithm algorithm, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        ThrowIfInvalidLength(algorithm, bytes.Length);

        Algorithm = algorithm;
        _bytes = bytes;
        HexString = Convert.ToHexStringLower(bytes);
    }

    public ChecksumAlgorithm Algorithm { get; }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public string HexString { get; }

    public static Checksum ParseHexString(ChecksumAlgorithm algorithm, string hex)
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

        return new Checksum(algorithm, bytes);
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

        return 
            Algorithm == other.Algorithm && 
            _bytes.AsSpan().SequenceEqual(other._bytes);
    }

    public override bool Equals(object? obj) => Equals(obj as Checksum);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Algorithm);
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    public static bool operator ==(Checksum? left, Checksum? right) =>
        EqualityComparer<Checksum>.Default.Equals(left, right);

    public static bool operator !=(Checksum? left, Checksum? right) =>
        !(left == right);

    private static void ThrowIfInvalidLength(ChecksumAlgorithm algorithm, int length)
    {
        int expectedLength = algorithm switch
        {
            ChecksumAlgorithm.Sha256 => 32,
            ChecksumAlgorithm.Sha512 => 64,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported checksum algorithm.")
        };

        if (length != expectedLength)
        {
            throw new ArgumentException(
                $"Checksum length {length} does not match algorithm {algorithm}. Expected {expectedLength} bytes.");
        }
    }
}
