using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Two <c>Module</c> blocks may each declare a module-scope name; they are separate
/// namespaces, not a redeclaration.
///
/// <para><b>This is the unfinished half of the procedure-local Const fix.</b> That change
/// deliberately left module scope first-wins, reasoning that "at module scope a repeated name
/// is a genuine redeclaration". That reasoning was wrong. <c>module.GlobalVariables</c> is one
/// flat bare-name-keyed dictionary shared by EVERY Module in the combined IR, so the second
/// Module's declaration was dropped outright — and the emitted code still referenced it,
/// producing an identifier declared nowhere.</para>
///
/// <para>Only the KEY needs to change, and only on collision: every consumer but one iterates
/// <c>.Values</c>, and the C# backend already groups those by <c>ModuleName</c> into per-module
/// static classes — so once both survive, a bare reference inside <c>Beta</c> resolves to
/// <c>Beta</c>'s copy by ordinary lexical scoping.</para>
/// </summary>
[TestFixture]
public class ModuleGlobalCollisionTests
{
    private static IRModule Build(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            string.Join(" | ", parser.Errors.Select(e => e.ToString())));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join(" | ", analyzer.Errors.Select(e => e.Message)));

        return new IRBuilder(analyzer).Build(ast, "T");
    }

    private static int CountGlobalsNamed(IRModule m, string name) =>
        m.GlobalVariables.Values.Count(v => v.Name == name);

    [Test]
    public void TwoModules_EachWithTheSameConstName_BothSurvive()
    {
        var m = Build(
            "Module Alpha\nConst Scale As Integer = 1\nEnd Module\n" +
            "Module Beta\nConst Scale As Integer = 2\nEnd Module\n" +
            "Sub Main()\nEnd Sub");

        Assert.That(CountGlobalsNamed(m, "Scale"), Is.EqualTo(2),
            "each Module owns its own constant; one was being dropped");
        Assert.That(m.GlobalVariables.Values.Where(v => v.Name == "Scale")
            .Select(v => v.ModuleName).Distinct().Count(), Is.EqualTo(2),
            "and they must be attributed to different Modules");
    }

    /// <summary>
    /// The type is what makes the loss dangerous on the JavaScript backend: a dropped
    /// declaration cannot draw BL7003, so a banned 64-bit constant reaches codegen unchecked.
    /// </summary>
    [Test]
    public void TwoModules_TheSecondConstKeepsItsOwnType()
    {
        var m = Build(
            "Module Alpha\nConst Scale As Integer = 1\nEnd Module\n" +
            "Module Beta\nConst Scale As Long = 2\nEnd Module\n" +
            "Sub Main()\nEnd Sub");

        var types = m.GlobalVariables.Values.Where(v => v.Name == "Scale")
            .Select(v => v.Type?.Name).OrderBy(x => x).ToList();

        Assert.That(types, Is.EqualTo(new[] { "Integer", "Long" }));
    }

    /// <summary>A module-level Dim is a masker too — the collision is not Const-vs-Const.</summary>
    [TestCase("Module Alpha\nDim Scale As Integer\nEnd Module\n" +
              "Module Beta\nConst Scale As Integer = 2\nEnd Module\nSub Main()\nEnd Sub",
        TestName = "DimThenConst")]
    [TestCase("Module Alpha\nConst Scale As Integer = 1\nEnd Module\n" +
              "Module Beta\nDim Scale As Integer\nEnd Module\nSub Main()\nEnd Sub",
        TestName = "ConstThenDim")]
    [TestCase("Const Scale As Integer = 1\n" +
              "Module Beta\nConst Scale As Integer = 2\nEnd Module\nSub Main()\nEnd Sub",
        TestName = "FileScopeThenModule")]
    public void MixedModuleGlobals_WithTheSameName_BothSurvive(string source)
    {
        Assert.That(CountGlobalsNamed(Build(source), "Scale"), Is.EqualTo(2));
    }

    [Test]
    public void ThreeModules_TheLastIsStillKept()
    {
        var m = Build(
            "Module Alpha\nConst Scale As Integer = 1\nEnd Module\n" +
            "Module Beta\nConst Scale As String = \"two\"\nEnd Module\n" +
            "Module Gamma\nConst Scale As Integer = 3\nEnd Module\n" +
            "Sub Main()\nEnd Sub");

        Assert.That(CountGlobalsNamed(m, "Scale"), Is.EqualTo(3));
    }

    /// <summary>
    /// The worst form, end to end: the masked constant is USED. Before the fix the emitted C#
    /// declared only Alpha's copy while Beta.Report() still referenced the name — an
    /// identifier declared nowhere, from a build that reported success.
    /// </summary>
    [Test]
    public void MaskedConstantThatIsUsed_IsDeclaredInTheOutput()
    {
        var m = Build(
            "Module Alpha\nConst Scale As Integer = 1\nEnd Module\n" +
            "Module Beta\nConst Scale As Integer = 42\n" +
            "Sub Report()\nConsole.WriteLine(Scale)\nEnd Sub\nEnd Module\n" +
            "Sub Main()\nReport()\nEnd Sub");

        var cs = new CSharpCodeGenerator().Generate(m);

        Assert.That(cs, Does.Contain("42"),
            "Beta's constant was dropped, so Beta.Report() referenced an undeclared name");
        Assert.That(cs, Does.Contain("= 1"));
    }

    /// <summary>
    /// The MULTI-FILE half. <c>CombineIRModules</c> had its own first-wins guard, and it is
    /// reached only through a real multi-file compile — the single-file tests above cannot
    /// exercise it, because each unit keys its own globals by bare name and the collision only
    /// happens when two units are merged.
    /// </summary>
    [Test]
    public void TwoFiles_EachWithTheSameModuleConstName_BothSurvive()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "BasicLang_GlobalCollide_" + System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var a = System.IO.Path.Combine(dir, "alpha.bas");
            var b = System.IO.Path.Combine(dir, "beta.bas");
            System.IO.File.WriteAllText(a,
                "Module Alpha\nConst Scale As Integer = 1\nEnd Module\nSub Main()\nEnd Sub\n");
            System.IO.File.WriteAllText(b,
                "Module Beta\nConst Scale As Integer = 2\nEnd Module\n");

            var result = new BasicCompiler().CompileProjectFiles(new[] { a, b });

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null);
            Assert.That(result.CombinedIR!.GlobalVariables.Values.Count(v => v.Name == "Scale"),
                Is.EqualTo(2),
                "CombineIRModules dropped one file's constant — the same first-wins defect, " +
                "one layer up");
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    // ---------------------------------------------------------------- controls

    [Test]
    public void SingleModuleConst_IsUnchanged()
    {
        var m = Build("Module M\nConst Limit As Integer = 5\nSub Main()\nEnd Sub\nEnd Module");

        Assert.That(m.GlobalVariables.ContainsKey("Limit"), Is.True,
            "with no collision the key stays bare — one consumer looks globals up by name");
        Assert.That(m.GlobalVariables["Limit"].IsConst, Is.True);
    }

    /// <summary>Procedure-local Consts must stay local; that fix is not undone here.</summary>
    [Test]
    public void ProcedureLocalConst_IsStillNotAGlobal()
    {
        var m = Build(
            "Module M\nSub First()\nConst S As Integer = 1\nEnd Sub\n" +
            "Sub Second()\nConst S As Integer = 99\nEnd Sub\nSub Main()\nEnd Sub\nEnd Module");

        Assert.That(m.GlobalVariables.Values.Any(v => v.Name == "S"), Is.False);
        Assert.That(m.Functions.Single(f => f.Name == "Second")
            .LocalVariables.Any(v => v.Name == "S" && v.IsConst), Is.True);
    }
}
