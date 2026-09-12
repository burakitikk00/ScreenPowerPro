using System;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenPowerPro.Core.Capture;

public class ChunkDroppedEventArgs : EventArgs
{
    public ICaptureChunk Chunk { get; }
    public string Reason { get; }

    public ChunkDroppedEventArgs(ICaptureChunk chunk, string reason)
    {
        Chunk = chunk;
        Reason = reason;
    }
}

public interface IRecorderEngine : IAsyncDisposable
{
    ValueTask StartAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Signals the producer to stop. Does NOT immediately close the stream,
    /// allows the ChunkWriter to drain its buffers completely.
    /// </summary>
    ValueTask StopAsync();

    event EventHandler<ChunkDroppedEventArgs> ChunkDropped;
}
