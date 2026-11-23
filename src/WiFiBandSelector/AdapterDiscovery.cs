using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WiFiBandSelector;

/// <summary>
/// Découverte de la carte Wi-Fi et de ses paramètres avancés dans le registre.
/// </summary>
public static class AdapterDiscovery
{
    public const string ClassKeyPath =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    /// <summary>IF_TYPE_IEEE80211.</summary>
    private const int IfTypeIeee80211 = 71;

    private static readonly Regex InstanceNamePattern = new(@"^\d{4}$", RegexOptions.Compiled);

    /// <summary>Cartes physiques 802.11 trouvées, triées par nom.</summary>
    public static List<DriverSnapshot> Discover()
    {
        var snapshots = new List<DriverSnapshot>();

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var classKey = baseKey.OpenSubKey(ClassKeyPath);
            if (classKey is null)
            {
                Logger.Write($"Clé de classe introuvable : {ClassKeyPath}");
                return snapshots;
            }

            string[] names;
            try
            {
                names = classKey.GetSubKeyNames();
            }
            catch (Exception ex)
            {
                Logger.Write($"Énumération de la clé de classe impossible : {ex.Message}");
                return snapshots;
            }

            foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!InstanceNamePattern.IsMatch(name))
                    continue;

                var snapshot = TryLoadSnapshot(baseKey, name);
                if (snapshot is not null)
                    snapshots.Add(snapshot);
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"Découverte de la carte impossible : {ex}");
        }

        AttachInterfaces(snapshots);
        return snapshots.OrderBy(s => s.DriverDesc, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static DriverSnapshot? TryLoadSnapshot(RegistryKey baseKey, string instanceName)
    {
        var keyPath = $@"{ClassKeyPath}\{instanceName}";

        try
        {
            using var instanceKey = baseKey.OpenSubKey(keyPath);
            if (instanceKey is null)
                return null;

            var ifType = ReadInt(instanceKey.GetValue("*IfType"));
            if (ifType != IfTypeIeee80211)
                return null;

            var driverDesc = instanceKey.GetValue("DriverDesc")?.ToString()?.Trim() ?? string.Empty;
            if (LooksVirtual(driverDesc))
                return null;

            using var ndi = instanceKey.OpenSubKey("Ndi");
            using var paramsKey = ndi?.OpenSubKey("params");
            if (paramsKey is null)
                return null;

            var parameters = new List<DriverParam>();
            string[] keywords;
            try
            {
                keywords = paramsKey.GetSubKeyNames();
            }
            catch
            {
                return null;
            }

            foreach (var keyword in keywords)
            {
                var param = TryReadParam(instanceKey, paramsKey, keyword);
                if (param is not null)
                    parameters.Add(param);
            }

            if (parameters.Count == 0)
                return null;

            return new DriverSnapshot
            {
                KeyPath = keyPath,
                DriverDesc = driverDesc.Length == 0 ? instanceName : driverDesc,
                NetCfgInstanceId = instanceKey.GetValue("NetCfgInstanceId")?.ToString(),
                IfType = ifType,
                Params = parameters,
            };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Logger.Write($"Lecture refusée pour {keyPath} : {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Write($"Lecture impossible pour {keyPath} : {ex.Message}");
            return null;
        }
    }

    private static DriverParam? TryReadParam(RegistryKey instanceKey, RegistryKey paramsKey, string keyword)
    {
        try
        {
            using var paramKey = paramsKey.OpenSubKey(keyword);
            if (paramKey is null)
                return null;

            var typeRaw = paramKey.GetValue("type");
            using var enumKey = paramKey.OpenSubKey("enum");

            var isEnum = enumKey is not null
                         && (typeRaw is null || string.Equals(typeRaw.ToString(), "enum", StringComparison.OrdinalIgnoreCase));

            var choices = new List<SettingChoice>();
            if (isEnum && enumKey is not null)
            {
                foreach (var valueName in enumKey.GetValueNames())
                {
                    if (valueName.Length == 0)
                        continue;

                    var text = enumKey.GetValue(valueName)?.ToString();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    choices.Add(new SettingChoice(valueName, text!));
                }
            }

            choices.Sort(CompareChoices);

            return new DriverParam
            {
                Keyword = keyword,
                ParamDesc = paramKey.GetValue("ParamDesc")?.ToString(),
                DefaultValue = paramKey.GetValue("default")?.ToString(),
                IsEnum = isEnum,
                Choices = choices,
                CurrentValue = instanceKey.GetValue(keyword)?.ToString(),
            };
        }
        catch (Exception ex)
        {
            Logger.Write($"Paramètre {keyword} illisible : {ex.Message}");
            return null;
        }
    }

    private static int CompareChoices(SettingChoice a, SettingChoice b)
    {
        var na = int.TryParse(a.Code, out var va);
        var nb = int.TryParse(b.Code, out var vb);

        if (na && nb)
            return va.CompareTo(vb);
        if (na)
            return -1;
        if (nb)
            return 1;

        return string.Compare(a.Code, b.Code, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Écarte les adaptateurs virtuels qui partagent le type 71.</summary>
    private static bool LooksVirtual(string driverDesc)
    {
        var d = driverDesc.ToLowerInvariant();
        return d.Contains("virtual") || d.Contains("direct");
    }

    private static void AttachInterfaces(List<DriverSnapshot> snapshots)
    {
        NetworkInterface[] nics;
        try
        {
            nics = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception ex)
        {
            Logger.Write($"Énumération des interfaces impossible : {ex.Message}");
            return;
        }

        var byId = new Dictionary<string, NetworkInterface>(StringComparer.OrdinalIgnoreCase);
        foreach (var nic in nics)
            byId[NormalizeId(nic.Id)] = nic;

        foreach (var snapshot in snapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot.NetCfgInstanceId))
                continue;

            var id = NormalizeId(snapshot.NetCfgInstanceId);
            if (byId.TryGetValue(id, out var nic))
            {
                snapshot.InterfaceAlias = nic.Name;
                snapshot.InterfaceUp = nic.OperationalStatus == OperationalStatus.Up;
            }

            if (Guid.TryParse(snapshot.NetCfgInstanceId, out var guid))
                snapshot.InterfaceGuid = guid;
        }
    }

    private static string NormalizeId(string? id) =>
        (id ?? string.Empty).Trim().Trim('{', '}').ToLowerInvariant();

    private static int ReadInt(object? value) => value switch
    {
        int i => i,
        long l => (int)l,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => -1,
    };
}
