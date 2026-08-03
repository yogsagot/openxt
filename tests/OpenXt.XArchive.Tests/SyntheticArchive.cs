using System.IO.Compression;
using System.Text;

namespace OpenXt.XArchive.Tests;

/// <summary>
/// Builds .cat/.dat pairs in memory using the same ciphers the real archives use.
///
/// This is what keeps the test suite free of copyrighted data: the format is exercised end to end
/// without a single EGOSOFT byte, so the tests run anywhere.
/// </summary>
internal sealed class SyntheticArchive : IDisposable
{
    private readonly string _directory;

    public string CatPath { get; }

    private SyntheticArchive(string directory, string catPath)
    {
        _directory = directory;
        CatPath = catPath;
    }

    /// <summary>
    /// Writes an archive containing the given entries, in order. Passing the same path twice is
    /// allowed and models X-Tension's append-log index.
    /// </summary>
    public static SyntheticArchive Create(params (string Path, byte[] Data)[] entries)
    {
        string directory = Path.Combine(Path.GetTempPath(), "openxt-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        StringBuilder index = new();
        index.Append("01.dat\r\n");

        MemoryStream blob = new();
        foreach ((string path, byte[] data) in entries)
        {
            index.Append($"{path} {data.Length}\r\n");
            blob.Write(data);
        }

        // .cat: XOR each byte with its own position plus the seed.
        byte[] catPlain = Encoding.Latin1.GetBytes(index.ToString());
        byte[] cat = new byte[catPlain.Length];
        for (int i = 0; i < catPlain.Length; i++)
            cat[i] = (byte)(catPlain[i] ^ (byte)(0xDB + i));

        // .dat: one constant across the whole blob.
        byte[] dat = blob.ToArray();
        for (int i = 0; i < dat.Length; i++)
            dat[i] ^= 0x33;

        string catPath = Path.Combine(directory, "01.cat");
        File.WriteAllBytes(catPath, cat);
        File.WriteAllBytes(Path.Combine(directory, "01.dat"), dat);

        return new SyntheticArchive(directory, catPath);
    }

    /// <summary>Wraps content the way a <c>.pbd</c> is wrapped: gzip, prefixed magic, XORed.</summary>
    public static byte[] Pck(string content, byte key = 0x5A)
    {
        using MemoryStream compressed = new();
        using (GZipStream gzip = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.Latin1.GetBytes(content));

        byte[] payload = compressed.ToArray();
        byte[] result = new byte[payload.Length + 1];

        result[0] = (byte)(0xC8 ^ key);
        for (int i = 0; i < payload.Length; i++)
            result[i + 1] = (byte)(payload[i] ^ key);

        return result;
    }

    public CatArchive Open() => CatArchive.Open(CatPath);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a test over.
        }
    }
}
