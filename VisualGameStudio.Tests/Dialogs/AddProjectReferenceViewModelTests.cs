using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Tests.Dialogs;

[TestFixture]
public class AddProjectReferenceViewModelTests
{
    [Test]
    public void Initialize_fills_candidates_unchecked()
    {
        var vm = new AddProjectReferenceViewModel();

        vm.Initialize(new[] { "A", "B", "C" });

        Assert.That(vm.Candidates.Select(c => c.Name), Is.EqualTo(new[] { "A", "B", "C" }));
        Assert.That(vm.Candidates.All(c => !c.IsChecked), Is.True);
        Assert.That(vm.SelectedNames, Is.Empty);
    }

    [Test]
    public void SelectedNames_includes_only_checked_items()
    {
        var vm = new AddProjectReferenceViewModel();
        vm.Initialize(new[] { "A", "B", "C" });

        vm.Candidates.First(c => c.Name == "A").IsChecked = true;
        vm.Candidates.First(c => c.Name == "C").IsChecked = true;

        Assert.That(vm.SelectedNames, Is.EquivalentTo(new[] { "A", "C" }));
        Assert.That(vm.SelectedNames, Does.Not.Contain("B"));
    }

    [Test]
    public void Ok_sets_DialogResult_true_and_invokes_CloseDialog()
    {
        var vm = new AddProjectReferenceViewModel();
        vm.Initialize(new[] { "A" });
        var closed = false;
        vm.CloseDialog = () => closed = true;

        vm.OkCommand.Execute(null);

        Assert.That(vm.DialogResult, Is.True);
        Assert.That(closed, Is.True);
    }

    [Test]
    public void Cancel_sets_DialogResult_false_and_invokes_CloseDialog()
    {
        var vm = new AddProjectReferenceViewModel();
        vm.Initialize(new[] { "A" });
        var closed = false;
        vm.CloseDialog = () => closed = true;

        vm.CancelCommand.Execute(null);

        Assert.That(vm.DialogResult, Is.False);
        Assert.That(closed, Is.True);
    }
}
