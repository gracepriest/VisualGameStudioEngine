using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class PushMembersDownDialogViewModel : ObservableObject
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
    private bool _removeFromBase = true;

    [ObservableProperty]
    private bool _makeAbstractInBase;

    [ObservableProperty]
    private bool _markAsOverrides;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _preview = "";

    public ObservableCollection<PushDestinationViewModel> Destinations { get; } = new();
    public ObservableCollection<PushMemberViewModel> Members { get; } = new();

    public bool CanPush => !IsLoading &&
                            string.IsNullOrEmpty(ErrorMessage) &&
                            Destinations.Any(d => d.IsSelected) &&
                            Members.Any(m => m.IsSelected);

    public PushMembersDownDialogViewModel(
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

    partial void OnRemoveFromBaseChanged(bool value)
    {
        if (value)
        {
            MakeAbstractInBase = false;
        }
        UpdatePreview();
    }

    partial void OnMakeAbstractInBaseChanged(bool value)
    {
        if (value)
        {
            RemoveFromBase = false;
            MarkAsOverrides = true;
        }
        UpdatePreview();
    }

    partial void OnMarkAsOverridesChanged(bool value)
    {
        UpdatePreview();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var pushInfo = await _refactoringService.GetPushMembersDownInfoAsync(
                _filePath, _line, _column);

            if (pushInfo == null)
            {
                ErrorMessage = "Could not find class at cursor position.";
                return;
            }

            SourceTypeName = pushInfo.SourceTypeName;
            SourceTypeDeclaration = pushInfo.SourceTypeDeclaration;

            // Populate destinations (derived classes)
            Destinations.Clear();
            foreach (var dest in pushInfo.Destinations)
            {
                var destVm = new PushDestinationViewModel(dest) { IsSelected = true };
                destVm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(PushDestinationViewModel.IsSelected))
                    {
                        OnPropertyChanged(nameof(CanPush));
                        UpdatePreview();
                    }
                };
                Destinations.Add(destVm);
            }

            if (Destinations.Count == 0)
            {
                ErrorMessage = "No derived classes found to push members to.";
                return;
            }

            // Populate members
            Members.Clear();
            foreach (var member in pushInfo.Members)
            {
                var memberVm = new PushMemberViewModel(member);
                memberVm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(PushMemberViewModel.IsSelected))
                    {
                        OnPropertyChanged(nameof(CanPush));
                        UpdatePreview();
                    }
                };
                Members.Add(memberVm);
            }

            if (Members.Count == 0)
            {
                ErrorMessage = "No members found that can be pushed down.";
            }

            UpdatePreview();
            OnPropertyChanged(nameof(CanPush));
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
        var selectedDestinations = Destinations.Where(d => d.IsSelected).ToList();
        var selectedMembers = Members.Where(m => m.IsSelected).ToList();

        if (selectedDestinations.Count == 0 || selectedMembers.Count == 0)
        {
            Preview = "";
            return;
        }

        var memberNames = string.Join(", ", selectedMembers.Select(m => m.Name));
        var destNames = selectedDestinations.Count == Destinations.Count
            ? "all derived classes"
            : string.Join(", ", selectedDestinations.Select(d => d.Name));

        var baseAction = MakeAbstractInBase
            ? " (make abstract in base)"
            : (RemoveFromBase ? " (remove from base)" : "");

        Preview = $"Push {memberNames} to {destNames}{baseAction}";
    }

    [RelayCommand]
    private void SelectAllMembers()
    {
        foreach (var member in Members)
        {
            member.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNoneMembers()
    {
        foreach (var member in Members)
        {
            member.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectAllDestinations()
    {
        foreach (var dest in Destinations)
        {
            dest.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNoneDestinations()
    {
        foreach (var dest in Destinations)
        {
            dest.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task PushDownAsync()
    {
        if (!CanPush)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var selectedMembers = Members.Where(m => m.IsSelected).Select(m => m.Name).ToList();
            var selectedDestinations = Destinations.Where(d => d.IsSelected).Select(d => d.Name).ToList();

            var options = new PushMembersDownOptions
            {
                MemberNames = selectedMembers,
                DestinationNames = selectedDestinations,
                RemoveFromBase = RemoveFromBase,
                MakeAbstractInBase = MakeAbstractInBase,
                MarkAsOverrides = MarkAsOverrides
            };

            var result = await _refactoringService.PushMembersDownAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to push members down.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error pushing members down: {ex.Message}";
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

public partial class PushDestinationViewModel : ObservableObject
{
    public string Name { get; }
    public string FilePath { get; }
    public bool IsInSameFile { get; }
    public string Declaration { get; }
    public string DisplayText { get; }
    public List<string> ExistingOverrides { get; }

    [ObservableProperty]
    private bool _isSelected;

    public PushDestinationViewModel(PushMembersDownDestination destination)
    {
        Name = destination.Name;
        FilePath = destination.FilePath;
        IsInSameFile = destination.IsInSameFile;
        Declaration = destination.Declaration;
        ExistingOverrides = destination.ExistingOverrides;
        DisplayText = $"{Name}{(IsInSameFile ? "" : " - external")}";
    }
}

public partial class PushMemberViewModel : ObservableObject
{
    public string Name { get; }
    public PushableMemberKind Kind { get; }
    public string KindDisplay { get; }
    public string Signature { get; }
    public string Accessibility { get; }
    public bool IsOverridable { get; }
    public bool IsShared { get; }
    public string DisplayText { get; }

    [ObservableProperty]
    private bool _isSelected;

    public PushMemberViewModel(PushableMember member)
    {
        Name = member.Name;
        Kind = member.Kind;
        KindDisplay = member.Kind.ToString();
        Signature = member.Signature;
        Accessibility = member.Accessibility;
        IsOverridable = member.IsOverridable;
        IsShared = member.IsShared;

        var modifiers = new List<string>();
        if (IsOverridable) modifiers.Add("Overridable");
        if (IsShared) modifiers.Add("Shared");

        var modifierText = modifiers.Count > 0 ? $" [{string.Join(", ", modifiers)}]" : "";
        DisplayText = $"{Accessibility} {Signature}{modifierText}";
    }
}
