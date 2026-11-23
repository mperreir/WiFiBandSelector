namespace WiFiBandSelector;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WiFiBandSelector");

    public static string LogFile => Path.Combine(DataDir, "log.txt");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    public static string NewReportFile() =>
        Path.Combine(DataDir, $"diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

    public static void Ensure() => Directory.CreateDirectory(DataDir);
}

/// <summary>Text log with simple rotation; never fails.</summary>
public static class Logger
{
    private const long MaxBytes = 200 * 1024;
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.Ensure();
                var info = new FileInfo(AppPaths.LogFile);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var previous = AppPaths.LogFile + ".1";
                    try
                    {
                        if (File.Exists(previous))
                            File.Delete(previous);
                        File.Move(AppPaths.LogFile, previous);
                    }
                    catch
                    {
                        // Rotation is a convenience: a failure must not prevent writing.
                    }
                }

                File.AppendAllText(
                    AppPaths.LogFile,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // No error path: logging must never bring down the application.
        }
    }

    public static IReadOnlyList<string> Tail(int count)
    {
        try
        {
            if (!File.Exists(AppPaths.LogFile))
                return Array.Empty<string>();

            var lines = File.ReadAllLines(AppPaths.LogFile);
            return lines.Length <= count ? lines : lines[^count..];
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
