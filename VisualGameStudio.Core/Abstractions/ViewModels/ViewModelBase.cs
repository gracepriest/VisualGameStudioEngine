using CommunityToolkit.Mvvm.ComponentModel;

namespace VisualGameStudio.Core.Abstractions.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string? _busyMessage;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string? BusyMessage
    {
        get => _busyMessage;
        set => SetProperty(ref _busyMessage, value);
    }

    protected void SetBusy(bool busy, string? message = null)
    {
        IsBusy = busy;
        BusyMessage = message;
    }
}
