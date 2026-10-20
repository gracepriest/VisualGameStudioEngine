using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>Spec 4.11's "out" rows — refused BY NAME at compile time, never lowered wrong (DecimalNotSupportedOnWeb).</summary>
[TestFixture]
public class DecimalRefusalTests
{
    private static void Refused(string body, string construct) =>
        Assert.That(() => JsTestSupport.Compile("Sub Main()\n Dim d As Decimal = 1.5D\n" + body + "\nEnd Sub"),
            Throws.Exception.With.Message.Contains("DecimalNotSupportedOnWeb").And.Message.Contains(construct));

    [Test] public void RoundWithAMidpointRule() => Refused(" Console.WriteLine(Math.Round(d, 1, MidpointRounding.AwayFromZero))", "MidpointRounding");
    [Test] public void AnotherMathMember() => Refused(" Console.WriteLine(Math.Sign(d))", "Math.Sign");
    [Test] public void ADictionaryKeyedByDecimal() => Refused(" Dim m As New Dictionary(Of Decimal, String)\n m(d) = \"x\"", "Dictionary");
    [Test] public void AHashSetOfDecimal() => Refused(" Dim s As New HashSet(Of Decimal)\n s.Add(d)", "HashSet");
    [Test] public void ListContains() => Refused(" Dim l As New List(Of Decimal)\n Console.WriteLine(l.Contains(d))", "Contains");
    [Test] public void ListIndexOf() => Refused(" Dim l As New List(Of Decimal)\n Console.WriteLine(l.IndexOf(d))", "IndexOf");
    [Test] public void ListRemove() => Refused(" Dim l As New List(Of Decimal)\n l.Remove(d)", "Remove");
    [Test] public void ArrayIndexOf() => Refused(" Dim a() As Decimal = {1.5D}\n Console.WriteLine(Array.IndexOf(a, d))", "Array.IndexOf");
    [Test] public void IntegerDivision() => Refused(" Console.WriteLine(d \\ 1D)", "Math.Truncate");
    [Test] public void AFormatSpecifier_InToString() => Refused(" Console.WriteLine(d.ToString(\"N2\"))", "ToString");
    [Test] public void AFormatSpecifier_InStringFormat() => Refused(" Console.WriteLine(String.Format(\"{0:F2}\", d))", "String.Format");
    [Test] public void AFormatSpecifier_InInterpolation() => Refused(" Console.WriteLine($\"{d:F2}\")", "interpolation");
    [Test] public void VbFormat() => Refused(" Console.WriteLine(Format(d, \"0.00\"))", "Format");

    [Test]
    public void TryParse_IsTheByRefRefusal_WithTheSpellingThatWorks() =>
        // MEASURED: `Dim ok As Boolean = ...` is refused by the ANALYZER first ("cannot assign
        // Object to Boolean"); `As Object` is the spelling that reaches the backend, where the
        // Decimal.TryParse call is refused for its out parameter.
        Assert.That(() => JsTestSupport.Compile("Sub Main()\n Dim d As Decimal\n Dim ok As Object = Decimal.TryParse(\"1\", d)\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7002").And.Message.Contains("Decimal.Parse inside Try"));
}
