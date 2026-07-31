using BasicLang.Compiler.CodeGen.CPlusPlus;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

[TestFixture]
public class BlnetContractTests
{
    [Test]
    public void StatusCodes_AreDenseFromZero_AndUniquelyNamed()
    {
        var codes = BlnetContract.StatusCodes;
        Assert.That(codes[0], Is.EqualTo(("BLNET_OK", 0, codes[0].Doc)));
        for (int i = 0; i < codes.Count; i++)
            Assert.That(codes[i].Value, Is.EqualTo(i), $"status values must be dense: {codes[i].Name}");
        Assert.That(codes.Select(c => c.Name), Is.Unique);
    }

    [Test]
    public void StatusCodes_ContainEverySpecStatus()
    {
        var names = BlnetContract.StatusCodes.Select(c => c.Name).ToHashSet();
        foreach (var required in new[]
        {
            "BLNET_OK", "BLNET_E_STALE_HANDLE", "BLNET_E_STALE_CALLBACK",
            "BLNET_E_MANAGED_EXCEPTION", "BLNET_E_NATIVE_EXCEPTION",
            "BLNET_E_CROSS_THREAD_RESULT", "BLNET_E_PUMP_REENTRY",
            "BLNET_E_VERSION_MISMATCH", "BLNET_E_ALLOC",
        })
            Assert.That(names, Does.Contain(required));
    }

    [Test]
    public void GenerateStatusHeader_EmitsOneDefinePerCode_WithGeneratedBanner()
    {
        var header = BlnetContract.GenerateStatusHeader();
        Assert.That(header, Does.StartWith("/* GENERATED from BlnetContract"));
        Assert.That(header, Does.Contain($"#define BLNET_ABI_VERSION {BlnetContract.AbiVersion}"));
        foreach (var (name, value, _) in BlnetContract.StatusCodes)
            Assert.That(header, Does.Contain($"#define {name} {value}"));
    }

    [Test]
    public void GenerateStatusEnumCs_EmitsOneMemberPerCode()
    {
        var cs = BlnetContract.GenerateStatusEnumCs();
        Assert.That(cs, Does.Contain("public enum BlnetStatus"));
        foreach (var (name, value, _) in BlnetContract.StatusCodes)
            Assert.That(cs, Does.Contain($"{name} = {value},"));
    }

    [Test]
    public void MapperInvariant_TypeMapKeys_Equal_BridgedPlusObject()
    {
        var expected = BasicLang.BoundaryTypeRegistry
            .NamesInCategory(BasicLang.BoundaryTypeCategory.Bridged)
            .Append("Object")
            .Select(n => n.ToLowerInvariant()).OrderBy(n => n).ToArray();
        var actual = new BasicLang.Compiler.CodeGen.CppTypeMapper().MappedTypeNamesForInvariantCheck
            .Select(n => n.ToLowerInvariant()).OrderBy(n => n).ToArray();
        Assert.That(actual, Is.EqualTo(expected),
            "CppTypeMapper._typeMap and BoundaryTypeRegistry drifted — update the registry, not a parallel list");
    }

    [Test]
    public void ShimStatusEnum_MatchesContract()
    {
        foreach (var (name, value, _) in BlnetContract.StatusCodes)
            Assert.That((int)Enum.Parse<BlnetTestShim.BlnetStatus>(name), Is.EqualTo(value),
                "BlnetStatus.cs drifted — regenerate from BlnetContract.GenerateStatusEnumCs()");
        Assert.That(BlnetTestShim.ShimAbi.AbiVersion, Is.EqualTo(BlnetContract.AbiVersion),
            "ShimAbi.AbiVersion drifted from BlnetContract.AbiVersion");
        Assert.That(Enum.GetValues<BlnetTestShim.BlnetStatus>().Length, Is.EqualTo(BlnetContract.StatusCodes.Count),
            "BlnetStatus has extra/missing members vs BlnetContract.StatusCodes — regenerate from GenerateStatusEnumCs()");
    }
}

[TestFixture]
public class BlnetRuntimeSourcesTests
{
    [Test]
    public void Header_ContainsGeneratedStatusSection() =>
        Assert.That(BlnetRuntimeSources.BlnetHeader,
            Does.Contain(BlnetContract.GenerateStatusHeader()));

