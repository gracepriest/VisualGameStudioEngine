using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Cross-file <c>Inherits</c> — a base class declared in a sibling project file.
///
/// <para><b>The defect these pin.</b> <c>SemanticAnalyzer.Visit(ClassNode)</c> resolved the base
/// through <c>_typeManager</c>, which holds only the CURRENT unit's declarations. Sibling classes
/// never land there: completed units reach <c>GlobalScope</c> via
/// <c>ImportImplicitProjectSymbols</c>, and not-yet-compiled ones via
/// <c>RegisterPendingSiblingSignatures</c>, which registers a shell and then fills its
/// <c>Members</c> in a second pass.</para>
///
/// <para>So the miss had two faces. With no <c>Using</c> in scope it was a hard
/// <c>Unknown base class</c> — cross-file inheritance simply did not work. With a <c>Using</c> in
/// scope it was worse than an error: the base fell into the opaque-.NET arm, which fabricates a
/// <c>new TypeInfo(name, TypeKind.Class)</c> whose <c>Members</c> dictionary is EMPTY. The build
/// went green and every inherited member silently degraded to Object. That second face is why
/// <see cref="WithUsingDirective_PrefersSiblingBase_OverOpaqueNetType"/> asserts on
/// <c>BaseType.Members</c> and not merely on <c>Success</c>: a success-only assertion passes both
/// before and after the fix and would prove nothing.</para>
///
/// <para><b>Unrelated, and still open:</b> cross-file <c>Implements</c> is broken by a different
/// mechanism — see <see cref="Probe_CrossFileImplements_StillUnresolved"/>.</para>
/// </summary>
[TestFixture]
public class CrossFileInheritanceTests
{
    private string _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "BasicLang_XFileInherit_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static BasicCompiler For(string backend)
        => new BasicCompiler(new CompilerOptions { TargetBackend = backend });

    private const string GreeterBase = @"
Class Greeter
    Public Sub Greet()
        Console.WriteLine(""hello"")
    End Sub
End Class
";

    private const string DerivedNoUsing = @"
Class LoudGreeter
    Inherits Greeter
End Class

Sub Main()
    Dim g As New LoudGreeter()
    g.Greet()
End Sub
";

    // ------------------------------------------------------------ the base resolves at all

    /// <summary>
    /// No <c>Using</c> anywhere, so <c>_netNamespaces</c> is empty and the opaque-.NET arm cannot
    /// fire. Before the fix this reported <c>Unknown base class 'Greeter'</c>.
    /// </summary>
    [TestCase("javascript")]
    [TestCase("cpp")]
    [TestCase("csharp")]
    public void CrossFileInherits_ResolvesSiblingBase(string backend)
    {
        var base_ = Write("Greeter.bas", GreeterBase);
        var derived = Write("LoudGreeter.bas", DerivedNoUsing);

        var result = For(backend).CompileProjectFiles(new[] { base_, derived });

        Assert.That(result.AllErrors.Select(e => e.Message),
            Has.None.Contains("Unknown base class"),
            $"[{backend}] the base is declared in a sibling unit and must resolve from GlobalScope");
        Assert.That(result.Success, Is.True,
            $"[{backend}] errors: {string.Join(" | ", result.AllErrors.Select(e => e.Message))}");
    }

    /// <summary>
    /// File ORDER must not matter: the sibling pass runs over every pending unit before the
    /// current one is visited, so the derived class compiling first is the same case.
    /// </summary>
    [Test]
    public void CrossFileInherits_DerivedListedFirst_StillResolves()
    {
        var base_ = Write("Greeter.bas", GreeterBase);
        var derived = Write("LoudGreeter.bas", DerivedNoUsing);

        var result = For("javascript").CompileProjectFiles(new[] { derived, base_ });

        Assert.That(result.Success, Is.True,
            "errors: " + string.Join(" | ", result.AllErrors.Select(e => e.Message)));
    }

    // ------------------------------------------------- the base resolves to the REAL type

