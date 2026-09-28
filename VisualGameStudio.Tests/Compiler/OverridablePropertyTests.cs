using System;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Overridable</c> / <c>Overrides</c> on a PROPERTY.
///
/// <para>⛔ THE MODIFIER WAS PARSED AND THEN THROWN AWAY, at every layer. The parser reads
/// Overridable/Overrides into locals for EVERY class member; the Function and Sub arms copy them
/// onto the node, and the PROPERTY arm copied Access, IsStatic, IsReadOnly and IsWriteOnly and
/// dropped the two it already held — because <c>PropertyNode</c> had nowhere to put them, and
/// neither did <c>IRProperty</c>, while <c>IRMethod</c> carried all four.</para>
///
/// <para>⛔ THE FAILURE WAS SILENT ON BOTH .NET BACKENDS. Measured, compiled and run before the
/// change: reading an overridden property through a base-typed variable answered the BASE's value
/// on MSIL and C#, with no diagnostic from either. The emitted C# was
/// <c>public string Name { get {…} }</c> on BOTH classes — no <c>virtual</c>, no <c>override</c>,
/// not even <c>new</c>, and C# member hiding is only a WARNING, so the file compiled. The emitted
/// IL marked neither accessor <c>virtual</c>, and the call site was ALREADY
/// <c>callvirt instance string Animal::get_Name()</c> — callvirt on a non-virtual method binds
/// statically. A three-level chain answered the TOPMOST value. JavaScript was right by accident:
/// JS class members always dispatch dynamically.</para>
///
/// <para>Every shape here is asserted on all four backends, compiled and run. C++ used to be
/// excluded: it lowered every property read to a field access and so could not reach a Get/Set
/// property at all (task #148). Its accessors now carry <c>virtual</c>/<c>override</c> and every
/// read and write calls them.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class OverridablePropertyTests
{
    private static string Norm(string s) => FourBackends.Norm(s);
    private static string Js(string p) => Norm(JavaScriptExecutionTests.RunJs(p));
    private static string Msil(string p) => Norm(MsilHarness.RunExpectingSuccess(p));
    private static string Cs(string p) => Norm(FourBackends.RunEmittedCSharp(p));

    /// <summary>C++, JavaScript, MSIL and C# all run <paramref name="program"/> and agree.</summary>
    private static void RunsOnEveryBackend(string program, string expected) =>
        FourBackends.RunsOnEveryBackend(program, expected);

    private static string EmittedCs(string program) => ReturnCoercionTests.EmitCSharpForTest(program);
    private static string EmittedIl(string program) => MsilHarness.CompileToIl(program);

    // ---------------------------------------------------------------- the defect, by shape

    /// <summary>The plainest form: a ReadOnly property overridden once, read through the base.</summary>
    [Test]
    public void AnOverriddenReadOnlyProperty_DispatchesToTheDerived()
        => RunsOnEveryBackend("""
            Class Animal
             Public Overridable ReadOnly Property Name As String
              Get
               Return "base"
              End Get
             End Property
            End Class
            Class Dog
             Inherits Animal
             Public Overrides ReadOnly Property Name As String
              Get
               Return "derived"
              End Get
             End Property
            End Class
            Sub Main()
             Dim a As Animal = New Dog()
             PrintLine(a.Name)
            End Sub
            """, "derived");

    /// <summary>A Get/Set property, which emits through a different arm than the ReadOnly one.</summary>
    [Test]
    public void AnOverriddenGetSetProperty_DispatchesToTheDerived()
        => RunsOnEveryBackend("""
            Class Animal
             Protected Store As String = "base"
             Public Overridable Property Tag As String
              Get
               Return Store
              End Get
              Set(value As String)
               Store = value
              End Set
             End Property
            End Class
            Class Dog
             Inherits Animal
             Public Overrides Property Tag As String
              Get
               Return "derived"
              End Get
              Set(value As String)
               Store = value
              End Set
             End Property
            End Class
            Sub Main()
             Dim a As Animal = New Dog()
             PrintLine(a.Tag)
            End Sub
            """, "derived");

    /// <summary>
    /// ⛔ THE SETTER HALF, which a read-only assertion cannot reach. Writing through a base-typed
    /// variable must run the DERIVED setter. Found by mutation: marking only the getter virtual
    /// left every test in this fixture green, because each one only READS. The write binds to the
    /// static type's accessor when the setter is not virtual, so this answers "base:x".
    /// </summary>
    [Test]
    public void AnOverriddenSetter_RunsTheDerivedSetter()
        => RunsOnEveryBackend("""
            Class Animal
             Public Store As String = "?"
             Public Overridable Property Tag As String
              Get
               Return Store
              End Get
              Set(value As String)
               Store = "base:" & value
              End Set
             End Property
            End Class
            Class Dog
             Inherits Animal
             Public Overrides Property Tag As String
              Get
               Return Store
              End Get
              Set(value As String)
               Store = "derived:" & value
              End Set
             End Property
            End Class
            Sub Main()
             Dim a As Animal = New Dog()
             a.Tag = "x"
             PrintLine(a.Tag)
            End Sub
            """, "derived:x");

    /// <summary>
    /// ⚠ THE TEMPLATE METHOD, the shape that makes an overridable property worth having: a BASE
    /// method reading its own Overridable property must see the DERIVED override. This one is not
    /// reachable through the receiver's static type at all — the base method's `Me` is what
    /// dispatches — so it fails even where a direct read might not.
    /// </summary>
    [Test]
    public void ABaseMethodReadingItsOwnOverridableProperty_SeesTheOverride()
        => RunsOnEveryBackend("""
            Class Animal
             Public Overridable ReadOnly Property Name As String
              Get
               Return "base"
              End Get
             End Property
             Public Function Describe() As String
              Return Name
             End Function
            End Class
            Class Dog
             Inherits Animal
             Public Overrides ReadOnly Property Name As String
              Get
               Return "derived"
              End Get
             End Property
            End Class
            Sub Main()
             Dim a As Animal = New Dog()
             PrintLine(a.Describe())
            End Sub
            """, "derived");

    /// <summary>
    /// ⛔ THE SHARPEST FORM. Before the change this answered `first` — the TOPMOST value — on MSIL
    /// and C#. A one-level test cannot tell "dispatches correctly" from "always takes the static
    /// type's accessor" when the static type IS the base; three levels can.
    /// </summary>
    [Test]
    public void APropertyOverriddenTwoLevelsDown_ReachesTheDeepestOverride()
        => RunsOnEveryBackend("""
            Class A
             Public Overridable ReadOnly Property N As String
              Get
               Return "first"
              End Get
             End Property
            End Class
            Class B
             Inherits A
             Public Overrides ReadOnly Property N As String
              Get
               Return "second"
              End Get
             End Property
            End Class
            Class C
             Inherits B
             Public Overrides ReadOnly Property N As String
              Get
               Return "third"
              End Get
             End Property
            End Class
            Sub Main()
             Dim a As A = New C()
             PrintLine(a.N)
            End Sub
            """, "third");

    /// <summary>The middle of a chain is both an override AND the base of the next one.</summary>
    [Test]
    public void AChainStoppedAtTheMiddleOverride_ReachesTheMiddle()
        => RunsOnEveryBackend("""
            Class A
             Public Overridable ReadOnly Property N As String
              Get
               Return "first"
              End Get
             End Property
            End Class
            Class B
             Inherits A
             Public Overrides ReadOnly Property N As String
              Get
               Return "second"
              End Get
             End Property
            End Class
            Class C
             Inherits B
            End Class
            Sub Main()
             Dim a As A = New C()
             PrintLine(a.N)
            End Sub
            """, "second");

    /// <summary>An Integer property, so the fix is not tied to String's emission path.</summary>
    [Test]
    public void AnOverriddenIntegerProperty_DispatchesToTheDerived()
        => RunsOnEveryBackend("""
            Class Animal
             Public Overridable ReadOnly Property N As Integer
              Get
               Return 1
              End Get
             End Property
            End Class
            Class Dog
             Inherits Animal
             Public Overrides ReadOnly Property N As Integer
              Get
               Return 7
              End Get
             End Property
            End Class
            Sub Main()
             Dim a As Animal = New Dog()
             PrintLine(CStr(a.N))
            End Sub
            """, "7");

    /// <summary>An AUTO-property override — a separate emission arm on both backends.</summary>
    [Test]
    public void AnOverriddenAutoProperty_DispatchesToTheDerived()
        => RunsOnEveryBackend("""
            Class Animal
             Public Overridable Property V As Integer
            End Class
            Class Dog
             Inherits Animal
             Public Overrides Property V As Integer
            End Class
            Sub Main()
             Dim a As Animal = New Dog()
             a.V = 5
             PrintLine(CStr(a.V))
            End Sub
            """, "5");

    private const string ThroughAParameter = """
        Class Animal
         Public Overridable ReadOnly Property Name As String
          Get
           Return "base"
          End Get
         End Property
        End Class
        Class Dog
         Inherits Animal
         Public Overrides ReadOnly Property Name As String
          Get
           Return "derived"
          End Get
         End Property
        End Class
        Sub Report(a As Animal)
         PrintLine(a.Name)
        End Sub
        Sub Main()
         Report(New Dog())
        End Sub
        """;

    /// <summary>
    /// A derived instance reaching its override through a base-typed PARAMETER.
    /// ⭐ PROMOTED FROM TWO BACKENDS TO THREE. This ran on JavaScript and C# only, because MSIL
    /// rendered a callee signature from the argument's DYNAMIC type and so called a
    /// `Report(class Dog)` that nobody declared — MissingMethodException. The pin that recorded
    /// that gap went RED when MSIL started taking signatures from the DECLARATION, which is what
    /// a pin is for; it has been deleted and this case now runs on all three.
    /// </summary>
    [Test]
    public void AnOverriddenProperty_ThroughABaseTypedParameter_Dispatches()
        => RunsOnEveryBackend(ThroughAParameter, "derived");

    // ---------------------------------------------------------------- controls

    /// <summary>
    /// ⚠ CONTROL. An Overridable property that nobody overrides must still answer the base's
    /// value. This passed BEFORE the change too — which is exactly the point: it proves the
    /// property machinery worked and only the OVERRIDE was lost, so a fix that broke plain
    /// properties to make overriding work would be caught here.
    /// </summary>
    [Test]
    public void ANonOverriddenOverridableProperty_StillAnswersTheBase()
        => RunsOnEveryBackend("""
            Class Animal
             Public Overridable ReadOnly Property Name As String
              Get
               Return "base"
              End Get
             End Property
            End Class
            Class Dog
             Inherits Animal
            End Class
            Sub Main()
             Dim a As Animal = New Dog()
             PrintLine(a.Name)
            End Sub
            """, "base");

    /// <summary>⚠ CONTROL: a property with no Overridable anywhere is untouched by the change.</summary>
    [Test]
    public void APlainPropertyOnOneClass_IsUnaffected()
        => RunsOnEveryBackend("""
            Class Animal
             Public ReadOnly Property Name As String
              Get
               Return "base"
              End Get
             End Property
            End Class
            Sub Main()
             Dim a As New Animal()
             PrintLine(a.Name)
            End Sub
            """, "base");

    /// <summary>
    /// ⚠ CONTROL: a Shared property is never virtual. `Overridable` on one is not legal VB, but
    /// the emitters compute the modifier from IsStatic first, and a `static virtual` accessor
    /// would not assemble — so the plain Shared property is pinned as still working.
    /// </summary>
    [Test]
    public void ASharedProperty_IsUnaffected()
        => RunsOnEveryBackend("""
            Class Counter
             Public Shared ReadOnly Property N As Integer
              Get
               Return 3
              End Get
             End Property
            End Class
            Sub Main()
             PrintLine(CStr(Counter.N))
            End Sub
            """, "3");

    /// <summary>
    /// ⚠ CONTROL: an Overridable METHOD already dispatched before the change. It is asserted here
    /// so a regression in the shared modifier spelling shows up beside the property it mirrors.
    /// </summary>
    [Test]
    public void AnOverriddenMethod_StillDispatches_OnEveryBackend()
        => FourBackends.RunsOnEveryBackend("""
            Class Animal
             Public Overridable Function Name() As String
              Return "base"
             End Function
            End Class
            Class Dog
             Inherits Animal
             Public Overrides Function Name() As String
              Return "derived"
             End Function
            End Class
            Sub Main()
             Dim a As Animal = New Dog()
             PrintLine(a.Name())
            End Sub
            """, "derived");

    // ---------------------------------------------------------------- the emitted text

    private const string OverriddenPair = """
        Class Animal
         Public Overridable ReadOnly Property Name As String
          Get
           Return "base"
          End Get
         End Property
        End Class
        Class Dog
         Inherits Animal
         Public Overrides ReadOnly Property Name As String
          Get
           Return "derived"
          End Get
         End Property
        End Class
        Sub Main()
         Dim a As Animal = New Dog()
         PrintLine(a.Name)
        End Sub
        """;

    /// <summary>
    /// ⛔ THE C# SPELLING IS PINNED because the run alone cannot distinguish the fix from luck:
    /// emitting `new` instead of `override` would also silence the warning while KEEPING the
    /// wrong answer, and emitting neither is what shipped. The property must be `virtual` on the
    /// base and `override` on the derived, and the two are mutually exclusive in C#.
    /// </summary>
    [Test]
    public void TheEmittedCSharp_MarksTheBaseVirtualAndTheDerivedOverride()
    {
        var cs = EmittedCs(OverriddenPair);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Match(@"public\s+virtual\s+string\s+Name"),
                "the base property must be virtual");
            Assert.That(cs, Does.Match(@"public\s+override\s+string\s+Name"),
                "the derived property must be override");
            Assert.That(cs, Does.Not.Match(@"public\s+virtual\s+override"),
                "virtual and override are mutually exclusive in C#");
            Assert.That(cs, Does.Not.Match(@"public\s+new\s+string\s+Name"),
                "hiding with 'new' silences the warning and KEEPS the wrong answer");
        });
    }

    /// <summary>
    /// ⛔ THE IL SPELLING IS PINNED for the same reason, and the slots matter: the base declares
    /// one (<c>newslot virtual</c>) and the override takes it (<c>virtual</c>, no newslot). An
    /// override emitted with <c>newslot</c> assembles and runs and answers the BASE's value —
    /// the exact failure this fixture exists for, invisible to a run-only assertion.
    /// </summary>
    [Test]
    public void TheEmittedIl_GivesTheBaseANewSlotAndTheOverrideTheSameSlot()
    {
        var il = EmittedIl(OverriddenPair);
        var getters = il.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith(".method") || l.Contains("get_Name()"))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Match(@"\.method[^\n]*specialname\s+newslot\s+virtual"),
                "the base accessor must declare a slot:\n" + string.Join("\n", getters));
            Assert.That(il, Does.Match(@"\.method[^\n]*specialname\s+virtual\s*\r?\n"),
                "the override must take the base's slot, not declare a new one:\n"
                + string.Join("\n", getters));
            Assert.That(System.Text.RegularExpressions.Regex.Matches(il, @"newslot\s+virtual").Count,
                Is.EqualTo(1),
                "exactly one of the two accessors declares a slot:\n" + string.Join("\n", getters));
        });
    }

    /// <summary>A Shared property's accessors must NOT be virtual — `static virtual` will not assemble.</summary>
    [Test]
    public void TheEmittedIl_LeavesASharedPropertysAccessorsNonVirtual()
    {
        var il = EmittedIl("""
            Class Counter
             Public Shared ReadOnly Property N As Integer
              Get
               Return 3
              End Get
             End Property
            End Class
            Sub Main()
             PrintLine(CStr(Counter.N))
            End Sub
            """);
        Assert.That(il, Does.Not.Contain("virtual"),
            "a Shared accessor is never virtual");
    }

    // ---------------------------------------------------------------- the front end

    /// <summary>
    /// ⚠ THE PARSER IS ASSERTED DIRECTLY, because every backend symptom flows from one dropped
    /// pair of locals. A backend-only assertion would stay green if the modifier were
    /// re-plumbed somewhere else and dropped here again.
    /// </summary>
    [Test]
    public void TheParser_RecordsOverridableAndOverridesOnAProperty()
    {
        var ast = new Parser(new Lexer(OverriddenPair).Tokenize()).Parse();
        var classes = ast.Declarations.OfType<ClassNode>().ToList();
        var baseProp = classes.Single(c => c.Name == "Animal").Members.OfType<PropertyNode>().Single();
        var derivedProp = classes.Single(c => c.Name == "Dog").Members.OfType<PropertyNode>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(baseProp.IsVirtual, Is.True, "Overridable was dropped on the base");
            Assert.That(baseProp.IsOverride, Is.False);
            Assert.That(derivedProp.IsOverride, Is.True, "Overrides was dropped on the derived");
        });
    }

    /// <summary>⚠ A property with no modifier must not come out virtual by default.</summary>
    [Test]
    public void TheParser_LeavesAPlainPropertyNeitherVirtualNorOverride()
    {
        var ast = new Parser(new Lexer("""
            Class Animal
             Public ReadOnly Property Name As String
              Get
               Return "base"
              End Get
             End Property
            End Class
            Sub Main()
            End Sub
            """).Tokenize()).Parse();
        var prop = ast.Declarations.OfType<ClassNode>().Single().Members.OfType<PropertyNode>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(prop.IsVirtual, Is.False);
            Assert.That(prop.IsOverride, Is.False);
        });
    }

    /// <summary>
    /// ⛔ `Public Shared Overridable Property` IS ACCEPTED BY THE FRONT END — measured; VB itself
    /// refuses it (BC30503), which is a separate front-end gap this change deliberately does not
    /// decide. What it must not do is REGRESS the shape: before the change C# emitted a plain
    /// `public static int N` that compiled and ran, and marking it `static virtual` does not
    /// compile at all (CS0112). `static virtual` will not assemble on MSIL either. So both
    /// emitters drop the modifier for a Shared property, and this pins that on BOTH — a guard on
    /// one backend only would leave the other emitting a file that cannot be built.
    /// </summary>
    [Test]
    public void ASharedOverridableProperty_KeepsCompilingAndIsNotVirtual()
    {
        const string program = """
            Class Counter
             Public Shared Overridable ReadOnly Property N As Integer
              Get
               Return 3
              End Get
             End Property
            End Class
            Sub Main()
             PrintLine(CStr(Counter.N))
            End Sub
            """;

        Assert.Multiple(() =>
        {
            Assert.That(Cs(program), Is.EqualTo("3"), "C# must still run it");
            Assert.That(Msil(program), Is.EqualTo("3"), "MSIL must still run it");
            Assert.That(EmittedCs(program), Does.Not.Match(@"static\s+virtual"),
                "static virtual does not compile in C#");
            Assert.That(EmittedIl(program), Does.Not.Contain("virtual"),
                "static virtual does not assemble");
        });
    }
}
