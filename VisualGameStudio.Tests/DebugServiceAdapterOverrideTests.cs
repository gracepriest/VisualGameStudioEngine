using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests;

/// <summary>
/// Task 9: <see cref="DebugConfiguration.AdapterExecutableOverride"/> honored by
/// <see cref="DebugService.ResolveCommand"/> — the extracted, unit-testable slice of
/// <c>DebugService.ResolveAdapterLaunch</c>'s override-vs-descriptor decision.
/// </summary>
[TestFixture]
public class DebugServiceAdapterOverrideTests
{
    [Test]
    public void ResolveCommand_WithOverride_UsesOverridePathAndEmptyArgs()
    {
        var descriptor = DebugAdapterDescriptor.LldbDap(() => @"C:\probed\lldb-dap.exe");
        var config = new DebugConfiguration
        {
            AdapterId = DebugAdapterDescriptor.LldbDapId,
            AdapterExecutableOverride = @"C:\overridden\lldb-dap.exe",
        };

        var command = DebugService.ResolveCommand(config, descriptor);

        Assert.That(command, Is.Not.Null);
        Assert.That(command!.FileName, Is.EqualTo(@"C:\overridden\lldb-dap.exe"));
        Assert.That(command.Arguments, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ResolveCommand_WithNullOverride_FallsBackToDescriptorResolution()
    {
        var descriptor = DebugAdapterDescriptor.LldbDap(() => @"C:\probed\lldb-dap.exe");
        var config = new DebugConfiguration
        {
            AdapterId = DebugAdapterDescriptor.LldbDapId,
            AdapterExecutableOverride = null,
        };

        var command = DebugService.ResolveCommand(config, descriptor);

        Assert.That(command, Is.Not.Null);
        Assert.That(command!.FileName, Is.EqualTo(@"C:\probed\lldb-dap.exe"));
        Assert.That(command.Arguments, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ResolveCommand_WithNullOverride_AndDescriptorNotInstalled_ReturnsNull()
    {
        var descriptor = DebugAdapterDescriptor.LldbDap(() => null);
        var config = new DebugConfiguration
        {
            AdapterId = DebugAdapterDescriptor.LldbDapId,
            AdapterExecutableOverride = null,
        };

        var command = DebugService.ResolveCommand(config, descriptor);

        Assert.That(command, Is.Null);
    }
}
