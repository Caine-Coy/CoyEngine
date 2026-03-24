using CoyEngine.Core;
using System.IO;
using System.IO.Compression;

namespace CoyEngine;

public static class CompressionUtils
{
    public static byte[] CompressBrotli(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream();
        using (var ds = new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            ds.Write(data);
        }
        return ms.ToArray();
    }

    public static byte[] DecompressBrotli(ReadOnlySpan<byte> compressed)
    {
        using var ms = new MemoryStream(compressed.ToArray());
        using var ds = new BrotliStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        ds.CopyTo(outMs);
        return outMs.ToArray();
    }
}