    /// <summary>
    /// The discriminating case, and the WinForms-shaped one. <c>Using System</c> populates
    /// <c>_netNamespaces</c>, which before the fix swallowed the sibling base into an opaque
    /// external-.NET <c>TypeInfo</c> with an empty <c>Members</c> dictionary — green build, no
    /// member knowledge. The sibling lookup must be consulted BEFORE that arm.
    /// </summary>
    [Test]
    public void WithUsingDirective_PrefersSiblingBase_OverOpaqueNetType()
    {
        var base_ = Write("Greeter.bas", GreeterBase);
        var derived = Write("LoudGreeter.bas", @"
Using System

Class LoudGreeter
    Inherits Greeter
End Class

Sub Main()
    Dim g As New LoudGreeter()
End Sub
");

        var result = For("csharp").CompileProjectFiles(new[] { base_, derived });

        Assert.That(result.Success, Is.True,
            "errors: " + string.Join(" | ", result.AllErrors.Select(e => e.Message)));

        // Match on file name, not the full path: FilePath is normalized by the resolver and the
        // temp root itself can be a symlink (/var → /private/var on macOS).
        var unit = result.Units.Single(
            u => Path.GetFileName(u.FilePath) == Path.GetFileName(derived));
        var derivedSymbol = unit.Symbols.Resolve("LoudGreeter");
        Assert.That(derivedSymbol, Is.Not.Null, "LoudGreeter should be in the unit's symbol table");

        var baseType = derivedSymbol.Type?.BaseType;
        Assert.That(baseType, Is.Not.Null, "LoudGreeter should carry a resolved BaseType");
        Assert.That(baseType.Name, Is.EqualTo("Greeter"));
        Assert.That(baseType.Members.Keys, Contains.Item("Greet"),
            "an EMPTY Members dictionary is the signature of the opaque-.NET arm: the sibling "
            + "lookup did not run, or ran after it");
    }

    /// <summary>
    /// <c>Inherits Form</c> — a genuine external .NET base with no sibling of that name — must
    /// still take the opaque arm. The sibling lookup narrows that path, it must not close it.
    /// </summary>
    [Test]
    public void GenuineNetBase_StillResolvesOpaquely()
    {
        var file = Write("MainForm.bas", @"
Using System.Windows.Forms

Class MainForm
    Inherits Form
End Class

Sub Main()
End Sub
");

        var result = For("csharp").CompileProjectFiles(new[] { file });

        Assert.That(result.AllErrors.Select(e => e.Message),
            Has.None.Contains("Unknown base class"),
            "a real .NET base must keep falling through to the opaque arm");
    }

    // ---------------------------------------------------------------------- the guard

    /// <summary>
    /// The sibling lookup reads <c>GlobalScope</c>, where a shell for THIS class may already sit.
    /// <c>Class X Inherits X</c> must not silently resolve to itself.
    /// </summary>
    [Test]
    public void SelfInheritance_IsNotResolvedFromGlobalScope()
    {
        var file = Write("Ouroboros.bas", @"
Class Ouroboros
    Inherits Ouroboros
End Class

Sub Main()
End Sub
");

        var result = For("javascript").CompileProjectFiles(new[] { file });

        Assert.That(result.Success, Is.False, "a class inheriting itself must not compile");
    }

    // ------------------------------------------------------------- the adjacent open defect

    /// <summary>
    /// PROBE, not a guarantee — documents behaviour this change deliberately does NOT fix.
    ///
    /// <para>Cross-file <c>Implements</c> fails by a different mechanism, in three places rather
    /// than one: <c>Compiler.CollectExportedSymbols</c> filters exports to
    /// Function/Subroutine/Class, so <c>SymbolKind.Interface</c> never leaves a COMPLETED unit;
    /// <c>RegisterPendingSiblingSignatures</c> shells only <c>ClassNode</c>, so a PENDING unit's
    /// interface is never registered either; and the interface arm of <c>Visit(ClassNode)</c>
    /// queries <c>_typeManager</c> alone. It also has no opaque escape hatch, so the failure is a
    /// hard error on every backend rather than a silent degradation.</para>
    ///
    /// <para>Change this test when that is fixed — it pins the defect, not the design.</para>
    /// </summary>
    [Test]
    public void Probe_CrossFileImplements_StillUnresolved()
    {
        var iface = Write("IGreet.bas", @"
Interface IGreet
    Sub Greet()
End Interface
");
        var impl = Write("Greeter.bas", @"
Class Greeter
    Implements IGreet
    Public Sub Greet()
    End Sub
End Class

Sub Main()
End Sub
");

        var result = For("javascript").CompileProjectFiles(new[] { iface, impl });

        Assert.That(result.AllErrors.Select(e => e.Message),
            Has.Some.Contains("Unknown interface"),
            "if this now passes, cross-file Implements was fixed — delete the probe and assert "
            + "the positive behaviour instead");
    }
}
