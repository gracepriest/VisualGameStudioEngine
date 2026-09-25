using System;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0005 D3 — how an interface property's TYPE is resolved. Two things, both new in the
/// types sub-step (<c>IRBuilder.cs</c>'s <see cref="IRBuilder.InterfacePropertyType"/> hook and
/// <c>SemanticAnalyzer.cs</c>'s <c>Visit(InterfaceNode)</c> per-property type resolution): the
/// hook itself refuses to invent a type when the analyzer gave it none (no more silent
/// class-kinded stand-in), and the analyzer now resolves an interface property's declared type
/// through the SAME resolver a class property's does, so the two agree.
///
/// <para><b>Sub-step attribution.</b> <see cref="IRBuilder.InterfacePropertyType"/> does not
/// exist before the types sub-step's own patch — a unit test naming it cannot even compile
/// against an earlier tree. The D3 contract test (b) reads what the analyzer now records, which
/// is the OTHER half of that same patch. Both land together, and this file is committed at
/// types — before the flags sub-step's accessor-flag fix, which D3's own obligation requires
/// (ADR-0005 D3 Obligations: "The type-resolution commit lands BEFORE the D1 flag-fix commit").
/// Measured: every shape below already resolves correctly at the types sub-step's own matrix
/// (<c>matrix-types.txt</c>) — the flags patch changes <c>HasGetter</c>/<c>HasSetter</c> and
/// accessor emission, not property TYPES, so this file needs nothing flags adds.</para>
/// </summary>
[TestFixture]
public class InterfacePropertyTypeResolutionTests
{
    // ====================================================================================
    // (a) The hook itself: BasicLang.Compiler.IR.IRBuilder.InterfacePropertyType.
    // ====================================================================================

    [Test]
    public void NullResolvedType_ThrowsNamingTheInterfaceMemberAndTheADR()
    {
        var prop = new PropertyNode(1, 1)
        {
            Name = "Items",
            PropertyType = new TypeReference("Integer") { IsArray = true }
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => IRBuilder.InterfacePropertyType(null, "IHolder", prop));

        Assert.That(ex!.Message, Does.Contain("IHolder.Items"));
        Assert.That(ex.Message, Does.Contain("ADR-0005 D3"));
    }

    [Test]
    public void ANonNullResolvedType_IsReturnedAsTheSameInstance()
    {
        var resolved = new TypeInfo("Integer", TypeKind.Primitive);
        var prop = new PropertyNode(1, 1)
        {
            Name = "Count",
            PropertyType = new TypeReference("Integer")
        };

        var result = IRBuilder.InterfacePropertyType(resolved, "IHolder", prop);

        Assert.That(result, Is.SameAs(resolved));
    }

    // ====================================================================================
    // (b) D3's contract: an interface property's TYPE matches the implementing class
    // property's TYPE, for Integer, Double, Boolean, String, a user Structure and a user Enum.
    // ====================================================================================

    private const string SixTypedProperties =
        "Structure Pt\n" +
        "    Public X As Integer\n" +
        "    Public Y As Integer\n" +
        "End Structure\n\n" +
        "Enum Color\n" +
        "    Red\n" +
        "    Green\n" +
        "    Blue\n" +
        "End Enum\n\n" +
        "Interface IHolder\n" +
        "    Property AnInteger As Integer\n" +
        "    Property ADouble As Double\n" +
        "    Property ABoolean As Boolean\n" +
        "    Property AString As String\n" +
        "    Property AStructure As Pt\n" +
        "    Property AnEnum As Color\n" +
        "End Interface\n\n" +
        "Class Holder\n" +
        "    Implements IHolder\n" +
        "    Public Property AnInteger As Integer\n" +
        "    Public Property ADouble As Double\n" +
        "    Public Property ABoolean As Boolean\n" +
        "    Public Property AString As String\n" +
        "    Public Property AStructure As Pt\n" +
        "    Public Property AnEnum As Color\n" +
        "End Class\n\n" +
        "Module M\n" +
        "    Sub Main()\n" +
        "    End Sub\n" +
        "End Module";

    /// <summary>
    /// Parses, analyses and builds <see cref="SixTypedProperties"/> exactly the way
    /// <c>MsilHarness.CompileToIl</c> does through the FRONT half (no codegen — this test is
    /// about IR shape, not any one backend), and fails with the analyzer's own diagnostics if
    /// the front end refuses the program.
    /// </summary>
    private static IRModule BuildModule(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.ToString())));

        return new IRBuilder(analyzer).Build(ast, "TypeResolutionProbe");
    }

    [TestCase("AnInteger")]
    [TestCase("ADouble")]
    [TestCase("ABoolean")]
    [TestCase("AString")]
    [TestCase("AStructure")]
    [TestCase("AnEnum")]
    public void AnInterfacePropertysType_MatchesTheImplementingClassPropertysType(string propertyName)
    {
        var module = BuildModule(SixTypedProperties);

        var iface = module.Interfaces["IHolder"];
        var irClass = module.Classes["Holder"];

        var interfaceType = iface.Properties.Single(p => p.Name == propertyName).Type;
        var classType = irClass.Properties.Single(p => p.Name == propertyName).Type;

        Assert.That(interfaceType, Is.Not.Null, $"{propertyName}: interface property has no type");
        Assert.That(classType, Is.Not.Null, $"{propertyName}: class property has no type");

        // D3's contract is stated as "structurally equal, or the same instance" — assert the
        // structural contract (TypeInfo.Equals), which is what every backend actually reads,
        // and separately record which of the two this measures as, so a future reader does not
        // have to re-derive it.
        Assert.That(interfaceType.Equals(classType), Is.True,
            $"{propertyName}: interface type '{interfaceType.Name}'/{interfaceType.Kind} does not "
            + $"structurally equal class type '{classType.Name}'/{classType.Kind}");

        // Not asserted either way (D3 only requires structural equality) — logged so a reader
        // does not have to re-derive which one the resolver actually produces.
        TestContext.Out.WriteLine(ReferenceEquals(interfaceType, classType)
            ? $"{propertyName}: same TypeInfo instance"
            : $"{propertyName}: structurally equal, different TypeInfo instances");
    }
}
