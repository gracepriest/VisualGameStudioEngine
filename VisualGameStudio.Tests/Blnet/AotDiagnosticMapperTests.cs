using System.Linq;
using BasicLang.Compiler.CodeGen.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// <see cref="AotDiagnosticMapper"/> — spec §11.3's AOT ceiling reported as BL6020, at the three
/// attribution tiers, with §15.10's severity split.
///
/// <para><b>The samples below are REAL captured ILC output, not invented text.</b> They were
/// produced by publishing a shim project shaped exactly like
/// <see cref="NetShimGenerator.EmitProject"/>'s output (net8.0, <c>PublishAot</c>,
/// <c>IsAotCompatible</c>, <c>NativeLib=Shared</c>, win-x64) whose <c>Exports.g.cs</c> carried
/// <c>[UnmanagedCallersOnly]</c> wrappers named with <see cref="BasicLang.Net.NetNameMangler"/>'s
/// shape, against a referenced assembly doing un-annotated reflection. The ONLY edit applied to
/// the captured lines is shortening the absolute scratch directory to <c>C:\p\</c> so the
/// literals stay readable — no punctuation, spacing, ordering or wording was touched. A parser
/// pinned only to text its own author imagined would be worth very little, which is why this
/// matters.</para>
///
/// <para><b>Four distinct real line shapes exist</b>, and the parser must handle all four —
/// discovering this was the point of capturing rather than inventing:</para>
/// <list type="number">
/// <item><description><b>ILC stage, with source</b> —
/// <c>file(line): Trim analysis warning IL2070: &lt;origin&gt;: &lt;msg&gt; [proj]</c>. Note the
/// line number carries NO column, unlike a csc diagnostic.</description></item>
/// <item><description><b>ILC stage, no source</b> — <c>ILC : AOT analysis warning IL3050:
/// &lt;origin&gt;: &lt;msg&gt; [proj]</c>. This is the shape a reference closure of
/// <c>&lt;Reference&gt;&lt;HintPath&gt;</c> assemblies actually produces, because a NuGet-sourced
/// assembly ships no PDB — i.e. it is the NORMAL P2a case, not an edge case.</description></item>
/// <item><description><b>Assembly-level aggregate</b> —
/// <c>&lt;dll&gt; : warning IL2104: Assembly 'X' produced trim warnings. …
/// [proj]</c>. Note the SPACE before the colon, and that no origin member exists at
/// all.</description></item>
/// <item><description><b>csc/analyzer stage</b> — <c>file(line,col): warning IL2057: &lt;msg&gt;
/// [proj]</c>, carrying a column but NO origin member and no "Trim analysis"
/// prefix.</description></item>
/// </list>
/// </summary>
[TestFixture]
public class AotDiagnosticMapperTests
{
    // ---- REAL captured ILC lines (see the fixture remarks) ------------------------------

    /// <summary>Tier 1, RequiresDynamicCode. §15.10: BL6020 error.</summary>
    private const string Il3050AgainstWrapper =
        @"C:\p\Shim\Exports.g.cs(18): AOT analysis warning IL3050: BlnetTestShim.Exports.bl_net_System_Type_MakeGenericType_5c3f9a21(UInt64,UInt64,UInt64*): Using member 'System.Type.MakeGenericType(Type[])' which has 'RequiresDynamicCodeAttribute' can break functionality when AOT compiling. The native code for this instantiation might not be available at runtime. [C:\p\Shim\ShimSample.csproj]";

    /// <summary>Tier 1, trim analysis. §15.10: BL6020 warning.</summary>
    private const string Il2026AgainstWrapper =
        @"C:\p\Shim\Exports.g.cs(31): Trim analysis warning IL2026: BlnetTestShim.Exports.bl_net_System_Text_Json_JsonSerializer_Serialize_8b1de470(UInt64,UInt64*): Using member 'System.Text.Json.JsonSerializer.Serialize<<>f__AnonymousType0`1<Int32>>(<>f__AnonymousType0`1<Int32>,JsonSerializerOptions)' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code. JSON serialization and deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo or JsonSerializerContext, or make sure all of the required types are preserved. [C:\p\Shim\ShimSample.csproj]";

    /// <summary>Tier 2 — origin member lives inside a referenced assembly.</summary>
    private const string Il2070InReferencedAssembly =
        @"ILC : Trim analysis warning IL2070: Contoso.Serialization.Reflector.MemberNames(Type): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicMethods' in call to 'System.Type.GetMethods()'. The parameter 't' of method 'Contoso.Serialization.Reflector.MemberNames(Type)' does not have matching annotations. The source value must declare at least the same requirements as those declared on the target location it is assigned to. [C:\p\Shim2\ShimHint.csproj]";

