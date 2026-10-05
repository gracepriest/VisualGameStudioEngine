using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.8, M13 (<c>HandlerSignatureMismatch</c>, portable-controls Task 10) — <c>AddHandler btn.Click, AddressOf H</c>
/// with <c>H(n As Integer)</c> against <c>Event Click(sender As Object, e As EventArgs2)</c> built green and ran, passing
/// the sender as n. The shape check existed (ValidateHandlerWiring) and never saw either side: events were not class
/// members, and a Private handler declared below its wiring was unbound when the wiring was analyzed. This is what makes
/// an unconverted <c>(e As DomEvent)</c> handler a build error on the web (spec §8).
/// </summary>
[TestFixture]
public class HandlerSignatureTests
{
    private static List<string> Errors(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors.Select(e => e.Message), Is.Empty, "the test's source must parse");
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.Select(e => e.Message).ToList();
    }

    private const string Button =
        "Public Class EventArgs2\nEnd Class\n" +
        "Public Class Btn\n Public Event Click(sender As Object, e As EventArgs2)\n" +
        " Public Sub Press()\n  RaiseEvent Click(Me, New EventArgs2())\n End Sub\nEnd Class\n";

    private static string Form(string handler, bool handlerFirst = false)
    {
        var init = " Private b As Btn\n Public Sub New()\n  b = New Btn()\n  AddHandler b.Click, AddressOf H\n End Sub\n";
        return "Public Class Form1\n" + (handlerFirst ? handler + init : init + handler) + "End Class\n";
    }

    [Test]
    public void TooFewParameters_BelowItsWiring_IsReported() =>
        Assert.That(Errors(Button + Form(" Private Sub H(n As Integer)\n End Sub\n")),
            Has.Some.Contains("takes 1 parameter(s) but the event supplies 2"));

    [Test]
    public void AWrongParameterType_BelowItsWiring_IsReported() =>
        Assert.That(Errors(Button + Form(" Private Sub H(sender As Object, e As Integer)\n End Sub\n")),
            Has.Some.Contains("takes 'Integer' as parameter 2, but the event supplies 'EventArgs2'"));

    /// <summary>Guard (green before the change): the right shape is clean whichever side of the wiring it is declared.</summary>
    [Test]
    public void TheWinFormsShape_BelowOrAbove_IsClean()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Errors(Button + Form(" Private Sub H(sender As Object, e As EventArgs2)\n End Sub\n")), Is.Empty);
            Assert.That(Errors(Button + Form(" Private Sub H(sender As Object, e As EventArgs2)\n End Sub\n", handlerFirst: true)), Is.Empty);
        });
    }

    [Test]
    public void TheEventsClass_DeclaredBelowTheWiring_IsStillChecked() =>
        Assert.That(Errors(Form(" Private Sub H(n As Integer)\n End Sub\n") + Button),
            Has.Some.Contains("takes 1 parameter(s) but the event supplies 2"));

    /// <summary>The handler ABOVE its wiring was already checked; it must stay so (and stay named).</summary>
    [Test]
    public void AWrongHandler_AboveItsWiring_IsReported() =>
        Assert.That(Errors(Button + Form(" Private Sub H(n As Integer)\n End Sub\n", handlerFirst: true)),
            Has.Some.Contains("takes 1 parameter(s) but the event supplies 2").And.Some.Contains("HandlerSignatureMismatch"));

    /// <summary>⛔ .NET events stay silent: on a WinForms build <c>b.Click</c> types as Object (K14) and csc checks it.</summary>
    [Test]
    public void AnUnresolvedDotNetEvent_StaysSilent() =>
        Assert.That(Errors("Using System.Windows.Forms\nPublic Class Form1\n Private b As Button\n" +
                           " Public Sub New()\n  AddHandler b.Click, AddressOf H\n End Sub\n" +
                           " Private Sub H(n As Integer)\n End Sub\nEnd Class\n"), Is.Empty);

    /// <summary>§11.7 — the diagnostic is assertable BY NAME.</summary>
    [Test]
    public void TheMismatch_IsNamed() =>
        Assert.That(Errors(Button + Form(" Private Sub H(n As Integer)\n End Sub\n")), Has.Some.Contains("HandlerSignatureMismatch"));

    /// <summary>The LIBRARY's shape: the event is declared on a BASE class and wired on a field of a DERIVED type.</summary>
    [Test]
    public void AnEventOfABaseClass_WiredOnADerivedField_IsChecked() =>
        Assert.That(Errors(
            "Public Class EventArgs2\nEnd Class\n" +
            "Public Class Control\n Public Event Click(sender As Object, e As EventArgs2)\nEnd Class\n" +
            "Public Class Button\n Inherits Control\nEnd Class\n" +
            "Public Class Form1\n Private b As Button\n Public Sub New()\n  b = New Button()\n  AddHandler b.Click, AddressOf H\n End Sub\n" +
            " Private Sub H(n As Integer)\n End Sub\nEnd Class\n"),
            Has.Some.Contains("takes 1 parameter(s) but the event supplies 2"));

    /// <summary>
    /// ⛔ Every form has its own <c>Button1_Click</c>. Pass 1 flattens class procedures into the global scope, first one
    /// wins, so judged at the wiring the SECOND form's wrong handler bound to the FIRST form's right one and passed.
    /// </summary>
    [Test]
    public void TwoForms_EachWithAHandlerOfTheSameName_EachIsJudgedByItsOwn() =>
        Assert.That(Errors(Button +
            "Public Class FormA\n Private b As Btn\n Public Sub New()\n  b = New Btn()\n  AddHandler b.Click, AddressOf H\n End Sub\n" +
            " Private Sub H(sender As Object, e As EventArgs2)\n End Sub\nEnd Class\n" +
            "Public Class FormB\n Private b As Btn\n Public Sub New()\n  b = New Btn()\n  AddHandler b.Click, AddressOf H\n End Sub\n" +
            " Private Sub H(n As Integer)\n End Sub\nEnd Class\n"),
            Has.Exactly(1).Contains("takes 1 parameter(s) but the event supplies 2"));

    /// <summary>
    /// The deferred check judges the handler in the class that WIRES it: a same-named handler of the right shape in an
    /// enclosing or sibling class must not answer for it.
    /// </summary>
    [Test]
    public void TheDeferredCheck_ReadsTheWiringClassesOwnHandler() =>
        Assert.That(Errors(Button +
            "Public Class Outer\n Private Sub H(sender As Object, e As EventArgs2)\n End Sub\n" +
            " Public Class Inner\n  Private b As Btn\n  Public Sub New()\n   b = New Btn()\n   AddHandler b.Click, AddressOf H\n  End Sub\n" +
            "  Private Sub H(n As Integer)\n  End Sub\n End Class\nEnd Class\n"),
            Has.Some.Contains("takes 1 parameter(s) but the event supplies 2"));
}
