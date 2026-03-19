using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// Represents a single extension in the Extensions panel.
/// Used for both installed extensions and marketplace search results.
/// </summary>
public partial class ExtensionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _version = "";

    [ObservableProperty]
    private string _publisher = "";

    [ObservableProperty]
    private string? _iconUrl;

    [ObservableProperty]
    private int _installCount;

    [ObservableProperty]
    private double _rating;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private ObservableCollection<string> _categories = new();

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _detailMarkdown = "";

    /// <summary>
    /// The namespace (publisher.name) used to identify the extension in the registry.
    /// </summary>
    [ObservableProperty]
    private string _namespace = "";

    /// <summary>
    /// URL to download the VSIX package.
    /// </summary>
    [ObservableProperty]
    private string? _downloadUrl;

    /// <summary>
    /// Local install path (for installed extensions).
    /// </summary>
    [ObservableProperty]
    private string? _installPath;

    /// <summary>
    /// Formatted install count for display (e.g., "1.2M", "45K").
    /// </summary>
    public string FormattedInstallCount
    {
        get
        {
            if (InstallCount >= 1_000_000)
                return $"{InstallCount / 1_000_000.0:0.#}M";
            if (InstallCount >= 1_000)
                return $"{InstallCount / 1_000.0:0.#}K";
            return InstallCount.ToString();
        }
    }

    /// <summary>
    /// Star rating display string (e.g., "4.5").
    /// </summary>
    public string FormattedRating => Rating > 0 ? Rating.ToString("0.0") : "";

    /// <summary>
    /// Whether this extension has a rating to display.
    /// </summary>
    public bool HasRating => Rating > 0;
}
