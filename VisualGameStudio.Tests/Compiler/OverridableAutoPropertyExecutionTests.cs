using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ #150, EXECUTION: an <c>Overridable</c> AUTO-property (no Get/Set block) that a derived class overrides
/// must dispatch to the override on JavaScript, and a <c>ReadOnly</c> one that its own constructor writes must
/// compile on C++. Every probe is compiled and RUN through the real <c>BasicLang</c> CLI (standard passes), the
/// real CLI with <c>--optimize</c> (aggressive passes) and <c>BasicCompiler.CompileProjectFiles</c> with
/// <c>OptimizeAggressive</c> (what a Release <c>.blproj</c> build and the IDE's build service call), and compared
/// with VB's own output (<c>TempExec.AssertMatchesInEveryEntryPoint</c>). The oracle is <c>vbc</c>, not a backend.
///
/// <para><b>What was wrong.</b> JavaScript emitted such a property as a class field (<c>V = 0;</c>), which is an
/// OWN data property of every instance and so shadows the derived class's prototype accessors: <c>V = 10</c> in the
/// base, <c>x.V = 5</c> through a base-typed variable, a grandparent chain and an interface receiver all wrote the
/// base's field (P16 printed <c>3,3</c>, vbc <c>12,3</c>). C++ called a <c>set_P</c> it never emitted for a
/// <c>ReadOnly</c> Overridable auto-property written in its own constructor (V3ro, E24).</para>
///
/// <para><b>The edge rows to read first.</b> <c>V8order</c> is the order trap: a DERIVED class's fields are
/// installed only after <c>super()</c> returns, and the base constructor already wrote the slot through the
/// dispatching setter, so an unguarded derived initialiser resets it (<c>0</c> where vbc prints <c>4 x</c>).
/// <c>V3ro</c> is the ReadOnly carve-out: the declaring class's constructor stores the slot, never a setter
/// (C++: <c>no member named 'set_Tag'</c>).</para>
///
/// <para><b>Known gaps — listed here, NOT tested (not this defect; every cell unchanged by #150):</b></para>
/// <list type="bullet">
///   <item><c>MyBase.P</c> on a PROPERTY dispatches to the override on all four backends (probe V4autoboth prints
///   <c>5,5</c> where vbc prints <c>5,0</c>; V5grand, a <c>MyBase.V</c> inside the override, recurses until the stack
///   overflows) — task #271.</item>
///   <item>C++ drops a bare store to a PLAIN (non-Overridable) auto-property (probe V6shared prints <c>10 0 6</c>
///   where vbc prints <c>12 4 6</c>) — #254 / PRa.</item>
///   <item>An auto-property initialiser (<c>Property V As Integer = 4</c>) does not parse — #210.</item>
/// </list>
/// </summary>
[TestFixture]
[Category("Integration")]
public class OverridableAutoPropertyExecutionTests
{
    // ---- the probes (vbc's answer beside each; S/t150/probes) ------------------------------------

    /// <summary>P16 — the base writes its OWN Overridable auto-property bare; the derived class overrides it with a Get/Set over K.</summary>
    private const string P16 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class BaseBox
            Public K As Integer

            Overridable Property V As Integer

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                V = 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Class Box
            Inherits BaseBox

            Overrides Property V As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    private const string P16Expected = "seed\nseed\n12,3";

    /// <summary>P16q — the same, written <c>Me.V = 10</c>.</summary>
    private const string P16q = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class BaseBox
            Public K As Integer

            Overridable Property V As Integer

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Me.V = 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Class Box
            Inherits BaseBox

            Overrides Property V As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value
                End Set
            End Property
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    /// <summary>V1var — a write and a read through a BASE-typed variable, a base instance, and the derived type.</summary>
    private const string V1var = """
        Class A
            Overridable Property V As Integer
        End Class

        Class B
            Inherits A

            Overrides Property V As Integer
                Get
                    Return 42
                End Get
                Set(value As Integer)
                    Console.WriteLine("B.set " & CStr(value))
                End Set
            End Property
        End Class

        Sub Main()
            Dim x As A = New B()
            x.V = 5
            Console.WriteLine(CStr(x.V))
            Dim y As A = New A()
            y.V = 7
            Console.WriteLine(CStr(y.V))
            Dim z As New B()
            z.V = 9
            Console.WriteLine(CStr(z.V))
        End Sub
        """;

