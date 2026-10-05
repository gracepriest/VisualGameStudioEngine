using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// O13 / spec §4.10 (portable-controls Task 14) — Char on the JavaScript backend, a one-character string, so
/// KeyPressEventArgs.KeyChar is a real Char on both targets. Measured before (M18): BL7004 "JavaScript has no character
/// type". The oracle is the same program on C#. Also the qualified <c>Math.*</c> surface the library needs, which had no
/// JavaScript lowering at all (bare <c>Max(a, b)</c> mapped; <c>Math.Max</c> did not).
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class JavaScriptCharTests
{
    private const string Program =
        "Public Class Holder\n Public C As Char\nEnd Class\n" +
        "Sub Show(c As Char)\n Console.WriteLine(\"[\" & c & \"]\")\nEnd Sub\n" +
        "Function Next1(c As Char) As Char\n Return ChrW(AscW(c) + 1)\nEnd Function\n" +
        "Sub Main()\n Dim a As Char = \"a\"c\n Dim z As Char = \"z\"c\n Console.WriteLine(a)\n Console.WriteLine(a < z)\n" +
        " Console.WriteLine(AscW(a))\n Console.WriteLine(ChrW(66))\n Console.WriteLine(Asc(\"A\"c))\n Console.WriteLine(Chr(67))\n" +
        " Console.WriteLine(CStr(a) & \"!\")\n Show(z)\n Console.WriteLine(Next1(a))\n" +
        " Dim h As New Holder()\n Console.WriteLine(AscW(h.C))\n" +
        " Dim list As New List(Of Char)\n list.Add(\"x\"c)\n list.Add(\"y\"c)\n Console.WriteLine(list.Count)\n" +
        " Console.WriteLine(a = \"a\"c)\nEnd Sub\n";

    [Test]
    public void TheCharTable_MatchesCSharp()
    {
        var js = FourBackends.Norm(JavaScriptExecutionTests.RunJs(Program));
        var cs = FourBackends.Norm(FourBackends.RunEmittedCSharp(Program));
        Assert.Multiple(() =>
        {
            Assert.That(js, Is.EqualTo(cs), "JavaScript vs the C# oracle");
            Assert.That(cs, Is.EqualTo("a\nTrue\n97\nB\n65\nC\na!\n[z]\nb\n0\n2\nTrue"), "the oracle itself");
        });
    }

    /// <summary>
    /// The QUALIFIED <c>Math.X</c> the library needs, with VB's banker's rounding for <c>Math.Round</c> (2.5 → 2, 3.5 → 4),
    /// against the C# oracle.
    /// </summary>
    [Test]
    public void TheMathSurface_MatchesCSharp()
    {
        const string program =
            "Sub Main()\n Console.WriteLine(Math.Max(3, 7))\n Console.WriteLine(Math.Min(3, 7))\n Console.WriteLine(Math.Abs(-4))\n" +
            " Console.WriteLine(Math.Floor(2.7))\n Console.WriteLine(Math.Ceiling(2.1))\n Console.WriteLine(Math.Round(2.5))\n" +
            " Console.WriteLine(Math.Round(3.5))\n Console.WriteLine(Math.Sqrt(16.0))\n Console.WriteLine(Math.Pow(2.0, 10.0))\nEnd Sub\n";
        var js = FourBackends.Norm(JavaScriptExecutionTests.RunJs(program));
        var cs = FourBackends.Norm(FourBackends.RunEmittedCSharp(program));
        Assert.Multiple(() =>
        {
            Assert.That(js, Is.EqualTo(cs), "JavaScript vs the C# oracle");
            Assert.That(cs, Is.EqualTo("7\n3\n4\n2\n3\n2\n4\n4\n1024"), "the oracle itself");
        });
    }
}
