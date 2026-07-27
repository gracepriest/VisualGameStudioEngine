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
}

[TestFixture]
public class BoundaryTypeRegistryTests
{
    [TestCase("Integer")] [TestCase("String")] [TestCase("ULong")] [TestCase("Void")]
    public void TodaysMappedPrimitives_AreBridged(string name) =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize(name),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Bridged));

    [TestCase("Object")] [TestCase("Decimal")] [TestCase("SByte")]
    [TestCase("DateTime")] [TestCase("DateTimeOffset")] [TestCase("TimeSpan")]
    [TestCase("Guid")] [TestCase("StringBuilder")] [TestCase("Regex")]
    [TestCase("Uri")] [TestCase("Stream")] [TestCase("FileInfo")] [TestCase("DirectoryInfo")]
    public void TodaysRejectList_IsRejected(string name) =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize(name),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Rejected));

    [Test]
    public void CategorizeIsCaseInsensitive() =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize("datetime"),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Rejected));

    [Test]
    public void UnknownName_IsUnknown() =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize("MyGameSprite"),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Unknown));

    [Test]
    public void NativeOwnedAndManagedOwned_StartEmpty_PreP1()
    {
        Assert.That(BasicLang.BoundaryTypeRegistry.NamesInCategory(
            BasicLang.BoundaryTypeCategory.NativeOwned), Is.Empty);
        Assert.That(BasicLang.BoundaryTypeRegistry.NamesInCategory(
            BasicLang.BoundaryTypeCategory.ManagedOwned), Is.Empty);
    }
}
