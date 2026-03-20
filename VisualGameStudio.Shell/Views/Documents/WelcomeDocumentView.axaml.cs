using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Shell.Views.Documents;

public partial class WelcomeDocumentView : UserControl
{
    public WelcomeDocumentView()
    {
        InitializeComponent();

        // Resolve ViewModel from DI if not already set
        if (DataContext == null)
        {
            try
            {
                DataContext = App.Services?.GetService<WelcomeDocumentViewModel>();
            }
            catch
            {
                // Design-time or service not available
            }
        }
    }
}
