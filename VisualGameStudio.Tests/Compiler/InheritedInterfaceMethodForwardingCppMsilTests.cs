using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #131 — an interface METHOD that a class inherits from its base, RUN on C++ and MSIL against vbc, through every entry point. The property twin is
//  InheritedInterfacePropertyForwardingCppMsilTests; this is the method half, and it runs the rows the fast tests (InheritedInterfaceMethodLookupTests, InheritedInterfaceMethodForwardingTextTests)
//  cannot settle by reading text.
//
//  THE SHAPE. `Class Rect : Inherits BaseShape : Implements IShape`, with only BaseShape declaring `Area`. C# and JavaScript always ran it (csc accepts an inherited public member as the implementation;
//  JavaScript has no interfaces at run time), so they are not here. C++ did not compile (`BaseShape::Area` and `IShape::Area` sit in unrelated bases: Rect stays abstract, a call through it is ambiguous)
//  and MSIL did not load (`BaseShape::Area` is not virtual: TypeLoadException "does not have an implementation"). The fix is a forwarding override (C++) and a `newslot virtual final` stub (MSIL) per slot.
//
//  ⭐ THE ORACLE IS vbc, and vbc REJECTS every inherited shape (BC30149: VB needs an `Implements I.M` clause on the member). So each row's expected text is vbc's output for its CONTROL — the same program
//  with the class declaring the method itself and the clause on it — as the implementer stored it (S/t131/probes/mat/<id>.exp). Every program calls the method through the CLASS, an INTERFACE variable
//  and a `List(Of IShape)` loop; the CLI, the CLI with `--optimize` and `CompileProjectFiles` (what the IDE's build calls) each run it. ⚠ Windows owes the MSVC run of every C++ cell (clang++ here).
//
//  The rows (each kills a mutant that the fast tests may catch only in text; S/t131/tw/mut):
//    m01x the plain shape (every mutant that lists or writes nothing: M1, M2);
//    m06y the class declares Area() and inherits Area(scale) (M5: the own method matched by name only);
//    m08x an Overridable base method with an Overrides further down, called through the interface (M6: `call` for `callvirt` prints 1 for 3 — on MSIL this row already ran before #131, the CLR
//         filled the slot itself, so it guards the stub);
//    m14x two interfaces declare Name() (M8: two C++ forwarders; M22: one MSIL stub per name); m25x the same with other parameter names (M11);
//    m17x the nearest base wins (M4);
//    m24x a nearer base declares Area(Long) over the grandparent's Area(Integer) (M12: the C++ call qualified by the direct base prints 300 for 6); m26x a nearer Area(Double) As Integer (M10: parameter
//         types not compared, 999 for 6).
//
//  ⛔ KNOWN GAP (follow-up F1, pinned, named so it flips when it lands): m03x on MSIL. The interface declares a ByRef parameter WITHOUT its `&`, so even a class's OWN implementation fails to load, and
//  the inherited shape keeps the TypeLoadException because the stub is skipped when the IL signatures differ (M7: a stub there is an InvalidProgramException at the first call). C++ prints vbc's text.
//  The other known-wrong cells (JavaScript; a case-mismatched name; shadowing; MustOverride; a member-level `Implements I.M`; the invalid programs of #132) are listed in HANDOFF.md with the follow-ups.
// ================================================================================================

/// <summary>
/// #131 RUN: an interface method filled by an inherited base-class method prints vbc's answer on C++ and MSIL through the class, an interface variable and a list loop, in every entry point.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C++ compiles and the spawned runners share the machine
public class InheritedInterfaceMethodForwardingCppMsilTests
{
    private static IEnumerable<TestCaseData> Rows()
        => new[] { "m01x_function", "m06y_ownandinherited", "m08x_overridable", "m14x_twointerfaces", "m17x_nearestbase", "m24x_nearersamename", "m25x_twointerfaces_othernames", "m26x_nearerdouble" }
            .Select(id => new TestCaseData(InheritedMethodProbes.All.Single(p => p.Id == id)).SetName(id));

    /// <summary>
    /// On C++ and on MSIL the program prints what vbc prints for its control, through the CLI, the CLI with --optimize and CompileProjectFiles. The C++ cell is asserted first, the MSIL cell second, both
    /// inside one multiple-assertion block, so a regression on one backend does not hide the other.
    /// </summary>
    [TestCaseSource(nameof(Rows))]
    public void AnInheritedMethod_PrintsVbcsAnswer_OnCppAndMsil(TempProbe probe)
    {
        TempExec.RequireTool(Bk.Cpp);
        TempExec.RequireTool(Bk.Msil);
        Assert.Multiple(() =>
        {
            TempExec.AssertMatchesInEveryEntryPoint(Bk.Cpp, probe.Source, probe.Vb, probe.Id);
            TempExec.AssertMatchesInEveryEntryPoint(Bk.Msil, probe.Source, probe.Vb, probe.Id);
        });
    }

    /// <summary>
    /// ⛔ m03x (a ByRef parameter) on MSIL is pinned at today's outcome: the program assembles and the CLR throws TypeLoadException for `Counter.Bump` — follow-up F1, the interface declares ByRef without `&amp;`.
    /// Kills M7 (the IL-signature guard removed), which turns it into an InvalidProgramException. A different outcome means F1 landed: update the pin, do not delete it.
    /// </summary>
    [Test]
    public void AByRefParameter_ThrowsTypeLoadOnMsil_KnownGap_F1()
    {
        var run = MsilHarness.Run(InheritedMethodProbes.All.Single(p => p.Id == "m03x_byref").Source);

        Assert.That(run.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed), run.Report);
        Assert.That(run.Output, Does.Contain("TypeLoadException"), run.Report);
        Assert.That(run.Output, Does.Contain("'Bump'").And.Contain("'Counter'").And.Contain("does not have an implementation"), run.Report);
    }
}
