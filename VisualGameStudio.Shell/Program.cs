using Avalonia;

namespace VisualGameStudio.Shell;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Write a startup marker to verify file writing works
        var logFile = System.IO.Path.Combine(AppContext.BaseDirectory, "debug.log");
        try
        {
            System.IO.File.WriteAllText(logFile, $"[{DateTime.Now}] IDE Starting...\n");
        }
        catch { }

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                System.IO.File.AppendAllText(logFile,
                    $"[UNHANDLED] {ex?.GetType().FullName}: {ex?.Message}\n{ex?.StackTrace}\n");
            }
            catch { }
        };

        AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
        {
            // Log ALL exceptions to find what's happening
            try
            {
                var ex = e.Exception;
                // Only log serious exceptions, skip common noise
                if (ex is System.OperationCanceledException) return;
                if (ex is System.IO.FileNotFoundException) return;
                if (ex is System.IO.DirectoryNotFoundException) return;
                if (ex is System.ArgumentException && ex.StackTrace?.Contains("Avalonia") == true) return;

                System.IO.File.AppendAllText(logFile,
                    $"[FIRST_CHANCE] {ex.GetType().Name}: {ex.Message}\n  at: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}\n");
            }
            catch { }
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText(logFile,
                    $"[FATAL] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n");
            }
            catch { }
        }

        try
        {
            System.IO.File.AppendAllText(logFile, $"[{DateTime.Now}] IDE Exited.\n");
        }
        catch { }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
