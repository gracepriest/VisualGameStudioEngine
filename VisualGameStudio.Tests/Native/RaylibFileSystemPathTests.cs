using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for rcore Batch core-C8 (raw raylib file-system path queries). Pure path/existence helpers — no
/// window, no GL, no device — so this fixture runs FULLY HEADLESS (no [Category("Integration")]) in the fast subset,
/// cross-checked against .NET's own filesystem view. The 7 const char* returns come back as IntPtr and are read via
/// PtrToStringAnsi (they point at raylib's OWN rotating STATIC buffer — copy immediately, never free); bool returns are
/// I1; the path-splitters are compared with separators normalized (raylib may return '\' on Windows).
///
/// ⚠ ChangeDirectory mutates the PROCESS working directory (global state), so that one test is [NonParallelizable] and
/// restores the original cwd in a finally — it must never run alongside other file-relative tests.
///
/// Self-contained local [DllImport]; self-skips (whole fixture) when the engine DLL isn't staged with the C8 exports.
/// </summary>
[TestFixture]
public class RaylibFileSystemPathTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_FileExists(string fileName);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_DirectoryExists(string dirPath);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsFileExtension(string fileName, string ext);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern int Framework_GetFileLength(string fileName);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_GetFileExtension(string fileName);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_GetFileName(string filePath);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_GetFileNameWithoutExt(string filePath);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_GetDirectoryPath(string filePath);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_GetPrevDirectoryPath(string dirPath);
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_GetWorkingDirectory();
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_GetApplicationDirectory();
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern int Framework_MakeDirectory(string dirPath);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_ChangeDirectory(string dir);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsPathFile(string path);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsFileNameValid(string fileName);

    private static string Str(IntPtr p) => Marshal.PtrToStringAnsi(p) ?? "";
    private static string Norm(string s) => s.Replace('\\', '/').TrimEnd('/');

    [OneTimeSetUp]
    public void ProbeEngine()
    {
        try { Framework_GetWorkingDirectory(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C8 exports; refresh IDE\\ first."); }
    }

    [Test]
    public void FileExists_DirectoryExists_IsPathFile_reflect_the_filesystem()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vgs_c8_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "probe.dat");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
        string missing = Path.Combine(dir, "does_not_exist.dat");
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(Framework_FileExists(file), Is.True, "existing file");
                Assert.That(Framework_FileExists(missing), Is.False, "missing file");
                Assert.That(Framework_DirectoryExists(dir), Is.True, "existing directory");
                Assert.That(Framework_DirectoryExists(missing), Is.False, "missing directory");
                Assert.That(Framework_IsPathFile(file), Is.True, "a file is a file");
                Assert.That(Framework_IsPathFile(dir), Is.False, "a directory is not a file");
            });
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public void GetFileLength_reports_the_byte_count()
    {
        string file = Path.Combine(Path.GetTempPath(), "vgs_c8_" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(file, new byte[1234]);
        try { Assert.That(Framework_GetFileLength(file), Is.EqualTo(1234)); }
        finally { File.Delete(file); }
    }

    [Test]
    public void Path_component_getters_split_a_path()
    {
        const string p = "resources/images/wabbit.png";
        Assert.Multiple(() =>
        {
            Assert.That(Str(Framework_GetFileName(p)), Is.EqualTo("wabbit.png"));
            Assert.That(Str(Framework_GetFileNameWithoutExt(p)), Is.EqualTo("wabbit"));
            Assert.That(Str(Framework_GetFileExtension(p)), Is.EqualTo(".png"));
            // raylib prepends "./" to a relative path with no drive/root — faithful passthrough.
            Assert.That(Norm(Str(Framework_GetDirectoryPath(p))), Is.EqualTo("./resources/images"));
            Assert.That(Norm(Str(Framework_GetPrevDirectoryPath("resources/images"))), Is.EqualTo("resources"));
            Assert.That(Framework_IsFileExtension(p, ".png"), Is.True, "matching extension");
            Assert.That(Framework_IsFileExtension(p, ".jpg"), Is.False, "non-matching extension");
            // No-dot input: raylib returns NULL -> the forwarder preserves nullptr -> PtrToStringAnsi(Zero) -> null ->
            // Str() maps to "". Exercises the GetFileExtension NULL-preservation branch of the engine-side fix.
            Assert.That(Str(Framework_GetFileExtension("resources/images/README")), Is.EqualTo(""), "no extension -> empty");
            Assert.That(Framework_IsFileNameValid("wabbit.png"), Is.True, "a plain name is valid");
            Assert.That(Framework_IsFileNameValid("bad<name>?.txt"), Is.False, "invalid characters -> not a valid filename");
        });
    }

    [Test]
    public void GetWorkingDirectory_and_GetApplicationDirectory_return_existing_dirs()
    {
        string cwd = Str(Framework_GetWorkingDirectory());
        string appDir = Str(Framework_GetApplicationDirectory());
        Assert.Multiple(() =>
        {
            Assert.That(cwd, Is.Not.Empty, "working directory is non-empty");
            Assert.That(Directory.Exists(cwd), Is.True, "working directory exists (.NET cross-check)");
            Assert.That(appDir, Is.Not.Empty, "application directory is non-empty");
            Assert.That(Directory.Exists(appDir), Is.True, "application directory exists (.NET cross-check)");
        });
    }

    [Test]
    public void MakeDirectory_creates_a_new_directory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vgs_c8_" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.That(Framework_MakeDirectory(dir), Is.EqualTo(0), "MakeDirectory returns 0 on success");
            Assert.That(Framework_DirectoryExists(dir), Is.True, "the directory now exists");
            Assert.That(Directory.Exists(dir), Is.True, ".NET sees the new directory too");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Test]
    [NonParallelizable] // mutates the process working directory — must run in isolation
    public void ChangeDirectory_moves_the_process_cwd_then_restores()
    {
        string target = Path.Combine(Path.GetTempPath(), "vgs_c8_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        string original = Str(Framework_GetWorkingDirectory());
        try
        {
            Assert.That(Framework_ChangeDirectory(target), Is.True, "ChangeDirectory reports success");
            string now = Str(Framework_GetWorkingDirectory());
            Assert.That(Norm(now), Is.Not.EqualTo(Norm(original)), "cwd actually changed");
            Assert.That(Norm(Path.GetFullPath(now)), Is.EqualTo(Norm(Path.GetFullPath(target))).IgnoreCase, "cwd is now the target");
        }
        finally
        {
            Framework_ChangeDirectory(original); // restore before any other file-relative test runs
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }
}
