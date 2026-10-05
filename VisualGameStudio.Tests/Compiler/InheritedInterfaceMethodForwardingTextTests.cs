using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #131 — the SHAPE of what the C++ and MSIL backends write for an interface method a class inherits from its base. Nothing runs here and nothing spawns: the text is read. Five tests,
//  each a table, each through the standard pipeline, the aggressive pipeline and the project entry point (`BasicCompiler.CompileProjectFiles`, what the IDE's build calls) — and, for C++, the split
//  emission the real `BasicLang build` writes (`App.g.h`).
//    C++   - the forwarder is `<the interface's declarator> override { return Declaring::M(args); }`, a bare call for a Sub, qualified by the class that DECLARES the method, not by the direct base
//            (M12: with Area(Integer) on the grandparent and Area(Long) on the parent, `MidShape::Area(w)` called the Long one and printed 300 for 6);
//          - ONE forwarder per C++ signature — name and parameter TYPES, not names (M8 no dedupe, M11 deduped by declarator: `class member cannot be redeclared`); the same name with other
//            types is two forwarders (M21);
//          - a class that declares the method itself, lists no interface, or inherits a Shared method gets none; the interface's pure virtual is byte-identical to before (nine parameter kinds).
//    MSIL  - one private `newslot virtual final` stub per interface SLOT (M22), its `.override` naming the slot in full, forwarding every argument with a `callvirt` on the DECLARING class (M6: `call`
//            skipped an Overrides in a further-derived class — 1 for 3; M26), `ldarg.s` past the third argument (M24);
//          - no stub when the IL signatures of slot and base method differ — a ByRef parameter, which the interface declares without its `&` (M7: a stub there is an InvalidProgramException) — and none
//            for a class that declares the method itself, lists no interface, or inherits a Shared method.
//  Mutants killed (S/t131/tw/mut): M1, M2, M6, M7, M8, M9, M10, M11, M12, M18-M22, M24-M26.
// ================================================================================================

[TestFixture]
public class InheritedInterfaceMethodForwardingTextTests
{
    // ============================================================================================
    // helpers
    // ============================================================================================

    private static string Probe(string id) => InheritedMethodProbes.All.Single(p => p.Id == id).Source;

    private enum Route { Standard, Aggressive, Project }

    private static string Cpp(string source, Route route) => route switch
    {
        Route.Standard => BclE2E.CompileToCppOptimized(source),
        Route.Aggressive => BclE2E.CompileToCppAggressive(source),
        _ => TempExec.Emit(Bk.Cpp, EntryPoint.ProjectRelease, source),
    };

    private static string Msil(string source, Route route) => route switch
    {
        Route.Standard => MsilHarness.CompileToIl(source),
        Route.Aggressive => MsilHarness.CompileToIl(source, aggressive: true),
        _ => TempExec.Emit(Bk.Msil, EntryPoint.ProjectRelease, source),
    };