    /// <summary>Tier 2 but ERROR severity — the tier and the severity are independent axes.</summary>
    private const string Il3050InReferencedAssembly =
        @"ILC : AOT analysis warning IL3050: Contoso.Serialization.Reflector.MakeClosed(Type,Type): Using member 'System.Type.MakeGenericType(Type[])' which has 'RequiresDynamicCodeAttribute' can break functionality when AOT compiling. The native code for this instantiation might not be available at runtime. [C:\p\Shim2\ShimHint.csproj]";

    private const string Il2055InReferencedAssembly =
        @"ILC : Trim analysis warning IL2055: Contoso.Serialization.Reflector.MakeClosed(Type,Type): Call to 'System.Type.MakeGenericType(Type[])' can not be statically analyzed. It's not possible to guarantee the availability of requirements of the generic type. [C:\p\Shim2\ShimHint.csproj]";

    private const string Il2075InReferencedAssembly =
        @"ILC : Trim analysis warning IL2075: Contoso.Serialization.Reflector.ReadProperty(Object,String): 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties' in call to 'System.Type.GetProperty(String,BindingFlags)'. The return value of method 'System.Object.GetType()' does not have matching annotations. The source value must declare at least the same requirements as those declared on the target location it is assigned to. [C:\p\Shim2\ShimHint.csproj]";

    /// <summary>Tier 3 — assembly-level trim aggregate.</summary>
    private const string Il2104Aggregate =
        @"C:\p\libs\Contoso.Serialization.dll : warning IL2104: Assembly 'Contoso.Serialization' produced trim warnings. For more information see https://aka.ms/dotnet-illink/libraries [C:\p\Shim\ShimSample.csproj]";

    /// <summary>Tier 3 — assembly-level AOT aggregate. §15.10 pins this to WARNING despite IL3xxx.</summary>
    private const string Il3053Aggregate =
        @"C:\p\libs\Contoso.Serialization.dll : warning IL3053: Assembly 'Contoso.Serialization' produced AOT analysis warnings. [C:\p\Shim\ShimSample.csproj]";

    /// <summary>csc/analyzer stage: a column, and no origin member.</summary>
    private const string Il2057FromCsc =
        @"C:\p\RefLib\Reflector.cs(16,17): warning IL2057: Unrecognized value passed to the parameter 'typeName' of method 'System.Type.GetType(String)'. It's not possible to guarantee the availability of the target type. [C:\p\RefLib\RefLib.csproj]";

    /// <summary>Real non-diagnostic publish chatter, verbatim. Must produce nothing.</summary>
    private const string Noise = @"  Determining projects to restore...
  Restored C:\p\RefLib\RefLib.csproj (in 731 ms).
  RefLib -> C:\p\RefLib\bin\Release\net8.0\Contoso.Serialization.dll
  ShimSample -> C:\p\Shim\bin\Release\net8.0\win-x64\ShimSample.dll
  Generating native code";

    // ---- Provenance -----------------------------------------------------------------------

    private const string MakeGenericTypeWrapper = "bl_net_System_Type_MakeGenericType_5c3f9a21";
    private const string JsonSerializeWrapper = "bl_net_System_Text_Json_JsonSerializer_Serialize_8b1de470";

    private static NetProvenanceMap Provenance() => new(new[]
    {
        new KeyValuePair<string, NetWrapperOrigin>(
            MakeGenericTypeWrapper,
            NetWrapperOrigin.BasCallSite(@"C:\game\MyGame.bas", 42, 17, "Type.MakeGenericType")),
        new KeyValuePair<string, NetWrapperOrigin>(
            JsonSerializeWrapper,
            NetWrapperOrigin.BasCallSite(@"C:\game\Save.bas", 108, 9, "JsonSerializer.Serialize")),
    });

    private static readonly string[] ReferencedAssemblies = { "Contoso.Serialization", "Newtonsoft.Json" };

    private static AotDiagnostic Single(string text) =>
        AotDiagnosticMapper.Map(text, Provenance(), ReferencedAssemblies).Single();

    // ---- Tier 1: mapped to the .bas line through the provenance map ----------------------

