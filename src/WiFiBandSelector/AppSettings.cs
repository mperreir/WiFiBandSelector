using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using WiFiBandSelector.Resources;

namespace WiFiBandSelector;

/// <summary>Per-user local application settings.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Adapter selected when several physical adapters are present.</summary>
    public string? AdapterKeyPath { get; set; }

    /// <summary>Auto, Soft or Restart: last apply strategy used.</summary>
    public string ApplyMode { get; set; } = "Auto";

    /// <summary>Backup of the last change, used for undo.</summary>
    public LastChange? LastChange { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
                return new AppSettings();

            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Logger.Write(Strings.LogSettingsUnreadable.FormatWith(ex.Message));
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            AppPaths.Ensure();
            var json = JsonSerializer.Serialize(this, JsonOptions);
            var temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Write(Strings.LogSettingsWriteFailed.FormatWith(ex.Message));
        }
    }
}

/// <summary>Values from before the last change, so it can be undone.</summary>
public sealed class LastChange
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string AdapterKeyPath { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Driver keyword to previous code.</summary>
    public Dictionary<string, string> Values { get; set; } = new();

    public string Describe() =>
        $"{Description} ({Timestamp.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)})";
}