    /// <summary>The header the real `BasicLang build` writes the class declarations into: the SPLIT emission, from <c>CompileProjectFiles</c> as the build calls it.</summary>
    private static string CppSplitHeader(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t131-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp", OptimizeAggressive = true });
            var result = compiler.CompileProjectFiles(new List<string> { path });
            Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
            var split = new CppCodeGenerator().GenerateSplit(result.CombinedIR, "App", result.Units.Select(u => u.IR).ToList(), emitMain: true);
            Assert.That(split.Files, Does.ContainKey(split.ProjectHeaderFileName));
            return split.Files[split.ProjectHeaderFileName];
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static IEnumerable<(string Name, string Text)> CppEmissions(string source)
    {
        foreach (var route in Enum.GetValues<Route>()) yield return (route.ToString(), Cpp(source, route));
        yield return ("split", CppSplitHeader(source));
    }

    /// <summary>The text of <c>class Name ... { ... };</c> (a C++ class or interface), trimmed lines.</summary>
    private static string[] CppClass(string cpp, string name)
    {
        var m = Regex.Match(cpp.Replace("\r\n", "\n"), @"^class " + Regex.Escape(name) + @"\b[^\n]*\n\{.*?\n\};", RegexOptions.Singleline | RegexOptions.Multiline);
        Assert.That(m.Success, Is.True, $"no `class {name}` in:\n{cpp}");
        return m.Value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
    }

    /// <summary>The method forwarders of a class block: every `... override { ... }` line, accessors left out.</summary>
    private static string[] MethodForwarders(string[] classBlock)
        => classBlock.Where(l => l.Contains(" override { ", StringComparison.Ordinal) && !Regex.IsMatch(l, @"\b(get|set)_\w+\(")).ToArray();

    private static void AssertCppForwarders(string id, string cls, params string[] forwarders)
    {
        foreach (var (name, cpp) in CppEmissions(Probe(id)))
        {
            var block = CppClass(cpp, cls);
            Assert.That(MethodForwarders(block), Is.EqualTo(forwarders), $"{id} {cls} through {name}:\n{string.Join("\n", block)}");
        }
    }

    // ============================================================================================
    // C++
    // ============================================================================================

    /// <summary>
    /// The forwarder is the interface's own declarator plus `override`, and the call is qualified by the class that DECLARES the method: a Sub is a bare call (M18), the `&amp;` of a ByRef is in the
    /// declarator, the grandparent is named and not the parent (m09x), the nearest base wins (m17x), a nearer same-named method is passed (m24x — M12: the direct base called the Long overload — and m26x),
    /// the base's spelling is called (m16x: `AREA`), the class's own Area() is no forwarder (m06y), and five parameters keep their order (m28x). M1, M12, M18, M19, M25.
    /// </summary>
    [Test]
    public void TheCppForwarder_IsTheInterfacesDeclarator_QualifiedByTheDeclaringClass()
    {
        AssertCppForwarders("m01x_function", "Rect", "int32_t Area(int32_t w, int32_t h) override { return BaseShape::Area(w, h); }");
        AssertCppForwarders("m02x_sub_string", "FileLog", "void Write(std::string msg) override { BaseLog::Write(msg); }");
        AssertCppForwarders("m03x_byref", "Counter", "void Bump(int32_t& x) override { BaseCounter::Bump(x); }");
        AssertCppForwarders("m09x_grandparent", "Rect", "int32_t Area(int32_t w, int32_t h) override { return GrandShape::Area(w, h); }");
        AssertCppForwarders("m17x_nearestbase", "Sq", "int32_t Area() override { return MidShape::Area(); }");
        AssertCppForwarders("m24x_nearersamename", "Rect", "int32_t Area(int32_t w) override { return GrandShape::Area(w); }");
        AssertCppForwarders("m26x_nearerdouble", "Rect", "int32_t Area(int32_t w) override { return GrandShape::Area(w); }");
        AssertCppForwarders("m16x_othercase", "Sq", "int32_t Area() override { return BaseShape::AREA(); }");
        AssertCppForwarders("m08x_overridable", "Sq", "int32_t Area() override { return BaseShape::Area(); }");
        AssertCppForwarders("m06y_ownandinherited", "Sq", "int32_t Area(int32_t scale) override { return BaseShape::Area(scale); }");
        AssertCppForwarders("m28x_fiveparams", "Mixer", "std::string Mix(int32_t a, std::string b, int32_t c, int32_t d, int32_t e) override { return BaseMixer::Mix(a, b, c, d, e); }");
    }

    /// <summary>
    /// ONE forwarder per C++ signature: two interfaces declaring `Name()` (m14x) are overridden by one function (M8: two — a redefinition), and so are `Pick(int32_t a)` and `Pick(int32_t b)` (m25x — M11,
    /// which dedupes on the declarator, parameter names included, writes two). The same name with other TYPES is two forwarders, each qualified by the base that declares it (m27x — M21 dedupes on the name).
    /// </summary>
    [Test]
    public void OneForwarderIsWrittenPerCppSignature_NotPerSlotAndNotPerName()
    {
        AssertCppForwarders("m14x_twointerfaces", "Both", "std::string Name() override { return BaseBoth::Name(); }");
        AssertCppForwarders("m25x_twointerfaces_othernames", "Both", "int32_t Pick(int32_t a) override { return BasePick::Pick(a); }");
        AssertCppForwarders("m27x_twointerfaces_othertypes", "Both",
            "int32_t Area(int32_t w) override { return GrandShape::Area(w); }",
            "int32_t Area(std::string s) override { return MidShape::Area(s); }");
    }

    /// <summary>
    /// A program with NO inherited method: an interface with nine parameter kinds, each implemented by the class itself. Its pure virtuals now share InterfaceMethodDeclarator with the forwarder; the text must
    /// be what it was (the whole C++ of this program was byte-identical to master's, measured). Nothing is forwarded: not for the class's own methods, not for a class that lists no interface (m10b), not for a
    /// Shared base method (m18s — M9 lists it).
    /// </summary>
    [Test]
    public void NothingIsForwarded_WhenNothingIsInherited_AndTheInterfacesPureVirtualsAreWhatTheyWere()
    {
        const string source = """
            Class Pt
                Public X As Integer
            End Class

            Interface IWorker
                Function Area(w As Integer, h As Integer) As Integer
                Sub Write(msg As String)
                Sub Bump(ByRef x As Integer)
                Function Move(p As Pt, dx As Integer) As String
                Function Sum(ParamArray xs() As Integer) As Integer
                Function Scale(x As Integer, Optional k As Integer = 2) As Integer
                Function Name() As String
                Function Ratio(a As Double, flag As Boolean) As Double
                Function Names(items As List(Of String)) As String
            End Interface

            Class Worker
                Implements IWorker
                Public Function Area(w As Integer, h As Integer) As Integer
                    Return w * h
                End Function
                Public Sub Write(msg As String)
                    Console.WriteLine(msg)
                End Sub
                Public Sub Bump(ByRef x As Integer)
                    x = x + 1
                End Sub
                Public Function Move(p As Pt, dx As Integer) As String
                    p.X = p.X + dx
                    Return "x"
                End Function
                Public Function Sum(ParamArray xs() As Integer) As Integer
                    Return xs.Length
                End Function
                Public Function Scale(x As Integer, Optional k As Integer = 2) As Integer
                    Return x * k
                End Function
                Public Function Name() As String
                    Return "w"
                End Function
                Public Function Ratio(a As Double, flag As Boolean) As Double
                    Return a
                End Function
                Public Function Names(items As List(Of String)) As String
                    Return "n"
                End Function
            End Class

            Module M
                Sub Main()
                    Dim w As IWorker = New Worker()
                    Console.WriteLine(w.Area(2, 3))
                End Sub
            End Module
            """;

        var mastersInterface = new[]
        {
            "class IWorker", "{", "public:", "virtual ~IWorker() = default;",
            "virtual int32_t Area(int32_t w, int32_t h) = 0;",
            "virtual void Write(std::string msg) = 0;",
            "virtual void Bump(int32_t& x) = 0;",
            "virtual std::string Move(std::shared_ptr<Pt> p, int32_t dx) = 0;",
            "virtual int32_t Sum(BasicLang::Array<int32_t> xs) = 0;",
            "virtual int32_t Scale(int32_t x, int32_t k) = 0;",
            "virtual std::string Name() = 0;",
            "virtual double Ratio(double a, bool flag) = 0;",
            "virtual std::string Names(std::shared_ptr<BasicLang::List<std::string>> items) = 0;",
            "};",
        };

        foreach (var (name, cpp) in CppEmissions(source))
        {
            Assert.That(CppClass(cpp, "IWorker"), Is.EqualTo(mastersInterface), name);
            Assert.That(CppClass(cpp, "Worker").Where(l => l.Contains("override", StringComparison.Ordinal)), Is.Empty, $"{name}: Worker declares them all itself");
        }

        AssertCppForwarders("m10b_baseimplements", "Rect");
        AssertCppForwarders("m10b_baseimplements", "BaseShape");
        AssertCppForwarders("m18s_shared", "Sq");
    }

    // ============================================================================================
    // MSIL
    // ============================================================================================

    /// <summary>The stub of a slot, as written (trimmed lines): the header, the `.override`, the stack, the arguments, the forward and the footer.</summary>
    private static string[] Stub(string iface, string slot, string returns, string slotParameters, int arguments, string owner, string method, string baseParameters)
    {
        var lines = new List<string>
        {
            ".method private hidebysig newslot virtual final",
            $"instance {returns} '{iface}.{slot}'({slotParameters}) cil managed",
            "{",
            $".override method instance {returns} '{iface}'::'{slot}'({slotParameters})",
            $".maxstack {Math.Max(8, arguments + 1)}",
            "ldarg.0",
        };
        for (var i = 1; i <= arguments; i++) lines.Add(i <= 3 ? $"ldarg.{i}" : $"ldarg.s {i}");
        lines.Add($"callvirt instance {returns} '{owner}'::'{method}'({baseParameters})");
        lines.Add("ret");
        lines.Add($"}} // end of method {slot} (inherited implementation)");
        return lines.ToArray();
    }

    /// <summary>The lines of <c>.class ... 'Name' ... } // end of class 'Name'</c> (or interface), trimmed.</summary>
    private static string[] IlClass(string il, string name)
    {
        var text = il.Replace("\r\n", "\n");
        var start = Regex.Match(text, @"^\.class [^\n]*'" + Regex.Escape(name) + @"'\n", RegexOptions.Multiline);
        Assert.That(start.Success, Is.True, $"no `.class ... '{name}'` in:\n{il}");
        var endMatch = Regex.Match(text.Substring(start.Index), @"^\} // end of (class|interface) '" + Regex.Escape(name) + "'", RegexOptions.Multiline);
        Assert.That(endMatch.Success, Is.True, $"no end of class or interface '{name}'");
        return text.Substring(start.Index, endMatch.Index).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
    }

    /// <summary>The inherited-method stubs of a class: each `.method private hidebysig newslot virtual final` up to its `(inherited implementation)` footer, accessor stubs (get_/set_) left out.</summary>
    private static List<string[]> Stubs(string[] classBlock)
    {
        var stubs = new List<string[]>();
        for (var i = 0; i < classBlock.Length; i++)
        {
            if (classBlock[i] != ".method private hidebysig newslot virtual final") continue;
            var end = Array.FindIndex(classBlock, i, l => l.EndsWith("(inherited implementation)", StringComparison.Ordinal));
            Assert.That(end, Is.GreaterThan(i), "a stub without its footer");
            var stub = classBlock.Skip(i).Take(end - i + 1).ToArray();
            if (!Regex.IsMatch(stub[1], @"'\w+\.(get|set)_\w+'\(")) stubs.Add(stub);
        }
        return stubs;
    }

    private static void AssertStubs(string id, string cls, params string[][] expected)
    {
        foreach (var route in Enum.GetValues<Route>())
        {
            var stubs = Stubs(IlClass(Msil(Probe(id), route), cls));
            Assert.That(stubs, Has.Count.EqualTo(expected.Length), $"{id} {cls} through {route}: the number of stubs");
            for (var i = 0; i < stubs.Count; i++)
                Assert.That(stubs[i], Is.EqualTo(expected[i]), $"{id} {cls} through {route}: stub {i}");
        }
    }

    /// <summary>
    /// One stub per interface SLOT, whole lines: the header (`private hidebysig newslot virtual final` — M20), the `.override` naming the slot in full, the arguments (`ldarg.s` past the third — M24: m28x),
    /// and a `callvirt` on the class that DECLARES the method (M6: `call` — m08x printed 1 for 3; M26: the direct base — m09x, m24x, m26x). Two interfaces are two stubs (m14x, m25x, m27x — M22 writes one
    /// per name), each with its own `.override`; the base's own spelling is called (m16x: `AREA`). M1 and M2 write none.
    /// </summary>
    [Test]
    public void TheMsilStub_IsOneNewslotVirtualFinalPerSlot_NamingTheSlotInFull_AndForwardingWithCallvirt()
    {
        AssertStubs("m01x_function", "Rect", Stub("IShape", "Area", "int32", "int32, int32", 2, "BaseShape", "Area", "int32, int32"));
        AssertStubs("m02x_sub_string", "FileLog", Stub("ILog", "Write", "void", "string", 1, "BaseLog", "Write", "string"));
        AssertStubs("m08x_overridable", "Sq", Stub("IShape", "Area", "int32", "", 0, "BaseShape", "Area", ""));
        AssertStubs("m09x_grandparent", "Rect", Stub("IShape", "Area", "int32", "int32, int32", 2, "GrandShape", "Area", "int32, int32"));
        AssertStubs("m24x_nearersamename", "Rect", Stub("IShape", "Area", "int32", "int32", 1, "GrandShape", "Area", "int32"));
        AssertStubs("m26x_nearerdouble", "Rect", Stub("IShape", "Area", "int32", "int32", 1, "GrandShape", "Area", "int32"));
        AssertStubs("m16x_othercase", "Sq", Stub("IShape", "Area", "int32", "", 0, "BaseShape", "AREA", ""));
        AssertStubs("m28x_fiveparams", "Mixer", Stub("IMixer", "Mix", "string", "int32, string, int32, int32, int32", 5, "BaseMixer", "Mix", "int32, string, int32, int32, int32"));
        AssertStubs("m14x_twointerfaces", "Both", Stub("IA", "Name", "string", "", 0, "BaseBoth", "Name", ""), Stub("IB", "Name", "string", "", 0, "BaseBoth", "Name", ""));
        AssertStubs("m25x_twointerfaces_othernames", "Both", Stub("IA", "Pick", "int32", "int32", 1, "BasePick", "Pick", "int32"), Stub("IB", "Pick", "int32", "int32", 1, "BasePick", "Pick", "int32"));
        AssertStubs("m27x_twointerfaces_othertypes", "Both", Stub("IA", "Area", "int32", "int32", 1, "GrandShape", "Area", "int32"), Stub("IB", "Area", "int32", "string", 1, "MidShape", "Area", "string"));
    }

    /// <summary>
    /// ⛔ m03x: a ByRef parameter gets NO stub (M7). The interface declares it without its `&amp;`, so the slot is `Bump(int32)` and the base method `Bump(int32&amp;)`: a stub would pass an int32 where an
    /// int32&amp; is expected, and the class — which the CLR now loads — fails at the first call with InvalidProgramException. Leaving it out keeps the TypeLoadException it had (follow-up F1). A class that
    /// lists no interface (m10b) and a Shared base method (m18s — M9) get none either, and the base class and the interface are untouched.
    /// </summary>
    [Test]
    public void NoMsilStubIsWritten_WhenTheIlSignaturesDiffer_OrNothingIsInherited()
    {
        foreach (var route in Enum.GetValues<Route>())
        {
            var byRef = Msil(Probe("m03x_byref"), route);
            Assert.Multiple(() =>
            {
                Assert.That(IlClass(byRef, "Counter").Where(l => l.Contains(".override", StringComparison.Ordinal) || l.Contains("newslot", StringComparison.Ordinal)), Is.Empty, $"{route}: no stub for a ByRef slot");
                Assert.That(IlClass(byRef, "ICounter"), Does.Contain("instance void 'Bump'(int32) cil managed"), $"{route}: the premise — the interface says int32, without the `&`");
                Assert.That(IlClass(byRef, "BaseCounter"), Does.Contain("instance void 'Bump'(int32& 'x') cil managed"), $"{route}: the premise — the base method says int32&");
                Assert.That(Stubs(IlClass(Msil(Probe("m10b_baseimplements"), route), "Rect")), Is.Empty, $"{route}: Rect lists no interface");
                Assert.That(Stubs(IlClass(Msil(Probe("m18s_shared"), route), "Sq")), Is.Empty, $"{route}: a Shared base method fills nothing");
                Assert.That(IlClass(Msil(Probe("m01x_function"), route), "BaseShape").Where(l => l.Contains(".override", StringComparison.Ordinal)), Is.Empty, $"{route}: the base class holds no stub");
            });
        }
    }
}
