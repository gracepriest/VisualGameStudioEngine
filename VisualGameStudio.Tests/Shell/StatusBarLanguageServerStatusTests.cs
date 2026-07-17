using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Pins the status bar's per-server language-server state.
///
/// <para>
/// The regression it exists to prevent: the IDE used to write a single GLOBAL status string with
/// no server identity ("Language server connected" / "…disconnected — IntelliSense unavailable")
/// from a handler subscribed to EVERY server. With one server that was merely redundant; with two
/// it is a lie — a down clangd firing ConnectionChanged(false) stamped "disconnected" over a
/// perfectly healthy BasicLang, last writer wins, and the user is told IntelliSense is gone for a
/// language that is working.
/// </para>
///
/// <para>
/// So the assertions here are on the WHOLE composed string, not <c>Does.Contain</c>: containment
/// cannot tell "BasicLang: Running · clangd: Stopped" from a string that merely mentions one of
/// them, which is precisely the bug.
/// </para>
/// </summary>
[TestFixture]
public class StatusBarLanguageServerStatusTests
{
    private static StatusBarViewModel WithBothServers(
        LanguageServerState basicLang, LanguageServerState clangd)
    {
        var vm = new StatusBarViewModel();
        vm.UpdateLanguageServerStatus("basiclang", "BasicLang", basicLang);
        vm.UpdateLanguageServerStatus("clangd", "clangd", clangd);
        return vm;
    }

    // THE FIX. Each server's state is reported next to its own name.
    [Test]
    public void ADownClangd_DoesNotStampOverAHealthyBasicLang()
    {
        var vm = WithBothServers(LanguageServerState.Running, LanguageServerState.Stopped);

        Assert.Multiple(() =>
        {
            Assert.That(vm.LspStatus, Is.EqualTo("BasicLang: Running · clangd: Stopped"));
            Assert.That(vm.IsLspRunning, Is.False, "not everything the IDE registered is up");
        });
    }

    // …and symmetrically: BasicLang restarting must not claim C++ IntelliSense is gone.
    [Test]
    public void ARestartingBasicLang_DoesNotStampOverAHealthyClangd()
    {
        var vm = WithBothServers(LanguageServerState.Stopped, LanguageServerState.Running);

        Assert.That(vm.LspStatus, Is.EqualTo("BasicLang: Stopped · clangd: Running"));
    }

    [Test]
    public void EveryServerRunning_IsTheOnlyGreenState()
    {
        var vm = WithBothServers(LanguageServerState.Running, LanguageServerState.Running);

        Assert.Multiple(() =>
        {
            Assert.That(vm.LspStatus, Is.EqualTo("BasicLang: Running · clangd: Running"));
            Assert.That(vm.IsLspRunning, Is.True);
        });
    }

    // The not-found hint (Task 12 is informational only; acquiring clangd is Phase 3b).
    // "Not found" is a distinct third state from "Stopped": a stopped server is one the IDE
    // launched and lost, a not-found one was never installed, and the user's next action
    // differs completely.
    [Test]
    public void ClangdNotFound_IsReportedAsNotFound_NotAsStopped()
    {
        var vm = WithBothServers(LanguageServerState.Running, LanguageServerState.NotFound);

        Assert.Multiple(() =>
        {
            Assert.That(vm.LspStatus, Is.EqualTo("BasicLang: Running · clangd: Not found"));
            Assert.That(vm.IsLspRunning, Is.False);
        });
    }

    // An update is an UPSERT keyed by server id — a reconnect must replace that server's entry,
    // never append a second one. (A dictionary makes this true by construction; the test is here
    // because a list-based rewrite would look identical at every other call site.)
    [Test]
    public void UpdatingTheSameServerTwice_ReplacesItsEntry_RatherThanAppending()
    {
        var vm = WithBothServers(LanguageServerState.Stopped, LanguageServerState.Stopped);

        vm.UpdateLanguageServerStatus("basiclang", "BasicLang", LanguageServerState.Running);

        Assert.That(vm.LspStatus, Is.EqualTo("BasicLang: Running · clangd: Stopped"));
    }

    // Order must not depend on the order servers happen to connect in — the status bar would
    // otherwise reshuffle under the user's cursor as servers come up.
    [Test]
    public void Order_IsStable_RegardlessOfConnectionOrder()
    {
        var vm = new StatusBarViewModel();
        vm.UpdateLanguageServerStatus("clangd", "clangd", LanguageServerState.Running);
        vm.UpdateLanguageServerStatus("basiclang", "BasicLang", LanguageServerState.Running);

        Assert.That(vm.LspStatus, Is.EqualTo("BasicLang: Running · clangd: Running"));
    }

    [Test]
    public void OneServerOnly_ReadsAsThatServerAlone()
    {
        var vm = new StatusBarViewModel();
        vm.UpdateLanguageServerStatus("basiclang", "BasicLang", LanguageServerState.Running);

        Assert.Multiple(() =>
        {
            Assert.That(vm.LspStatus, Is.EqualTo("BasicLang: Running"));
            Assert.That(vm.IsLspRunning, Is.True,
                "a machine with no clangd and a connected BasicLang is a fully healthy machine");
        });
    }

    // Before any server reports in, the indicator must not claim anything is running.
    [Test]
    public void BeforeAnyServerReports_NothingClaimsToBeRunning()
    {
        var vm = new StatusBarViewModel();

        Assert.That(vm.IsLspRunning, Is.False);
    }
}
