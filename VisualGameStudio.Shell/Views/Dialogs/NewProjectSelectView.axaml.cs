using System;
using Avalonia.Controls;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class NewProjectSelectView : Window
{
    private readonly NewProjectWizardViewModel? _vm;
    private bool _configureOpen;
    private ProjectCreationResult? _result;

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

        _configureOpen = true;
        var configure = new NewProjectConfigureView(_vm);
        await configure.ShowDialog(this);
        _configureOpen = false;

        switch (configure.Outcome)
        {
            case NewProjectConfigureView.WizardOutcome.Created:
                _result = configure.Result;
                Close(_result);
                break;
            case NewProjectConfigureView.WizardOutcome.Cancelled:
                _result = null;
                Close(_result);
                break;
            case NewProjectConfigureView.WizardOutcome.Back:
                // stay on this window with state preserved
                break;
        }
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        // While window 2 is open it owns cancel; ignore the echo here.
        if (_configureOpen) return;
        _result = null;
        Close(_result);
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
