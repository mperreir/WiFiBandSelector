using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace WiFiBandSelector;

/// <summary>Rapport texte destiné au support : tout ce qu'il faut pour comprendre une carte distante.</summary>
public static class Diagnostics
{
    public static string BuildReport()
    {
        var sb = new StringBuilder();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        sb.AppendLine($"WiFiBandSelector {version} — rapport de diagnostic");
        sb.AppendLine($"Date : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Windows : {RuntimeInformation.OSDescription} (build {Environment.OSVersion.Version})");
        sb.AppendLine($"Architecture : {RuntimeInformation.ProcessArchitecture} / OS {RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"Droits administrateur : {(Interop.IsElevated ? "oui" : "non")}");
        sb.AppendLine($"Réglages : {AppPaths.SettingsFile}");
        sb.AppendLine();

        List<DriverSnapshot> snapshots;
        try
        {
            snapshots = AdapterDiscovery.Discover();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Découverte impossible : {ex}");
            return sb.ToString();
        }

        if (snapshots.Count == 0)
        {
            sb.AppendLine("Aucune carte Wi-Fi physique (IfType 71) trouvée dans le registre.");
            sb.AppendLine();
        }

        foreach (var snapshot in snapshots)
        {
            AppendAdapter(sb, snapshot);
            sb.AppendLine();
        }

        sb.AppendLine("Paramètres avancés de toutes les cartes 802.11 (y compris virtuelles) :");
        AppendAllInterfaces(sb);
        sb.AppendLine();

        sb.AppendLine("Dernières lignes du journal :");
        foreach (var line in Logger.Tail(60))
            sb.AppendLine($"  {line}");

        return sb.ToString();
    }

    private static void AppendAdapter(StringBuilder sb, DriverSnapshot snapshot)
    {
        sb.AppendLine(new string('=', 70));
        sb.AppendLine($"Carte : {snapshot.DriverDesc}");
        sb.AppendLine($"  Alias : {snapshot.InterfaceAlias ?? "(inconnu)"}");
        sb.AppendLine($"  GUID : {snapshot.NetCfgInstanceId ?? "(inconnu)"}");
        sb.AppendLine($"  Clé : HKLM\\{snapshot.KeyPath}");
        sb.AppendLine($"  IfType : {snapshot.IfType}");

        var status = snapshot.InterfaceGuid is Guid guid ? WlanApi.QueryStatus(guid) : WlanStatus.Disconnected;
        sb.AppendLine(status.Connected
            ? $"  Connexion : {status.Ssid} (profil « {status.ProfileName} », BSSID {status.Bssid})"
            : "  Connexion : non connecté");
        sb.AppendLine(status.Connected
            ? $"  Bande réelle : {BandInfo.Describe(status.Band ?? Band.Band24)} ({status.FrequencyMhz} MHz)"
            : "  Bande réelle : inconnue");

        AdapterCapabilities caps;
        try
        {
            caps = SettingClassifier.Classify(snapshot.Params);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Classement impossible : {ex.Message}");
            return;
        }

        sb.AppendLine($"  Support : {(caps.IsSupported ? "oui" : "non")}");
        sb.AppendLine($"  Préférence : {Describe(caps.Preference)}");
        sb.AppendLine($"  Verrou : {Describe(caps.Lock)}");
        sb.AppendLine($"  Itinérance : {Describe(caps.Roaming)}");
        sb.AppendLine();

        sb.AppendLine("  Paramètres enum du pilote :");
        foreach (var param in snapshot.Params.OrderBy(p => p.Keyword, StringComparer.OrdinalIgnoreCase))
        {
            if (!param.IsEnum || param.Choices.Count == 0)
                continue;

            sb.AppendLine($"    {param.Keyword} — « {param.DisplayName} »");
            sb.AppendLine($"        courant = {param.CurrentValue ?? "(absent)"} ; défaut = {param.DefaultValue ?? "(absent)"}");
            foreach (var choice in param.Choices)
            {
                var marker = string.Equals(choice.Code, param.CurrentValue, StringComparison.OrdinalIgnoreCase) ? " ←" : string.Empty;
                sb.AppendLine($"        {choice.Code} = {choice.Label}{marker}");
            }
        }

        if (caps.Notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  Notes de classement :");
            foreach (var note in caps.Notes)
                sb.AppendLine($"    - {note}");
        }
    }

    private static void AppendAllInterfaces(StringBuilder sb)
    {
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var classKey = baseKey.OpenSubKey(AdapterDiscovery.ClassKeyPath);
            if (classKey is null)
            {
                sb.AppendLine("  Clé de classe inaccessible.");
                return;
            }

            foreach (var name in classKey.GetSubKeyNames().Where(n => n.Length == 4 && char.IsDigit(n[0])))
            {
                using var instance = classKey.OpenSubKey(name);
                if (instance is null)
                    continue;

                var ifType = instance.GetValue("*IfType");
                if (ifType?.ToString() != "71")
                    continue;

                var desc = instance.GetValue("DriverDesc")?.ToString() ?? name;
                sb.AppendLine($"  [{name}] {desc}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Lecture impossible : {ex.Message}");
        }
    }

    private static string Describe(DriverSetting? setting)
    {
        if (setting is null)
            return "non exposée";

        var current = setting.Current;
        return $"{setting.Keyword} — « {setting.Title} » = {current?.DisplayLabel ?? setting.CurrentCode} " +
               $"(défaut {setting.DefaultCode ?? "?"})";
    }
}
