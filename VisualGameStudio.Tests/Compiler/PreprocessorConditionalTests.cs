using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-29 §4.1 — VB's <c>#If … Then / #ElseIf … Then / #Else / #End If</c>, beside the
/// existing <c>#IfDef</c>/<c>#IfNDef</c>/<c>#EndIf</c>. Measured before (M9): <c>#If WEB Then</c> reached the
/// parser as "Unexpected token in expression: '#If'" and its <c>#Else</c> was "#Else without matching
/// #IfDef or #IfNDef".
/// </summary>
[TestFixture]
public class PreprocessorConditionalTests
{
    private static (string[] Lines, List<PreprocessorError> Errors) Run(string source, params string[] symbols)
    {
        var pre = new Preprocessor();
        foreach (var s in symbols) pre.Define(s);
        var output = pre.Process(source, "test.bas");
        var lines = output.Replace("\r\n", "\n").Split('\n');
        return (lines, pre.Errors);
    }

    /// <summary>The lines that reach the lexer as CODE: not blank, not a comment.</summary>
    private static string[] Active(string source, params string[] symbols) =>
        Run(source, symbols).Lines.Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("'")).ToArray();

    private const string Chain =
        "#If A Then\nONE\n#ElseIf B Then\nTWO\n#ElseIf C Then\nTHREE\n#Else\nFOUR\n#End If";

    [TestCase(new[] { "A" }, "ONE")]
    [TestCase(new[] { "B" }, "TWO")]
    [TestCase(new[] { "B", "C" }, "TWO")]   // the FIRST true branch wins
    [TestCase(new[] { "C" }, "THREE")]
    [TestCase(new string[0], "FOUR")]
    [TestCase(new[] { "A", "B", "C" }, "ONE")]
    public void AnIfChain_CompilesExactlyOneBranch(string[] symbols, string expected) =>
        Assert.That(Active(Chain, symbols), Is.EqualTo(new[] { expected }));

    [TestCase("WEB", "Not WEB", false)]
    [TestCase("WEB", "WEB And DEBUG", false)]
    [TestCase("WEB", "WEB Or DEBUG", true)]
    [TestCase("WEB", "WEB AndAlso Not DEBUG", true)]
    [TestCase("WEB", "DEBUG OrElse (WEB And Not DESKTOP)", true)]
    [TestCase("WEB", "True", true)]
    [TestCase("WEB", "False Or False", false)]
    [TestCase("WEB", "web", true)]                       // symbols are case-insensitive, as #IfDef's are
    [TestCase("WEB", "Not (WEB Or DEBUG)", false)]
    [TestCase("WEB", "WEB Or DEBUG And False", true)]    // VB precedence: And binds tighter than Or
    [TestCase("WEB", "False And WEB Or WEB", true)]      // (False And WEB) Or WEB — never False And (WEB Or WEB)
    public void TheCondition_IsEvaluatedWithVbOperators(string defined, string condition, bool taken) =>
        Assert.That(Active($"#If {condition} Then\nYES\n#Else\nNO\n#End If", defined),
            Is.EqualTo(new[] { taken ? "YES" : "NO" }));

    [Test]
    public void DirectivesAreCaseInsensitive_AndEndIfHasBothSpellings()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Active("#if WEB then\nA\n#elseif X then\nB\n#else\nC\n#end if", "WEB"), Is.EqualTo(new[] { "A" }));
            Assert.That(Active("#If WEB Then\nA\n#EndIf", "WEB"), Is.EqualTo(new[] { "A" }));
            Assert.That(Run("#If WEB Then\nA\n#EndIf", "WEB").Errors, Is.Empty);
            Assert.That(Run("#IfDef WEB\nA\n#End If", "WEB").Errors, Is.Empty);
        });
    }

    /// <summary>⛔ The prefix trap: "#ElseIf" starts with "#Else". Taken as #Else it would run the chain wrong.</summary>
    [Test]
    public void ElseIf_IsNotMistakenForElse() =>
        Assert.That(Active("#If A Then\nONE\n#ElseIf B Then\nTWO\n#End If"), Is.Empty,
            "neither A nor B is defined: nothing is active");

    [Test]
    public void NestedBlocks_InsideAnInactiveBranch_StayInactive()
    {
        const string src = "#If A Then\n#If B Then\nINNER\n#Else\nINNER_ELSE\n#End If\n#Else\nOUTER_ELSE\n#End If";
        Assert.Multiple(() =>
        {
            Assert.That(Active(src, "B"), Is.EqualTo(new[] { "OUTER_ELSE" }));
            Assert.That(Active(src, "A"), Is.EqualTo(new[] { "INNER_ELSE" }));
            Assert.That(Active(src, "A", "B"), Is.EqualTo(new[] { "INNER" }));
        });
    }

    [Test]
    public void IfAndIfDef_Mix() =>
        Assert.That(Active("#IfDef A\n#If Not B Then\nX\n#End If\n#EndIf", "A"), Is.EqualTo(new[] { "X" }));

    /// <summary>Every source line keeps its line number: the directives and skipped lines are commented, never removed.</summary>
    [Test]
    public void TheOutput_HasOneLinePerSourceLine()
    {
        var (lines, _) = Run(Chain + "\nAFTER", "B");
        Assert.Multiple(() =>
        {
            Assert.That(lines.Length - 1, Is.EqualTo(Chain.Split('\n').Length + 1),
                "the trailing newline of the last AppendLine adds one empty element");
            Assert.That(lines[3], Is.EqualTo("TWO"));
            Assert.That(lines[9], Is.EqualTo("AFTER"));
        });
    }

    [TestCase("#If WEB\nX\n#End If", "#If requires a condition followed by 'Then'")]
    [TestCase("#If WEB + 1 Then\nX\n#End If", "Invalid #If condition")]
    [TestCase("#If Then\nX\n#End If", "#If requires a condition followed by 'Then'")]
    [TestCase("#If (WEB Then\nX\n#End If", "Invalid #If condition")]
    [TestCase("#If A Then\n#Else\n#ElseIf B Then\n#End If", "#ElseIf after #Else")]
    [TestCase("#IfDef A\n#ElseIf B Then\n#EndIf", "#ElseIf is only valid inside #If")]
    [TestCase("#ElseIf B Then", "#ElseIf without matching #If")]
    [TestCase("#Else", "#Else without matching #If, #IfDef or #IfNDef")]
    [TestCase("#End If", "#End If without matching #If, #IfDef or #IfNDef")]
    [TestCase("#If A Then\nX", "Unclosed conditional block")]
    [TestCase("#If A Then\n#Else\n#Else\n#End If", "Duplicate #Else in conditional block")]
    public void MalformedConditionals_AreReported(string source, string expected) =>
        Assert.That(Run(source).Errors.Select(e => e.Message), Has.Some.Contains(expected));

    /// <summary>#JsImport and #CppInclude honour an inactive #If exactly as they honour an inactive #IfDef.</summary>
    [Test]
    public void GatedDirectives_HonourAnInactiveIf()
    {
        var pre = new Preprocessor();
        pre.Process("#If WEB Then\n#JsImport \"./web.js\"\n#Else\n#CppInclude <unistd.h>\n#End If", "test.bas");
        Assert.Multiple(() =>
        {
            Assert.That(pre.JsImports, Is.Empty);
            Assert.That(pre.CppIncludes, Is.EqualTo(new[] { "<unistd.h>" }));
        });
    }
}
