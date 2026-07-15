using NUnit.Framework;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class LanguageFileTypesTests
{
    [TestCase("a.bas", "basiclang")]
    [TestCase("a.bl", "basiclang")]
    [TestCase("a.mod", "basiclang")]     // regression: used to yield "mod"
    [TestCase("a.cls", "basiclang")]     // regression: used to yield "cls"
    [TestCase("a.class", "basiclang")]
    [TestCase("a.cpp", "cpp")]
    [TestCase("a.cc", "cpp")]
    [TestCase("a.cxx", "cpp")]
    [TestCase("a.hpp", "cpp")]
    [TestCase("a.hh", "cpp")]            // regression: was omitted
    [TestCase("a.hxx", "cpp")]           // regression: was omitted
    [TestCase("a.inl", "cpp")]           // regression: only HighlightingLoader knew this one
    [TestCase("A.CPP", "cpp")]           // case-insensitive
    public void GetLanguageId_MapsKnownExtensions(string path, string expected)
        => Assert.That(LanguageFileTypes.GetLanguageId(path), Is.EqualTo(expected));

    // `.h` is C++ here BY DECISION (spec §4 assigns .h to clangd). Pin it so nobody "fixes" it to "c".
    [Test]
    public void GetLanguageId_DotH_IsCpp_ByDecision()
        => Assert.That(LanguageFileTypes.GetLanguageId("a.h"), Is.EqualTo("cpp"));

    // `.c` is intentionally NOT in the LSP map for Phase 3a — keep it minimal and honest.
    [Test]
    public void GetLanguageId_DotC_IsNotRouted_InPhase3a()
        => Assert.That(LanguageFileTypes.GetLanguageId("a.c"), Is.Null);

    // The LSP map is nullable ON PURPOSE: null means "no language server owns this file".
    [TestCase("a.txt")]
    [TestCase("a.json")]
    [TestCase("noextension")]
    [TestCase("")]
    [TestCase(null)]
    public void GetLanguageId_UnknownOrEmpty_ReturnsNull(string? path)
        => Assert.That(LanguageFileTypes.GetLanguageId(path), Is.Null);

    // ---- The EXTENSION-HOST map is a DIFFERENT, TOTAL function. ----

    [TestCase("a.py", "python")]
    [TestCase("a.ts", "typescript")]
    [TestCase("a.json", "json")]
    [TestCase("a.md", "markdown")]
    [TestCase("a.cs", "csharp")]
    public void GetEditorLanguageId_KeepsTheThirtyLanguageMap(string path, string expected)
        => Assert.That(LanguageFileTypes.GetEditorLanguageId(path), Is.EqualTo(expected));

    // THE regression guard. ExtensionService.HasExtensionProviders does ContainsKey(languageId)
    // and throws ArgumentNullException on null. This must NEVER return null.
    [TestCase("a.txt")]
    [TestCase("a.wildlyunknown")]
    [TestCase("noextension")]
    [TestCase("")]
    [TestCase(null)]
    public void GetEditorLanguageId_IsTotal_NeverReturnsNull(string? path)
        => Assert.That(LanguageFileTypes.GetEditorLanguageId(path), Is.Not.Null);

    // The drift fix: these used to yield the junk ids "mod"/"cls" via `_ => ext.TrimStart('.')`.
    [TestCase("a.mod")]
    [TestCase("a.cls")]
    [TestCase("a.class")]
    public void GetEditorLanguageId_BasicLangExtensions_AreBasicLang_NotJunk(string path)
        => Assert.That(LanguageFileTypes.GetEditorLanguageId(path), Is.EqualTo("basiclang"));

    // The C++ extensions the editor map used to omit.
    [TestCase("a.hh")]
    [TestCase("a.hxx")]
    [TestCase("a.inl")]
    public void GetEditorLanguageId_MissingCppExtensions_AreCpp(string path)
        => Assert.That(LanguageFileTypes.GetEditorLanguageId(path), Is.EqualTo("cpp"));

    // Unchanged verbatim behavior from the original ~30-language map: the editor map
    // still distinguishes .c as "c", and still knows .blproj.
    [TestCase("a.c", "c")]
    [TestCase("a.blproj", "basiclang")]
    [TestCase("a.unknownext", "unknownext")]
    public void GetEditorLanguageId_PreservesOriginalBehavior(string path, string expected)
        => Assert.That(LanguageFileTypes.GetEditorLanguageId(path), Is.EqualTo(expected));

    // Where both maps DO agree, they must not drift apart again.
    [TestCase("a.bas")]
    [TestCase("a.cpp")]
    [TestCase("a.h")]
    public void TheTwoMaps_AgreeOnLanguagesTheyBothKnow(string path)
        => Assert.That(LanguageFileTypes.GetEditorLanguageId(path),
                       Is.EqualTo(LanguageFileTypes.GetLanguageId(path)));

    // Every extension the LSP map routes must get the same id from the editor map.
    [Test]
    public void TheTwoMaps_NeverDisagreeOnAnyRoutedExtension()
    {
        foreach (var ext in LanguageFileTypes.AllKnownExtensions)
        {
            var path = "f" + ext;
            Assert.That(LanguageFileTypes.GetEditorLanguageId(path),
                Is.EqualTo(LanguageFileTypes.GetLanguageId(path)),
                $"maps disagree on {ext}");
        }
    }

    [Test]
    public void IsCppSourceFile_And_IsBasicLangSourceFile_AreDisjoint()
    {
        foreach (var ext in LanguageFileTypes.AllKnownExtensions)
        {
            var p = "f" + ext;
            Assert.That(LanguageFileTypes.IsCppSourceFile(p) && BasicLangFileTypes.IsBasicLangSourceFile(p),
                Is.False, $"{ext} claimed by both languages");
        }
    }

    // AllKnownExtensions is the disjointness/agreement surface — it must actually
    // cover both languages, or the loops above pass vacuously.
    [Test]
    public void AllKnownExtensions_CoversBothLanguages()
    {
        Assert.That(LanguageFileTypes.AllKnownExtensions, Does.Contain(".bas"));
        Assert.That(LanguageFileTypes.AllKnownExtensions, Does.Contain(".cpp"));
        Assert.That(LanguageFileTypes.AllKnownExtensions, Is.All.StartWith("."));
    }

    [TestCase("a.cpp", true)]
    [TestCase("a.h", true)]
    [TestCase("a.inl", true)]
    [TestCase("A.HPP", true)]
    [TestCase("a.bas", false)]
    [TestCase("a.txt", false)]
    [TestCase("noextension", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsCppSourceFile_MatchesTheCppArmOfTheLspMap(string? path, bool expected)
        => Assert.That(LanguageFileTypes.IsCppSourceFile(path), Is.EqualTo(expected));

    // BasicLangFileTypes must keep answering exactly as before — ~26 call sites depend on it.
    [TestCase("a.bas", true)]
    [TestCase("a.cpp", false)]
    [TestCase("a.txt", false)]
    public void BasicLangFileTypes_BehaviorUnchanged(string path, bool expected)
        => Assert.That(BasicLangFileTypes.IsBasicLangSourceFile(path), Is.EqualTo(expected));
}