    [Test]
    public void Header_DefinesCallMacro_HandleTypes_AndAllExportNames()
    {
        var h = BlnetRuntimeSources.BlnetHeader;
        Assert.That(h, Does.Contain("#define BLNET_CALL"));
        Assert.That(h, Does.Contain("typedef uint64_t blnet_handle;"));
        // Reads BlnetContract, not a fourth hand-typed copy of the seven names. This half is a
        // wiring check by construction (the header is GENERATED from the same list); the real
        // oracle is CoreExportMacros_AreAllBoundByBindCore below, whose other side is a
        // hand-written C++ constant.
        foreach (var export in BlnetContract.CoreExportNames)
            Assert.That(h, Does.Contain($"\"{export}\""));
    }

    /// <summary>
    /// THE drift oracle for <see cref="BlnetContract.CoreExports"/>: <c>blnet_bind_core</c> is a
    /// hand-written C++ literal inside <c>BlnetRuntimeSources.Runtime</c>, derived from nothing,
    /// so this genuinely compares two independent things. Adding a core export to the contract
    /// without teaching <c>blnet_bind_core</c> to resolve it would otherwise ship a shim export
    /// that no native code ever binds — silent, and only observable as a null slot at a call site.
    /// </summary>
    [Test]
    public void CoreExportMacros_AreAllBoundByBindCore()
    {
        var runtime = BlnetRuntimeSources.BlnetRuntime;
        foreach (var (macro, name, _) in BlnetContract.CoreExports)
            Assert.That(runtime, Does.Contain($"blnet_get_symbol(module, {macro})"),
                $"blnet_bind_core in BlnetRuntimeSources does not resolve '{name}' via {macro}. "
                + "Fix blnet_runtime.hpp's blnet_bind_core (the C++ side), or drop the export "
                + "from BlnetContract.CoreExports if it was added by mistake.");
    }

    /// <summary>
    /// P2a §9.3's three additions (plan Task 12). Two are transport-neutral and P2b reuses
    /// them; the third — the platform loader — is transport-A-specific.
    /// </summary>
    [Test]
    public void Runtime_CarriesTheStartupSymbols()
    {
        var r = BlnetRuntimeSources.BlnetRuntime;

        Assert.Multiple(() =>
        {
            Assert.That(r, Does.Contain(
                "inline BlnetNativeVtable g_native_vtable{ &blnet_invoke_callback, "
                + "&blnet_get_native_error };"),
                "g_native_vtable is the native half of P0's 2-slot POSITIONAL vtable. Its slot "
                + "order must match BlnetNativeVtable in blnet.h; swapping the two routes every "
                + "callback invocation into the error-message puller instead.");
            Assert.That(r, Does.Contain("inline const char* blnet_bind_core(void* module)"),
                "blnet_bind_core binds P0's seven exports into g_shim and is what the generated "
                + "startup TU calls. It returns the first UNRESOLVED export name (nullptr on "
                + "success) rather than a status, because the contract has no missing-export "
                + "code and adding one bumps BLNET_ABI_VERSION (rule C7).");
            Assert.That(r, Does.Contain("void* blnet_load_module(const char* name);"),
                "blnet_load_module is DECLARED unconditionally and defined only under "
                + "BLNET_IMPLEMENT_LOADER.");
            Assert.That(r, Does.Contain("void* blnet_get_symbol(void* module, const char* name);"));
            Assert.That(r, Does.Contain("const char* blnet_load_error();"));
        });
    }

    /// <summary>
    /// The platform headers must reach exactly ONE translation unit. <c>&lt;windows.h&gt;</c>
    /// macro-replaces ordinary identifiers (<c>CreateFile</c>, <c>DeleteFile</c>,
    /// <c>CopyFile</c>, <c>min</c>, <c>max</c>, ...), and this header is included by every
    /// generated BasicLang TU in a .NET-using project — so an unguarded include here would
    /// break user programs whose only sin is naming a Sub <c>DeleteFile</c>.
    /// </summary>
    [Test]
    public void Runtime_ConfinesPlatformHeadersBehindTheLoaderMacro()
    {
        var r = BlnetRuntimeSources.BlnetRuntime;
        var guard = r.IndexOf("#ifdef BLNET_IMPLEMENT_LOADER", StringComparison.Ordinal);

        Assert.That(guard, Is.GreaterThan(-1),
            "blnet_runtime.hpp lost its BLNET_IMPLEMENT_LOADER guard.");
        Assert.Multiple(() =>
        {
            Assert.That(r.IndexOf("#include <windows.h>", StringComparison.Ordinal),
                Is.GreaterThan(guard),
                "<windows.h> escaped the BLNET_IMPLEMENT_LOADER guard in blnet_runtime.hpp. "
                + "Every generated .NET TU includes this header; windows.h's macros would then "
                + "rewrite ordinary identifiers in user code.");
            Assert.That(r.IndexOf("#include <dlfcn.h>", StringComparison.Ordinal),
                Is.GreaterThan(guard),
                "<dlfcn.h> escaped the BLNET_IMPLEMENT_LOADER guard in blnet_runtime.hpp.");
            Assert.That(r, Does.Contain("#define NOMINMAX"),
                "windows.h must be pulled in with NOMINMAX — std::min/std::max appear in "
                + "generated C++ and the macros break them.");
        });
    }
}

