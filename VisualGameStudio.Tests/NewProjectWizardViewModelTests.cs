using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Tests;

[TestFixture]
public class NewProjectWizardViewModelTests
{
    // Fake service that mirrors the real filtering: templates are returned for a
    // solution type when SupportedSolutionTypes contains its id. CreateProjectAsync
    // captures the options so tests can assert the state -> options mapping.
    private sealed class FakeTemplateService : IProjectTemplateService
    {
        public CreateProjectOptions? LastOptions { get; private set; }
        public IReadOnlyList<SolutionType> GetSolutionTypes() => SolutionTypes.All;
        public IReadOnlyList<ProjectTemplate> GetProjectTemplates(SolutionType solutionType) =>
            ProjectTemplates.All.Where(t => t.SupportedSolutionTypes.Contains(solutionType.Id)).ToList();
        public IReadOnlyList<ProjectTemplate> GetAllProjectTemplates() => ProjectTemplates.All;
        public Task<ProjectCreationResult> CreateProjectAsync(CreateProjectOptions options, CancellationToken ct = default)
        {
            LastOptions = options;
            return Task.FromResult(new ProjectCreationResult { Success = true, ProjectPath = "X:/proj/proj.blproj" });
        }
        public Task<SolutionCreationResult> CreateSolutionAsync(CreateSolutionOptions options, CancellationToken ct = default)
            => Task.FromResult(new SolutionCreationResult { Success = true });
        public ProjectValidationResult ValidateProjectOptions(CreateProjectOptions options) => new() { IsValid = true };
        public void RegisterTemplate(ProjectTemplate template) { }
        public IReadOnlyList<ProjectTemplate> GetRecentTemplates() => new List<ProjectTemplate>();
    }

    private static NewProjectWizardViewModel NewVm(out FakeTemplateService svc)
    {
        svc = new FakeTemplateService();
        return new NewProjectWizardViewModel(svc);
    }

    [Test]
    public void BasicLang_Backends_MapToSolutionTypes()
    {
        var vm = NewVm(out _);
        vm.SelectedLanguage = ProjectLanguage.BasicLang;

        var ids = vm.Backends.Select(b => b.SolutionType.Id).ToList();
        Assert.That(ids, Is.EqualTo(new[] { "dotnet", "msil", "native", "llvm" }));
        Assert.That(vm.Backends.All(b => b.ToolchainId == null), Is.True);
    }

    [Test]
    public void Cpp_Backends_AreToolchains_OverCppSolutionType()
    {
        var vm = NewVm(out _);
        vm.SelectedLanguage = ProjectLanguage.Cpp;

        Assert.That(vm.Backends.Select(b => b.Name).ToList(),
            Is.EqualTo(new[] { "LLVM (clang++)", "GCC (g++)", "MSVC" }));
        Assert.That(vm.Backends.All(b => b.SolutionType.Id == "cpp"), Is.True);
        Assert.That(vm.Backends.Select(b => b.ToolchainId).ToList(),
            Is.EqualTo(new[] { "llvm", "gcc", "msvc" }));
    }

    [Test]
    public void SwitchingLanguage_ReselectsFirstBackend_AndReloadsTemplates()
    {
        var vm = NewVm(out _);
        vm.SelectedLanguage = ProjectLanguage.Cpp;

        Assert.That(vm.SelectedBackend!.SolutionType.Id, Is.EqualTo("cpp"));
        // cpp templates only: cpp-console-app, cpp-library, cpp-game-app
        Assert.That(vm.VisibleTemplates.Select(t => t.Id),
            Is.EquivalentTo(new[] { "cpp-console-app", "cpp-library", "cpp-game-app" }));
    }

    [Test]
    public void SelectingLanguageOption_DrivesBackendAndTemplateCascade()
    {
        var vm = NewVm(out _);
        vm.SelectedLanguageOption = vm.Languages.First(o => o.Value == ProjectLanguage.Cpp);

        Assert.That(vm.SelectedLanguage, Is.EqualTo(ProjectLanguage.Cpp));
        Assert.That(vm.Backends.Select(b => b.SolutionType.Id), Is.All.EqualTo("cpp"));
        Assert.That(vm.VisibleTemplates.Select(t => t.Id),
            Is.EquivalentTo(new[] { "cpp-console-app", "cpp-library", "cpp-game-app" }));
    }

    [Test]
    public void PlatformFilter_CrossPlatform_ExcludesWinFormsAndWpf()
    {
        var vm = NewVm(out _);
        vm.SelectedLanguage = ProjectLanguage.BasicLang;
        vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");

        var withAll = vm.VisibleTemplates.Select(t => t.Id).ToList();
        Assert.That(withAll, Does.Contain("winforms-app"));

        vm.SelectedPlatform = "Cross-platform";
        var ids = vm.VisibleTemplates.Select(t => t.Id).ToList();
        Assert.That(ids, Does.Not.Contain("winforms-app"));
        Assert.That(ids, Does.Not.Contain("wpf-app"));
        Assert.That(ids, Does.Contain("avalonia-app"));
    }

    [Test]
    public void CategoryFilter_NarrowsToOneCategory()
    {
        var vm = NewVm(out _);
        vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");

        vm.SelectedCategory = "Library";
        Assert.That(vm.VisibleTemplates.All(t => t.Category == "Library"), Is.True);
        Assert.That(vm.VisibleTemplates.Select(t => t.Id), Does.Contain("class-library"));
    }