    [Test]
    public void Il3050AgainstAGeneratedWrapper_IsABl6020ErrorAtTheBasCallSite()
    {
        var d = Single(Il3050AgainstWrapper);

        Assert.Multiple(() =>
        {
            Assert.That(d.Code, Is.EqualTo("BL6020"));
            Assert.That(d.IlCode, Is.EqualTo("IL3050"));
            // §15.10: RequiresDynamicCode throws at runtime, so building would ship a known crash.
            Assert.That(d.IsWarning, Is.False, "IL3050 is RequiresDynamicCode — spec §15.10 pins it to ERROR.");
            Assert.That(d.Tier, Is.EqualTo(AotAttribution.WrapperCallSite));
            Assert.That(d.File, Is.EqualTo(@"C:\game\MyGame.bas"),
                "Tier 1 must report the .bas location from the provenance map, not Exports.g.cs.");
            Assert.That(d.Line, Is.EqualTo(42));
            Assert.That(d.Column, Is.EqualTo(17));
            Assert.That(d.OriginMember, Does.Contain(MakeGenericTypeWrapper));
        });
    }

    [Test]
    public void Il2026AgainstAGeneratedWrapper_IsABl6020WarningAtTheBasLine()
    {
        var d = Single(Il2026AgainstWrapper);

        Assert.Multiple(() =>
        {
            Assert.That(d.IlCode, Is.EqualTo("IL2026"));
            // §15.10: trim-analysis warnings are often conservative and not end-developer actionable.
            Assert.That(d.IsWarning, Is.True, "IL2026 is trim analysis — spec §15.10 pins it to WARNING.");
            Assert.That(d.Tier, Is.EqualTo(AotAttribution.WrapperCallSite));
            Assert.That(d.File, Is.EqualTo(@"C:\game\Save.bas"));
            Assert.That(d.Line, Is.EqualTo(108));
        });
    }

    [Test]
    public void ANetProxyDeclaredWrapper_IsAttributedToTheBlprojItem()
    {
        // §7.2's second surface source: the origin is a <NetProxy> item, not a .bas call site.
        var provenance = new NetProvenanceMap(new[]
        {
            new KeyValuePair<string, NetWrapperOrigin>(
                MakeGenericTypeWrapper,
                NetWrapperOrigin.NetProxyDeclaration(@"C:\game\MyGame.blproj", "System.Type", 12)),
        });

        var d = AotDiagnosticMapper.Map(Il3050AgainstWrapper, provenance, ReferencedAssemblies).Single();

        Assert.Multiple(() =>
        {
            Assert.That(d.Tier, Is.EqualTo(AotAttribution.WrapperCallSite));
            Assert.That(d.OriginKind, Is.EqualTo(NetWrapperOriginKind.NetProxyDeclaration));
            Assert.That(d.File, Is.EqualTo(@"C:\game\MyGame.blproj"));
            Assert.That(d.Line, Is.EqualTo(12));
            Assert.That(d.Format(), Does.Contain("<NetProxy"),
                "A declared-surface origin must say so; a user who wrote no .bas call site would "
                + "otherwise hunt for one that does not exist.");
        });
    }

    // ---- Tier 2: origin inside a referenced assembly -------------------------------------

    [Test]
    public void ATrimWarningInsideAReferencedAssembly_NamesAssemblyAndOriginMember()
    {
        var d = Single(Il2070InReferencedAssembly);

        Assert.Multiple(() =>
        {
            Assert.That(d.IlCode, Is.EqualTo("IL2070"));
            Assert.That(d.IsWarning, Is.True);
            Assert.That(d.Tier, Is.EqualTo(AotAttribution.ReferencedAssembly));
            Assert.That(d.AssemblyName, Is.EqualTo("Contoso.Serialization"));
            Assert.That(d.OriginMember, Is.EqualTo("Contoso.Serialization.Reflector.MemberNames(Type)"));
            // §11.3 tier 2: "a wrapper-keyed map cannot resolve it" — so no .bas location.
            Assert.That(d.File, Is.Null, "Tier 2 has no .bas location to claim.");
        });
    }

    [Test]
    public void SeverityAndTierAreIndependent_Il3050InAReferencedAssemblyIsStillAnError()
    {
        var d = Single(Il3050InReferencedAssembly);

        Assert.Multiple(() =>
        {
            Assert.That(d.Tier, Is.EqualTo(AotAttribution.ReferencedAssembly));
            Assert.That(d.IsWarning, Is.False,
                "§15.10 splits severity by DIAGNOSTIC CLASS, not by attribution tier — fix the "
                + "severity rule in AotDiagnosticMapper, not the tier rule.");
            Assert.That(d.AssemblyName, Is.EqualTo("Contoso.Serialization"));
        });
    }

