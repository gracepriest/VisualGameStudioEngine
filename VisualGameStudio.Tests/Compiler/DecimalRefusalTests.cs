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
}