    private const string V1varExpected = "B.set 5\n42\n7\nB.set 9\n42";

    /// <summary>V2inside — the base reads and writes the property from its OWN methods, bare and <c>Me.</c>-qualified.</summary>
    private const string V2inside = """
        Class A
            Overridable Property V As Integer

            Function Show() As Integer
                Return V + Me.V
            End Function

            Sub Bump()
                V = V + 1
            End Sub
        End Class

        Class B
            Inherits A
            Public Hits As Integer

            Overrides Property V As Integer
                Get
                    Return 100
                End Get
                Set(value As Integer)
                    Hits = Hits + value
                End Set
            End Property
        End Class

        Sub Main()
            Dim a As New A()
            a.Bump()
            a.Bump()
            Console.WriteLine(CStr(a.Show()))
            Dim b As New B()
            b.Bump()
            Console.WriteLine(CStr(b.Show()) & " " & CStr(b.Hits))
        End Sub
        """;

    private const string V2insideExpected = "4\n200 101";

    /// <summary>V3ro — an Overridable ReadOnly auto-property the base's own constructor writes (the carve-out), overridden Get-only. ⛔ Kills M3 (C++) and M1.</summary>
    private const string V3ro = """
        Class A
            Overridable ReadOnly Property Tag As String

            Overridable ReadOnly Property Name As String
                Get
                    Return "A"
                End Get
            End Property

            Sub New()
                Tag = "tagA"
            End Sub

            Sub Show()
                Console.WriteLine(Name & " " & Tag)
            End Sub
        End Class

        Class B
            Inherits A

            Overrides ReadOnly Property Tag As String
                Get
                    Return "tagB"
                End Get
            End Property

            Overrides ReadOnly Property Name As String
                Get
                    Return "B"
                End Get
            End Property
        End Class

        Sub Main()
            Dim a As A = New A()
            a.Show()
            Dim b As A = New B()
            b.Show()
            Console.WriteLine(b.Tag & " " & a.Tag)
        End Sub
        """;

    private const string V3roExpected = "A tagA\nB tagB\ntagB tagA";

    /// <summary>V5bchain — a three-level override chain (B, C, then D inheriting C's) behind a base-typed receiver.</summary>
    private const string V5bchain = """
        Class A
            Overridable Property V As Integer

            Sub Put(x As Integer)
                V = x
            End Sub

            Function Read() As Integer
                Return V
            End Function
        End Class

        Class B
            Inherits A
            Public Store As Integer

            Overrides Property V As Integer
                Get
                    Return Store + 1000
                End Get
                Set(value As Integer)
                    Store = value * 2
                End Set
            End Property
        End Class

        Class C
            Inherits B

            Overrides Property V As Integer
                Get
                    Return Store + 5
                End Get
                Set(value As Integer)
                    Store = value * 3
                End Set
            End Property
        End Class

        Class D
            Inherits C
        End Class

        Sub Main()
            Dim a As A = New D()
            a.Put(2)
            Console.WriteLine(CStr(a.Read()) & " " & CStr(a.V))
            Dim b As A = New B()
            b.Put(2)
            Console.WriteLine(CStr(b.Read()))
            Dim x As New A()
            x.Put(2)
            Console.WriteLine(CStr(x.Read()))
        End Sub
        """;

    private const string V5bchainExpected = "11 11\n1004\n2";

