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
}
