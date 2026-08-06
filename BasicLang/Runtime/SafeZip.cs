using System;
using System.IO;
using System.IO.Compression;

namespace BasicLang.Runtime
{
    /// <summary>
    /// Archive extraction that refuses entries escaping the destination directory.
    ///
    /// <para><b>Why this exists.</b> An entry named <c>../evil.txt</c> is written OUTSIDE the
    /// directory it was extracted into — "zip slip". This repo extracts archives at five
    /// sites, and one of them takes fully untrusted input: third-party <c>.vsix</c> packages
    /// downloaded from Open VSX. One shared, tested guard is cheaper and safer than trusting
    /// five call sites to have each thought about it independently.</para>
    ///
    /// <para><b>Validate-then-extract, not validate-as-you-go.</b> Every entry is checked
    /// before ANY of them is written, so a crafted archive whose first entry is innocent and
    /// second is hostile cannot plant the innocent one and merely report an error.</para>
    /// </summary>
    public static class SafeZip
    {
        /// <summary>
        /// Extracts <paramref name="archivePath"/> into <paramref name="destinationDirectory"/>,
        /// refusing the whole archive if any entry would land outside it.
        /// </summary>
        /// <exception cref="InvalidDataException">An entry escapes the destination.</exception>
        public static void ExtractToDirectory(string archivePath, string destinationDirectory,
            bool overwriteFiles = false)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
                throw new ArgumentException("An archive path is required.", nameof(archivePath));
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                throw new ArgumentException("A destination is required.", nameof(destinationDirectory));

            var root = Path.GetFullPath(destinationDirectory);

            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!IsWithin(root, entry.FullName))
                        throw new InvalidDataException(
                            $"Refusing to extract '{archivePath}': the entry '{entry.FullName}' " +
                            $"would be written outside '{destinationDirectory}'. The archive is " +
                            "malformed or malicious.");
                }
            }

            Directory.CreateDirectory(root);
            ZipFile.ExtractToDirectory(archivePath, root, overwriteFiles);
        }

        /// <summary>
        /// True when <paramref name="entryName"/> resolves inside <paramref name="root"/>.
        /// </summary>
        /// <remarks>
        /// Three ways to get this wrong, all covered:
        /// <list type="bullet">
        /// <item>A ROOTED entry (<c>C:\x</c>, <c>/etc/x</c>) escapes without containing
        /// <c>..</c> at all — <c>Path.Combine</c> would silently DISCARD the root and return
        /// the rooted path.</item>
        /// <item>Backslash separators are legal in archives written by
        /// <c>Compress-Archive</c>, and the lldb-dap release runbook depends on them being
        /// accepted — so they are normalised, not rejected.</item>
        /// <item>A plain <c>StartsWith</c> says <c>/out_evil</c> is inside <c>/out</c>. The
        /// trailing separator forces the comparison onto a directory boundary.</item>
        /// </list>
        /// </remarks>
        internal static bool IsWithin(string root, string entryName)
        {
            if (string.IsNullOrEmpty(entryName)) return false;

            var relative = entryName.Replace('\\', Path.DirectorySeparatorChar)
                                    .Replace('/', Path.DirectorySeparatorChar);

            // A directory entry is just its parent path; the trailing separator carries no
            // extra meaning for containment.
            relative = relative.TrimEnd(Path.DirectorySeparatorChar);
            if (relative.Length == 0) return true;

            if (Path.IsPathRooted(relative)) return false;

            string full;
            try { full = Path.GetFullPath(Path.Combine(root, relative)); }
            catch { return false; }   // invalid characters, over-long path, …

            var prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;

            // Windows paths are case-insensitive; an ordinal comparison there would let a
            // differently-cased escape through.
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return full.StartsWith(prefix, comparison);
        }
    }
}