    [Test]
    public void Search_MatchesNameDescriptionOrTags()
    {
        var vm = NewVm(out _);
        vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");

        vm.SearchText = "winforms"; // matches a tag
        Assert.That(vm.VisibleTemplates.Select(t => t.Id), Is.EqualTo(new[] { "winforms-app" }));
    }

    [Test]
    public void Filters_Compose_CategoryPlusSearch()
    {
        var vm = NewVm(out _);
        vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");

        vm.SelectedCategory = "Desktop";
        vm.SearchText = "avalonia";
        Assert.That(vm.VisibleTemplates.Select(t => t.Id), Is.EqualTo(new[] { "avalonia-app" }));
    }

    [Test]
    public void SwitchingBackend_ResetsCategoryToAll_AndRebuildsCategoryList()
    {
        var vm = NewVm(out _);
        vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");
        vm.SelectedCategory = "Web";

        vm.SelectedLanguage = ProjectLanguage.Cpp; // reselects cpp backend
        Assert.That(vm.SelectedCategory, Is.EqualTo("All"));
        Assert.That(vm.Categories, Does.Not.Contain("Web")); // cpp has no Web template
    }

    [Test]
    public void CanGoNext_RequiresSelectedTemplate()
    {
        var vm = NewVm(out _);
        Assert.That(vm.CanGoNext, Is.True); // a template auto-selected on load
        vm.SelectedTemplate = null;
        Assert.That(vm.CanGoNext, Is.False);
    }

    [Test]
    public void CanCreate_RequiresNameAndLocation()
    {
        var vm = NewVm(out _);
        vm.ProjectName = "";
        Assert.That(vm.CanCreate, Is.False);
        vm.ProjectName = "Demo";
        Assert.That(vm.CanCreate, Is.True); // Location defaulted in ctor
        vm.Location = "";
        Assert.That(vm.CanCreate, Is.False);
    }

    [Test]
    public void GoNext_RaisesNextRequested_OnlyWhenAllowed()
    {
        var vm = NewVm(out _);
        int fired = 0;
        vm.NextRequested += (_, _) => fired++;

        vm.SelectedTemplate = null;
        vm.GoNextCommand.Execute(null);
        Assert.That(fired, Is.EqualTo(0));

        // Re-select a template directly. (Do NOT re-assign the already-selected
        // backend: CommunityToolkit's [ObservableProperty] setter guards on
        // equality, and Backends[0] is reference-equal to the current SelectedBackend,
        // so that assignment is a no-op and would not repopulate SelectedTemplate.)
        vm.SelectedTemplate = vm.VisibleTemplates.First();
        vm.GoNextCommand.Execute(null);
        Assert.That(fired, Is.EqualTo(1));
    }

    [Test]
    public async Task CreateProject_MapsState_ToOptions_AndFiresProjectCreated()
    {
        var vm = NewVm(out var svc);
        vm.SelectedLanguage = ProjectLanguage.BasicLang;
        vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");
        vm.ProjectName = "Demo";
        vm.Location = "X:/here";
        vm.TargetFramework = "net7.0";
        vm.CustomNamespace = "Acme.Demo";

        ProjectCreationResult? got = null;
        vm.ProjectCreated += (_, r) => got = r;

        await vm.CreateProjectCommand.ExecuteAsync(null);

        Assert.That(svc.LastOptions, Is.Not.Null);
        Assert.That(svc.LastOptions!.Name, Is.EqualTo("Demo"));
        Assert.That(svc.LastOptions.SolutionType.Id, Is.EqualTo("dotnet"));
        Assert.That(svc.LastOptions.TargetFramework, Is.EqualTo("net7.0"));
        Assert.That(svc.LastOptions.Namespace, Is.EqualTo("Acme.Demo"));
        Assert.That(got, Is.Not.Null.And.Property("Success").True);
    }

    [Test]
    public async Task CreateProject_Cpp_UsesCppSolutionType_AndIgnoresToolchain()
    {
        var vm = NewVm(out var svc);
        vm.SelectedLanguage = ProjectLanguage.Cpp;
        vm.SelectedBackend = vm.Backends.First(b => b.ToolchainId == "gcc"); // display-only
        vm.ProjectName = "NativeDemo";
        vm.Location = "X:/here";

        await vm.CreateProjectCommand.ExecuteAsync(null);

        Assert.That(svc.LastOptions!.SolutionType.Id, Is.EqualTo("cpp"));
        // CreateProjectOptions has no toolchain field — the gcc choice cannot leak.
    }

    [Test]
    public void NameWarning_SetForSpecialCharacters_NullWhenClean()
    {
        var vm = NewVm(out _);
        vm.ProjectName = "Clean";
        Assert.That(vm.NameWarning, Is.Null);
        vm.ProjectName = "Bad;Name";
        Assert.That(vm.NameWarning, Is.Not.Null);
    }

    [Test]
    public void ShowSelectors_TrackLanguageAndBackend()
    {
        var vm = NewVm(out _);
        vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");
        Assert.That(vm.ShowFrameworkSelector, Is.True);
        Assert.That(vm.ShowCppStandardSelector, Is.False);

        vm.SelectedLanguage = ProjectLanguage.Cpp;
        Assert.That(vm.ShowFrameworkSelector, Is.False);
        Assert.That(vm.ShowCppStandardSelector, Is.True);
    }

    [Test]
    public void GoBack_ClearsAnyPriorCreateError()
    {
        var vm = NewVm(out _);
        vm.HasError = true;
        vm.ErrorMessage = "boom";

        vm.GoBackCommand.Execute(null);

        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.ErrorMessage, Is.Null);
    }
}
