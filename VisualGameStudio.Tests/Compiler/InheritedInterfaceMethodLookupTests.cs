using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.IR;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #131 — `InterfaceImplementationLookup.InheritedInterfaceMethods`, the ONE rule that says which interface methods a class takes on through its own Implements list but leaves to an
//  INHERITED method. C++ (a forwarding override) and MSIL (a `newslot virtual final` stub) both ask it, so a change here moves both. This fixture asks it DIRECTLY, over the IR of small
//  programs (InheritedMethodProbes), and runs nothing. Five tests, each a list of cases:
//    - one entry per interface SLOT, naming the declaring class (two interfaces: two entries; a class that lists no interface: nothing);
//    - the class's own method fills a slot only with the slot's signature (another case too), so a same-NAME method with other parameters leaves it to the base;
//    - the NEAREST base declaring a matching method wins, even past a nearer same-named method with other parameters or another parameter type;
//    - the signature: name (any case), parameter count, each type and ByRef, the return type; not Optional, ParamArray or parameter names; a generic method and a Shared one never match;
//    - a cyclic base chain terminates.
//  Mutants killed (S/t131/tw/mut): M1 no loop, M3 a case-sensitive name, M4 the farthest base, M5 the own method matched by name only, M9 a Shared method listed, M10 parameter types not compared,
//  M13 ByRef not compared, M14 the return type not compared, M15 a generic method matching, M16 Optional/ParamArray compared, M17 the cycle guard removed, M23 the base's interfaces walked too.
// ================================================================================================

[TestFixture]
public class InheritedInterfaceMethodLookupTests
{
    private static IRModule Build(string source) => JsTestSupport.BuildModule(source);

    private static IRClass Class(IRModule module, string name) => module.Classes.Values.Single(c => c.Name == name);

    /// <summary><c>IShape.Area &lt;- BaseShape.Area</c>: the slot, then the class that DECLARES the method filling it and the method's own spelling.</summary>
    private static string Describe(InterfaceImplementationLookup.InheritedMethod m) => $"{m.Interface.Name}.{m.InterfaceMethod.Name} <- {m.DeclaringClass.Name}.{m.Method.Name}";

    private static string[] Described(string source, string className)
    {
        var module = Build(source);
        return InterfaceImplementationLookup.InheritedInterfaceMethods(module, Class(module, className)).Select(Describe).ToArray();
    }

    private static string Probe(string id) => InheritedMethodProbes.All.Single(p => p.Id == id).Source;

    /// <summary>A program whose interface holds one member and whose base class holds another, the class declaring nothing.</summary>
    private static string Program(string interfaceMember, string baseMember)
        => $$"""
            Interface IThing
                {{interfaceMember}}
            End Interface

            Class BaseThing
                {{baseMember}}
            End Class

            Class Thing
                Inherits BaseThing
                Implements IThing
            End Class
            """;

    private const string FunctionBody = "\n        Return 1\n    End Function";

    // ============================================================================================
    // one entry per slot
    // ============================================================================================

