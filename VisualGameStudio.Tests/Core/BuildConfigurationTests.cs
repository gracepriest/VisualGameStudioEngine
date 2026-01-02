using NUnit.Framework;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class BuildConfigurationTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var config = new BuildConfiguration();

        Assert.That(config.Name, Is.EqualTo("Debug"));
        Assert.That(config.OutputPath, Is.EqualTo("bin\\Debug"));
        Assert.That(config.DebugSymbols, Is.True);
        Assert.That(config.Optimize, Is.False);
        Assert.That(config.DefineConstants, Is.Null);
        Assert.That(config.WarningLevel, Is.EqualTo(WarningLevel.Default));
        Assert.That(config.TreatWarningsAsErrors, Is.False);
        Assert.That(config.AdditionalProperties, Is.Empty);
    }

    [Test]
    public void Name_CanBeSetAndRetrieved()
    {
        var config = new BuildConfiguration { Name = "Release" };

        Assert.That(config.Name, Is.EqualTo("Release"));
    }

    [Test]
    public void OutputPath_CanBeSetAndRetrieved()
    {
        var config = new BuildConfiguration { OutputPath = "bin\\Release" };

        Assert.That(config.OutputPath, Is.EqualTo("bin\\Release"));
    }

    [Test]
    public void DebugSymbols_CanBeSetToFalse()
    {
        var config = new BuildConfiguration { DebugSymbols = false };

        Assert.That(config.DebugSymbols, Is.False);
    }

    [Test]
    public void Optimize_CanBeSetToTrue()
    {
        var config = new BuildConfiguration { Optimize = true };

        Assert.That(config.Optimize, Is.True);
    }

    [Test]
    public void DefineConstants_CanBeSetAndRetrieved()
    {
        var config = new BuildConfiguration { DefineConstants = "DEBUG;TRACE" };

        Assert.That(config.DefineConstants, Is.EqualTo("DEBUG;TRACE"));
    }

    [Test]
    public void WarningLevel_CanBeSetToNone()
    {
        var config = new BuildConfiguration { WarningLevel = WarningLevel.None };

        Assert.That(config.WarningLevel, Is.EqualTo(WarningLevel.None));
    }

    [Test]
    public void WarningLevel_CanBeSetToLow()
    {
        var config = new BuildConfiguration { WarningLevel = WarningLevel.Low };

        Assert.That(config.WarningLevel, Is.EqualTo(WarningLevel.Low));
    }

    [Test]
    public void WarningLevel_CanBeSetToHigh()
    {
        var config = new BuildConfiguration { WarningLevel = WarningLevel.High };

        Assert.That(config.WarningLevel, Is.EqualTo(WarningLevel.High));
    }

    [Test]
    public void WarningLevel_CanBeSetToAll()
    {
        var config = new BuildConfiguration { WarningLevel = WarningLevel.All };

        Assert.That(config.WarningLevel, Is.EqualTo(WarningLevel.All));
    }

    [Test]
    public void TreatWarningsAsErrors_CanBeSetToTrue()
    {
        var config = new BuildConfiguration { TreatWarningsAsErrors = true };

        Assert.That(config.TreatWarningsAsErrors, Is.True);
    }

    [Test]
    public void AdditionalProperties_CanAddAndRetrieve()
    {
        var config = new BuildConfiguration();
        config.AdditionalProperties["LangVersion"] = "latest";
        config.AdditionalProperties["Nullable"] = "enable";

        Assert.That(config.AdditionalProperties["LangVersion"], Is.EqualTo("latest"));
        Assert.That(config.AdditionalProperties["Nullable"], Is.EqualTo("enable"));
    }

    [Test]
    public void DebugConfiguration_TypicalSetup()
    {
        var config = new BuildConfiguration
        {
            Name = "Debug",
            OutputPath = "bin\\Debug",
            DebugSymbols = true,
            Optimize = false,
            DefineConstants = "DEBUG;TRACE",
            WarningLevel = WarningLevel.Default,
            TreatWarningsAsErrors = false
        };

        Assert.That(config.Name, Is.EqualTo("Debug"));
        Assert.That(config.DebugSymbols, Is.True);
        Assert.That(config.Optimize, Is.False);
    }

    [Test]
    public void ReleaseConfiguration_TypicalSetup()
    {
        var config = new BuildConfiguration
        {
            Name = "Release",
            OutputPath = "bin\\Release",
            DebugSymbols = false,
            Optimize = true,
            DefineConstants = "RELEASE",
            WarningLevel = WarningLevel.High,
            TreatWarningsAsErrors = true
        };

        Assert.That(config.Name, Is.EqualTo("Release"));
        Assert.That(config.DebugSymbols, Is.False);
        Assert.That(config.Optimize, Is.True);
        Assert.That(config.TreatWarningsAsErrors, Is.True);
    }
}

[TestFixture]
public class WarningLevelTests
{
    [Test]
    public void None_HasValue0()
    {
        Assert.That((int)WarningLevel.None, Is.EqualTo(0));
    }

    [Test]
    public void Low_HasValue1()
    {
        Assert.That((int)WarningLevel.Low, Is.EqualTo(1));
    }

    [Test]
    public void Default_HasValue2()
    {
        Assert.That((int)WarningLevel.Default, Is.EqualTo(2));
    }

    [Test]
    public void High_HasValue3()
    {
        Assert.That((int)WarningLevel.High, Is.EqualTo(3));
    }

    [Test]
    public void All_HasValue4()
    {
        Assert.That((int)WarningLevel.All, Is.EqualTo(4));
    }

    [Test]
    public void HasFiveValues()
    {
        var values = Enum.GetValues<WarningLevel>();
        Assert.That(values, Has.Length.EqualTo(5));
    }

    [Test]
    public void CanCompare_WarningLevels()
    {
        Assert.That(WarningLevel.All, Is.GreaterThan(WarningLevel.High));
        Assert.That(WarningLevel.High, Is.GreaterThan(WarningLevel.Default));
        Assert.That(WarningLevel.Default, Is.GreaterThan(WarningLevel.Low));
        Assert.That(WarningLevel.Low, Is.GreaterThan(WarningLevel.None));
    }
}
