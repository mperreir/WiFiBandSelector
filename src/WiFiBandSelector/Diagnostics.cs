using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using WiFiBandSelector.Resources;

namespace WiFiBandSelector;

/// <summary>Text report for support: everything needed to understand a remote adapter.</summary>
public static class Diagnostics
{
    public static string BuildReport()
    {
        var sb = new StringBuilder();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

        sb.AppendLine(Strings.ReportTitle.FormatWith(version));
        sb.AppendLine(Strings.ReportDate.FormatWith(now));
        sb.AppendLine(Strings.ReportWindows.FormatWith(RuntimeInformation.OSDescription, Environment.OSVersion.Version));
        sb.AppendLine(Strings.ReportArchitecture.FormatWith(RuntimeInformation.ProcessArchitecture, RuntimeInformation.OSArchitecture));
        sb.AppendLine(Strings.ReportElevated.FormatWith(Interop.IsElevated ? Strings.Yes : Strings.No));
        sb.AppendLine(Strings.ReportSettings.FormatWith(AppPaths.SettingsFile));
        sb.AppendLine();

        List<DriverSnapshot> snapshots;
        try
        {
            snapshots = AdapterDiscovery.Discover();
        }
        catch (Exception ex)
        {
            sb.AppendLine(Strings.ReportDiscoveryFailed.FormatWith(ex));
            return sb.ToString();
        }

        if (snapshots.Count == 0)
        {
            sb.AppendLine(Strings.ReportNoAdapter);
            sb.AppendLine();
        }

        foreach (var snapshot in snapshots)
        {
            AppendAdapter(sb, snapshot);
            sb.AppendLine();
        }

        sb.AppendLine(Strings.ReportAllInterfaces);
        AppendAllInterfaces(sb);
        sb.AppendLine();

        sb.AppendLine(Strings.ReportLogTail);
        foreach (var line in Logger.Tail(60))
            sb.AppendLine($"  {line}");

        return sb.ToString();
    }

    private static void AppendAdapter(StringBuilder sb, DriverSnapshot snapshot)
    {
        sb.AppendLine(new string('=', 70));
        sb.AppendLine(Strings.ReportAdapter.FormatWith(snapshot.DriverDesc));
        sb.AppendLine(Strings.ReportAlias.FormatWith(snapshot.InterfaceAlias ?? Strings.Unknown));
        sb.AppendLine(Strings.ReportGuid.FormatWith(snapshot.NetCfgInstanceId ?? Strings.Unknown));
        sb.AppendLine(Strings.ReportKey.FormatWith(snapshot.KeyPath));
        sb.AppendLine(Strings.ReportIfType.FormatWith(snapshot.IfType));

        var status = snapshot.InterfaceGuid is Guid guid ? WlanApi.QueryStatus(guid) : WlanStatus.Disconnected;
        sb.AppendLine(status.Connected
            ? Strings.ReportConnection.FormatWith(status.Ssid ?? string.Empty, status.ProfileName ?? string.Empty, status.Bssid ?? string.Empty)
            : Strings.ReportNotConnected);
        sb.AppendLine(status.Connected
            ? Strings.ReportRealBand.FormatWith(BandInfo.Describe(status.Band ?? Band.Band24), status.FrequencyMhz)
            : Strings.ReportRealBandUnknown);

        AdapterCapabilities caps;
        try
        {
            caps = SettingClassifier.Classify(snapshot.Params);
        }
        catch (Exception ex)
        {
            sb.AppendLine(Strings.ReportClassifyFailed.FormatWith(ex.Message));
            return;
        }

        sb.AppendLine(Strings.ReportSupport.FormatWith(caps.IsSupported ? Strings.Yes : Strings.No));
        sb.AppendLine(Strings.ReportPreference.FormatWith(Describe(caps.Preference)));
        sb.AppendLine(Strings.ReportLock.FormatWith(Describe(caps.Lock)));
        sb.AppendLine(Strings.ReportRoaming.FormatWith(Describe(caps.Roaming)));
        sb.AppendLine(Strings.ReportPowerSaving.FormatWith(Describe(caps.PowerSaving)));
        sb.AppendLine();

        sb.AppendLine(Strings.ReportEnumParams);
        foreach (var param in snapshot.Params.OrderBy(p => p.Keyword, StringComparer.OrdinalIgnoreCase))
        {
            if (!param.IsEnum || param.Choices.Count == 0)
                continue;

            sb.AppendLine($"    {param.Keyword} — « {param.DisplayName} »");
            sb.AppendLine(Strings.ReportCurrentDefault.FormatWith(param.CurrentValue ?? Strings.Absent, param.DefaultValue ?? Strings.Absent));
            foreach (var choice in param.Choices)
            {
                var marker = string.Equals(choice.Code, param.CurrentValue, StringComparison.OrdinalIgnoreCase) ? " ←" : string.Empty;
                sb.AppendLine($"        {choice.Code} = {choice.Label}{marker}");
            }
        }

        if (caps.Notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Strings.ReportNotes);
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
                sb.AppendLine(Strings.ReportClassKeyInaccessible);
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
            sb.AppendLine(Strings.ReportReadFailed.FormatWith(ex.Message));
        }
    }

    private static string Describe(DriverSetting? setting)
    {
        if (setting is null)
            return Strings.ReportNotExposed;

        var current = setting.Current;
        return Strings.ReportSettingDesc.FormatWith(
            setting.Keyword, setting.Title, current?.DisplayLabel ?? setting.CurrentCode, setting.DefaultCode ?? "?");
    }
}