    /// <summary>
    /// A method the class inherits is ONE entry per interface slot, with every fact a backend needs. Two interfaces declaring the same method (m14x) are two entries naming one method — MSIL needs a
    /// stub per slot — and so are two spellings of the parameter (m25x). A class that lists no interface yields nothing, whatever its base lists (m10b). M1 lists nothing; M23 lists m10b's.
    /// </summary>
    [Test]
    public void EachSlotTheClassLeavesToItsBase_IsOneEntry_NamingTheDeclaringClass()
    {
        var m01 = Build(Probe("m01x_function"));
        var plain = InterfaceImplementationLookup.InheritedInterfaceMethods(m01, Class(m01, "Rect")).ToList();
        var m10b = Build(Probe("m10b_baseimplements"));

        Assert.Multiple(() =>
        {
            Assert.That(plain, Has.Count.EqualTo(1));
            Assert.That(plain[0].Class.Name, Is.EqualTo("Rect"));
            Assert.That(plain[0].InterfaceMethod.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "w", "h" }), "the slot's own parameter names: the C++ forwarder is declared with them");
            Assert.That(Describe(plain[0]), Is.EqualTo("IShape.Area <- BaseShape.Area"));

            Assert.That(Described(Probe("m14x_twointerfaces"), "Both"), Is.EqualTo(new[] { "IA.Name <- BaseBoth.Name", "IB.Name <- BaseBoth.Name" }));
            Assert.That(Described(Probe("m25x_twointerfaces_othernames"), "Both"), Is.EqualTo(new[] { "IA.Pick <- BasePick.Pick", "IB.Pick <- BasePick.Pick" }));
            Assert.That(Described(Probe("m27x_twointerfaces_othertypes"), "Both"), Is.EqualTo(new[] { "IA.Area <- GrandShape.Area", "IB.Area <- MidShape.Area" }), "the same name, two types, two bases");
            Assert.That(Described(Probe("m09x_grandparent"), "Rect"), Is.EqualTo(new[] { "IShape.Area <- GrandShape.Area" }), "declared on the grandparent");

            Assert.That(InterfaceImplementationLookup.InheritedInterfaceMethods(m10b, Class(m10b, "Rect")), Is.Empty, "Rect lists no interface: the base's mapping stands");
            Assert.That(InterfaceImplementationLookup.InheritedInterfaceMethods(m10b, Class(m10b, "BaseShape")), Is.Empty, "the base declares its own");
        });
    }

    // ============================================================================================
    // the class's own method, and the nearest base
    // ============================================================================================

    /// <summary>
    /// The class's own method fills the slot only with the slot's signature. It does with another case of the name (M3 lists the base's method), and it does NOT with the same name and other
    /// parameters (m06y: the class declares Area() and inherits the Area(scale) its interface asks for — M5, which matches by name only, leaves the slot empty).
    /// </summary>
    [Test]
    public void TheClassesOwnMethod_FillsTheSlotOnlyWithTheSlotsSignature()
    {
        const string ownAnotherCase = """
            Interface IShape
                Function Area() As Integer
            End Interface

            Class BaseShape
                Public Function Area() As Integer
                    Return 1
                End Function
            End Class

            Class Sq
                Inherits BaseShape
                Implements IShape
                Public Function AREA() As Integer
                    Return 2
                End Function
            End Class
            """;

        Assert.Multiple(() =>
        {
            Assert.That(Described(ownAnotherCase, "Sq"), Is.Empty, "its own AREA fills Area, so the base's is not listed");
            Assert.That(Described(Probe("m06y_ownandinherited"), "Sq"), Is.EqualTo(new[] { "IShape.Area <- BaseShape.Area" }));
        });
    }

    /// <summary>
    /// The nearest base declaring a MATCHING method wins: m17x (parent and grandparent both declare Area(): the parent — M4 takes the grandparent), m24x (the parent's Area(Long) is not a match, so the
    /// grandparent's, and the entry names the class that DECLARES it: a C++ call qualified by the parent called the Long one), m26x (the parent's Area(Double) As Integer: the same count and return type, so
    /// only the parameter TYPE tells — M10), and m16x (the base spells the name AREA: a match, and the entry keeps the base's spelling — M3).
    /// </summary>
    [Test]
    public void TheNearestMatchingBaseWins_PastANearerSameNamedMethodOfOtherParameters()
        => Assert.Multiple(() =>
        {
            Assert.That(Described(Probe("m17x_nearestbase"), "Sq"), Is.EqualTo(new[] { "IShape.Area <- MidShape.Area" }));
            Assert.That(Described(Probe("m24x_nearersamename"), "Rect"), Is.EqualTo(new[] { "IShape.Area <- GrandShape.Area" }));
            Assert.That(Described(Probe("m26x_nearerdouble"), "Rect"), Is.EqualTo(new[] { "IShape.Area <- GrandShape.Area" }));
            Assert.That(Described(Probe("m16x_othercase"), "Sq"), Is.EqualTo(new[] { "IShape.Area <- BaseShape.AREA" }));
        });

    // ============================================================================================
    // what a match is
    // ============================================================================================

    /// <summary>
    /// The signature: the parameter COUNT, each parameter's TYPE and ByRef-ness and the RETURN type are part of it (M10, M13, M14); Optional, ParamArray and parameter names are not (M16); a Shared
    /// method (m18s) and a generic one never match (M9, M15 — the front end cannot write a generic method into a slot, so the IR is edited by hand). Every pair is one source and its twin with one
    /// change, so a check that is "never a match" fails too.
    /// </summary>
    [Test]
    public void TheSignatureIsNameCountTypesByRefAndReturn_NotOptionalParamArrayOrNames()
    {
        const string slot = "Function Area(w As Integer, h As Integer) As Integer";
        string baseWith(string parameters, string returns = "Integer") => $"Public Function Area({parameters}) As {returns}" + FunctionBody;
        var area = new[] { "IThing.Area <- BaseThing.Area" };

        var generic = Build(Probe("m01x_function"));
        Class(generic, "BaseShape").Methods.Single(m => m.Name == "Area").GenericParameters.Add("T");

        Assert.Multiple(() =>
        {
            // count, types, names, return type
            Assert.That(Described(Program(slot, baseWith("w As Integer, h As Integer")), "Thing"), Is.EqualTo(area), "the control");
            Assert.That(Described(Program(slot, baseWith("a As Integer, b As Integer")), "Thing"), Is.EqualTo(area), "parameter names are not part of it");
            Assert.That(Described(Program(slot, baseWith("w As Integer")), "Thing"), Is.Empty, "one parameter too few");
            Assert.That(Described(Program(slot, baseWith("w As Integer, h As Integer, d As Integer")), "Thing"), Is.Empty, "one too many");
            Assert.That(Described(Program(slot, baseWith("w As Integer, h As String")), "Thing"), Is.Empty, "a String for an Integer");
            Assert.That(Described(Program(slot, baseWith("w As Long, h As Long")), "Thing"), Is.Empty, "a Long for an Integer");
            Assert.That(Described(Program(slot, baseWith("w As Integer, h As Integer", "Long")), "Thing"), Is.Empty, "a Long result for an Integer slot");
            Assert.That(Described(Program("Sub Area()", "Public Sub Area()\n    End Sub"), "Thing"), Is.EqualTo(area), "a Sub for a Sub");
            Assert.That(Described(Program("Function Area() As Integer", "Public Sub Area()\n    End Sub"), "Thing"), Is.Empty, "a Sub for a Function slot");

            // ByRef
            Assert.That(Described(Program("Sub Bump(ByRef x As Integer)", "Public Sub Bump(ByRef x As Integer)\n        x = x + 1\n    End Sub"), "Thing"), Is.EqualTo(new[] { "IThing.Bump <- BaseThing.Bump" }), "ByRef for ByRef");
            Assert.That(Described(Program("Sub Bump(ByRef x As Integer)", "Public Sub Bump(x As Integer)\n        x = x + 1\n    End Sub"), "Thing"), Is.Empty, "a ByVal base method for a ByRef slot");
            Assert.That(Described(Program("Sub Bump(x As Integer)", "Public Sub Bump(ByRef x As Integer)\n        x = x + 1\n    End Sub"), "Thing"), Is.Empty, "a ByRef base method for a ByVal slot");

            // Optional and ParamArray are not part of it, in either direction
            Assert.That(Described(Program("Function Scale(x As Integer, Optional k As Integer = 2) As Integer", "Public Function Scale(x As Integer, k As Integer) As Integer" + FunctionBody), "Thing"), Is.EqualTo(new[] { "IThing.Scale <- BaseThing.Scale" }), "an Optional slot, a plain method");
            Assert.That(Described(Program("Function Scale(x As Integer, k As Integer) As Integer", "Public Function Scale(x As Integer, Optional k As Integer = 2) As Integer" + FunctionBody), "Thing"), Is.EqualTo(new[] { "IThing.Scale <- BaseThing.Scale" }), "a plain slot, an Optional method");
            Assert.That(Described(Program("Function Sum(ParamArray xs() As Integer) As Integer", "Public Function Sum(xs() As Integer) As Integer" + FunctionBody), "Thing"), Is.EqualTo(new[] { "IThing.Sum <- BaseThing.Sum" }), "a ParamArray slot, an array method");
            Assert.That(Described(Program("Function Sum(xs() As Integer) As Integer", "Public Function Sum(ParamArray xs() As Integer) As Integer" + FunctionBody), "Thing"), Is.EqualTo(new[] { "IThing.Sum <- BaseThing.Sum" }), "an array slot, a ParamArray method");

            // Shared and generic
            Assert.That(Described(Program("Function Area() As Integer", "Public Function Area() As Integer" + FunctionBody), "Thing"), Is.EqualTo(area), "the control of the next two");
            Assert.That(Described(Probe("m18s_shared"), "Sq"), Is.Empty, "a Shared method fills nothing");
            Assert.That(InterfaceImplementationLookup.InheritedInterfaceMethods(generic, Class(generic, "Rect")), Is.Empty, "a generic method never matches");
        });
    }

    /// <summary>
    /// A cyclic base chain terminates, and so does a class that inherits ITSELF. The front end does not let a program inherit in a circle, so the IR is edited by hand. The walk has a `seen` set; M17
    /// (it removed) walks forever — so each lookup runs on its own background thread with a limit and the test FAILS, instead of freezing the host.
    /// </summary>
    [Test]
    public void ACyclicBaseChain_AndAClassThatInheritsItself_Terminate()
    {
        var cycle = Build("""
            Interface IShape
                Function Area() As Integer
            End Interface

            Class A
                Implements IShape
            End Class

            Class B
            End Class
            """);
        Class(cycle, "A").BaseClass = "B";
        Class(cycle, "B").BaseClass = "A";

        var self = Build(Probe("m01x_function"));
        Class(self, "Rect").BaseClass = "Rect";

        List<InterfaceImplementationLookup.InheritedMethod>? fromCycle = null, fromSelf = null;
        var finished = new CountdownEvent(2);
        new Thread(() => { fromCycle = InterfaceImplementationLookup.InheritedInterfaceMethods(cycle, Class(cycle, "A")).ToList(); finished.Signal(); }) { IsBackground = true }.Start();
        new Thread(() => { fromSelf = InterfaceImplementationLookup.InheritedInterfaceMethods(self, Class(self, "Rect")).ToList(); finished.Signal(); }) { IsBackground = true }.Start();

        Assert.That(finished.Wait(TimeSpan.FromSeconds(20)), Is.True, "a lookup is still walking a circular base chain after 20 s");
        Assert.Multiple(() =>
        {
            Assert.That(fromCycle, Is.Empty, "the chain A, B, A holds no method");
            Assert.That(fromSelf, Is.Empty, "a class that inherits itself holds none either");
        });
    }
}
