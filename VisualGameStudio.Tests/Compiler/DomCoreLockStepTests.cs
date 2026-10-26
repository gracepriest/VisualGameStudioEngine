using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 28 — the IDE ships its own copy of <c>dom-core.bli</c>; the two copies are
/// load-bearing and must stay byte-identical. A source-only edit to one of them would make the
/// compiler and the editor disagree about the DOM surface, which is exactly the drift the
/// lock-step test exists to catch.
/// </summary>
[TestFixture]
public class DomCoreLockStepTests
{
    private static string? FindRepoPath(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Test]
    public void BasicLangAndIdeCopies_AreByteIdentical()
    {
        var compilerCopy = FindRepoPath("BasicLang", "lib", "js", "dom-core.bli");
        var ideCopy = FindRepoPath("IDE", "lib", "js", "dom-core.bli");

        Assert.That(compilerCopy, Is.Not.Null, "BasicLang/lib/js/dom-core.bli was not found");
        Assert.That(ideCopy, Is.Not.Null, "IDE/lib/js/dom-core.bli was not found");
        Assert.That(File.ReadAllBytes(ideCopy!), Is.EqualTo(File.ReadAllBytes(compilerCopy!)),
            "the two dom-core.bli copies must be byte-identical");
    }

    [Test]
    public void BothCopies_ContainTheTask28Surface()
    {
        var compilerCopy = FindRepoPath("BasicLang", "lib", "js", "dom-core.bli");
        Assert.That(compilerCopy, Is.Not.Null);
        var text = File.ReadAllText(compilerCopy!);

        foreach (var member in new[]
                 {
                     "getBoundingClientRect", "clientWidth", "offsetHeight", "closest", "contains",
                     "position", "relatedTarget", "getComputedStyle", "styleSheets",
                     "CSSStyleSheet", "CSSMediaRule"
                 })
        {
            Assert.That(text, Does.Contain(member), member);
        }
    }
}