[TestFixture]
public class BoundaryTypeRegistryTests
{
    [TestCase("Integer")] [TestCase("String")] [TestCase("ULong")] [TestCase("Void")]
    public void TodaysMappedPrimitives_AreBridged(string name) =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize(name),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Bridged));

    /// <summary>
    /// The POST-P1 reject list: the six native BCL types and SByte moved out
    /// (P1 Task 10 flip); what remains is P2 territory (Object's void* erasure
    /// is unsound; Regex/Uri/Stream/FileInfo/DirectoryInfo need the managed shim).
    /// </summary>
    [TestCase("Object")] [TestCase("Regex")]
    [TestCase("Uri")] [TestCase("Stream")] [TestCase("FileInfo")] [TestCase("DirectoryInfo")]
    public void TodaysRejectList_IsRejected(string name) =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize(name),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Rejected));

    /// <summary>Spec §2: the six P1 types are NativeOwned (pure C++, never a handle).</summary>
    [TestCase("DateTime")] [TestCase("TimeSpan")] [TestCase("Guid")]
    [TestCase("StringBuilder")] [TestCase("Decimal")] [TestCase("DateTimeOffset")]
    public void P1Types_AreNativeOwned(string name) =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize(name),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.NativeOwned));

    /// <summary>
    /// Spec §2: SByte is a plain primitive, NOT a native BCL type — it joins
    /// CppTypeMapper._typeMap as int8_t and therefore Bridged (MapperInvariant
    /// above holds the two in lockstep).
    /// </summary>
    [Test]
    public void SByte_IsBridged() =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize("SByte"),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Bridged));

    [Test]
    public void CategorizeIsCaseInsensitive() =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize("regex"),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Rejected));

    /// <summary>
    /// Task 10 rider (b): a leading `System.` qualifier normalizes away, mirroring
    /// NativeBclSurface.Normalize. Without this, `System.DateTime` categorized
    /// Unknown — a hole in the reject net pre-P1, and post-flip it would bypass
    /// EVERY piece of NativeOwned codegen machinery (all of which keys off
    /// Categorize) and silently emit a bare, undefined C++ type name.
    /// </summary>
    [TestCase("System.DateTime", BasicLang.BoundaryTypeCategory.NativeOwned)]
    [TestCase("System.Text.StringBuilder", BasicLang.BoundaryTypeCategory.NativeOwned)]
    [TestCase("System.Object", BasicLang.BoundaryTypeCategory.Rejected)]
    [TestCase("System.Text.RegularExpressions.Regex", BasicLang.BoundaryTypeCategory.Rejected)]
    [TestCase("System.Int32", BasicLang.BoundaryTypeCategory.Unknown)] // .NET CLR spelling is not a BL name
    public void QualifiedSystemName_NormalizesToTheSameCategory(
        string name, BasicLang.BoundaryTypeCategory expected) =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize(name), Is.EqualTo(expected));

    [Test]
    public void UnknownName_IsUnknown() =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize("MyGameSprite"),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Unknown));

    [Test]
    public void NativeOwned_ContainsExactlyTheP1Six() =>
        Assert.That(BasicLang.BoundaryTypeRegistry.NamesInCategory(
                BasicLang.BoundaryTypeCategory.NativeOwned),
            Is.EquivalentTo(new[]
            {
                "DateTime", "TimeSpan", "Guid", "StringBuilder", "Decimal", "DateTimeOffset"
            }));

    /// <summary>ManagedOwned is P2 territory — still empty after the P1 flip.</summary>
    [Test]
    public void ManagedOwned_StillEmpty() =>
        Assert.That(BasicLang.BoundaryTypeRegistry.NamesInCategory(
            BasicLang.BoundaryTypeCategory.ManagedOwned), Is.Empty);
}
