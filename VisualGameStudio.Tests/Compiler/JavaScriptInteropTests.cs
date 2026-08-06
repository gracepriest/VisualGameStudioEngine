using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class JavaScriptInteropTests
{
    [Test]
    public void JsImport_IsCollected()
    {
        var module = JsTestSupport.BuildModule(
            "#JsImport \"./chart.js\"\nSub Main()\nEnd Sub", runPreprocessor: true);

        Assert.That(module.JsImports, Is.EqualTo(new[] { "./chart.js" }));
    }

    /// <summary>
    /// An import gated behind an inactive conditional must not be collected.
    ///
    /// ⛔ #IfDef, NOT #If. The Preprocessor implements #IfDef/#IfNDef/#Else/#EndIf; `#If` is a
    /// LEXER/parser construct and never reaches the directive collector, so a test written
    /// with #If fails through JsTestSupport's parse guard no matter how correct the
    /// implementation is.
    /// </summary>
    [Test]
    public void JsImport_InsideInactiveConditional_IsNotCollected()
    {
        var module = JsTestSupport.BuildModule(
            "#IfDef NEVER_DEFINED\n#JsImport \"./nope.js\"\n#EndIf\nSub Main()\nEnd Sub",
            runPreprocessor: true);

        Assert.That(module.JsImports, Is.Empty);
    }

    /// <summary>
    /// The directive must be COMMENTED OUT, not removed. A source map built from
    /// IRInstruction.SourceLine is off by one for the whole file otherwise — exactly the
    /// .mod/.cls off-by-one class of bug this backend already had to fix once.
    /// </summary>
    [Test]
    public void JsImport_PreservesLineNumbers()
    {
        var module = JsTestSupport.BuildModule(
            "#JsImport \"./a.js\"\nSub Main()\nConsole.WriteLine(1)\nEnd Sub",
            runPreprocessor: true, sourceFilePath: "prog.bas");

        var lines = module.Functions.Single(f => f.Name == "Main")
            .Blocks.SelectMany(b => b.Instructions)
            .Where(i => i.SourceLine > 0).Select(i => i.SourceLine).ToList();

        Assert.That(lines, Does.Contain(3), "Console.WriteLine is on source line 3");
    }

    /// <summary>There are no angle-bracket module specifiers in JavaScript.</summary>
    [Test]
    public void JsImport_WithoutQuotes_IsAnError()
        => Assert.That(() => JsTestSupport.BuildModule(
                "#JsImport ./a.js\nSub Main()\nEnd Sub", runPreprocessor: true),
            Throws.Exception);
}
