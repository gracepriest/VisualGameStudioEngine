using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace VisualGameStudio.ProjectSystem.Services
{
    /// <summary>
    /// A loopback-only static file server for previewing a built JavaScript project.
    ///
    /// <para>A browser cannot load an ES module over <c>file://</c> — module scripts are
    /// subject to CORS, and a <c>file://</c> origin is opaque, so the page silently loads
    /// nothing. Source maps have the same problem. So even a purely local preview needs a real
    /// HTTP origin.</para>
    ///
    /// <para><b>No interface, deliberately.</b> The two closest analogues in this assembly,
    /// FileDownloader and ClangdInstaller, are both <c>public sealed class … : IDisposable</c>
    /// with none. The interface-plus-impl convention here applies to IDE-facing services, not
    /// to I/O infrastructure.</para>
    /// </summary>
    public sealed class WebPreviewServer : IDisposable
    {
        private readonly object _gate = new object();
        private HttpListener _listener;
        private Task _acceptLoop;
        private string _root;

        /// <summary>The base URL currently served, or null when stopped.</summary>
        public string Url { get; private set; }

        /// <summary>
        /// Serves <paramref name="rootDirectory"/> on a fresh loopback port and returns the
        /// base URL. Restarting an already-running server rebinds it.
        /// </summary>
        public string Start(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("A root directory is required.", nameof(rootDirectory));

            var root = Path.GetFullPath(rootDirectory);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException($"Preview root not found: {root}");

            lock (_gate)
            {
                StopCore();

                _root = root;
                _listener = Bind(out var baseUrl);
                Url = baseUrl;

                var listener = _listener;
                _acceptLoop = Task.Run(() => AcceptLoop(listener));
                return Url;
            }
        }

        public void Stop()
        {
            lock (_gate) StopCore();
        }

        private void StopCore()
        {
            var listener = _listener;
            var loop = _acceptLoop;

            _listener = null;
            _acceptLoop = null;
            Url = null;

            if (listener == null) return;

            // Stop() makes the pending GetContextAsync throw, which is how the loop ends.
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
            try { loop?.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }

        public void Dispose() => Stop();

        /// <summary>
        /// Binds a free loopback port.
        ///
        /// <para><b>127.0.0.1 first, then <c>localhost</c>.</b> The plan says "bind 127.0.0.1
        /// only, never + or *" — the security half of that is right and is honoured, but
        /// binding ONLY the numeric form throws on machines that ACL-restrict that prefix for
        /// non-elevated processes. <c>localhost</c> is equally loopback, so the fallback keeps
        /// the guarantee while not failing on those hosts. This is the same algorithm the
        /// repo's existing loopback test fixture arrived at.</para>
        ///
        /// <para>The retry loop exists because the port is chosen by letting the OS assign one
        /// to a TcpListener and then handing it to HttpListener — another process can take it
        /// in the gap between.</para>
        /// </summary>
        private static HttpListener Bind(out string baseUrl)
        {
            Exception lastFailure = null;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();

                foreach (var host in new[] { "127.0.0.1", "localhost" })
                {
                    var prefix = $"http://{host}:{port}/";
                    var listener = new HttpListener();
                    listener.Prefixes.Add(prefix);
                    try
                    {
                        listener.Start();
                        baseUrl = prefix;
                        return listener;
                    }
                    catch (HttpListenerException ex)
                    {
                        lastFailure = ex;
                        try { listener.Close(); } catch { }
                    }
                }
            }

            throw new InvalidOperationException(
                "Could not bind a loopback preview server on any candidate port.", lastFailure);
        }

        private void AcceptLoop(HttpListener listener)
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = listener.GetContext();
                }
                catch
                {
                    // Stop()/Close() is the normal way out of this loop.
                    return;
                }

                try { Respond(context); }
                catch { /* one bad request must never take the server down */ }
                finally { try { context.Response.Close(); } catch { } }
            }
        }

        private void Respond(HttpListenerContext context)
        {
            var response = context.Response;

            // AbsolutePath is already percent-DECODED, which matters: a guard that inspects
            // the raw query string misses `%2e%2e%2f` while the file system does not.
            var relative = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? "/");
            if (relative.StartsWith("/", StringComparison.Ordinal)) relative = relative.Substring(1);
            if (relative.Length == 0) relative = "index.html";

            var path = ResolveWithinRoot(relative);
            if (path == null || !File.Exists(path))
            {
                // 404 for an escape attempt as well as a genuine miss: answering 403 confirms
                // that something is there, which is a probing oracle.
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (IOException)
            {
                // A rebuild can be mid-write. 503 tells the browser to try again rather than
                // caching an empty 404 for a file that exists.
                response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                return;
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = ContentTypeFor(path);
            response.ContentLength64 = bytes.Length;

            // A preview must never serve yesterday's build out of the browser cache.
            response.Headers["Cache-Control"] = "no-store, must-revalidate";

            response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Resolves a request path against the served root, or null when it escapes.
        /// </summary>
        private string ResolveWithinRoot(string relative)
        {
            if (relative.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;

            string candidate;
            try { candidate = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar))); }
            catch { return null; }

            return IsUnder(_root, candidate) ? candidate : null;
        }

        /// <summary>
        /// True when <paramref name="path"/> lies inside <paramref name="directory"/>.
        ///
        /// <para>⛔ A plain <c>StartsWith</c> is WRONG: <c>C:\site_evil\x</c> starts with
        /// <c>C:\site</c> and is not inside it. The trailing separator forces the comparison
        /// to land on a directory boundary.</para>
        ///
        /// <para>Public rather than internal because this assembly's convention is public
        /// seams — it grants no <c>InternalsVisibleTo</c> to the test project.</para>
        /// </summary>
        public static bool IsUnder(string directory, string path)
        {
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(path)) return false;

            var root = Path.GetFullPath(directory);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                root += Path.DirectorySeparatorChar;

            var full = Path.GetFullPath(path);

            // Windows paths are case-insensitive; comparing ordinally there would let a
            // differently-cased escape through.
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return full.StartsWith(root, comparison);
        }

        /// <summary>
        /// ⚠ <c>.wasm</c> is the entry that MUST be right: WebAssembly streaming
        /// instantiation rejects any other content type outright, so a wrong value there
        /// breaks the feature rather than merely looking untidy.
        /// </summary>
        private static readonly Dictionary<string, string> MimeTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".html"] = "text/html",
                [".htm"] = "text/html",
                [".js"] = "text/javascript",
                [".mjs"] = "text/javascript",
                [".map"] = "application/json",
                [".json"] = "application/json",
                [".wasm"] = "application/wasm",
                [".css"] = "text/css",
                [".svg"] = "image/svg+xml",
                [".png"] = "image/png",
                [".jpg"] = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".gif"] = "image/gif",
                [".webp"] = "image/webp",
                [".ico"] = "image/x-icon",
                [".woff"] = "font/woff",
                [".woff2"] = "font/woff2",
                [".txt"] = "text/plain",
                [".bas"] = "text/plain",   // a source map may reference the original next to it
                [".mod"] = "text/plain",
                [".cls"] = "text/plain",
                [".wav"] = "audio/wav",
                [".mp3"] = "audio/mpeg",
                [".ogg"] = "audio/ogg",
            };

        private static string ContentTypeFor(string path)
        {
            var type = MimeTypes.TryGetValue(Path.GetExtension(path), out var known)
                ? known
                // Unknown means unknown. Guessing text/plain would make a browser render a
                // binary as mojibake instead of downloading it.
                : "application/octet-stream";

            return type.StartsWith("text/", StringComparison.Ordinal) ||
                   type == "application/json" || type == "image/svg+xml"
                ? type + "; charset=utf-8"
                : type;
        }
    }
}
