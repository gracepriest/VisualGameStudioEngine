using System;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #171 — front end: a <c>String</c> <c>For Each</c> collection enumerates as <c>Char</c>
/// (VB's rule; <c>SemanticAnalyzer.IsStringForEachCollection</c>), never <c>Object</c>. No
/// backend runs here — see <see cref="ForEachOverStringExecutionTests"/> for that half of the
/// contract.
///
/// <para><b>Two independent type sources, on purpose.</b> The element type a bare
/// <c>For Each ch In s</c> gets is <see cref="SemanticAnalyzer.GetNodeType"/>'s answer, read
/// straight off the built <see cref="IRForEach"/>'s <c>ElementType</c> below rather than
/// re-implemented. Task #168's REUSE form (<c>Dim c As Char : For Each c In s</c>) additionally
/// defines a hidden symbol for the loop to assign FROM — a second, independent type source
/// (<c>SemanticAnalyzer.cs</c>'s <c>_currentScope.Define(new Symbol(hiddenName, ...))</c>) that
/// must agree with the first. A binding-only check cannot see the second go stale (measured:
/// mutating <c>IRBuilder</c>'s hidden-variable <c>ElementType</c> to <c>Object</c> leaves the
/// binding fully Char-typed and green, and only breaks at the BACKEND — CS0266 on C#, garbage on
/// MSIL). So the reuse form is checked on BOTH: <see cref="ReuseForm_ProducesNoError"/> (the
/// binding) and <see cref="ReuseForm_HiddenIRVariable_IsTypedChar"/> (the IR node).</para>
///
/// <para><b>The <c>System.String</c>-handle arm of <c>IsStringForEachCollection</c> is not
/// tested here.</b> Attempted and found unreachable through any current front-end path:
/// <c>Dim s As System.String = "abc"</c> fails semantic analysis on its OWN terms — "Cannot
/// assign value of type 'String' to variable of type 'System.String'" — because
/// <c>ResolveTypeReference("System.String")</c> yields a TypeInfo distinct from the plain
/// <c>String</c> a literal or a <c>String</c>-typed value carries, with no widening between the
/// two; and <c>NetHandleResultTypeInfo</c> never builds a HANDLE for <c>String</c> at all, since
/// <c>BoundaryTypeRegistry.Categorize("String")</c> puts it in the native-owned primitives that
/// return early, before a handle is ever constructed. So no value of either shape can reach a
/// <c>For Each</c> collection position today; the arm is defensive for a path nothing yet
/// opens. If a future change opens one, this comment is where a real test belongs.</para>
/// </summary>
[TestFixture]
public class ForEachOverStringTests
{
    /// <summary>Parse + analyze, returning whether it refused and every error message.</summary>
    private static (bool HasErrors, string[] Messages) Analyze(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(ast);
        return (!ok, analyzer.Errors.Select(e => e.Message).ToArray());
    }

    /// <summary>Parse, analyze (asserting success) and build IR, returning the analyzer and the
    /// module for the caller to pick apart.</summary>
    private static (SemanticAnalyzer Analyzer, IRModule Module) BuildIr(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        return (analyzer, module);
    }

    /// <summary>The single <see cref="IRForEach"/> instruction in <c>Sub Main</c>'s body.</summary>
    private static IRForEach SingleForEachInMain(IRModule module)
    {
        var main = module.Functions.Single(f =>
            string.Equals(f.Name, "Main", StringComparison.OrdinalIgnoreCase));
        return main.Blocks.SelectMany(b => b.Instructions).OfType<IRForEach>().Single();
    }

    // ========================================================================================
    // INFERENCE — a bare `For Each ch In <String>` types `ch` as Char, for every shape of
    // String-typed collection expression. Read off the built IRForEach.ElementType, which is
    // SemanticAnalyzer.GetNodeType(node)'s own answer (IRBuilder.cs ~3670), not re-derived.
    // ========================================================================================