    // ---- Tier 3: assembly-level aggregates -----------------------------------------------

    [Test]
    public void Il3053Aggregate_IsAWarningNamingTheAssemblyOnly()
    {
        var d = Single(Il3053Aggregate);

        Assert.Multiple(() =>
        {
            Assert.That(d.IlCode, Is.EqualTo("IL3053"));
            // §15.10: an aggregate does not prove the program's OWN paths break.
            Assert.That(d.IsWarning, Is.True,
                "IL3053 is an assembly-level aggregate — §15.10 pins it to WARNING even though it "
                + "is an IL3xxx code.");
            Assert.That(d.Tier, Is.EqualTo(AotAttribution.AssemblyAggregate));
            Assert.That(d.AssemblyName, Is.EqualTo("Contoso.Serialization"));
            Assert.That(d.OriginMember, Is.Null, "§11.3 tier 3 names the assembly ONLY.");
            Assert.That(d.File, Is.Null);
        });
    }

    [Test]
    public void Il2104Aggregate_IsAWarningNamingTheAssemblyOnly()
    {
        var d = Single(Il2104Aggregate);

        Assert.Multiple(() =>
        {
            Assert.That(d.IlCode, Is.EqualTo("IL2104"));
            Assert.That(d.IsWarning, Is.True);
            Assert.That(d.Tier, Is.EqualTo(AotAttribution.AssemblyAggregate));
            Assert.That(d.AssemblyName, Is.EqualTo("Contoso.Serialization"));
            Assert.That(d.OriginMember, Is.Null);
        });
    }

    // ---- Never dropped --------------------------------------------------------------------

