using CoyEngine.Core;
using System;
using System.Security.Cryptography;

namespace CoyEngine;

public static class ChecksumUtils
{
    // Compute a 32-bit checksum by hashing with SHA256 and taking the first 4 bytes.
    public static uint SHA256First32(ReadOnlySpan<byte> data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data.ToArray());
        return (uint)(hash[0] << 24 | hash[1] << 16 | hash[2] << 8 | hash[3]);
    }
}
