using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class PullMembersUpDialogViewModel : ObservableObject
{
    private readonly IRefactoringService _refactoringService;
    private readonly string _filePath;
    private readonly int _line;
    private readonly int _column;
    private Action<bool>? _closeAction;

    [ObservableProperty]
    private string _sourceTypeName = "";

    [ObservableProperty]
    private string _sourceTypeDeclaration = "";

    [ObservableProperty]
    private PullDestinationViewModel? _selectedDestination;

    [ObservableProperty]
    private bool _makeAbstract;

    [ObservableProperty]
    private bool _keepImplementation = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _preview = "";

    public ObservableCollection<PullDestinationViewModel> Destinations { get; } = new();
    public ObservableCollection<PullMemberViewModel> Members { get; } = new();

    public bool CanPull => !IsLoading &&
                            string.IsNullOrEmpty(ErrorMessage) &&
                            SelectedDestination != null &&
                            Members.Any(m => m.IsSelected);

    public bool IsBaseClassSelected => SelectedDestination?.Kind == PullDestinationKind.BaseClass;

    public PullMembersUpDialogViewModel(
        IRefactoringService refactoringService,
        string filePath,
        int line,
        int column)
    {
        _refactoringService = refactoringService;
        _filePath = filePath;
        _line = line;
        _column = column;
    }

    public void SetCloseAction(Action<bool> closeAction)
    {
        _closeAction = closeAction;
    }

    partial void OnSelectedDestinationChanged(PullDestinationViewModel? value)
    {
        OnPropertyChanged(nameof(IsBaseClassSelected));
        OnPropertyChanged(nameof(CanPull));
        UpdatePreview();
    }

    partial void OnMakeAbstractChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnKeepImplementationChanged(bool value)
    {
        UpdatePreview();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var pullInfo = await _refactoringService.GetPullMembersUpInfoAsync(
                _filePath, _line, _column);

            if (pullInfo == null)
            {
                ErrorMessage = "Could not find class at cursor position or no base class/interface available.";
                return;
            }

            SourceTypeName = pullInfo.SourceTypeName;
            SourceTypeDeclaration = pullInfo.SourceTypeDeclaration;

            // Populate destinations
            Destinations.Clear();
            foreach (var dest in pullInfo.Destinations)
            {
                Destinations.Add(new PullDestinationViewModel(dest));
            }

            if (Destinations.Count > 0)
            {
                SelectedDestination = Destinations[0];
            }

            // Populate members
            Members.Clear();
            foreach (var member in pullInfo.Members)
            {
                var memberVm = new PullMemberViewModel(member);
                memberVm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(PullMemberViewModel.IsSelected))
                    {
                        OnPropertyChanged(nameof(CanPull));
                        UpdatePreview();
                    }
                };
                Members.Add(memberVm);
            }

            if (Members.Count == 0)
            {
                ErrorMessage = "No members found that can be pulled up.";
            }

            UpdatePreview();
            OnPropertyChanged(nameof(CanPull));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading class info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdatePreview()
    {
        var selectedMembers = Members.Where(m => m.IsSelected).ToList();
        if (SelectedDestination == null || selectedMembers.Count == 0)
        {
            Preview = "";
            return;
        }

        var memberNames = string.Join(", ", selectedMembers.Select(m => m.Name));
        var destKind = SelectedDestination.Kind == PullDestinationKind.BaseClass ? "base class" : "interface";

        var actionText = SelectedDestination.Kind == PullDestinationKind.Interface
            ? "Add signatures to"
            : (MakeAbstract ? "Add abstract declarations to" : "Move to");

        Preview = $"{actionText} {destKind} '{SelectedDestination.Name}': {memberNames}";
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var member in Members)
        {
            member.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var member in Members)
        {
            member.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task PullUpAsync()
    {
        if (!CanPull || SelectedDestination == null)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var selectedMembers = Members.Where(m => m.IsSelected).Select(m => m.Name).ToList();

            var options = new PullMembersUpOptions
            {
                DestinationName = SelectedDestination.Name,
                DestinationKind = SelectedDestination.Kind,
                MemberNames = selectedMembers,
                MakeAbstract = MakeAbstract,
                KeepImplementation = KeepImplementation
            };

            var result = await _refactoringService.PullMembersUpAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to pull members up.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error pulling members up: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _closeAction?.Invoke(false);
    }
}

public partial class PullDestinationViewModel : ObservableObject
{
    public string Name { get; }
    public PullDestinationKind Kind { get; }
    public string KindDisplay { get; }
    public bool IsInSameFile { get; }
    public string Declaration { get; }
    public string DisplayText { get; }

    public PullDestinationViewModel(PullMembersUpDestination destination)
    {
        Name = destination.Name;
        Kind = destination.Kind;
        KindDisplay = destination.Kind == PullDestinationKind.BaseClass ? "Base Class" : "Interface";
        IsInSameFile = destination.IsInSameFile;
        Declaration = destination.Declaration;
        DisplayText = $"{Name} ({KindDisplay}){(IsInSameFile ? "" : " - external")}";
    }
}

public partial class PullMemberViewModel : ObservableObject
{
    public string Name { get; }
    public PullableMemberKind Kind { get; }
    public string KindDisplay { get; }
    public string Signature { get; }
    public string Accessibility { get; }
    public bool IsShared { get; }
    public bool IsVirtual { get; }
    public bool IsOverride { get; }
    public string DisplayText { get; }

    [ObservableProperty]
    private bool _isSelected;

    public PullMemberViewModel(PullableMember member)
    {
        Name = member.Name;
        Kind = member.Kind;
        KindDisplay = member.Kind.ToString();
        Signature = member.Signature;
        Accessibility = member.Accessibility;
        IsShared = member.IsShared;
        IsVirtual = member.IsVirtual;
        IsOverride = member.IsOverride;

        var modifiers = new List<string>();
        if (IsShared) modifiers.Add("Shared");
        if (IsVirtual) modifiers.Add("Overridable");
        if (IsOverride) modifiers.Add("Overrides");

        var modifierText = modifiers.Count > 0 ? $" [{string.Join(", ", modifiers)}]" : "";
        DisplayText = $"{Accessibility} {Signature}{modifierText}";
    }
}
