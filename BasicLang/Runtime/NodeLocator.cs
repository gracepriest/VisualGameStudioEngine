using System;
using System.Diagnostics;
using System.IO;

namespace BasicLang.Runtime
{
    /// <summary>
    /// Locates a Node.js executable.
    ///
    /// <para><b>Why this lives in the compiler assembly.</b> Two consumers need it and they
    /// live on opposite sides of the IDE/compiler split: the IDE's extension host spawns
    /// Node to run VS Code extensions, and the JavaScript backend's test tier spawns Node
    /// to actually RUN emitted JS. This is the shared-resolver rule from CLAUDE.md —
    /// "change it once, not per-consumer" — applied before the duplication happened rather
    /// than after. <c>ExtensionHost.FindNodeExecutable</c> delegates here; the probe chain
    /// exists in exactly one place, so the IDE and the compiler cannot disagree about which
    /// Node they found.</para>
    ///
    /// <para>Probe order: <c>node</c>/<c>node.exe</c> on PATH (verified by actually running
    /// <c>--version</c>, not merely by existing), then common install locations, then NVM
    /// for Windows via <c>NVM_SYMLINK</c>.</para>
    /// </summary>
    public static class NodeLocator
    {
        /// <summary>The located Node executable (a bare name if on PATH), or null.</summary>
        public static string? Find()
        {
            // On PATH. Running --version rather than probing the filesystem is deliberate:
            // it also proves the binary is executable on this machine.
            foreach (var name in new[] { "node", "node.exe" })
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = name,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit(5000);
                        if (proc.ExitCode == 0) return name;
                    }
                }
                catch
                {
                    // Not found — try the next candidate.
                }
            }

            var commonPaths = new[]
            {
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                "/usr/local/bin/node",
                "/usr/bin/node"
            };

            foreach (var path in commonPaths)
                if (File.Exists(path)) return path;

            // NVM for Windows.
            var nvmDir = Environment.GetEnvironmentVariable("NVM_HOME");
            if (!string.IsNullOrEmpty(nvmDir))
            {
                var nvmSymlink = Environment.GetEnvironmentVariable("NVM_SYMLINK");
                if (!string.IsNullOrEmpty(nvmSymlink))
                {
                    var nvmNode = Path.Combine(nvmSymlink, "node.exe");
                    if (File.Exists(nvmNode)) return nvmNode;
                }
            }

            return null;
        }
    }
}
