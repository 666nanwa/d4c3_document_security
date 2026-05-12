namespace D4C3Jiami.App;

internal static class StartupLogger
{
    public static void Write(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "D4C3Jiami");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "startup.log");
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Startup logging must never prevent the app window from opening.
        }
    }
}
