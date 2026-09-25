using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The C# backend rebuilds structured statements from IR blocks and skips every control-flow
/// instruction when it emits a block's body, so each block's TERMINATOR must be dispatched
/// explicitly. The sites that continue past an If, a loop or a Try, and the If/Else/Try/Catch
/// bodies, dispatched only branches: a <c>Select Case</c> there (an IRSwitch terminator) was
/// dropped, together with every statement after it. The Case and Finally bodies dispatched no
/// conditional branch either, so an If inside a Case, a Case Else or a Finally was dropped.
/// Nothing failed to compile; the code just vanished. C++ and JavaScript were unaffected.
/// </summary>
[TestFixture]
public class CSharpNestedTerminatorCodeGenTests
{
    private static string Cs(string source) =>
        new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(JsTestSupport.BuildModule(source));

    private const string Sel = "Select Case x\n Case 5\n Console.WriteLine(\"marker\")\n End Select";

    private static string InMain(string body) =>
        "Sub Main()\n Dim x As Integer = 5\n" + body + "\n Console.WriteLine(\"end\")\nEnd Sub";

    [TestCase("after an If", " If x > 3 Then\n Console.WriteLine(1)\n End If\n " + Sel)]
    [TestCase("after an If/Else", " If x > 3 Then\n Console.WriteLine(1)\n Else\n Console.WriteLine(2)\n End If\n " + Sel)]
    [TestCase("in a Then", " If x > 3 Then\n " + Sel + "\n End If")]
    [TestCase("in an Else", " If x < 3 Then\n Console.WriteLine(1)\n Else\n " + Sel + "\n End If")]
    [TestCase("after a While", " While x < 5\n x += 1\n Wend\n " + Sel)]
    [TestCase("after a For", " For i As Integer = 1 To 2\n Console.WriteLine(i)\n Next\n " + Sel)]
    [TestCase("after a Do Loop", " Do\n x += 1\n Loop Until x > 6\n " + Sel)]
    [TestCase("after a For Each", " Dim l As New List(Of Integer)()\n For Each v As Integer In l\n Console.WriteLine(v)\n Next\n " + Sel)]
    [TestCase("after a Try", " Try\n Console.WriteLine(1)\n Catch ex As Exception\n End Try\n " + Sel)]
    [TestCase("in a Try", " Try\n " + Sel + "\n Catch ex As Exception\n End Try")]
    [TestCase("in a Catch", " Try\n Console.WriteLine(1)\n Catch ex As Exception\n " + Sel + "\n End Try")]
    [TestCase("in a Case", " Select Case x\n Case 5\n " + Sel + "\n End Select")]
    public void ASelectCase_IsEmitted(string where, string body)
    {
        var cs = Cs(InMain(body));
        Assert.That(cs, Does.Contain("\"marker\""), where + ":\n" + cs);
        Assert.That(cs, Does.Contain("\"end\""), where + ":\n" + cs);
    }

    private const string If = "If x > 3 Then\n Console.WriteLine(\"marker\")\n End If";

    [TestCase("in a Case", " Select Case x\n Case 5\n " + If + "\n End Select")]
    [TestCase("in a Case Else", " Select Case x\n Case 1\n Console.WriteLine(1)\n Case Else\n " + If + "\n End Select")]
    [TestCase("in a Finally", " Try\n Console.WriteLine(1)\n Finally\n " + If + "\n End Try")]
    public void AnIf_IsEmitted(string where, string body)
    {
        var cs = Cs(InMain(body));
        Assert.That(cs, Does.Contain("\"marker\""), where + ":\n" + cs);
        Assert.That(cs, Does.Contain("\"end\""), where + ":\n" + cs);
    }
}

[TestFixture]
[Category("Integration")]   // builds and runs with dotnet through the CLI, and spawns node
public class CSharpNestedTerminatorExecutionTests
{
    /// <summary>
    /// Every placement in one program; each letter records one statement that ran. The expected
    /// line is what VB semantics give. JavaScript runs it too (it needed the `_sel0` fix: two
    /// Selects in one scope). C++ is not a leg: a Select in both a Try and its Catch jumps past
    /// an initialisation there, an unrelated gap.
    /// </summary>
    private const string Program = @"
Function Name(n As Integer) As String
    Select Case n
        Case 1 : Return ""one""
        Case 2 : Return ""two""
        Case Else : Return ""many""
    End Select
End Function

Sub Main()
    Dim x As Integer = 5
    Dim log As String = """"
    If x > 3 Then
        log = log & ""a""
    Else
        log = log & ""b""
    End If
    Select Case x
        Case 5 : log = log & ""c""
        Case Else : log = log & ""d""
    End Select
    If x > 3 Then
        Select Case x
            Case 5 : log = log & ""e""
        End Select
    Else
        Select Case x
            Case 5 : log = log & ""f""
        End Select
    End If
    For i As Integer = 1 To 2
        log = log & CStr(i)
    Next
    Select Case x
        Case 5 : log = log & ""g""
    End Select
    While x < 7
        x += 1
    Wend
    Select Case x
        Case 7 : log = log & ""h""
    End Select
    Dim nums As New List(Of Integer)()
    nums.Add(1)
    nums.Add(2)
    For Each v As Integer In nums
        log = log & ""i""
    Next
    Select Case x
        Case 7 : log = log & ""j""
    End Select
    Try
        Select Case x
            Case 7 : log = log & ""k""
        End Select
        Throw New Exception(""x"")
    Catch ex As Exception
        Select Case x
            Case 7 : log = log & ""l""
        End Select
    Finally
        If x = 7 Then
            log = log & ""m""
        End If
    End Try
    Select Case x
        Case 7
            If x > 3 Then
                log = log & ""n""
            Else
                log = log & ""o""
            End If
            Select Case x
                Case 7 : log = log & ""p""
            End Select
        Case Else
            log = log & ""q""
    End Select
    Select Case x
        Case 1 : log = log & ""r""
        Case Else
            If x > 3 Then log = log & ""s""
    End Select
    Do
        x += 1
    Loop Until x > 8
    Select Case x
        Case 9 : log = log & ""t""
    End Select
    Console.WriteLine(log)
    Console.WriteLine(Name(1) & Name(2) & Name(3))
    Console.WriteLine(""end"")
End Sub";

    private const string Expected = "ace12ghiijklmnpst\nonetwomany\nend";

    [Test]
    public void EveryPlacement_Runs() =>
        Assert.That(CliTestHarness.CompileRunCSharp(Program).Replace("\r\n", "\n").TrimEnd('\n'),
            Is.EqualTo(Expected));

    [Test]
    public void EveryPlacement_Runs_OnJavaScript() =>
        Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(Program).Replace("\r\n", "\n").TrimEnd('\n'),
            Is.EqualTo(Expected));
}
