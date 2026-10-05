using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.7, M12 (portable-controls Task 9) — <c>Dim k As Shade = Shade.Dark</c> was "Cannot assign value of type
/// 'Object' to variable of type 'Shade'" on JavaScript AND C#: the member access found no member on the Enum's
/// TypeInfo (Members was never populated) and fell to the PascalCase ".NET type" fallback, typed Object. The
/// library's Keys, MouseButtons, DockStyle, AnchorStyles, BorderStyle, ContentAlignment, DialogResult and
/// MessageBoxButtons depend on this.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class EnumMemberTypingTests
{
    /// <summary>Every shipping backend, through the optimizer too (CLAUDE.md: validate through the optimizer).</summary>
    private static void OnEveryBackend(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(program)), Is.EqualTo(expected), "JavaScript (optimized)");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
        });
        // Outside Assert.Multiple: CompileRun IGNORES when there is no C++ compiler (CrossFileBindingTests' rule).
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++ (optimized)");
    }

    private const string Decl = "Enum Shade\n Light\n Dark\n Keyed = 65\nEnd Enum\n";

    [Test]
    public void AssignedToADeclaredLocal() => OnEveryBackend(Decl +
        "Sub Main()\n Dim k As Shade = Shade.Dark\n Console.WriteLine(CInt(k))\n Dim j As Shade\n j = Shade.Keyed\n Console.WriteLine(CInt(j))\nEnd Sub", "1\n65");

    [Test]
    public void ComparedAndSelected() => OnEveryBackend(Decl +
        "Sub Main()\n Dim k As Shade = Shade.Light\n If k = Shade.Light Then Console.WriteLine(\"light\")\n" +
        " Select Case k\n  Case Shade.Dark\n   Console.WriteLine(\"dark\")\n  Case Shade.Light\n   Console.WriteLine(\"case light\")\n End Select\nEnd Sub",
        "light\ncase light");

    [Test]
    public void PassedAsAnArgument() => OnEveryBackend(Decl +
        "Sub Show(s As Shade)\n Console.WriteLine(CInt(s))\nEnd Sub\nSub Main()\n Show(Shade.Dark)\nEnd Sub", "1");

    [Test]
    public void InsideAModule() => OnEveryBackend(
        "Module M\n" + Decl + " Sub Main()\n  Dim k As Shade = Shade.Dark\n  Console.WriteLine(CInt(k))\n End Sub\nEnd Module", "1");

    [Test]
    public void DeclaredBelowItsUse() => OnEveryBackend(
        "Sub Main()\n Dim k As Shade = Shade.Dark\n Console.WriteLine(CInt(k))\nEnd Sub\n" + Decl, "1");

    [Test]
    public void DeclaredInASiblingFile()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-enum-" + Path.GetRandomFileName())).FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Shade.bas"), Decl);
            File.WriteAllText(Path.Combine(dir, "Main.bas"), "Module Program\n Sub Main()\n  Dim k As Shade = Shade.Dark\n  PrintLine(CInt(k))\n End Sub\nEnd Module\n");
            foreach (var order in new[] { new[] { "Shade.bas", "Main.bas" }, new[] { "Main.bas", "Shade.bas" } })
            {
                var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" })
                    .CompileProjectFiles(order.Select(f => Path.Combine(dir, f)).ToArray());
                Assert.That(r.HasErrors, Is.False, $"[{string.Join(",", order)}] " + string.Join(" | ", r.AllErrors.Select(e => e.Message)));
                Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(new JavaScriptCodeGenerator().Generate(r.CombinedIR!))),
                    Is.EqualTo("1"), string.Join(",", order));
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>A name that is not a member of the Enum is still refused — the member table must not make it permissive.</summary>
    [Test]
    public void AMissingMember_IsStillAnError() =>
        Assert.That(() => JavaScriptExecutionTests.RunJs(Decl + "Sub Main()\n Dim k As Shade = Shade.Dusk\nEnd Sub\n"),
            Throws.Exception.With.Message.Contains("Dusk"));
}
