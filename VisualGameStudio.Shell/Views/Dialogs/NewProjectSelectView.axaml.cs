using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class NewProjectSelectView : Window
{
    public enum WizardOutcome { Created, Cancelled, Back }

    private readonly NewProjectWizardViewModel? _vm;
    private bool _configureOpen;
    private ProjectCreationResult? _result;

    // The Back button (New-Solution mode only) and an X-close both leave this at
    // its default, matching NewProjectConfigureView.WizardOutcome's convention:
    // Outcome/Result are set explicitly on every path that decides the flow.
    public WizardOutcome Outcome { get; private set; } = WizardOutcome.Cancelled;
    public ProjectCreationResult? Result { get; private set; }

    public NewProjectSelectView()
    {
        InitializeComponent();
    }

    public NewProjectSelectView(NewProjectWizardViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        vm.NextRequested += OnNextRequested;
        vm.Cancelled += OnCancelled;
    }

    private async void OnNextRequested(object? sender, EventArgs e)
    {
        if (_vm == null) return;
        try
        {
            _configureOpen = true;
            var configure = new NewProjectConfigureView(_vm);
            await configure.ShowDialog(this);
            _configureOpen = false;

            switch (configure.Outcome)
            {
                case NewProjectConfigureView.WizardOutcome.Created:
                    _result = configure.Result;
                    Outcome = WizardOutcome.Created;
                    Result = configure.Result;
                    Close(_result);
                    break;
                case NewProjectConfigureView.WizardOutcome.Cancelled:
                    _result = null;
                    Outcome = WizardOutcome.Cancelled;
                    Close(_result);
                    break;
                case NewProjectConfigureView.WizardOutcome.Back:
                    // stay on this window with state preserved
                    break;
            }
        }
        catch (Exception)
        {
            _configureOpen = false; // never leave the guard wedged if window 2 threw
        }
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        // While window 2 is open it owns cancel; ignore the echo here.
        if (_configureOpen) return;
        _result = null;
        Outcome = WizardOutcome.Cancelled;
        Close(_result);
    }

    // Shown only in New-Solution mode (IsNewSolutionMode) to let the host distinguish
    // "go back to the New Solution window" from a full Cancel of the whole flow.
    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        Outcome = WizardOutcome.Back;
        Close(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm != null)
        {
            _vm.NextRequested -= OnNextRequested;
            _vm.Cancelled -= OnCancelled;
        }
        base.OnClosed(e);
    }
}
