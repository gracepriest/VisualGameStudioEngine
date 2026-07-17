using System.Net.Http.Headers;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Streams a URL to a file with progress reporting, a per-call deadline, and partial-file
/// hygiene. This is the download loop of <c>OpenVsxClient.DownloadVsixToFileAsync</c> lifted
/// into a standalone class, fixing three hazards of the original:
///
/// <para><b>Why an infinite client timeout plus a per-call deadline:</b> <c>OpenVsxClient</c>
/// sets <c>Timeout = TimeSpan.FromSeconds(30)</c> on its <see cref="HttpClient"/>
/// (<c>OpenVsxClient.cs:36</c>). Under <see cref="HttpCompletionOption.ResponseHeadersRead"/>
/// that timeout covers the WHOLE body read, so any transfer longer than 30 seconds is aborted
/// mid-stream. Here the client's timeout is <see cref="Timeout.InfiniteTimeSpan"/> and each
/// call bounds itself with a caller-chosen deadline via a linked
/// <see cref="CancellationTokenSource"/> instead.</para>
///
/// <para><b>Why stage-then-move:</b> the original streamed straight into the destination, so a
/// mid-transfer failure left a truncated file behind. This class streams to
/// <c>destinationPath + ".partial"</c> and moves it into place only on success; on ANY failure
/// the partial is deleted.</para>
///
/// <para><b>Why the headers changed:</b> the <c>User-Agent: VisualGameStudio/1.0</c> is kept —
/// GitHub rejects requests without one, and the eventual consumer downloads a GitHub release
/// asset. The original's <c>Accept: application/json</c> is dropped: it is wrong for binary
/// downloads.</para>
///
/// <para><b>Deadline surface:</b> when the deadline (not the caller's token) aborts the
/// transfer, the call throws <see cref="TimeoutException"/> naming the deadline — a plain
/// <see cref="OperationCanceledException"/> would be indistinguishable from a caller
/// cancellation. A caller cancellation still surfaces as
/// <see cref="OperationCanceledException"/>.</para>
/// </summary>
public sealed class FileDownloader : IDisposable
{
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public FileDownloader()
    {
        _httpClient = new HttpClient
        {
            // Per-call deadlines only — see the class doc for why the 30s client-wide
            // timeout of OpenVsxClient (OpenVsxClient.cs:36) is a trap for streamed bodies.
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse("VisualGameStudio/1.0"));
        // Deliberately NO Accept header: this client fetches binaries.
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destinationPath"/>, streaming through
    /// <c>destinationPath + ".partial"</c> and moving into place only on success.
    /// </summary>
    /// <param name="url">The URL to download.</param>
    /// <param name="destinationPath">Where the finished file lands. Its directory is created if missing.</param>
    /// <param name="deadline">Upper bound on the whole transfer. When it fires (and the caller's
    /// <paramref name="ct"/> is not cancelled) the call throws <see cref="TimeoutException"/>.</param>
    /// <param name="progress">Optional per-chunk reporter of (bytes downloaded so far, total bytes
    /// or -1 when the server sent no Content-Length).</param>
    /// <param name="ct">Caller cancellation; surfaces as <see cref="OperationCanceledException"/>.</param>
    public async Task DownloadAsync(
        string url,
        string destinationPath,
        TimeSpan deadline,
        IProgress<(long bytesDownloaded, long totalBytes)>? progress = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var partialPath = destinationPath + ".partial";
        var completed = false;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(deadline);
        var linked = linkedCts.Token;

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using (var contentStream = await response.Content.ReadAsStreamAsync(linked).ConfigureAwait(false))
            await using (var fileStream = File.Create(partialPath))
            {
                var buffer = new byte[81920]; // 80KB buffer, same as the original loop
                var totalRead = 0L;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, linked).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), linked).ConfigureAwait(false);
                    totalRead += bytesRead;
                    progress?.Report((totalRead, totalBytes));
                }
            }

            // Both streams are closed here (their await-using scopes ended), so the move
            // cannot race an open handle on the partial.
            File.Move(partialPath, destinationPath, overwrite: true);
            completed = true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The linked source fired but the caller's token did not: that is the deadline.
            throw new TimeoutException($"Download of '{url}' did not complete within the deadline of {deadline}.");
        }
        finally
        {
            if (!completed)
            {
                try
                {
                    if (File.Exists(partialPath))
                    {
                        File.Delete(partialPath);
                    }
                }
                catch
                {
                    // Cleanup is best-effort; the original failure is what surfaces.
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }
}
