namespace VisualGameStudio.Core.Abstractions.Services;

public interface ISettingsService
{
    T? GetValue<T>(string key, T? defaultValue = default);
    void SetValue<T>(string key, T value);
    Task SaveAsync();
    Task LoadAsync();

    event EventHandler<SettingChangedEventArgs>? SettingChanged;
}

public class SettingChangedEventArgs : EventArgs
{
    public string Key { get; }
    public object? OldValue { get; }
    public object? NewValue { get; }

    public SettingChangedEventArgs(string key, object? oldValue, object? newValue)
    {
        Key = key;
        OldValue = oldValue;
        NewValue = newValue;
    }
}

public static class SettingsKeys
{
    public const string Theme = "Appearance.Theme";
    public const string FontFamily = "Editor.FontFamily";
    public const string FontSize = "Editor.FontSize";
    public const string TabSize = "Editor.TabSize";
    public const string InsertSpaces = "Editor.InsertSpaces";
    public const string ShowLineNumbers = "Editor.ShowLineNumbers";
    public const string HighlightCurrentLine = "Editor.HighlightCurrentLine";
    public const string WordWrap = "Editor.WordWrap";
    public const string RecentProjects = "RecentProjects";
    public const string LastOpenedProject = "LastOpenedProject";
    public const string WindowState = "Window.State";
    public const string WindowBounds = "Window.Bounds";
}
