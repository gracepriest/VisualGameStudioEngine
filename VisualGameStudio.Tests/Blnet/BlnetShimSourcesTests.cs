using System.Text.RegularExpressions;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// The three spec §12.4 shim drift invariants assigned to P2a-1. These tie the fixed C#
/// scaffolding the product will GENERATE (<see cref="BlnetShimSources"/>) to the two things
/// that already exist and are already trusted: the hand-written shim under
/// <c>TestAssets/BlnetTestShim/</c> that the frozen 16-scenario P0 conformance suite
/// validates, and <see cref="BlnetContract"/>.
/// </summary>
[TestFixture]
public class BlnetShimSourcesTests
{
    /// <summary>
    /// Locates the hand-written shim's <c>HandleTable.cs</c> by walking up from the test
    /// binary to the repo root (the established pattern — cf. <c>BlnetShimPublisher</c> in
    /// this folder and the <c>Raylib*ParityTests</c> fixtures). Throws rather than returning
    /// a sentinel: a drift test that cannot find the file it compares against must fail
    /// loudly, never silently pass.
    /// </summary>
    private static string PathToTestShimHandleTable()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        for (var dir = new DirectoryInfo(testDir); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "VisualGameStudioEngine.sln")))
                return Path.Combine(
                    dir.FullName, "VisualGameStudio.Tests", "TestAssets", "BlnetTestShim", "HandleTable.cs");
        throw new DirectoryNotFoundException(
            $"VisualGameStudioEngine.sln not found in any parent of {testDir}; cannot locate the " +
            "hand-written shim's HandleTable.cs to drift-test BlnetShimSources.HandleTable against.");
    }

    [Test]
    public void HandleTableMatchesTheHandWrittenShimTheFrozenSuiteValidates()
    {
        var path = PathToTestShimHandleTable();
        Assert.That(File.Exists(path), Is.True,
            $"The hand-written shim's HandleTable.cs is missing at {path}. This drift test cannot " +
            "run without it — restore the file rather than deleting the test.");

        var shipped = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.That(BlnetShimSources.HandleTable.Replace("\r\n", "\n"), Is.EqualTo(shipped),
            "BlnetShimSources.HandleTable drifted from VisualGameStudio.Tests/TestAssets/" +
            "BlnetTestShim/HandleTable.cs. The frozen P0 conformance suite (spec §12.2) validates " +
            "the hand-written copy; if the generated shim's handle model differs, the suite proves " +
            "nothing about generated shims. Update BOTH, or neither.");
    }

    [Test]
    public void StatusEnumComesFromTheContract() =>
        Assert.That(BlnetShimSources.BlnetStatusCs, Is.EqualTo(BlnetContract.GenerateStatusEnumCs()),
            "The shim's status enum must be generated from BlnetContract — never hand-copied. " +
            "Fix BlnetShimSources.BlnetStatusCs to delegate to BlnetContract.GenerateStatusEnumCs(); " +
            "do not edit the enum text in BlnetShimSources.");

    [Test]
    public void ShimAbiConstantComesFromTheContract() =>
        Assert.That(BlnetShimSources.ShimAbiCs, Does.Contain($"= {BlnetContract.AbiVersion};"),
            "The generated shim's ABI constant must be interpolated from BlnetContract.AbiVersion — " +
            "fix BlnetShimSources.ShimAbiCs, not BlnetContract. The existing pin " +
            "(BlnetContractTests.ShimStatusEnum_MatchesContract) covers the HAND shim only, whose " +
            "ShimAbi is hand-appended to BlnetStatus.cs:26-27 — unusable by a generated shim.");

    /// <summary>
    /// The namespace decision, made executable for NetShimGenerator (plan Task 14): every
    /// generated shim file that declares a namespace declares the SAME one as the hand shim's
    /// HandleTable, so invariant 1 above can stay a byte-equality assert. Unlike the two asserts
    /// above, both sides here are hand-written scaffolding text in BlnetShimSources — a typo in
    /// either turns this red. (BlnetStatusCs is deliberately excluded: it is pinned byte-for-byte
    /// to BlnetContract.GenerateStatusEnumCs(), which emits no namespace.)
    /// </summary>
    [Test]
    public void ShimAbiDeclaresTheSameNamespaceAsHandleTable()
    {
        var handleTableNs = Regex.Match(BlnetShimSources.HandleTable, @"(?m)^namespace [^\r\n]+;$").Value;
        Assert.That(handleTableNs, Is.EqualTo("namespace BlnetTestShim;"),
            "BlnetShimSources.HandleTable no longer declares 'namespace BlnetTestShim;'. That name is " +
            "the P2a-1 namespace decision and Task 14's Exports.g.cs depends on it — change the " +
            "decision deliberately (and the hand shim with it), or revert BlnetShimSources.");

        Assert.That(BlnetShimSources.ShimAbiCs, Does.Contain(handleTableNs),
            $"BlnetShimSources.ShimAbiCs does not declare '{handleTableNs}'. Every generated shim file " +
            "that declares a namespace must agree; fix ShimAbiPrefix in BlnetShimSources.");

        Assert.That(BlnetShimSources.ShimAbiCs, Does.Contain("public static class ShimAbi"),
            "BlnetShimSources.ShimAbiCs must declare 'public static class ShimAbi' — Task 14's " +
            "Exports.g.cs calls ShimAbi.AbiVersion. Fix ShimAbiPrefix in BlnetShimSources.");
    }
}
