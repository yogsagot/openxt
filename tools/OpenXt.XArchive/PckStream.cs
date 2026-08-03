using System.IO.Compression;

namespace OpenXt.XArchive;

/// <summary>
/// EGOSOFT's PCK wrapper, used by every <c>.pbd</c> in the archives.
///
/// Layout after the .dat's own decryption: the whole file is XORed with a per-file key, and the
/// first plaintext byte is a fixed <c>0xC8</c> magic. That magic is what lets us recover the key
/// from the first byte alone. Everything after it is a plain gzip stream.
/// </summary>
public static class PckStream
{
    private const byte Magic = 0xC8;

    /// <summary>True if <paramref name="data"/> looks like a PCK (magic recovers and gzip follows).</summary>
    public static bool IsPck(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return false;

        byte key = (byte)(data[0] ^ Magic);
        // gzip: 0x1F 0x8B, deflate method 0x08.
        return (byte)(data[1] ^ key) == 0x1F
               && (byte)(data[2] ^ key) == 0x8B
               && (byte)(data[3] ^ key) == 0x08;
    }

    /// <summary>Decrypts and decompresses a PCK payload.</summary>
    public static byte[] Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            throw new InvalidDataException($"PCK payload is too short ({data.Length} bytes).");

        byte key = (byte)(data[0] ^ Magic);

        // Skip the magic itself: the gzip stream starts at byte 1.
        byte[] deciphered = new byte[data.Length - 1];
        for (int i = 0; i < deciphered.Length; i++)
            deciphered[i] = (byte)(data[i + 1] ^ key);

        if (deciphered.Length < 2 || deciphered[0] != 0x1F || deciphered[1] != 0x8B)
        {
            throw new InvalidDataException(
                $"PCK payload did not decrypt to a gzip stream (key 0x{key:X2}, " +
                $"got 0x{deciphered[0]:X2} 0x{(deciphered.Length > 1 ? deciphered[1] : 0):X2}).");
        }

        using MemoryStream source = new(deciphered, writable: false);
        using GZipStream gzip = new(source, CompressionMode.Decompress);
        using MemoryStream output = new(deciphered.Length * 4);
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
