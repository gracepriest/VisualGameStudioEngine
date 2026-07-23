using System;
using System.IO;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Tests.Dialogs;

[TestFixture]
public class NewSolutionViewModelTests
{
    [Test]
    public void CanConfirm_false_when_name_empty()
    {
        var vm = new NewSolutionViewModel { Location = @"C:\x" };
        Assert.That(vm.CanConfirm, Is.False);
    }

    [Test]
    public void Invalid_filename_chars_block_confirm()
    {
        var vm = new NewSolutionViewModel { SolutionName = "a:b", Location = @"C:\x" };
        Assert.That(vm.CanConfirm, Is.False);
        Assert.That(vm.ErrorMessage, Is.Not.Empty);
    }

    [Test]
    public void SolutionFilePreview_composes_path()
    {
        var vm = new NewSolutionViewModel { SolutionName = "MySln", Location = @"C:\src" };
        Assert.That(vm.SolutionFilePreview, Does.EndWith(@"MySln\MySln.blsln"));
        Assert.That(vm.CanConfirm, Is.True);
    }

    [Test]
    public void Existing_nonempty_target_dir_blocks_confirm()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vgs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "MySln"));
        File.WriteAllText(Path.Combine(dir, "MySln", "x.txt"), "x");   // target already populated
        try
        {
            var vm = new NewSolutionViewModel { SolutionName = "MySln", Location = dir };
            Assert.That(vm.CanConfirm, Is.False);
            Assert.That(vm.ErrorMessage, Does.Contain("exists"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void InitializeGit_defaults_true()
        => Assert.That(new NewSolutionViewModel().InitializeGit, Is.True);
}
