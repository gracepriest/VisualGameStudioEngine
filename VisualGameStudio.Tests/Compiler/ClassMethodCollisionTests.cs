using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Two classes may each declare a method with the same name.
///
/// <para><b>The defect.</b> Class methods flatten into <c>module.Functions</c> with
/// UNQUALIFIED names, and <c>CombineIRModules</c> deduped that list first-wins BY NAME. So
/// <c>Class B</c>'s <c>Handle</c> was discarded outright — body, parameters and all — before
/// any backend ran. <c>Handle</c> is not a contrived name; Update, Draw, Run and ToString
/// collide identically.</para>
///
/// <para><b>Why it hid.</b> The C# backend emits class methods from
/// <c>IRClass.Methods[].Implementation</c>, which still points at the dropped IRFunction
/// object, so its output looked correct. The JavaScript backend walks
/// <c>module.Functions</c>, and there the method simply vanished. Measured: C# emitted both
/// bodies, JS emitted only A's.</para>
/// </summary>
[TestFixture]
public class ClassMethodCollisionTests
{
    private const string TwoClasses =
        "Class A\nPublic Sub Handle()\nConsole.WriteLine(\"from A\")\nEnd Sub\nEnd Class\n" +
        "Class B\nPublic Sub Handle()\nConsole.WriteLine(\"from B\")\nEnd Sub\nEnd Class\n" +
        "Sub Main()\nConsole.WriteLine(\"main\")\nEnd Sub";

    private static IRModule CombineViaCompiler(string source)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "BasicLang_MethodCollide_" + System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var file = System.IO.Path.Combine(dir, "prog.bas");
            System.IO.File.WriteAllText(file, source);

            var result = new BasicCompiler().CompileProjectFiles(new[] { file });
            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null);
            return result.CombinedIR!;
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// Goes through the real compiler, not IRBuilder directly: IRBuilder alone keeps both
    /// (it appends to a List), and the loss happens later in CombineIRModules. A test built
    /// on the IRBuilder-only helper would pass while the product stayed broken.
    /// </summary>
    [Test]
    public void TwoClasses_WithTheSameMethodName_BothImplementationsSurvive()
    {
        var module = CombineViaCompiler(TwoClasses);

        Assert.That(module.Functions.Count(f => f.Name == "Handle"), Is.EqualTo(2),
            "Class B's Handle was discarded by the first-wins name dedupe");
    }

    /// <summary>Each class must still reach its OWN implementation, not share one.</summary>
    [Test]
    public void EachClass_KeepsItsOwnImplementation()
    {
        var module = CombineViaCompiler(TwoClasses);

        var impls = module.Classes.Values
            .SelectMany(c => c.Methods)
            .Where(m => m.Name == "Handle")
            .Select(m => m.Implementation)
            .ToList();

        Assert.That(impls, Has.Count.EqualTo(2));
        Assert.That(impls[0], Is.Not.SameAs(impls[1]),
            "both classes pointed at a single shared IRFunction");
        Assert.That(impls.All(i => i != null), Is.True);
    }

    [Test]
    public void CSharpOutput_ContainsBothBodies()
    {
        var cs = new CSharpCodeGenerator().Generate(CombineViaCompiler(TwoClasses));

        Assert.That(cs, Does.Contain("from A"));
        Assert.That(cs, Does.Contain("from B"));
    }

    /// <summary>
    /// The JavaScript generator walks module.Functions, so preserving both implementations
    /// would make it emit two top-level `function Handle()` declarations — the second
    /// silently winning. Class emission is plan task 17; until then a class-member
    /// implementation must be SKIPPED by the top-level walk rather than emitted as a free
    /// function under a name that collides.
    /// </summary>
    [Test]
    public void JavaScriptOutput_DoesNotEmitClassMethodsAsCollidingFreeFunctions()
    {
        var js = new JavaScriptCodeGenerator().Generate(CombineViaCompiler(TwoClasses));

        Assert.That(System.Text.RegularExpressions.Regex.Matches(js, @"function Handle\(").Count,
            Is.LessThanOrEqualTo(1),
            "two same-named top-level functions: the second definition silently wins");
        Assert.That(js, Does.Contain("function Main("));
    }

    // ---------------------------------------------------------------- controls

    /// <summary>
    /// A single class is unaffected, and its method still reaches the IR exactly once.
    /// </summary>
    [Test]
    public void SingleClassMethod_IsUnchanged()
    {
        var module = CombineViaCompiler(
            "Class A\nPublic Sub Handle()\nEnd Sub\nEnd Class\nSub Main()\nEnd Sub");

        Assert.That(module.Functions.Count(f => f.Name == "Handle"), Is.EqualTo(1));
    }

    /// <summary>
    /// Free functions keep their existing dedupe. Only class MEMBERS are exempt — they are
    /// the ones whose names are unqualified by construction.
    /// </summary>
    [Test]
    public void FreeFunctions_AreStillDeduped()
    {
        var module = CombineViaCompiler(
            "Sub Helper()\nEnd Sub\nSub Main()\nHelper()\nEnd Sub");

        Assert.That(module.Functions.Count(f => f.Name == "Helper"), Is.EqualTo(1));
    }
}
