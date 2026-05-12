using System.Runtime.CompilerServices;

namespace D4C3Jiami.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupLogger.Write("Main entered.");
        try
        {
            StartWinUi();
            StartupLogger.Write("WinUI application returned.");
        }
        catch (Exception ex)
        {
            StartupLogger.Write(ex.ToString());
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartWinUi()
    {
        StartupLogger.Write("Initializing COM wrappers.");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        StartupLogger.Write("Starting WinUI application.");
        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
