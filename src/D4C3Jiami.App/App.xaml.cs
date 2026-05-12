using Microsoft.UI.Xaml;
namespace D4C3Jiami.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        StartupLogger.Write("App constructor entered.");
        try
        {
            InitializeComponent();
            StartupLogger.Write("App resources initialized.");
        }
        catch (Exception ex)
        {
            StartupLogger.Write("App InitializeComponent failed: " + ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupLogger.Write("OnLaunched entered.");
        try
        {
            _window = new MainWindow();
            StartupLogger.Write("MainWindow constructed.");
            _window.Activate();
            StartupLogger.Write("MainWindow activated.");
        }
        catch (Exception ex)
        {
            StartupLogger.Write("OnLaunched failed: " + ex);
            throw;
        }
    }
}
