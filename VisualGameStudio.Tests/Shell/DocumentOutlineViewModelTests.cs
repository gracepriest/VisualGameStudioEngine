using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// The document outline's text-parser fallback (<see cref="DocumentOutlineViewModel.UpdateOutline"/>)
/// is BasicLang-specific: it keys on <c>Module</c>/<c>Class</c>/<c>Sub</c>/… at line start
/// (case-insensitively) and treats only <c>'</c>/<c>REM</c> as comments. Run on a C++ file it
/// produces a wrong, partial outline — <c>class Foo {</c> becomes a bogus "Foo" node and <c>//</c>
/// comments are parsed as code.
///
/// <para>
/// Task 7 routes <c>.cpp</c> to clangd's <c>documentSymbol</c> and restricts the text fallback to
/// BasicLang via <see cref="DocumentOutlineViewModel.UpdateOutlineFromTextFallback"/>. These pin
/// that seam: the fallback yields nothing for non-BasicLang files but still parses BasicLang.
/// </para>
/// </summary>
[TestFixture]
public class DocumentOutlineViewModelTests
{
    // The live bug: `class Foo {` in a .cpp file previously produced a BasicLang-parsed "Foo" node.
    [Test]
    public void UpdateOutlineFromTextFallback_CppFile_ProducesNoNodes()
    {
        var vm = new DocumentOutlineViewModel();

        vm.UpdateOutlineFromTextFallback(@"C:\proj\Game.cpp", "class Foo {\npublic:\n    void Bar();\n};\n");

        Assert.That(vm.Nodes, Is.Empty,
            "the BasicLang text parser must not run on a .cpp file — `class Foo {` must not become an outline node");
    }

    // A header file is C++ too (routed to clangd), so the BasicLang fallback must not fire.
    [Test]
    public void UpdateOutlineFromTextFallback_HeaderFile_ProducesNoNodes()
    {
        var vm = new DocumentOutlineViewModel();

        vm.UpdateOutlineFromTextFallback(@"C:\proj\Game.h", "class Widget {\n};\n");

        Assert.That(vm.Nodes, Is.Empty);
    }

    // Switching from a .bas (with an outline) to a .cpp must clear the stale BasicLang outline,
    // not leave it behind.
    [Test]
    public void UpdateOutlineFromTextFallback_CppAfterBasicLang_ClearsTheStaleOutline()
    {
        var vm = new DocumentOutlineViewModel();
        vm.UpdateOutlineFromTextFallback(@"C:\proj\Game.bas", "Class Player\nEnd Class\n");
        Assert.That(vm.Nodes, Is.Not.Empty, "precondition: the .bas file produced an outline");

        vm.UpdateOutlineFromTextFallback(@"C:\proj\Game.cpp", "class Foo {\n};\n");

        Assert.That(vm.Nodes, Is.Empty, "the stale BasicLang outline must be cleared for the .cpp file");
    }

    // Positive control / mutation-catcher: the fallback still parses BasicLang. Without this, a
    // mutation that made the method Clear() unconditionally would pass the .cpp tests above.
    [Test]
    public void UpdateOutlineFromTextFallback_BasicLangFile_StillParsesTheOutline()
    {
        var vm = new DocumentOutlineViewModel();

        vm.UpdateOutlineFromTextFallback(@"C:\proj\Game.bas", "Class Player\nEnd Class\n");

        Assert.That(vm.Nodes, Has.Count.EqualTo(1), "a .bas file must still get its BasicLang-parsed outline");
        Assert.That(vm.Nodes[0].Name, Is.EqualTo("Player"));
        Assert.That(vm.Nodes[0].NodeType, Is.EqualTo(OutlineNodeType.Class));
    }
}