    [Test]
    public void AnUnparsableDiagnosticLine_IsStillReported()
    {
        const string mangled = "ILC : AOT analysis warning IL9999 something entirely unexpected happened";

        var all = AotDiagnosticMapper.Map(mangled, Provenance(), ReferencedAssemblies);

        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(1),
                "An IL-coded line that no form matches must still be REPORTED — silently dropping "
                + "it hides the AOT ceiling, which is the one thing BL6020 exists to make visible.");
            Assert.That(all[0].IlCode, Is.EqualTo("IL9999"));
            Assert.That(all[0].Tier, Is.EqualTo(AotAttribution.Unattributed));
            Assert.That(all[0].RawText, Is.EqualTo(mangled));
        });
    }

    [Test]
    public void ACscStageDiagnosticWithAColumnAndNoOriginMember_IsStillReported()
    {
        var d = Single(Il2057FromCsc);

        Assert.Multiple(() =>
        {
            Assert.That(d.IlCode, Is.EqualTo("IL2057"));
            Assert.That(d.IsWarning, Is.True);
            Assert.That(d.OriginMember, Is.Null,
                "A csc-stage IL diagnostic carries no origin member — do not invent one from the message.");
            Assert.That(d.Tier, Is.EqualTo(AotAttribution.Unattributed));
        });
    }

    [Test]
    public void PublishChatterProducesNoDiagnostics()
    {
        Assert.That(AotDiagnosticMapper.Map(Noise, Provenance(), ReferencedAssemblies), Is.Empty);
    }

    [Test]
    public void EmptyAndNullOutputAreHandled()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AotDiagnosticMapper.Map(null, Provenance(), ReferencedAssemblies), Is.Empty);
            Assert.That(AotDiagnosticMapper.Map("", Provenance(), ReferencedAssemblies), Is.Empty);
            Assert.That(AotDiagnosticMapper.Map(Il3053Aggregate, null, null), Has.Count.EqualTo(1),
                "A null provenance map means 'nothing to resolve', not a crash.");
        });
    }

    // ---- Not a two-code allowlist ---------------------------------------------------------

    [Test]
    public void EveryIlCodeIsScanned_NotJustIl3050AndIl2026()
    {
        // §11.3: "scans ALL ILxxxx trim/AOT diagnostics — not a two-code allowlist".
        var text = string.Join("\n",
            Noise, Il3050AgainstWrapper, Il2026AgainstWrapper, Il2070InReferencedAssembly,
            Il3050InReferencedAssembly, Il2055InReferencedAssembly, Il2075InReferencedAssembly,
            Il2104Aggregate, Il3053Aggregate, Il2057FromCsc);

        var codes = AotDiagnosticMapper.Map(text, Provenance(), ReferencedAssemblies)
            .Select(d => d.IlCode).ToList();

        Assert.That(codes, Is.EquivalentTo(new[]
        {
            "IL3050", "IL2026", "IL2070", "IL3050", "IL2055", "IL2075", "IL2104", "IL3053", "IL2057",
        }), "An allowlist crept back into AotDiagnosticMapper — widen the scan, do not extend a list.");
    }

    [Test]
    public void EveryMappedDiagnosticCarriesBl6020AndTheRemedy()
    {
        var text = string.Join("\n",
            Il3050AgainstWrapper, Il2070InReferencedAssembly, Il3053Aggregate, Il2057FromCsc);

        foreach (var d in AotDiagnosticMapper.Map(text, Provenance(), ReferencedAssemblies))
        {
            Assert.That(d.Code, Is.EqualTo("BL6020"));
            Assert.That(d.Format(), Does.Contain("BL6020"));
            Assert.That(d.Format(), Does.Contain("CoreCLR hosting transport"),
                "§11.3 requires the remedy to name the real fix even though P2b does not exist yet.");
            Assert.That(d.Format(), Does.Contain(d.IlCode),
                "The originating IL code must survive into the message — it is the only thing a "
                + "user can search for.");
        }
    }

    [Test]
    public void TheSeveritySplitIsByDiagnosticClass()
    {
        Assert.Multiple(() =>
        {
            // IL3xxx (RequiresDynamicCode) -> error.
            Assert.That(AotDiagnosticMapper.IsErrorCode("IL3050"), Is.True);
            Assert.That(AotDiagnosticMapper.IsErrorCode("IL3051"), Is.True);
            // IL2xxx (trim analysis) -> warning.
            Assert.That(AotDiagnosticMapper.IsErrorCode("IL2026"), Is.False);
            Assert.That(AotDiagnosticMapper.IsErrorCode("IL2070"), Is.False);
            // Aggregates -> warning, both of them, regardless of IL2/IL3 prefix.
            Assert.That(AotDiagnosticMapper.IsErrorCode("IL2104"), Is.False);
            Assert.That(AotDiagnosticMapper.IsErrorCode("IL3053"), Is.False);
        });
    }

    [Test]
    public void TierOneFormatMatchesTheSpecShape()
    {
        // §11.3's worked tier-1 example: quoted member, the IL code, the remedy, the .bas location.
        var text = Single(Il3050AgainstWrapper).Format();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("IL3050"));
            Assert.That(text, Does.Contain(@"MyGame.bas(42,17)"));
            Assert.That(text, Does.Contain("CoreCLR hosting transport"));
        });
    }

    [Test]
    public void TierThreeFormatNamesTheAssemblyAndSaysAggregated()
    {
        var text = Single(Il3053Aggregate).Format();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Contoso.Serialization"));
            Assert.That(text, Does.Contain("IL3053"));
            Assert.That(text, Does.Not.Contain(".bas"),
                "Tier 3 has no source location — do not fabricate one.");
        });
    }

    [Test]
    public void AnOriginInAnAssemblyThatIsNotInTheClosure_IsStillTierTwoWithoutAnAssemblyName()
    {
        // The assembly attribution is a namespace-prefix match against the reference closure
        // (ILC's per-warning text does not carry the assembly). A miss must degrade, not guess.
        var d = AotDiagnosticMapper.Map(Il2070InReferencedAssembly, Provenance(), new[] { "Newtonsoft.Json" })
            .Single();

        Assert.Multiple(() =>
        {
            Assert.That(d.Tier, Is.EqualTo(AotAttribution.ReferencedAssembly));
            Assert.That(d.AssemblyName, Is.Null);
            Assert.That(d.OriginMember, Is.EqualTo("Contoso.Serialization.Reflector.MemberNames(Type)"));
        });
    }

    [Test]
    public void CarriageReturnsInTheCapturedOutputAreTolerated()
    {
        // Publish output is captured through StringBuilder.AppendLine on Windows -> \r\n.
        var crlf = Il3050AgainstWrapper + "\r\n" + Il3053Aggregate + "\r\n";

        var all = AotDiagnosticMapper.Map(crlf, Provenance(), ReferencedAssemblies);

        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(2));
            Assert.That(all[0].RawText, Does.Not.Contain("\r"),
                "A trailing \\r would leak into the project-file suffix and every RawText pin.");
            Assert.That(all[1].AssemblyName, Is.EqualTo("Contoso.Serialization"));
        });
    }
}
