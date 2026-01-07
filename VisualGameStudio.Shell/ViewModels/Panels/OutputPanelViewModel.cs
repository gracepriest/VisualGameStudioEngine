using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class OutputPanelViewModel : ViewModelBase
{
    private readonly IOutputService _outputService;
    private readonly IDebugService _debugService;
    private readonly StringBuilder _outputBuffer = new();

    [ObservableProperty]
    private string _outputText = "";

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private OutputCategory _selectedCategory = OutputCategory.Build;

    [ObservableProperty]
    private bool _isInputEnabled = false;

    public OutputPanelViewModel(IOutputService outputService, IDebugService debugService)
    {
        _outputService = outputService;
        _debugService = debugService;
        _outputService.OutputReceived += OnOutputReceived;
        _debugService.StateChanged += OnDebugStateChanged;
    }

    private void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsInputEnabled = e.NewState == DebugState.Running;
            if (e.NewState == DebugState.Stopped)
            {
                InputText = "";
            }
        });
    }

    private void OnOutputReceived(object? sender, OutputEventArgs e)
    {
        if (e.Category == SelectedCategory)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _outputBuffer.Append(e.Message);
                OutputText = _outputBuffer.ToString();
            });
        }
    }

    public void AppendOutput(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _outputBuffer.Append(text);
            OutputText = _outputBuffer.ToString();
        });
    }

    partial void OnSelectedCategoryChanged(OutputCategory value)
    {
        RefreshOutput();
    }

    private void RefreshOutput()
    {
        _outputBuffer.Clear();
        var messages = _outputService.GetMessages(SelectedCategory);
        foreach (var message in messages)
        {
            _outputBuffer.Append(message);
        }
        OutputText = _outputBuffer.ToString();
    }

    [RelayCommand]
    private void Clear()
    {
        _outputService.Clear(SelectedCategory);
        _outputBuffer.Clear();
        OutputText = "";
    }

    [RelayCommand]
    private void ShowBuildOutput()
    {
        SelectedCategory = OutputCategory.Build;
    }

    [RelayCommand]
    private void ShowGeneralOutput()
    {
        SelectedCategory = OutputCategory.General;
    }

    [RelayCommand]
    private async Task SendInput()
    {
        if (!string.IsNullOrEmpty(InputText) && IsInputEnabled)
        {
            var input = InputText;
            InputText = "";

            // Echo the input to the output
            AppendOutput($"> {input}\n");

            // Send to the running process
            await _debugService.SendInputAsync(input);
        }
    }
}
