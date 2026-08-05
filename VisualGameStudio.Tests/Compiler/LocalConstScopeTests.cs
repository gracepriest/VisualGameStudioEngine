using System;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A procedure-local <c>Const</c> must be scoped to its procedure.
///
/// <para><b>The bug this pins.</b> <c>Visit(ConstantDeclarationNode)</c> hoisted EVERY
/// constant — module-scope or procedure-local — into the one flat, unqualified,
/// name-keyed <c>module.GlobalVariables</c> dictionary, and the write was FIRST-WINS. Two
/// procedures each declaring <c>Const Scale</c> therefore collapsed into a single constant
/// carrying the first one's value, and the second procedure silently read the wrong
/// number. The build succeeded and the emitted C# compiled — it just computed the wrong
/// answer, which is the worst failure shape available.</para>
///
/// <para>Note the asymmetry that hid it: <c>CreateGlobalVariable</c> is LAST-wins, so a
/// colliding <c>Dim</c> overwrites and is at least visible; only <c>Const</c> vanished.</para>
/// </summary>
[TestFixture]
public class LocalConstScopeTests
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

    private const string Colliding =
        "Module M\n" +
        "Sub First()\n" +
        "Const Scale As Integer = 1\n" +
        "Console.WriteLine(Scale)\n" +
        "End Sub\n" +
        "Sub Second()\n" +
        "Const Scale As Integer = 99\n" +
        "Console.WriteLine(Scale)\n" +
        "End Sub\n" +
        "Sub Main()\n" +
        "First()\n" +
        "Second()\n" +
        "End Sub\n" +
        "End Module";

    /// <summary>
    /// The headline: both values must survive to the output. Before the fix the emitted C#
    /// contained a single `private const int Scale = 1;` and NO 99 anywhere, so Second()
    /// printed 1.
    /// </summary>
    [Test]
    public void LocalConsts_WithTheSameName_KeepTheirOwnValues()
    {
        var cs = new CSharpCodeGenerator().Generate(Build(Colliding));

        Assert.That(cs, Does.Contain("99"),
            "the second procedure's constant was dropped — it silently read the first's value");
        Assert.That(cs, Does.Contain("1"));
    }

    /// <summary>
    /// A procedure-local Const must not leak into module scope at all. This is the root
    /// cause rather than a symptom: as long as locals are hoisted into one flat name-keyed
    /// dictionary, any two procedures sharing a constant name collide.
    /// </summary>
    [Test]
    public void LocalConst_DoesNotBecomeAModuleGlobal()
    {
        var module = Build(Colliding);

        Assert.That(module.GlobalVariables.ContainsKey("Scale"), Is.False,
            "a Const declared inside a Sub is local to that Sub, not a module global");
    }

    [Test]
    public void LocalConst_IsRegisteredAsALocalOfItsOwnFunction()
    {
        var module = Build(Colliding);

        foreach (var fn in new[] { "First", "Second" })
        {
            var f = module.Functions.SingleOrDefault(x => x.Name == fn);
            Assert.That(f, Is.Not.Null, $"{fn} must exist");
            Assert.That(f!.LocalVariables.Any(v => v.Name == "Scale" && v.IsConst), Is.True,
                $"{fn} must own its own 'Scale' constant");
        }
    }

    /// <summary>A module-scope Const is genuinely module-wide and must stay a global.</summary>
    [Test]
    public void ModuleScopeConst_StillBecomesAGlobal()
    {
        var module = Build(
            "Module M\n" +
            "Const Limit As Integer = 5\n" +
            "Sub Main()\n" +
            "Console.WriteLine(Limit)\n" +
            "End Sub\n" +
            "End Module");

        Assert.That(module.GlobalVariables.ContainsKey("Limit"), Is.True);
        Assert.That(module.GlobalVariables["Limit"].IsConst, Is.True);
    }

    /// <summary>
    /// A local Const must not be shadowed by, or shadow, a module-scope name. Before the
    /// fix the module-scope Dim won and the local Const was discarded entirely.
    /// </summary>
    [Test]
    public void LocalConst_DoesNotCollideWithAModuleScopeDim()
    {
        var module = Build(
            "Module M\n" +
            "Dim Scale As Integer\n" +
            "Sub Second()\n" +
            "Const Scale As Integer = 42\n" +
            "Console.WriteLine(Scale)\n" +
            "End Sub\n" +
            "Sub Main()\n" +
            "Second()\n" +
            "End Sub\n" +
            "End Module");

        var second = module.Functions.Single(f => f.Name == "Second");
        Assert.That(second.LocalVariables.Any(v => v.Name == "Scale" && v.IsConst), Is.True,
            "the local constant must survive alongside a module-scope variable of the same name");
    }
}