    [Test]
    public void Literal_InfersCharType()
    {
        var (_, module) = BuildIr(
            "Sub Main()\n" +
            " For Each ch In \"abc\"\n" +
            "  Console.WriteLine(ch)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(SingleForEachInMain(module).ElementType?.Name, Is.EqualTo("Char"));
    }

    [Test]
    public void StringVariable_InfersCharType()
    {
        var (_, module) = BuildIr(
            "Sub Main()\n" +
            " Dim s As String = \"hey\"\n" +
            " For Each ch In s\n" +
            "  Console.WriteLine(ch)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(SingleForEachInMain(module).ElementType?.Name, Is.EqualTo("Char"));
    }

    [Test]
    public void StringFunctionResult_InfersCharType()
    {
        var (_, module) = BuildIr(
            "Function Name() As String\n" +
            " Return \"Qz\"\n" +
            "End Function\n" +
            "Sub Main()\n" +
            " For Each ch In Name()\n" +
            "  Console.WriteLine(ch)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(SingleForEachInMain(module).ElementType?.Name, Is.EqualTo("Char"));
    }

    [Test]
    public void StringField_InfersCharType()
    {
        var (_, module) = BuildIr(
            "Class Box\n" +
            " Public Text As String\n" +
            " Public Sub New(t As String)\n" +
            "  Text = t\n" +
            " End Sub\n" +
            "End Class\n" +
            "Sub Main()\n" +
            " Dim b As New Box(\"abc\")\n" +
            " For Each ch In b.Text\n" +
            "  Console.WriteLine(ch)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(SingleForEachInMain(module).ElementType?.Name, Is.EqualTo("Char"));
    }

    /// <summary>
    /// A <c>String()</c> array's element type is ALSO named "String", so it must still enumerate
    /// as String, never Char.
    ///
    /// <para>⚠ <b>Measured, not assumed: today this is protected TWICE, redundantly.</b>
    /// <c>IsStringForEachCollection</c> excludes arrays explicitly
    /// (<c>collectionType.Kind == TypeKind.Array</c> returns false first) — but BOTH of its two
    /// call sites in <c>Visit(ForEachLoopNode)</c> are the <c>else if</c> of a SIBLING
    /// <c>if (collectionType.Kind == TypeKind.Array) { … }</c>, so an array collection is always
    /// claimed by that outer arm first and <c>IsStringForEachCollection</c> is never even
    /// INVOKED with one. Built the (f) mutant for real (dropping the array exclusion from
    /// <c>IsStringForEachCollection</c> itself, rebuilding <c>BasicLang.dll</c>, and running this
    /// test against it) and it stayed GREEN — confirming the inner guard is presently dead code,
    /// not the thing protecting this behavior. This test is kept because the BEHAVIOR (a String
    /// array enumerates as String) still needs a pin; it just documents that today's protection
    /// is the outer <c>Kind == Array</c> arm, not <c>IsStringForEachCollection</c>'s own guard —
    /// which would matter the day a caller ever invokes it directly on an array-kind TypeInfo.
    /// </para>
    /// </summary>
    [Test]
    public void StringArray_StillEnumeratesAsString()
    {
        var (_, module) = BuildIr(
            "Sub Main()\n" +
            " Dim a() As String = {\"x\", \"y\"}\n" +
            " For Each w In a\n" +
            "  Console.WriteLine(w)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(SingleForEachInMain(module).ElementType?.Name, Is.EqualTo("String"));
    }

    // ========================================================================================
    // TASK #168 REUSE — `Dim c As Char : For Each c In "xyz"` type-checks (Object -> Char used
    // to refuse it), and its hidden element variable carries Char on BOTH type sources.
    // ========================================================================================

    /// <summary>
    /// S1's reuse form produces no analyzer error. Kills (a) — the whole String inference arm
    /// removed, which leaves the hidden variable Object-typed and refuses `hidden -> c` — and
    /// (e1) — only the hidden SYMBOL's definition forced to Object, same refusal.
    /// </summary>
    [Test]
    public void ReuseForm_ProducesNoError()
    {
        var (hasErrors, messages) = Analyze(
            "Sub Main()\n" +
            " Dim c As Char\n" +
            " For Each c In \"xyz\"\n" +
            "  Console.Write(c)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(hasErrors, Is.False, string.Join(" | ", messages));
    }

    /// <summary>
    /// The IR-level half of the reuse form's contract: the hidden element variable
    /// <see cref="IRForEach"/> iterates is itself typed Char, not just the SYMBOL the body's
    /// `c = hidden` assignment binds against. Kills (e2) — IRBuilder forcing the reused-control
    /// case's <c>IRForEach.ElementType</c> to Object while leaving the symbol (and so the
    /// assignment's own type check) untouched, which compiles at the front end and only fails at
    /// the backend (CS0266 on C#; garbage on MSIL).
    /// </summary>
    [Test]
    public void ReuseForm_HiddenIRVariable_IsTypedChar()
    {
        var (_, module) = BuildIr(
            "Sub Main()\n" +
            " Dim c As Char\n" +
            " For Each c In \"xyz\"\n" +
            "  Console.Write(c)\n" +
            " Next\n" +
            "End Sub");

        var forEach = SingleForEachInMain(module);
        Assert.That(forEach.VariableName, Does.StartWith("__foreach_"),
            "must be the #168 hidden-variable reuse form — got: " + forEach.VariableName);
        Assert.That(forEach.ElementType?.Name, Is.EqualTo("Char"));
    }

    // ========================================================================================
    // EXPLICIT TYPE — `For Each x As T In s` follows the Array arm's rule: T must accept the
    // element type the collection actually yields (Char for a String).
    // ========================================================================================

    [Test]
    public void ExplicitChar_IsAccepted()
    {
        var (hasErrors, messages) = Analyze(
            "Sub Main()\n" +
            " For Each ch As Char In \"ok\"\n" +
            "  Console.Write(ch)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(hasErrors, Is.False, string.Join(" | ", messages));
    }

    [Test]
    public void ExplicitObject_IsAccepted()
    {
        var (hasErrors, messages) = Analyze(
            "Sub Main()\n" +
            " For Each o As Object In \"ab\"\n" +
            "  Console.WriteLine(o)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(hasErrors, Is.False, string.Join(" | ", messages));
    }

    /// <summary>
    /// E5 — `As Integer` over a String. Before this fix the backends silently DISAGREED
    /// (C# 97/98, C++ 97/98/0, JavaScript a/b, MSIL InvalidCastException); now it is refused at
    /// the front end, with the exact message the Array arm's sibling check produces. Kills (d) —
    /// the explicit-type assignability check removed.
    /// </summary>
    [Test]
    public void ExplicitInteger_IsRefused_WithExactMessage()
    {
        var (hasErrors, messages) = Analyze(
            "Sub Main()\n" +
            " For Each n As Integer In \"ab\"\n" +
            "  Console.WriteLine(n)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(hasErrors, Is.True);
        Assert.That(messages, Has.One.EqualTo(
            "Cannot assign String element type 'Char' to loop variable of type 'Integer'"));
    }

    /// <summary>
    /// E9 — `As String` over a String: refused by the SAME rule as `As Integer`, matching the
    /// existing refusal `Dim s As String = c` already gives a Char. This is NOT the same
    /// question as widening Char TO String on assignment — task #184 is the owner's decision on
    /// whether that should ever be allowed; until it lands, a String loop variable over a String
    /// collection stays refused here, deliberately.
    /// </summary>
    [Test]
    public void ExplicitString_IsRefused_PerTask184()
    {
        var (hasErrors, messages) = Analyze(
            "Sub Main()\n" +
            " For Each s As String In \"ab\"\n" +
            "  Console.WriteLine(s)\n" +
            " Next\n" +
            "End Sub");

        Assert.That(hasErrors, Is.True);
        Assert.That(messages, Has.One.EqualTo(
            "Cannot assign String element type 'Char' to loop variable of type 'String'"));
    }
}
