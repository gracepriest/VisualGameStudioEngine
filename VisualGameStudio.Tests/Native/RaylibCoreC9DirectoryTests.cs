using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the HEADLESS half of rcore Batch core-C9 (directory listing + file mod time). These are pure
/// filesystem ops — no GL window — so this fixture runs in the fast subset with a REAL oracle: it creates a temp directory
/// with known files and asserts the native listing matches. It also exercises the module's FIRST by-value
/// struct-with-pointer-array return (FilePathList) end-to-end: the local mirror walks the returned char** with
/// Marshal.ReadIntPtr + PtrToStringAnsi, which would surface a wrong struct layout or a bad pointer as garbage/AV. The
/// dropped-file half (window state) lives in RaylibCoreC9DroppedFilesTests.
/// </summary>
[TestFixture]
public class RaylibCoreC9DirectoryTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)]
    private struct FilePathList { public uint capacity; public uint count; public IntPtr paths; }

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern FilePathList Framework_LoadDirectoryFiles(string dirPath);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern FilePathList Framework_LoadDirectoryFilesEx(string basePath, string filter, [MarshalAs(UnmanagedType.I1)] bool scanSubdirs);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadDirectoryFiles(FilePathList files);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern int Framework_GetFileModTime(string fileName);

    private static bool Available()
    {
        try { _ = Framework_GetFileModTime(AppContext.BaseDirectory); return true; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    private static void SkipIfUnavailable()
    {
        if (!Available()) Assert.Ignore($"{DLL} not staged / predates rcore C9 exports; refresh IDE\\ first.");
    }

    // Copy the char** paths out of a FilePathList (mirror of the wrapper's MarshalFilePathList).
    private static string[] Paths(FilePathList list)
    {
        if (list.count == 0 || list.paths == IntPtr.Zero) return Array.Empty<string>();
        var res = new string[list.count];
        for (int i = 0; i < list.count; i++)
        {
            IntPtr p = Marshal.ReadIntPtr(list.paths, i * IntPtr.Size);
            res[i] = p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p);
        }
        return res;
    }

    [Test]
    public void LoadDirectoryFiles_lists_the_real_directory_contents()
    {
        SkipIfUnavailable();
        string dir = Path.Combine(Path.GetTempPath(), "vgs_c9_dir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "alpha.txt"), "a");
            File.WriteAllText(Path.Combine(dir, "beta.txt"), "b");
            File.WriteAllText(Path.Combine(dir, "gamma.dat"), "g");

            FilePathList list = Framework_LoadDirectoryFiles(dir);
            try
            {
                var names = Paths(list).Select(Path.GetFileName).ToHashSet();
                Assert.Multiple(() =>
                {
                    Assert.That(list.count, Is.EqualTo(3u), "3 directory entries");
                    Assert.That(list.paths, Is.Not.EqualTo(IntPtr.Zero), "paths char** is non-null");
                    Assert.That(names, Is.SupersetOf(new[] { "alpha.txt", "beta.txt", "gamma.dat" }),
                        "the FilePathList lists exactly the created files (proves the char** marshaling)");
                });
            }
            finally { Framework_UnloadDirectoryFiles(list); }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void LoadDirectoryFilesEx_filters_by_extension()
    {
        SkipIfUnavailable();
        string dir = Path.Combine(Path.GetTempPath(), "vgs_c9_ex_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "keep1.txt"), "1");
            File.WriteAllText(Path.Combine(dir, "keep2.txt"), "2");
            File.WriteAllText(Path.Combine(dir, "skip.dat"), "x");

            FilePathList list = Framework_LoadDirectoryFilesEx(dir, ".txt", false);
            try
            {
                var names = Paths(list).Select(Path.GetFileName).ToHashSet();
                Assert.Multiple(() =>
                {
                    Assert.That(list.count, Is.EqualTo(2u), "only the two .txt files match the filter");
                    Assert.That(names, Is.EquivalentTo(new[] { "keep1.txt", "keep2.txt" }), "extension filter excludes skip.dat");
                });
            }
            finally { Framework_UnloadDirectoryFiles(list); }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void GetFileModTime_matches_the_filesystem_within_tolerance()
    {
        SkipIfUnavailable();
        string file = Path.Combine(Path.GetTempPath(), "vgs_c9_mod_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "x");
        try
        {
            int mod = Framework_GetFileModTime(file);
            long dotnet = ((DateTimeOffset)File.GetLastWriteTimeUtc(file)).ToUnixTimeSeconds();
            Assert.Multiple(() =>
            {
                Assert.That(mod, Is.GreaterThan(0), "mod time is a positive Unix timestamp (proves the 32-bit long marshals)");
                Assert.That(Math.Abs(mod - dotnet), Is.LessThanOrEqualTo(5), "raylib mod time is within 5s of .NET's last-write time");
            });
        }
        finally { File.Delete(file); }
    }
}
