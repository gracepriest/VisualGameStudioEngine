using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 36, spec 12: the IDE drop is a copy of the portable library, not a second source tree.
/// Every file under <c>BasicLang/lib/js/forms</c> and <c>BasicLang/lib/js/dom-core.bli</c> must
/// have a byte-identical copy under <c>IDE/lib/js</c>. This gate is expected to fail between
/// drops, so it runs only after the drop (and at 2b's drop), never in the fast subset.
/// </summary>
[TestFixture]
[Category("Integration")]
public class IdeDropLibraryMatchesSourceTests
{
    [Test]
    public void EveryPortableLibraryFile_IsByteIdenticalInTheIdeDrop()
    {
        if (!OperatingSystem.IsWindows())
        {
            TestSkip.IgnoreEvenInsideMultiple("the IDE drop gate reads the shipped Windows drop");
        }

        var root = FindRepoRoot();
        Assert.That(root, Is.Not.Null, "could not find the repository root from the test output directory");
        if (root == null)
        {
            return;
        }

        var sourceRoot = Path.Combine(root, "BasicLang", "lib", "js");
        var ideRoot = Path.Combine(root, "IDE", "lib", "js");
        var sourceForms = Path.Combine(sourceRoot, "forms");
        var ideForms = Path.Combine(ideRoot, "forms");

        Assert.That(Directory.Exists(sourceForms), Is.True, $"the source library is missing: {sourceForms}");
        Assert.That(Directory.Exists(ideForms), Is.True,
            $"the IDE drop has no portable library: {ideForms}. Run the Task 36 robocopy drop.");

        var sourceFiles = Directory.GetFiles(sourceForms, "*", SearchOption.AllDirectories)
            .Concat(new[] { Path.Combine(sourceRoot, "dom-core.bli") })
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.That(sourceFiles, Is.Not.Empty, "the source portable library has no files");

        var problems = new List<string>();
        foreach (var source in sourceFiles)
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            var ide = Path.Combine(ideRoot, relative);
            if (!File.Exists(ide))
            {
                problems.Add($"missing in the IDE drop: {relative}");
                continue;
            }

            if (!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(ide)))
            {
                problems.Add($"differs from source: {relative}");
            }
        }

        var sourceRelative = sourceFiles
            .Select(f => Path.GetRelativePath(sourceRoot, f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ide in Directory.GetFiles(ideForms, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(ideRoot, ide);
            if (!sourceRelative.Contains(relative))
            {
                problems.Add($"extra file in the IDE drop: {relative}");
            }
        }

        Assert.That(problems, Is.Empty,
            "the IDE drop is not a byte-for-byte copy of the portable library:\n" + string.Join("\n", problems));
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BasicLang", "lib", "js", "dom-core.bli")) &&
                Directory.Exists(Path.Combine(dir.FullName, "BasicLang", "lib", "js", "forms")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
