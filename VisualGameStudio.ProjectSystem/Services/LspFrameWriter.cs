using System.Text;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Serializes LSP frames (Content-Length header + JSON body) onto a writer,
/// one at a time.
///
/// ALL waiting is bounded by the caller's CancellationToken — including the
/// wait behind another frame's in-flight write. A wedged-but-alive server
/// that stops draining its stdin leaves the OS pipe full and a write blocked
/// forever; before this class the per-request timeout armed only AFTER the
/// write completed, so one blocked write hung every subsequent LSP call with
/// no timeout.
///
/// When a write is abandoned by cancellation the internal lock stays held
/// until the underlying write actually finishes, so a later frame can never
/// interleave into a partially written one (which would permanently corrupt
/// the protocol stream).
/// </summary>
public sealed class LspFrameWriter : IDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Writes one complete frame. Throws OperationCanceledException when the
    /// token fires while waiting for the lock or while the write is blocked
    /// (wedged pipe); IO failures propagate to the caller.
    /// </summary>
    public async Task WriteFrameAsync(TextWriter writer, string json, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        var releaseDeferred = false;
        try
        {
            // Content-Length is a BYTE count of the UTF-8 encoded body
            var frame = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";

            var writeTask = WriteAndFlushAsync(writer, frame);
            try
            {
                await writeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The write is still in flight on a (probably wedged) pipe.
                // Keep the lock until it truly completes — released from a
                // continuation — so no other frame can interleave with it.
                releaseDeferred = true;
                _ = writeTask.ContinueWith(
                    t =>
                    {
                        _ = t.Exception; // observe faults
                        try { _writeLock.Release(); }
                        catch (ObjectDisposedException) { }
                        catch (SemaphoreFullException) { }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw;
            }
        }
        finally
        {
            if (!releaseDeferred)
            {
                try { _writeLock.Release(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private static async Task WriteAndFlushAsync(TextWriter writer, string frame)
    {
        await writer.WriteAsync(frame).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}
