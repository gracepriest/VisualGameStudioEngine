using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class CppToolchainResolutionTests
{
    [Test]
    public void TryFindById_UnknownId_ReturnsNull()
        => Assert.That(CppToolchain.TryFindById("borland"), Is.Null);

    [Test]
    public void TryFindById_IsCaseInsensitive_ForInstalledToolchain()
    {
        var avail = CppToolchain.ProbeAvailability();
        if (!avail.Msvc) Assert.Ignore("MSVC not installed on this machine");
        Assert.That(CppToolchain.TryFindById("MSVC"), Is.Not.Null);
    }

    [Test]
    public void ProbeAvailability_AgreesWithFind()
    {
        var avail = CppToolchain.ProbeAvailability();
        var found = CppToolchain.Find();
        Assert.That(found is not null, Is.EqualTo(avail.Llvm || avail.Gcc || avail.Msvc));
    }

    [Test]
    public void TryFindById_NotInstalled_ReturnsNull()
    {
        var avail = CppToolchain.ProbeAvailability();
        if (avail.Gcc) Assert.Ignore("g++ is on PATH on this machine");
        Assert.That(CppToolchain.TryFindById("gcc"), Is.Null);
    }
}
