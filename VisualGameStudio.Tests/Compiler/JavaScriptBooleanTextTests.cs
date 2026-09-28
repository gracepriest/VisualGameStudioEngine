using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// On the JavaScript backend a value becomes text through one helper (<c>TextOf</c>), so a
/// Boolean prints <c>True</c>/<c>False</c> as .NET does, not JavaScript's <c>true</c>.
///
/// <para>Before, every site used JS's own conversion: <c>"b=" &amp; flag</c>, <c>CStr(flag)</c>,
/// <c>$"{flag}"</c> and <c>Console.WriteLine(flag)</c> printed lowercase, and
/// <c>flag.ToString()</c> and <c>Console.Write(flag)</c> threw at run time (as they did for a
/// number: a JS primitive has no <c>ToString</c> method, and <c>process.stdout.write</c> takes
/// only strings).</para>
/// </summary>
[TestFixture]
public class JavaScriptBooleanTextCodegenTests
{
    private const string Source = @"
Function Flag() As Boolean
    Return False
End Function

Sub Main()
    Dim b As Boolean = Flag()
    Console.WriteLine(""b="" & b)
    Console.WriteLine(CStr(b))
    Console.WriteLine(b)
End Sub";

    [Test]
    public void BooleanText_IsTheDotNetSpelling_NotJavaScripts()
    {
        foreach (var js in new[] { JsTestSupport.Compile(Source), JsTestSupport.CompileOptimized(Source) })
        {
            Assert.That(js, Does.Contain("? \"True\" : \"False\")"), js);
            Assert.That(js, Does.Not.Contain("String(b)"), js);
        }
    }

    [Test]
    public void RuntimeHelper_IsEmittedOnlyWhenAnObjectValueBecomesText()
    {
        Assert.That(JsTestSupport.Compile(Source), Does.Not.Contain("__blStr"));

        var js = JsTestSupport.Compile(@"
Sub Show(o As Object)
    Console.WriteLine(""v="" & o)
End Sub

Sub Main()
    Show(True)
End Sub");
        Assert.That(js, Does.Contain("function __blStr(x)"));
        Assert.That(js, Does.Contain("__blStr(o)"));
    }

    [Test]
    public void AClassesOwnToString_IsStillCalled()
    {
        var js = JsTestSupport.Compile(@"
Class Pt
    Public Overrides Function ToString() As String
        Return ""Pt""
    End Function
End Class

Sub Main()
    Dim p As New Pt()
    Console.WriteLine(p.ToString())
End Sub");
        Assert.That(js, Does.Contain("p.ToString()"));
    }
}

[TestFixture]
[Category("Integration")]   // spawns node
[NonParallelizable]
public class JavaScriptBooleanTextExecutionTests
{
    private static string Run(string source) =>
        JavaScriptOptimizedExecutionTests.RunOptimized(source).Replace("\r\n", "\n").TrimEnd('\n');

    [Test]
    public void EveryBooleanToTextRoute_PrintsWhatDotNetPrints()
    {
        const string source = @"
Function Flag() As Boolean
    Return False
End Function

Sub Main()
    Dim b As Boolean = True
    Console.WriteLine(""1 "" & b)
    Console.WriteLine(b & "" 2"")
    Console.WriteLine(CStr(b))
    Console.WriteLine($""4 {b}"")
    Console.WriteLine(b)
    Console.WriteLine(b.ToString())
    Console.WriteLine(""7 "" & (3 > 2))
    Console.WriteLine(""8 "" & Flag())
    Console.WriteLine(Not b)
    Console.Write(b)
    Console.WriteLine()
End Sub";

        Assert.That(Run(source), Is.EqualTo(
            "1 True\nTrue 2\nTrue\n4 True\nTrue\nTrue\n7 True\n8 False\nFalse\nTrue"));
    }

    [Test]
    public void AnObjectValue_IsCheckedAtRunTime()
    {
        const string source = @"
Sub Show(o As Object)
    Console.WriteLine(""v="" & o)
    Console.WriteLine(o)
    Console.WriteLine(CStr(o))
    Console.Write(o)
    Console.WriteLine()
End Sub

Sub Main()
    Show(True)
    Show(7)
    Show(""s"")
End Sub";

        Assert.That(Run(source), Is.EqualTo(
            "v=True\nTrue\nTrue\nTrue\nv=7\n7\n7\n7\nv=s\ns\ns\ns"));
    }

    [Test]
    public void ToStringAndConsoleWrite_OnOtherPrimitives_NoLongerThrow()
    {
        const string source = @"
Sub Main()
    Dim n As Integer = 5
    Dim d As Double = 2.5
    Dim s As String = ""str""
    Console.WriteLine(n.ToString() & ""|"" & d.ToString() & ""|"" & s.ToString())
    Console.Write(n)
    Console.Write(d)
    Console.Write(s)
    Console.WriteLine()
End Sub";

        Assert.That(Run(source), Is.EqualTo("5|2.5|str\n52.5str"));
    }
}
