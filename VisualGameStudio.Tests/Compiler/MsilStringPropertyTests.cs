using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A .NET property read on a <c>String</c> receiver (<c>s.Length</c>), on MSIL.
///
/// <para>⛔ <b>What was wrong.</b> <c>Visit(IRFieldAccess)</c> had no arm for a String receiver,
/// so <c>s.Length</c> fell through to the default <c>ldfld int32 [mscorlib]System.String::
/// 'Length'</c>. <c>ilasm</c> accepts a field reference it never resolves, so the program
/// ASSEMBLED and died at run time with <c>MissingFieldException: Field not found:
/// 'System.String.Length'</c>. Now it emits <c>callvirt instance int32 [mscorlib]System.String::
/// get_Length()</c> — see <c>MSILCodeGenerator.StringMembers</c> / <c>TryStringMember</c>.</para>
///
/// <para>⚠ <b>The METHOD path beside it was never broken.</b> <c>s.ToUpper()</c> and
/// <c>s.Substring(1, 3)</c> already went out as <c>callvirt</c> through
/// <c>Visit(IRInstanceMethodCall)</c>, which renders the receiver and calls it correctly — only a
/// member reaching <c>Visit(IRFieldAccess)</c> (i.e. named WITHOUT parentheses) was affected. See
/// <see cref="StringMethod_WasAlreadyWorking_BeforeThisFix"/> and
/// <see cref="StringMethodWithArguments_WasAlreadyWorking_BeforeThisFix"/>.</para>
///
/// <para>⛔ <b>The refusal is stronger here than for the Collection/Exception member tables
/// beside it.</b> Those types may legitimately grow a field a future table row should name;
/// <c>System.String</c> has NO public instance fields at all, so any member reaching this point
/// and not found in <c>StringMembers</c> is refused at compile time (<c>GenerateFailed</c>)
/// rather than falling through to an <c>ldfld</c> that cannot be right whatever it names. See the
/// "AN UNRECORDED MEMBER IS REFUSED" section.</para>
///
/// <para>⚠ <b>Not fixed, not widened to: the interface-property read.</b> <c>h.Slot</c> through an
/// <c>IHolder</c> reference still gives <c>MissingFieldException: 'IHolder.Slot'</c> —
/// <c>TryResolveProperty</c> resolves through <c>TryFindClass</c> only, so an interface receiver
/// misses every arm (the String one included) and still falls to the old <c>ldfld</c>. The C#
/// backend cannot be the oracle for this shape either: it emits an accessor-less interface
/// property and does not compile (CS0548 + CS0200). JavaScript answers 5. <b>This is recorded as
/// a known, pre-existing gap and deliberately has NO test here</b> — pinning MSIL's current
/// <c>MissingFieldException</c> would assert a defect as correct behaviour, and there is no
/// backend that computes the right answer to assert it against instead.</para>
///
/// <para>⚠ <b><c>HashSet.Count</c> is unobservable, unrelated to this family.</b> <c>h.Add(1)</c>
/// is refused first, by <c>TryCollectionMember</c> — a pre-existing gap this fix neither causes
/// nor touches — so no runnable program can reach a <c>HashSet</c>'s <c>Count</c>. Not tested
/// here for the same reason <see cref="MsilForEachTests"/> leaves the standalone <c>IRFor</c>
/// bug undisturbed: it belongs to a different defect.</para>
///
/// <para>⚠ <b><c>callvirt</c> vs <c>call</c> for <c>get_Length()</c>, considered and kept as
/// <c>callvirt</c>.</b> <c>System.String</c> is sealed and <c>get_Length</c> is not virtual, so
/// there is no override for <c>callvirt</c>'s dispatch to find, and both spellings give the
/// identical <c>NullReferenceException</c> on a null receiver (<c>callvirt</c>'s null check is
/// guaranteed by the CLI spec; <c>call</c>'s is a JIT implementation detail). No shape can
/// distinguish them — not tested here — but every other accessor emission in this file
/// (<c>EmitPropertyGet</c>, the collection-member arm, the exception-member arm) spells it
/// <c>callvirt</c>, and this site matches them for the same reason.</para>
///
/// <para>⚠ Kept to ONE shape per test — see <see cref="MsilStringIntrinsicTests"/>'s fixture note
/// for why.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class MsilStringPropertyTests
{
    /// <summary>Both .NET backends compile AND RUN the program, and agree with each other.</summary>
    private static void MsilAgreesWithCSharp(string program, string expected)
    {
        var cs = FourBackends.Norm(FourBackends.RunEmittedCSharp(program));
        var msil = FourBackends.Norm(MsilHarness.RunExpectingSuccess(program));
        Assert.Multiple(() =>
        {
            Assert.That(cs, Is.EqualTo(expected), "C# (the reference .NET backend)");
            Assert.That(msil, Is.EqualTo(expected), "MSIL");
        });
    }

    /// <summary>MSIL refuses to generate the program at all — a named diagnostic, not a crash.</summary>
    private static void Refused(string program)
    {
        var r = MsilHarness.Run(program);
        Assert.That(r.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.GenerateFailed), r.Report);
    }

    // ============================================================================================
    // THE DEFECT — s.Length itself, and the method-call path beside it that must keep working.
    // ============================================================================================

    [Test]
    public void LengthOfAStringLocal()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim s As String = ""hello""
    PrintLine(CStr(s.Length))
