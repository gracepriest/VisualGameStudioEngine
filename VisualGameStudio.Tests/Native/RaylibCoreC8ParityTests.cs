using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for rcore Batch core-C8 (raw raylib file-system path-query passthroughs). Pure text
/// scan — no engine load. None of the 15 names collide with existing exports or convenience helpers, so all bind
/// unsuffixed with raylib's exact spelling. The trailing '(' anchor keeps near-name boundaries apart
/// (Framework_GetFileName( != Framework_GetFileNameWithoutExt(; Framework_GetDirectoryPath( != Framework_GetPrevDirectoryPath().
/// </summary>
[TestFixture]
public class RaylibCoreC8ParityTests
{
    // All 15 raw path-query names bind unsuffixed (import name == export name) — no collision.
    private static readonly string[] PathNames =
    {
        "FileExists", "DirectoryExists", "IsFileExtension", "GetFileLength", "GetFileExtension",
        "GetFileName", "GetFileNameWithoutExt", "GetDirectoryPath", "GetPrevDirectoryPath",
        "GetWorkingDirectory", "GetApplicationDirectory", "MakeDirectory", "ChangeDirectory",
        "IsPathFile", "IsFileNameValid",
    };

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public void Every_core_C8_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in PathNames)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(PathNames.Length, Is.EqualTo(15));
        });
    }
}