    /// <summary>V7ctor — the base's CONSTRUCTOR writes the property, so a derived setter runs before the derived object is complete.</summary>
    private const string V7ctor = """
        Class A
            Overridable Property V As Integer

            Sub New()
                V = 4
            End Sub

            Function Get2() As Integer
                Return V
            End Function
        End Class

        Class B
            Inherits A

            Overrides Property V As Integer
                Get
                    Return 50
                End Get
                Set(value As Integer)
                    Console.WriteLine("B.set " & CStr(value))
                End Set
            End Property
        End Class

        Sub Main()
            Dim a As New A()
            Console.WriteLine(CStr(a.Get2()))
            Dim b As A = New B()
            Console.WriteLine(CStr(b.Get2()) & " " & CStr(b.V))
        End Sub
        """;

    private const string V7ctorExpected = "4\nB.set 4\n50 50";

    /// <summary>V8order — the base ctor writes V and S; the derived class overrides BOTH as AUTO-properties (and a grandchild adds a field). The derived slot must KEEP the base ctor's write. ⛔ Kills M2.</summary>
    private const string V8order = """
        Class A
            Overridable Property V As Integer
            Overridable Property S As String

            Sub New()
                V = 4
                S = "x"
            End Sub
        End Class

        Class B
            Inherits A

            Overrides Property V As Integer
            Overrides Property S As String
        End Class

        Class C
            Inherits B
            Public K As Integer
        End Class

        Sub Main()
            Dim b As A = New B()
            Console.WriteLine(CStr(b.V) & " " & b.S)
            Dim c As A = New C()
            Console.WriteLine(CStr(c.V) & " " & c.S)
            Dim a As New A()
            Console.WriteLine(CStr(a.V) & " " & a.S)
            b.V = 9
            Console.WriteLine(CStr(b.V))
        End Sub
        """;

    private const string V8orderExpected = "4 x\n4 x\n4 x\n9";

    /// <summary>V9iface — the property reached through an INTERFACE receiver whose implementation is the Overridable auto-property.</summary>
    private const string V9iface = """
        Interface IHas
            Property V As Integer
        End Interface

        Class A
            Implements IHas
            Overridable Property V As Integer
        End Class

        Class B
            Inherits A

            Overrides Property V As Integer
                Get
                    Return 77
                End Get
                Set(value As Integer)
                    Console.WriteLine("B.set " & CStr(value))
                End Set
            End Property
        End Class

        Sub Main()
            Dim ab As A = New B()
            Dim h As IHas = ab
            h.V = 3
            Console.WriteLine(CStr(h.V))
            Dim h2 As IHas = New A()
            h2.V = 3
            Console.WriteLine(CStr(h2.V))
        End Sub
        """;

    private const string V9ifaceExpected = "B.set 3\n77\n3";

    /// <summary>E24 — the smallest ReadOnly Overridable auto-property written by its own constructor (a constructor PARAMETER, not a literal), no derived class at all.</summary>
    private const string E24 = """
        Class Ctx
            Public Overridable ReadOnly Property P As Integer
            Public Sub New(a As Integer)
                P = a
            End Sub
        End Class
        Sub Main()
            Dim c As New Ctx(9)
            Console.WriteLine(c.P)
        End Sub
        """;

    private const string E24Expected = "9";

    // ---- JavaScript: the override is reached --------------------------------------------------------

    /// <summary>Every JavaScript row printed the BASE's value before #150 (a class field shadows the derived accessor); each must now print vbc's, through all three entry points.</summary>
    [TestCase(P16, P16Expected, TestName = "P16_BaseWritesItsOwnOverridableAutoProperty_JavaScript")]
    [TestCase(P16q, P16Expected, TestName = "P16q_BaseWritesMeV_JavaScript")]
    [TestCase(V1var, V1varExpected, TestName = "V1var_WriteAndReadThroughABaseTypedVariable_JavaScript")]
    [TestCase(V2inside, V2insideExpected, TestName = "V2inside_BaseReadsAndWritesItsOwnProperty_JavaScript")]
    [TestCase(V3ro, V3roExpected, TestName = "V3ro_ReadOnlyCtorWriteStoresTheSlot_JavaScript")]
    [TestCase(V5bchain, V5bchainExpected, TestName = "V5bchain_ThreeLevelOverrideChain_JavaScript")]
    [TestCase(V7ctor, V7ctorExpected, TestName = "V7ctor_BaseCtorWriteReachesTheDerivedSetter_JavaScript")]
    [TestCase(V8order, V8orderExpected, TestName = "V8order_DerivedAutoOverrideKeepsTheBaseCtorWrite_JavaScript")]
    [TestCase(V9iface, V9ifaceExpected, TestName = "V9iface_ThroughAnInterfaceReceiver_JavaScript")]
    public void AnOverridableAutoProperty_DispatchesToTheOverride_OnJavaScript(string source, string expected)
        => TempExec.AssertMatchesInEveryEntryPoint(Bk.JavaScript, source, expected, "#150");

