using System;
using System.IO;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins Task 2.5 — the Git settings group wired through <see cref="ISettingsService"/>:
///
/// * <c>git.confirmSync</c> gates pull / push / sync with a confirmation dialog;
/// * the status-bar Sync button (previously a dead event with zero subscribers) drives the Git
///   panel's Sync command — pull-then-push, single confirmation;
/// * <c>git.autoFetch</c> + <c>git.autoFetchInterval</c> drive the background fetch timer in
///   <see cref="GitAutoFetchService"/> (enabled gate + interval resolution with a 60 s floor), and
///   no fetch is issued when no repository is open;
/// * every wired key names a consumer in <see cref="SettingsConsumerRegistry"/>.
///
/// The confirm gate is verified both as a pure seam and behaviorally through the real Pull/Sync
/// commands; the timer decision logic is pinned as pure seams (the live timer runs on a >= 60 s
/// period and is code-trace verified).
/// </summary>
[TestFixture]
public class SettingsGitWiringTests
{
    private string _homeDir = null!;
    private SettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), $"SettingsGitWiring_{Guid.NewGuid()}");
        Directory.CreateDirectory(_homeDir);
        _service = new SettingsService(_homeDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { _service.Dispose(); } catch { /* ignore */ }
        try { if (Directory.Exists(_homeDir)) Directory.Delete(_homeDir, true); }
        catch { /* ignore cleanup errors */ }
    }

    // ---- git.confirmSync (pure seam) ----

    [Test]
    public void ConfirmSync_DefaultEnabled()
        => Assert.That(GitChangesViewModel.ShouldConfirmSync(_service), Is.True);

    [Test]
    public void ConfirmSync_WhenDisabled_ReturnsFalse()
    {
        _service.Set("git.confirmSync", false);
        Assert.That(GitChangesViewModel.ShouldConfirmSync(_service), Is.False);
    }

    [Test]
    public void ConfirmSync_NullService_TreatedAsEnabled()
        => Assert.That(GitChangesViewModel.ShouldConfirmSync(null), Is.True);

    // ---- git.autoFetch (pure seam) ----

    [Test]
    public void AutoFetch_DefaultEnabled()
        => Assert.That(GitAutoFetchService.ResolveAutoFetchEnabled(_service), Is.True);

    [Test]
    public void AutoFetch_WhenDisabled_ReturnsFalse()
    {
        _service.Set("git.autoFetch", false);
        Assert.That(GitAutoFetchService.ResolveAutoFetchEnabled(_service), Is.False);
    }

    [Test]
    public void AutoFetch_NullService_TreatedAsEnabled()
        => Assert.That(GitAutoFetchService.ResolveAutoFetchEnabled(null), Is.True);

    // ---- git.autoFetchInterval (pure seam) ----

    [Test]
    public void AutoFetchInterval_DefaultIsSchema180Seconds()
        => Assert.That(GitAutoFetchService.ResolveAutoFetchIntervalMs(_service), Is.EqualTo(180_000));

    [Test]
    public void AutoFetchInterval_CustomValue_IsHonored()
    {
        _service.Set("git.autoFetchInterval", 300);
        Assert.That(GitAutoFetchService.ResolveAutoFetchIntervalMs(_service), Is.EqualTo(300_000));
    }

    [Test]
    public void AutoFetchInterval_BelowFloor_ClampsTo60Seconds()
    {
        _service.Set("git.autoFetchInterval", 5);
        Assert.That(GitAutoFetchService.ResolveAutoFetchIntervalMs(_service), Is.EqualTo(60_000),
            "the schema minimum is 60 s; smaller values are clamped up");
    }

    [Test]
    public void AutoFetchInterval_NullService_FallsBackToDefault()
        => Assert.That(GitAutoFetchService.ResolveAutoFetchIntervalMs(null), Is.EqualTo(180_000));

    // ---- ShouldFetchNow gate (pure seam) ----

    [Test]
    public void ShouldFetchNow_OnlyWhenEnabledAndRepoOpen()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GitAutoFetchService.ShouldFetchNow(enabled: true, isRepository: true), Is.True);
            Assert.That(GitAutoFetchService.ShouldFetchNow(enabled: true, isRepository: false), Is.False,
                "no fetch when no repository is open");
            Assert.That(GitAutoFetchService.ShouldFetchNow(enabled: false, isRepository: true), Is.False,
                "no fetch when auto-fetch is disabled");
            Assert.That(GitAutoFetchService.ShouldFetchNow(enabled: false, isRepository: false), Is.False);
        });
    }

    // ---- GitAutoFetchService construction / disposal ----

    [Test]
    public void AutoFetchService_ConstructsAndDisposesCleanly()
    {
        var git = new Mock<IGitService>();
        GitAutoFetchService svc = null!;
        Assert.DoesNotThrow(() => svc = new GitAutoFetchService(git.Object, _service));
        Assert.DoesNotThrow(() => svc.Dispose());
        // Double-dispose must be safe.
        Assert.DoesNotThrow(() => svc.Dispose());
    }

    // ---- Confirm gate behavior through the real Pull command ----

    [Test]
    public async Task Pull_ConfirmDisabled_ProceedsWithoutPrompt()
    {
        var git = NewGitMock(isRepo: true);
        var dialog = new Mock<IDialogService>();
        _service.Set("git.confirmSync", false);

        var vm = NewChangesVm(git, dialog);
        await vm.PullCommand.ExecuteAsync(null);

        git.Verify(g => g.PullAsync(), Times.Once);
        dialog.Verify(d => d.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>(),
            DialogButtons.YesNo, It.IsAny<DialogIcon>()), Times.Never,
            "confirmSync off means no confirmation dialog");
    }

    [Test]
    public async Task Pull_ConfirmEnabled_UserDeclines_DoesNotPull()
    {
        var git = NewGitMock(isRepo: true);
        var dialog = NewDialogMock(confirm: false);
        // git.confirmSync defaults to true

        var vm = NewChangesVm(git, dialog);
        await vm.PullCommand.ExecuteAsync(null);

        git.Verify(g => g.PullAsync(), Times.Never, "declining the confirmation aborts the pull");
    }

    [Test]
    public async Task Pull_ConfirmEnabled_UserAccepts_Pulls()
    {
        var git = NewGitMock(isRepo: true);
        var dialog = NewDialogMock(confirm: true);

        var vm = NewChangesVm(git, dialog);
        await vm.PullCommand.ExecuteAsync(null);

        git.Verify(g => g.PullAsync(), Times.Once);
    }

    // ---- Sync command (drives pull-then-push; what the status-bar button now triggers) ----

    [Test]
    public async Task Sync_ConfirmDisabled_RepoOpen_PullsThenPushes()
    {
        var git = NewGitMock(isRepo: true);
        var dialog = new Mock<IDialogService>();
        _service.Set("git.confirmSync", false);

        var vm = NewChangesVm(git, dialog);
        await vm.SyncCommand.ExecuteAsync(null);

        git.Verify(g => g.PullAsync(), Times.Once);
        git.Verify(g => g.PushAsync(), Times.Once);
    }

    [Test]
    public async Task Sync_NotARepository_DoesNothing()
    {
        var git = NewGitMock(isRepo: false);
        var dialog = new Mock<IDialogService>();

        var vm = NewChangesVm(git, dialog);
        await vm.SyncCommand.ExecuteAsync(null);

        git.Verify(g => g.PullAsync(), Times.Never);
        git.Verify(g => g.PushAsync(), Times.Never);
    }

    [Test]
    public async Task Sync_PullFails_DoesNotPush()
    {
        var git = NewGitMock(isRepo: true);
        git.Setup(g => g.PullAsync()).ReturnsAsync(new GitPullResult { Success = false, ErrorMessage = "boom" });
        var dialog = new Mock<IDialogService>();
        _service.Set("git.confirmSync", false);

        var vm = NewChangesVm(git, dialog);
        await vm.SyncCommand.ExecuteAsync(null);

        git.Verify(g => g.PullAsync(), Times.Once);
        git.Verify(g => g.PushAsync(), Times.Never, "a failed pull must not push unmerged work");
    }

    // ---- Consumer registry ----

    [Test]
    public void GitConsumers_AreNamed_AfterConstruction()
    {
        _ = NewChangesVm(NewGitMock(isRepo: false), new Mock<IDialogService>()); // registers git.confirmSync
        _ = new GitAutoFetchService(new Mock<IGitService>().Object, _service);   // registers git.autoFetch*

        foreach (var key in new[] { "git.confirmSync", "git.autoFetch", "git.autoFetchInterval" })
        {
            Assert.That(SettingsConsumerRegistry.IsRegistered(key), Is.True,
                $"{key} must name a consumer in SettingsConsumerRegistry (wired in Task 2.5)");
        }
    }

    // ---- helpers ----

    private static Mock<IGitService> NewGitMock(bool isRepo)
    {
        var git = new Mock<IGitService>();
        git.Setup(g => g.IsGitRepository).Returns(isRepo);
        git.Setup(g => g.PullAsync()).ReturnsAsync(new GitPullResult { Success = true });
        git.Setup(g => g.PushAsync()).ReturnsAsync(new GitPushResult { Success = true });
        return git;
    }

    private static Mock<IDialogService> NewDialogMock(bool confirm)
    {
        var dialog = new Mock<IDialogService>();
        // ConfirmAsync is an extension over ShowMessageAsync(YesNo/Question).
        dialog.Setup(d => d.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>(),
                DialogButtons.YesNo, DialogIcon.Question))
            .ReturnsAsync(confirm ? DialogResult.Yes : DialogResult.No);
        return dialog;
    }

    private GitChangesViewModel NewChangesVm(Mock<IGitService> git, Mock<IDialogService> dialog)
    {
        var vm = new GitChangesViewModel(git.Object, dialog.Object, new Mock<IOutputService>().Object, _service);
        // Disarm the auto-refresh timer so it can't fire during the (short) test.
        vm.IsAutoRefreshEnabled = false;
        return vm;
    }
}
