using System;

namespace ScreenPowerPro.Core.Capture;

public enum ChunkType
{
    Video,
    Audio,
    MouseEvent,
    Metadata
}

public interface ICaptureChunk
{
    ChunkType Type { get; }
    ReadOnlyMemory<byte> Data { get; }
    long TimestampTicks { get; }
}

public class CaptureChunk : ICaptureChunk
{
    public ChunkType Type { get; }
    public ReadOnlyMemory<byte> Data { get; }
    public long TimestampTicks { get; }

    public CaptureChunk(ChunkType type, ReadOnlyMemory<byte> data, long timestampTicks)
    {
        Type = type;
        Data = data;
        TimestampTicks = timestampTicks;
    }
}
