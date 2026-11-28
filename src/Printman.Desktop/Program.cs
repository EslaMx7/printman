using Avalonia;

namespace Printman.Desktop;

public static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        bool createdNew = true;
        try
        {
            var mutexName = OperatingSystem.IsWindows()
                ? @"Local\Printman.Desktop.SingleInstance"
                : "Printman.Desktop.SingleInstance";
            _singleInstanceMutex = new Mutex(
                initiallyOwned: false,
                name: mutexName,
                out createdNew);
        }
        catch
        {
            createdNew = true;
        }

        if (!createdNew)
        {
            return;
        }

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try
            {
                _singleInstanceMutex?.Dispose();
            }
            catch
            {
                // Ignore mutex disposal errors on exit
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
