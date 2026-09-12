using System;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenPowerPro.Core.Capture;

public interface IChunkWriter : IAsyncDisposable
{
    ValueTask EnqueueAsync(ICaptureChunk chunk, CancellationToken ct = default);
    
    /// <summary>
    /// Flushes all enqueued chunks and seals the container/file.
    /// This should be awaited during the StopAsync phase before closing handles.
    /// </summary>
    ValueTask FlushAndSealAsync();
}
