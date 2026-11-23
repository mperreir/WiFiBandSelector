using System.Text.Encodings.Web;
using System.Text.Json;

namespace WiFiBandSelector;

/// <summary>Réglages locaux de l'application (par utilisateur).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Carte choisie quand plusieurs cartes physiques existent.</summary>
    public string? AdapterKeyPath { get; set; }

    /// <summary>Auto, Soft ou Restart : trace de la dernière stratégie d'application.</summary>
    public string ApplyMode { get; set; } = "Auto";

    /// <summary>Sauvegarde de la dernière modification, pour l'annulation.</summary>
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
            Logger.Write($"Réglages illisibles, valeurs par défaut : {ex.Message}");
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
            Logger.Write($"Écriture des réglages impossible : {ex.Message}");
        }
    }
}

/// <summary>Valeurs d'avant la dernière modification, pour pouvoir revenir en arrière.</summary>
public sealed class LastChange
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string AdapterKeyPath { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Mot-clé du pilote → ancien code.</summary>
    public Dictionary<string, string> Values { get; set; } = new();

    public string Describe() =>
        $"{Description} ({Timestamp.LocalDateTime:dd/MM/yyyy HH:mm})";
}
