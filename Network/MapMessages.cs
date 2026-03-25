using System;
using MessagePack;

namespace CoyEngine;

[MessagePackObject]
public record MapRequest
{
    [Key(0)] public int MapId { get; init; }

    public MapRequest() { }
    public MapRequest(int mapId) => MapId = mapId;
}

[MessagePackObject]
public record MapResponse
{
    [Key(0)] public int MapId { get; init; }
    [Key(1)] public int Width { get; init; }
    [Key(2)] public int Height { get; init; }
    [Key(3)] public int Version { get; init; }
    [Key(4)] public uint Checksum { get; init; }
    [Key(5)] public bool Compressed { get; init; }
    [Key(6)] public byte[] Payload { get; init; } = Array.Empty<byte>();

    public MapResponse() { }

    public MapResponse(int mapId, int width, int height, int version, uint checksum, bool compressed, byte[] payload)
    {
        MapId = mapId;
        Width = width;
        Height = height;
        Version = version;
        Checksum = checksum;
        Compressed = compressed;
        Payload = payload;
    }
}

[MessagePackObject]
public record PreviewResponse
{
    [Key(0)] public int Width { get; init; }
    [Key(1)] public int Height { get; init; }
    [Key(2)] public bool Compressed { get; init; }
    [Key(3)] public byte[] Payload { get; init; } = Array.Empty<byte>();
    [Key(4)] public byte[]? RiverPayload { get; init; }
    [Key(5)] public byte[]? ElevationPayload { get; init; }
    [Key(6)] public byte[]? FlowPayload { get; init; }

    public PreviewResponse() { }

    public PreviewResponse(int width, int height, bool compressed, byte[] payload, byte[]? riverPayload = null, byte[]? elevationPayload = null, byte[]? flowPayload = null)
    {
        Width = width;
        Height = height;
        Compressed = compressed;
        Payload = payload;
        RiverPayload = riverPayload;
        ElevationPayload = elevationPayload;
        FlowPayload = flowPayload;
    }
}
