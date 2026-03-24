using CoyEngine.Core;
using System;

namespace CoyEngine;

public static class MapEncoding
{
    // Pack an array of TileType (bytes) into a byte[] using 4-bit nibbles (2 tiles per byte).
    // Tiles must be in 0..15 range.
    public static byte[] PackTiles(ReadOnlySpan<TileType> tiles)
    {
        int count = tiles.Length;
        int outLen = (count + 1) / 2;
        var outBytes = new byte[outLen];

        for (int i = 0, j = 0; i < count; i += 2, j++)
        {
            byte a = (byte)tiles[i];
            byte b = 0;
            if (i + 1 < count) b = (byte)tiles[i + 1];

            if (a >= 16 || b >= 16)
                throw new ArgumentOutOfRangeException("Tile value must fit in 4 bits (0..15)");

            outBytes[j] = (byte)((a << 4) | (b & 0x0F));
        }

        return outBytes;
    }

    // Unpack nibble-packed tiles into TileType[] with expected count
    public static TileType[] UnpackTiles(ReadOnlySpan<byte> packed, int count)
    {
        var outTiles = new TileType[count];
        for (int i = 0, j = 0; i < count; i += 2, j++)
        {
            byte b = packed[j];
            byte a = (byte)(b >> 4);
            byte c = (byte)(b & 0x0F);
            outTiles[i] = (TileType)a;
            if (i + 1 < count)
                outTiles[i + 1] = (TileType)c;
        }
        return outTiles;
    }

    // Pack liquid data: 4 bytes per tile (type, depth, direction, flowStrength)
    public static byte[] PackLiquids(Liquid[] liquids)
    {
        var data = new byte[liquids.Length * 4];
        for (int i = 0; i < liquids.Length; i++)
        {
            int off = i * 4;
            data[off + 0] = (byte)liquids[i].Type;
            data[off + 1] = liquids[i].Depth;
            data[off + 2] = (byte)liquids[i].Direction;
            data[off + 3] = liquids[i].FlowStrength;
        }
        return data;
    }

    // Unpack liquid data from raw bytes
    public static Liquid[] UnpackLiquids(ReadOnlySpan<byte> data, int count)
    {
        var liquids = new Liquid[count];
        for (int i = 0; i < count; i++)
        {
            int off = i * 4;
            liquids[i] = new Liquid(
                (LiquidType)data[off + 0],
                data[off + 1],
                (FlowDirection)data[off + 2],
                data[off + 3]
            );
        }
        return liquids;
    }

    // Pack vegetation data: 1 byte per tile
    public static byte[] PackVegetation(VegetationType[] vegetation)
    {
        var data = new byte[vegetation.Length];
        for (int i = 0; i < vegetation.Length; i++)
        {
            data[i] = (byte)vegetation[i];
        }
        return data;
    }

    // Unpack vegetation data from raw bytes
    public static VegetationType[] UnpackVegetation(ReadOnlySpan<byte> data, int count)
    {
        var vegetation = new VegetationType[count];
        for (int i = 0; i < count; i++)
        {
            vegetation[i] = (VegetationType)data[i];
        }
        return vegetation;
    }
}