    // ---- C++: a ReadOnly constructor write compiles -----------------------------------------------

    /// <summary>The ReadOnly carve-out write was <c>set_P(a)</c> / <c>set_Tag(...)</c> on C++ — a member never emitted ("no member named 'set_Tag'"), so the program did not compile.</summary>
    [TestCase(V3ro, V3roExpected, TestName = "V3ro_ReadOnlyCtorWriteStoresTheDataMember_Cpp")]
    [TestCase(E24, E24Expected, TestName = "E24_SmallestReadOnlyCtorWrite_Cpp")]
    public void AnOverridableReadOnlyAutoProperty_WrittenByItsConstructor_CompilesAndRuns_OnCpp(string source, string expected)
        => TempExec.AssertMatchesInEveryEntryPoint(Bk.Cpp, source, expected, "#150");
}

/// <summary>
/// #150, TEXT (fast subset, no Node): the JavaScript shape the execution rows depend on. A running row is the
/// proof; these two name the shape so a failure says WHAT moved — an Overridable / Overrides auto-property is a
/// get/set pair over a slot named by its DECLARING class, and every other auto-property is still a plain field.
/// Both are read through the standard, the optimised and the aggressive pipelines.
/// </summary>
[TestFixture]
public class OverridableAutoPropertyEmissionTests
{
    private const string Source = """
        Class A
            Overridable Property V As Integer
            Public Property Plain As Integer
            Public Shared Property Count As Integer
        End Class

        Class B
            Inherits A
            Overrides Property V As Integer
        End Class

        Sub Main()
            Dim b As New B()
            b.V = 3
            b.Plain = 4
            Console.WriteLine(CStr(b.V) & CStr(b.Plain))
        End Sub
        """;

    private static string[] EveryPipeline() => new[]
    {
        JsTestSupport.Compile(Source),
        JsTestSupport.CompileOptimized(Source),
        JsTestSupport.CompileAggressive(Source),
    };

    [Test]
    public void AnOverridableAutoProperty_IsAGetSetPairOverASlotNamedByItsDeclaringClass()
    {
        foreach (var js in EveryPipeline())
            Assert.Multiple(() =>
            {
                Assert.That(js, Does.Contain("$A$V = 0;"), "the base's slot");
                Assert.That(js, Does.Contain("get V() { return this.$A$V; }"));
                Assert.That(js, Does.Contain("set V(value) { this.$A$V = value; }"));
                Assert.That(js, Does.Contain("$B$V = \"$B$V\" in this ? this.$B$V : 0;"),
                    "the derived auto-override has its OWN slot, which keeps a value the base constructor wrote through the setter");
                Assert.That(js, Does.Contain("get V() { return this.$B$V; }"));
                Assert.That(js, Does.Not.Match(@"(?m)^\s*V = 0;"),
                    "a plain `V = 0;` field is an own data property of the instance and shadows every accessor on the prototype chain");
            });
    }

    [Test]
    public void ANonOverridableAutoProperty_IsStillAPlainField()
    {
        foreach (var js in EveryPipeline())
            Assert.Multiple(() =>
            {
                Assert.That(js, Does.Contain("    Plain = 0;"));
                Assert.That(js, Does.Contain("static Count = 0;"));
                Assert.That(js, Does.Not.Contain("$A$Plain"));
                Assert.That(js, Does.Not.Contain("$A$Count"));
            });
    }
}
