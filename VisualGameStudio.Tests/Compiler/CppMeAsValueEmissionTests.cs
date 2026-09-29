using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0015 (task #200), string-level emission pins. Cheap (no C++ compiler, no process spawn —
/// pure in-process compile), and each one kills a mutant the RUNTIME probes in
/// <see cref="CppMeAsValueTests"/> cannot: <c>Self</c> used at a member RECEIVER
/// (<c>Self(this)-&gt;V</c> instead of <c>this-&gt;V</c>) still compiles and still runs correctly
/// — <c>shared_ptr::operator-&gt;</c> gives back the same object — so no probe's OUTPUT differs
/// (the ADR's own mutation matrix measured this: <c>m3_self_at_receiver</c>, 0 probes differ from
/// WIP). Only reading the generated TEXT can see it.
/// </summary>
[TestFixture]
public class CppMeAsValueEmissionTests
{
    private static string Cpp(string program) =>
        CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(program));

    /// <summary>
    /// D3's closed list of two "keep raw <c>this</c>" contexts, and nothing else: a member
    /// receiver (a field access AND a method call) render <c>this-&gt;…</c>, never
    /// <c>BasicLang::Self(this)-&gt;…</c> — the emission-level kill for mutant
    /// <c>m3_self_at_receiver</c>, which no run-time probe can see (see the fixture doc comment).
    /// </summary>
    [Test]
    public void AMemberReceiver_RendersRawThis_NeverSelfOfThis()
    {
        var cpp = Cpp("""
            Class Node
                Public V As Integer
                Public Sub Bump()
                    Me.V = Me.V + 1
                    Me.Describe()
                End Sub
                Public Sub Describe()
                    Console.WriteLine(Me.V)
                End Sub
            End Class
            Sub Main()
                Dim n As New Node()
                n.Bump()
            End Sub
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("this->V"), "a field receiver must render raw `this`:\n" + cpp);
            Assert.That(cpp, Does.Contain("this->Describe("), "a call receiver must render raw `this`:\n" + cpp);
            Assert.That(cpp, Does.Not.Contain("Self(this)->"),
                "no member receiver may render through BasicLang::Self — that is the " +
                "m3_self_at_receiver mutant, silent at run time:\n" + cpp);
        });
    }

    /// <summary>
    /// D3's value-site default: <c>Me</c> passed as an ordinary ARGUMENT renders the owning
    /// <c>shared_ptr</c>, <c>BasicLang::Self(this)</c> — the positive twin of the receiver test
    /// above (mutant <c>m2_no_self_at_value_sites</c>: every M-probe COMPILE-FAILs under it).
    /// </summary>
    [Test]
    public void MeAsAnArgument_RendersBasicLangSelfOfThis_NeverRawThis()
    {
        var cpp = Cpp("""
            Class Sink
                Public Sub Take(n As Node)
                End Sub
            End Class
            Class Node
                Public Sub Give(k As Sink)
                    k.Take(Me)
                End Sub
            End Class
            Sub Main()
            End Sub
            """);

        Assert.That(cpp, Does.Contain("Take(BasicLang::Self(this))"),
            "Me as an argument must render the owning shared_ptr:\n" + cpp);
    }

    /// <summary>
    /// D1: a hierarchy ROOT's class head ends with <c>enable_shared_from_this&lt;Root&gt;</c>,
    /// unconditionally (even though this program never uses <c>Me</c> as a value at all) — a
    /// DERIVED class adds nothing of its own. Two such subobjects on one object would leave
    /// <c>weak_this</c> silently unset (mutant <c>m1_esft_every_class</c>).
    /// </summary>
    [Test]
    public void ARootClassHead_GetsEnableSharedFromThis_ADerivedClassGetsNone()
    {
        var cpp = Cpp("""
            Class Root
                Public V As Integer
            End Class
            Class Child
                Inherits Root
                Public W As Integer
            End Class
            Sub Main()
            End Sub
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("class Root : public std::enable_shared_from_this<Root>\n"),
                "the root's head must carry enable_shared_from_this, unconditionally:\n" + cpp);
            Assert.That(cpp, Does.Contain("class Child : public Root\n"),
                "a derived class's head must add nothing of its own:\n" + cpp);
            Assert.That(cpp, Does.Not.Contain("enable_shared_from_this<Child>"),
                "a derived class must never carry its own enable_shared_from_this — two such " +
                "subobjects leave weak_this unset (bad_weak_ptr at run time, not compile time):\n" + cpp);
        });
    }

    /// <summary>
    /// D2: every <c>New</c> site of a user class renders <c>BasicLang::New&lt;C&gt;(…)</c> — the
    /// two-phase constructor — never the old one-phase <c>std::make_shared&lt;C&gt;(…)</c>
    /// spelling.
    /// </summary>
    [Test]
    public void ANewSite_RendersBasicLangNew_NeverMakeSharedDirectly()
    {
        var cpp = Cpp("""
            Class Box
                Public V As Integer
            End Class
            Sub Main()
                Dim b As New Box()
            End Sub
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("BasicLang::New<Box>("), "a New site must call BasicLang::New:\n" + cpp);
            Assert.That(cpp, Does.Not.Contain("std::make_shared<Box>("),
                "a user class must never be constructed by a bare make_shared any more:\n" + cpp);
        });
    }

    /// <summary>
    /// Byte identity (ADR-0015's Obligations): a program that declares NO class and calls NO
    /// class <c>New</c> gets none of ADR-0015's machinery spliced in at all — the runtime block
    /// is spliced ON DEMAND, so this program's C++ is exactly as it was before #200.
    /// </summary>
    [Test]
    public void AProgramWithNoClass_HasNoObjectModelRuntimeBlock()
    {
        // WithoutBclRuntime strips the P1 BCL splices (StringBuilder included, which keeps its
        // OWN enable_shared_from_this per the ADR's Obligations — legitimate, unrelated to a
        // user class) so this test is not fooled by text the backend always emits.
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized("""
            Sub Main()
                Console.WriteLine("no classes here")
            End Sub
            """));

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Not.Contain("BASICLANG_OBJECT_MODEL_RUNTIME"),
                "a class-free program must not splice ADR-0015's runtime block:\n" + cpp);
            Assert.That(cpp, Does.Not.Contain("BasicLang::Self"));
            Assert.That(cpp, Does.Not.Contain("BasicLang::New<"));
            Assert.That(cpp, Does.Not.Contain("enable_shared_from_this"));
        });
    }
}
