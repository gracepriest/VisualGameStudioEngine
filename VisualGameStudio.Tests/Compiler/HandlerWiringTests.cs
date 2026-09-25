using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 15: a missing handler is a hard error (D8).
///
/// <para>⛔ The guarantee this replaces was PUNCTUATION-DEPENDENT. <c>AddressOf OnClick</c> naming a
/// handler the user had deleted produced no diagnostic at all, while <c>AddressOf On_Click</c>
/// produced "Undefined identifier" — the difference being <c>IsNetType</c>, which treats any
/// PascalCase identifier without an underscore as a .NET type. D8 wires every designer-generated
/// handler through <c>AddressOf</c>, so that silence was a handler that never fires and never
/// says so.</para>
///
/// <para>⚠ The tests that assert SILENCE matter as much as the ones that assert an error. The
/// resolver cannot see <c>System.Windows.Forms</c> at all — <c>EnableNetResolution</c> returns
/// early for <c>UseWindowsForms</c> — so every member access on a WinForms receiver has a null
/// symbol and always will. A tightening that did not exempt them would reject every correct
/// WinForms program, this designer's own output included.</para>
/// </summary>
[TestFixture]
public class HandlerWiringTests
{
    private static SemanticAnalyzer Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer;
    }

    private static string Errors(SemanticAnalyzer analyzer) =>
        string.Join("\n", analyzer.Errors.Select(e => e.Message));

    // ==================================================================
    // The defect
    // ==================================================================

    [Test]
    public void AddressOf_AHandlerThatDoesNotExist_IsAnError()
    {
        var analyzer = Analyze("""
            Sub Main()
                Dim h As Action = AddressOf OnClick
            End Sub
            """);

        Assert.That(analyzer.Errors, Is.Not.Empty,
            "a deleted handler must not compile silently — D8 wires every handler this way");
        Assert.That(Errors(analyzer), Does.Contain("OnClick"),
            "the diagnostic must name the handler, or the user cannot find it");
    }

    [Test]
    public void AddressOf_AHandlerThatDoesNotExist_IsAnError_RegardlessOfPunctuation()
    {
        // ⛔ The whole point. Before this task the underscored spelling errored and the
        // un-underscored one did not, purely because of the .NET-type heuristic. Both must fail,
        // and this test fails if the PascalCase case ever goes quiet again.
        var withUnderscore = Analyze("""
            Sub Main()
                Dim h As Action = AddressOf On_Click
            End Sub
            """);
        var withoutUnderscore = Analyze("""
            Sub Main()
                Dim h As Action = AddressOf OnClick
            End Sub
            """);

        Assert.Multiple(() =>
        {
            Assert.That(withUnderscore.Errors, Is.Not.Empty);
            Assert.That(withoutUnderscore.Errors, Is.Not.Empty,
                "the PascalCase spelling used to be accepted — that is the bug");
        });
    }

    // ==================================================================
    // What must still compile
    // ==================================================================

    [Test]
    public void AddressOf_AHandlerThatExists_StillCompiles()
    {
        var analyzer = Analyze("""
            Sub OnClick()
                PrintLine("hi")
            End Sub

            Sub Main()
                Dim h As Action = AddressOf OnClick
                h()
            End Sub
            """);

        Assert.That(analyzer.Errors, Is.Empty, Errors(analyzer));
    }

    [Test]
    public void AddressOf_AHandlerWithParameters_StillCompiles()
    {
        var analyzer = Analyze("""
            Sub OnClick(sender As Object, e As Object)
                PrintLine("hi")
            End Sub

            Sub Main()
                Dim h As Action(Of Object, Object) = AddressOf OnClick
            End Sub
            """);

        Assert.That(analyzer.Errors, Is.Empty, Errors(analyzer));
    }

    [Test]
    public void AddressOf_AMemberAccess_IsNeverRejected_EvenWhenUnresolvable()
    {
        // ⛔⛔ The WinForms exemption, pinned. The resolver closure cannot reach
        // System.Windows.Forms.dll, so `Me.btnLogin_Click` has no symbol here and never will.
        // If this ever starts failing, every AddHandler the designer generates has become a
        // build error.
        //
        // ⚠ Asserted as "this diagnostic does not fire", not as "there are no errors". An
        // unresolvable member access still falls to the pre-existing pointer fallback, and
        // assigning `Pointer To Object` to a declared Action was an error before this task and
        // remains one. Asserting an empty list would pin that unrelated behaviour here and make
        // this test fail for a reason that has nothing to do with what it is testing.
        var analyzer = Analyze("""
            Class Form1
                Sub Wire()
                    Dim h As Action = AddressOf Me.SomethingUnresolvable
                End Sub
            End Class
            """);

        Assert.That(Errors(analyzer), Does.Not.Contain("AddressOf cannot take its address"),
            "a member access must never hit the bare-name check — the resolver cannot see through "
            + "it and never will");
    }

    // ==================================================================
    // AddHandler / RemoveHandler
    // ==================================================================

    [Test]
    public void AddHandler_OnAnUnresolvedReceiver_StaysSilent()
    {
        // The ordinary WinForms shape, written the way the designer's own region writer emits it:
        // a declared field of an unresolvable type, wired to a handler declared above.
        var analyzer = Analyze("""
            Class Form1
                Private btnLogin As Button

                Private Sub OnClick()
                End Sub

                Sub Wire()
                    AddHandler btnLogin.Click, AddressOf Me.OnClick
                End Sub
            End Class
            """);

        Assert.That(analyzer.Errors, Is.Empty, Errors(analyzer));
    }

    [Test]
    public void AddHandler_OnAResolvedNonEvent_IsAnError()
    {
        // Evidence of a real mistake: the event side resolved, and it is an Integer.
        var analyzer = Analyze("""
            Sub OnClick()
            End Sub

            Sub Main()
                Dim counter As Integer = 0
                AddHandler counter, AddressOf OnClick
            End Sub
            """);

        Assert.That(analyzer.Errors, Is.Not.Empty,
            "wiring a handler to an Integer attaches nothing and used to report nothing");
        Assert.That(Errors(analyzer), Does.Contain("not an event"));
    }

    [Test]
    public void RemoveHandler_GetsTheSameValidation_AsAddHandler()
    {
        // The RemoveHandler visitor was an identical two-statement stub; a fix applied to one and
        // not the other is the likeliest way for this to rot.
        var analyzer = Analyze("""
            Sub OnClick()
            End Sub

            Sub Main()
                Dim counter As Integer = 0
                RemoveHandler counter, AddressOf OnClick
            End Sub
            """);

        Assert.That(Errors(analyzer), Does.Contain("not an event"));
    }
}
