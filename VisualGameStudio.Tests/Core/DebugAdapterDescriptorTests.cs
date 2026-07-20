using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Tests.Core;

/// <summary>
/// Pins the declarative surface of <see cref="DebugAdapterDescriptor"/>: routing by
/// <see cref="BasicLangProject.IsNativeBuild"/>, session-start (never cached) launch
/// resolution, and the per-adapter metadata (timeouts, fallback filters, toolchains).
/// </summary>
[TestFixture]
public class DebugAdapterDescriptorTests
{
    private static DebugAdapterDescriptor Managed() =>
        DebugAdapterDescriptor.BasicLangManaged(() => null);

    private static DebugAdapterDescriptor Lldb() =>
        DebugAdapterDescriptor.LldbDap(() => null);

    [Test]
    public void Routing_ManagedServesManaged_LldbServesNative()
    {
        var managed = Managed();
        var lldb = Lldb();

        // Default: BasicLang language + C# backend = managed build.
        var csharpProject = new BasicLangProject();
        // BasicLang source on the C++ backend builds natively (IsNativeBuild, BasicLangProject.cs:14).
        var cppBackendProject = new BasicLangProject { TargetBackend = TargetBackend.Cpp };
        // Hand-written C++ project — also native.
        var cppLanguageProject = new BasicLangProject { Language = ProjectLanguage.Cpp };

        Assert.That(managed.Serves(csharpProject), Is.True);
        Assert.That(lldb.Serves(csharpProject), Is.False);

        Assert.That(managed.Serves(cppBackendProject), Is.False);
        Assert.That(lldb.Serves(cppBackendProject), Is.True);

        Assert.That(managed.Serves(cppLanguageProject), Is.False);
        Assert.That(lldb.Serves(cppLanguageProject), Is.True);
    }

    [Test]
    public void LaunchCommand_IsResolvedPerCall_NeverCached()
    {
        // The installed-mid-session contract: the first resolution finds nothing, the
        // adapter is installed, the next session's resolution finds it. A cached answer
        // (clangd's DI-time resolution, by contrast) would return null forever.
        var lldbCalls = 0;
        var lldb = DebugAdapterDescriptor.LldbDap(
            () => ++lldbCalls == 1 ? null : @"C:\tools\lldb-dap.exe");

        Assert.That(lldb.ResolveLaunchCommand(), Is.Null);
        var found = lldb.ResolveLaunchCommand();

        Assert.That(lldbCalls, Is.EqualTo(2));
        Assert.That(found, Is.Not.Null);
        Assert.That(found!.FileName, Is.EqualTo(@"C:\tools\lldb-dap.exe"));

        var managedCalls = 0;
        var managed = DebugAdapterDescriptor.BasicLangManaged(
            () => { managedCalls++; return @"C:\IDE\BasicLang.dll"; });

        managed.ResolveLaunchCommand();
        managed.ResolveLaunchCommand();

        Assert.That(managedCalls, Is.EqualTo(2));
    }

    [Test]
    public void LaunchCommand_NullWhenTheLocatorFindsNothing()
    {
        Assert.That(Managed().ResolveLaunchCommand(), Is.Null);
        Assert.That(Lldb().ResolveLaunchCommand(), Is.Null);
    }

    [Test]
    public void ManagedFactory_ComposesTheDotnetDebugAdapterCommand()
    {
        var managed = DebugAdapterDescriptor.BasicLangManaged(() => @"C:\IDE\BasicLang.dll");

        var command = managed.ResolveLaunchCommand();

        Assert.That(command, Is.Not.Null);
        Assert.That(command!.FileName, Is.EqualTo("dotnet"));
        Assert.That(command.Arguments, Is.EqualTo(@"""C:\IDE\BasicLang.dll"" --debug-adapter"));
    }

    [Test]
    public void Timeouts_LldbLaunchIsSixtySeconds_ManagedThirty()
    {
        Assert.That(Lldb().Timeouts.Launch, Is.EqualTo(TimeSpan.FromSeconds(60)));
        Assert.That(Managed().Timeouts.Launch, Is.EqualTo(TimeSpan.FromSeconds(30)));

        // The full budgets are the T4 profiles, not lookalikes.
        Assert.That(Lldb().Timeouts, Is.EqualTo(DapTimeoutProfile.LldbDap));
        Assert.That(Managed().Timeouts, Is.EqualTo(DapTimeoutProfile.Managed));
    }

    [Test]
    public void FallbackFilters_AreThePinnedVocabulary()
    {
        // lldb-dap's disclosed vocabulary, offered even when initialize discloses nothing.
        Assert.That(Lldb().FallbackExceptionFilters, Is.EqualTo(new[]
        {
            new DapExceptionFilter("cpp_throw", "C++ Throw", false),
            new DapExceptionFilter("cpp_catch", "C++ Catch", false),
        }));

        // The managed adapter's vocabulary — matches the .NET-shaped exception dialog's
        // categories (MainWindowViewModel exception settings: all/uncaught/thrown).
        Assert.That(Managed().FallbackExceptionFilters, Is.EqualTo(new[]
        {
            new DapExceptionFilter("all", "All Exceptions", false),
            new DapExceptionFilter("uncaught", "Uncaught Exceptions", true),
            new DapExceptionFilter("thrown", "Thrown Exceptions", false, SupportsCondition: true),
        }));
    }

    [Test]
    public void Toolchains_PairingMetadata_IsPinned()
    {
        // Spec §6's one-engine/three-routes claim: lldb-dap debugs MSVC, clang and g++
        // output alike. Pinned in order so a route can't silently drop.
        Assert.That(Lldb().Toolchains, Is.EqualTo(new[] { "msvc", "clang", "g++" }));
        Assert.That(Managed().Toolchains, Is.Empty);
    }
}
