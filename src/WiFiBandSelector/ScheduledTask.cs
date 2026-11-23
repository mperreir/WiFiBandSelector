using System.Xml.Linq;
using WiFiBandSelector.Resources;

namespace WiFiBandSelector;

/// <summary>State of the "WiFiBandSelector" scheduled task (start with Windows).</summary>
public static class ScheduledTask
{
    public const string TaskName = "WiFiBandSelector";

    private static readonly object Gate = new();
    private static DateTime _lastCheck = DateTime.MinValue;
    private static bool? _lastState;

    /// <summary>True if the task exists and is enabled; null if missing or unreadable.</summary>
    public static bool? IsEnabled()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _lastCheck < TimeSpan.FromSeconds(10))
                return _lastState;

            _lastState = Query();
            _lastCheck = DateTime.UtcNow;
            return _lastState;
        }
    }

    private static bool? Query()
    {
        var (code, output) = Interop.RunCapture("schtasks", $"/Query /TN \"{TaskName}\" /XML", 15000);
        if (code != 0 || string.IsNullOrWhiteSpace(output))
            return null;

        try
        {
            var document = XDocument.Parse(output);
            var root = document.Root;
            if (root is null)
                return null;

            var ns = root.GetDefaultNamespace();
            var enabled = root.Element(ns + "Settings")?.Element(ns + "Enabled")?.Value;
            if (enabled is null)
                return true; // A missing <Enabled> element means enabled by default.

            return string.Equals(enabled.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.Write(Strings.LogTaskReadFailed.FormatWith(ex.Message));
            return null;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        var (code, _) = Interop.RunCapture(
            "schtasks",
            $"/Change /TN \"{TaskName}\" /{(enabled ? "ENABLE" : "DISABLE")}",
            20000);

        lock (Gate)
        {
            _lastCheck = DateTime.MinValue;
            _lastState = null;
        }

        return code == 0;
    }

    /// <summary>Checks that the installed executable matches the one currently running.</summary>
    public static bool Exists() => Query() is not null;

    internal static string InstalledPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WiFiBandSelector", "WiFiBandSelector.exe");

    internal static bool IsRunningFromInstallDirectory()
    {
        try
        {
            var current = Environment.ProcessPath;
            return current is not null
                   && string.Equals(
                       Path.GetFullPath(current),
                       Path.GetFullPath(InstalledPath),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