End Sub", "5");

    [Test]
    public void LengthOfAnEmptyString_IsZero()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim s As String = """"
    PrintLine(CStr(s.Length))
End Sub", "0");

    [Test]
    public void LengthOfAStringLiteralReceiver()
        => MsilAgreesWithCSharp(@"
Sub Main()
    PrintLine(CStr(""hello"".Length))
End Sub", "5");

    /// <summary>The METHOD half beside the broken property half — never affected, unchanged
    /// by this fix, kept here so a future regression on the String arm cannot swallow it.</summary>
    [Test]
    public void StringMethod_WasAlreadyWorking_BeforeThisFix()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim s As String = ""hello""
    PrintLine(s.ToUpper())
End Sub", "HELLO");

    [Test]
    public void StringMethodWithArguments_WasAlreadyWorking_BeforeThisFix()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim s As String = ""hello""
    PrintLine(s.Substring(1, 3))
End Sub", "ell");

    /// <summary>A property read feeding a method call's result — chains the fixed arm onto the
    /// arm that already worked.</summary>
    [Test]
    public void LengthOfAMethodCallsResult_ChainedOffTheFixedArm()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim s As String = ""hello""
    PrintLine(CStr(s.ToUpper().Length))
End Sub", "5");

    // ============================================================================================
    // s.Length IN THE PLACES IT ACTUALLY GETS WRITTEN — a loop bound, a parameter, a field's
    // string. Every one of these MissingFieldException'd before the fix.
    // ============================================================================================

    [Test]
    public void LengthAsALoopBound()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim s As String = ""abc""
    For i As Integer = 0 To s.Length - 1
        PrintLine(CStr(i))
    Next
End Sub", "0\n1\n2");

    [Test]
    public void LengthOfAParameter()
        => MsilAgreesWithCSharp(@"
Function Size(s As String) As Integer
    Return s.Length
End Function

Sub Main()
    PrintLine(CStr(Size(""hello"")))
End Sub", "5");

    [Test]
    public void LengthOfAStringHeldInAField()
        => MsilAgreesWithCSharp(@"
Class Bag
    Public Tag As String
End Class

Sub Main()
    Dim b As New Bag()
    b.Tag = ""hello""
    PrintLine(CStr(b.Tag.Length))
End Sub", "5");

    // ============================================================================================
    // RISKY EDGE — controls. NONE of these was ever broken; ALL THREE together are what kills a
    // mutant that widens the String arm and swallows the receiver-type test (`s5-receiver-test-
    // removed`). List.Count and Dictionary.Count share the collection-member visitor arm with
    // String.Length; Array.Length is a COMPLETELY DIFFERENT path — `ldlen` + `conv.i4`, not an
    // accessor call at all — so it alone cannot be assumed safe just because Count is.
    // ============================================================================================

    [Test]
    public void ListCount_IsAControl_NeverBroken()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(1)
    l.Add(2)
    PrintLine(CStr(l.Count))
End Sub", "2");

    [Test]
    public void DictionaryCount_IsAControl_NeverBroken()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim d As New Dictionary(Of String, Integer)()
    d.Add(""a"", 1)
    PrintLine(CStr(d.Count))
End Sub", "1");

    /// <summary>⭐ A DIFFERENT emission path from Count (the dedicated <c>ldlen</c> opcode) — a
    /// control that a future widening of the String property arm must not accidentally reroute.</summary>
    [Test]
    public void ArrayLength_IsAControlOnADifferentPath_NeverBroken()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Dim a(2) As Integer
    a(0) = 5
    PrintLine(CStr(a.Length))
End Sub", "2");

    // ============================================================================================
    // CONTROLS — the class-member arms beside the String one, unaffected and unchanged.
    // ============================================================================================

    [Test]
    public void UserClassAutoProperty_IsAControl()
        => MsilAgreesWithCSharp(@"
Class Box
    Public Property Alpha As Integer
End Class

Sub Main()
    Dim b As New Box()
    b.Alpha = 7
    PrintLine(CStr(b.Alpha))
End Sub", "7");

    [Test]
    public void UserClassField_IsAControl()
        => MsilAgreesWithCSharp(@"
Class Bag
    Public N As Integer
End Class

Sub Main()
    Dim b As New Bag()
    b.N = 9
    PrintLine(CStr(b.N))
End Sub", "9");

    [Test]
    public void ExceptionMessage_IsAControl()
        => MsilAgreesWithCSharp(@"
Sub Main()
    Try
        Throw New Exception(""boom"")
    Catch ex As Exception
        PrintLine(ex.Message)
    End Try
End Sub", "boom");

    // ============================================================================================
    // RISKY EDGE — an unrecorded String member is now REFUSED at compile time, where it
    // previously failed at RUN time (MissingFieldException, measured on the parent tree for every
    // shape below). The refusal moves the failure earlier; it does not break a shape that worked.
    // FIVE shapes reach the arm and are needed together to kill the mutant that replaces the
    // refusal with a silent fall-through to the old ldfld (`s7-unknown-member-falls-through`).
    // ============================================================================================

    [Test]
    public void AnUnknownStringMember_IsRefused()
        => Refused(@"
Sub Main()
    Dim s As String = ""hello""
    PrintLine(CStr(s.Foo))
End Sub");

    /// <summary>A name that LOOKS plausible for a String (there is no public <c>Chars</c> field —
    /// the indexer is <c>get_Chars(Int32)</c>, not a parameterless member) still reaches the
    /// refusal rather than a guess.</summary>
    [Test]
    public void CharsWithoutAnIndexer_IsRefused()
        => Refused(@"
Sub Main()
    Dim s As String = ""hello""
    PrintLine(CStr(s.Chars))
End Sub");

    /// <summary><c>String.Empty</c> is a STATIC field on the TYPE, not an instance member on a
    /// receiver — reached the same way and refused the same way.</summary>
    [Test]
    public void EmptyOnAnInstance_IsRefused()
        => Refused(@"
Sub Main()
    Dim s As String = ""hello""
    PrintLine(s.Empty)
End Sub");

    /// <summary>⚠ A String METHOD named WITHOUT parentheses reaches the SAME arm as a genuinely
    /// unknown member — <c>ToUpper</c> is a real method, but naming it with no call syntax makes
    /// it a field access, not a method call.</summary>
    [Test]
    public void AParenthesisFreeStringMethodName_IsRefused()
        => Refused(@"
Sub Main()
    Dim s As String = ""hello""
    PrintLine(s.ToUpper)
End Sub");

    [Test]
    public void AnotherParenthesisFreeStringMethodName_IsRefused()
        => Refused(@"
Sub Main()
    Dim s As String = ""hello""
    PrintLine(s.Trim)
End Sub");

    // ============================================================================================
    // RISKY EDGE — a null String receiver diverges from C# BY DESIGN, predates this family, and
    // is not this family's to fix: the C# backend initialises a declared-but-unassigned String
    // local to "" (so s.Length reads 0 there), MSIL does not (so it is a real null dereference).
    // MSIL's own outcome only — never compared against C#'s "0" as if that were the shared answer.
    // ============================================================================================

    [Test]
    public void LengthOfAnUnassignedStringLocal_IsANullReference_UnlikeCSharp()
    {
        var r = MsilHarness.Run(@"
Sub Main()
    Dim s As String
    PrintLine(CStr(s.Length))
End Sub");
        Assert.Multiple(() =>
        {
            Assert.That(r.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed), r.Report);
            Assert.That(r.Output, Does.Contain("NullReferenceException"), r.Report);
        });
    }
}
