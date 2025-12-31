using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.Services;

public class DialogService : IDialogService
{
    private Window? GetMainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }

    public async Task<string?> ShowOpenFileDialogAsync(FileDialogOptions options)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var storageProvider = window.StorageProvider;
        var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = options.Title,
            AllowMultiple = false,
            FileTypeFilter = options.Filters.Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Select(e => e.StartsWith("*") ? e : $"*.{e}").ToList()
            }).ToList(),
            SuggestedStartLocation = options.InitialDirectory != null
                ? await storageProvider.TryGetFolderFromPathAsync(options.InitialDirectory)
                : null
        });

        return result.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string[]?> ShowOpenFilesDialogAsync(FileDialogOptions options)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var storageProvider = window.StorageProvider;
        var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = options.Title,
            AllowMultiple = true,
            FileTypeFilter = options.Filters.Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Select(e => e.StartsWith("*") ? e : $"*.{e}").ToList()
            }).ToList(),
            SuggestedStartLocation = options.InitialDirectory != null
                ? await storageProvider.TryGetFolderFromPathAsync(options.InitialDirectory)
                : null
        });

        return result.Select(r => r.Path.LocalPath).ToArray();
    }

    public async Task<string?> ShowSaveFileDialogAsync(FileDialogOptions options)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var storageProvider = window.StorageProvider;
        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = options.Title,
            SuggestedFileName = options.InitialFileName,
            FileTypeChoices = options.Filters.Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Select(e => e.StartsWith("*") ? e : $"*.{e}").ToList()
            }).ToList(),
            SuggestedStartLocation = options.InitialDirectory != null
                ? await storageProvider.TryGetFolderFromPathAsync(options.InitialDirectory)
                : null
        });

        return result?.Path.LocalPath;
    }

    public async Task<string?> ShowFolderDialogAsync(FolderDialogOptions options)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var storageProvider = window.StorageProvider;
        var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = options.Title,
            AllowMultiple = false,
            SuggestedStartLocation = options.InitialDirectory != null
                ? await storageProvider.TryGetFolderFromPathAsync(options.InitialDirectory)
                : null
        });

        return result.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<DialogResult> ShowMessageAsync(string title, string message, DialogButtons buttons = DialogButtons.Ok, DialogIcon icon = DialogIcon.Information)
    {
        var window = GetMainWindow();
        if (window == null) return DialogResult.None;

        // For now, use a simple message box approach
        // In a full implementation, you'd create a custom dialog window
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };

        var result = DialogResult.None;

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 15
        };

        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10
        };

        void AddButton(string text, DialogResult dialogResult)
        {
            var btn = new Button { Content = text, MinWidth = 75 };
            btn.Click += (s, e) =>
            {
                result = dialogResult;
                dialog.Close();
            };
            buttonPanel.Children.Add(btn);
        }

        switch (buttons)
        {
            case DialogButtons.Ok:
                AddButton("OK", DialogResult.Ok);
                break;
            case DialogButtons.OkCancel:
                AddButton("OK", DialogResult.Ok);
                AddButton("Cancel", DialogResult.Cancel);
                break;
            case DialogButtons.YesNo:
                AddButton("Yes", DialogResult.Yes);
                AddButton("No", DialogResult.No);
                break;
            case DialogButtons.YesNoCancel:
                AddButton("Yes", DialogResult.Yes);
                AddButton("No", DialogResult.No);
                AddButton("Cancel", DialogResult.Cancel);
                break;
        }

        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        await dialog.ShowDialog(window);
        return result;
    }

    public async Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue = "")
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };

        string? result = null;
        var textBox = new TextBox { Text = defaultValue };

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 10
        };

        panel.Children.Add(new TextBlock { Text = prompt });
        panel.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10
        };

        var okButton = new Button { Content = "OK", MinWidth = 75 };
        okButton.Click += (s, e) =>
        {
            result = textBox.Text;
            dialog.Close();
        };

        var cancelButton = new Button { Content = "Cancel", MinWidth = 75 };
        cancelButton.Click += (s, e) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        await dialog.ShowDialog(window);
        return result;
    }

    public async Task<string?> ShowFunctionBreakpointDialogAsync()
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var viewModel = new ViewModels.Dialogs.FunctionBreakpointDialogViewModel();
        var dialog = new Views.Dialogs.FunctionBreakpointDialog(viewModel);

        var result = await dialog.ShowDialog<string?>(window);
        return result;
    }

    public async Task<BreakpointConditionResult?> ShowBreakpointConditionDialogAsync(string location, string? condition, string? hitCount, string? logMessage)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var viewModel = new ViewModels.Dialogs.BreakpointConditionDialogViewModel(location, condition, hitCount, logMessage);
        var dialog = new Views.Dialogs.BreakpointConditionDialog(viewModel);

        var result = await dialog.ShowDialog<ViewModels.Dialogs.BreakpointConditionDialogViewModel?>(window);

        if (result != null && result.DialogResult)
        {
            return new BreakpointConditionResult
            {
                Condition = result.ResultCondition,
                HitCount = result.ResultHitCount,
                LogMessage = result.ResultLogMessage,
                DialogResult = true
            };
        }

        return null;
    }

    public async Task<List<ExceptionSettingResult>?> ShowExceptionSettingsDialogAsync(IEnumerable<ExceptionSettingResult>? currentSettings = null)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        // Convert to internal ExceptionSetting type
        var internalSettings = currentSettings?.Select(s => new ViewModels.Dialogs.ExceptionSetting
        {
            ExceptionType = s.ExceptionType,
            BreakWhenThrown = s.BreakWhenThrown,
            BreakWhenUserUnhandled = s.BreakWhenUserUnhandled
        });

        var viewModel = new ViewModels.Dialogs.ExceptionSettingsViewModel(internalSettings);
        var dialog = new Views.Dialogs.ExceptionSettingsDialog(viewModel);

        var result = await dialog.ShowDialog<List<ViewModels.Dialogs.ExceptionSetting>?>(window);

        if (result != null)
        {
            return result.Select(s => new ExceptionSettingResult
            {
                ExceptionType = s.ExceptionType,
                BreakWhenThrown = s.BreakWhenThrown,
                BreakWhenUserUnhandled = s.BreakWhenUserUnhandled
            }).ToList();
        }

        return null;
    }

    public async Task<int?> ShowGoToLineDialogAsync(int currentLine, int totalLines)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var viewModel = new ViewModels.Dialogs.GoToLineDialogViewModel(currentLine, totalLines);
        var dialog = new Views.Dialogs.GoToLineDialog(viewModel);

        var result = await dialog.ShowDialog<int?>(window);
        return result;
    }

    public async Task<GoToSymbolResult?> ShowGoToSymbolDialogAsync(string sourceCode, string? filePath = null)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var viewModel = new ViewModels.Dialogs.GoToSymbolDialogViewModel(sourceCode, filePath);
        var dialog = new Views.Dialogs.GoToSymbolDialog(viewModel);

        var result = await dialog.ShowDialog<ViewModels.Dialogs.SymbolItem?>(window);

        if (result != null)
        {
            return new GoToSymbolResult
            {
                Name = result.Name,
                Line = result.Line,
                Column = result.Column,
                FilePath = result.FilePath
            };
        }

        return null;
    }

    public async Task<int> ShowListSelectionAsync(string title, string prompt, IEnumerable<string> items)
    {
        var window = GetMainWindow();
        if (window == null) return -1;

        var itemList = items.ToList();

        var viewModel = new ViewModels.Dialogs.ListSelectionDialogViewModel(title, prompt, itemList);
        var dialog = new Views.Dialogs.ListSelectionDialog(viewModel);

        var result = await dialog.ShowDialog<int?>(window);
        return result ?? -1;
    }
}
