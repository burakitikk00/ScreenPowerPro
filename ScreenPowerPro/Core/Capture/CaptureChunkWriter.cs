using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ScreenPowerPro.Core.Capture;

public class CaptureChunkWriter : IChunkWriter
{
    private readonly Channel<ICaptureChunk> _channel;
    private readonly FileStream _fileStream;
    private readonly Task _drainLoop;
    private int _isSealed = 0;

    public CaptureChunkWriter(string filePath)
    {
        _channel = Channel.CreateBounded<ICaptureChunk>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        _drainLoop = Task.Run(DrainLoopAsync);
    }

    public async ValueTask EnqueueAsync(ICaptureChunk chunk, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _isSealed) == 1)
        {
            return;
        }

        await _channel.Writer.WriteAsync(chunk, ct).ConfigureAwait(false);
    }

    private async Task DrainLoopAsync()
    {
        try
        {
            await foreach (var chunk in _channel.Reader.ReadAllAsync())
            {
                // Write chunk data to the file stream.
                // Depending on the exact container format, you might write headers/lengths here.
                // For raw audio or simple telemetry logs, we just write the bytes.
                await _fileStream.WriteAsync(chunk.Data).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CaptureChunkWriter] DrainLoop error: {ex.Message}");
        }
    }

    public async ValueTask FlushAndSealAsync()
    {
        if (Interlocked.CompareExchange(ref _isSealed, 1, 0) == 0)
        {
            _channel.Writer.Complete();
            
            // Await the drain loop to ensure all enqueued chunks are fully written to the FileStream
            await _drainLoop.ConfigureAwait(false);
            
            // Flush the OS file buffers
            await _fileStream.FlushAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAndSealAsync().ConfigureAwait(false);
        await _fileStream.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
