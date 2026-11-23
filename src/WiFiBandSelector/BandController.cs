using Microsoft.Win32;
using WiFiBandSelector.Resources;

namespace WiFiBandSelector;

public enum ApplyStatus
{
    /// <summary>Written and observed (or no observable effect expected).</summary>
    Applied,

    /// <summary>Written, but the adapter does not follow the requested preference.</summary>
    AppliedNotObserved,

    /// <summary>Written then automatically reverted because no connection came back.</summary>
    Reverted,

    /// <summary>Refused before any write.</summary>
    Refused,

    /// <summary>Technical error.</summary>
    Failed,
}

public sealed record ApplyOutcome(ApplyStatus Status, string Message)
{
    public bool Ok => Status is ApplyStatus.Applied or ApplyStatus.AppliedNotObserved;
}

/// <summary>
/// Actually reads and writes settings: backup, verified write, apply (soft reconnect
/// then adapter restart) and rollback.
/// </summary>
public sealed class BandController
{
    private readonly AppSettings _settings;

    public BandController(DriverSnapshot snapshot, AdapterCapabilities capabilities, AppSettings settings)
    {
        Snapshot = snapshot;
        Capabilities = capabilities;
        _settings = settings;
    }

    public DriverSnapshot Snapshot { get; }

    public AdapterCapabilities Capabilities { get; internal set; }

    /// <summary>Actual radio state, re-read on every refresh.</summary>
    public WlanStatus Status { get; private set; } = WlanStatus.Disconnected;

    public Band? AssociatedBand => Status.Band;

    public static string FamilyLabel(SettingFamily family) => family switch
    {
        SettingFamily.Preference => Strings.FamilyPreference,
        SettingFamily.Lock => Strings.FamilyLock,
        SettingFamily.Roaming => Strings.FamilyRoaming,
        _ => family.ToString(),
    };

    /// <summary>Re-reads registry values and radio state. Never uses a durable cache.</summary>
    public void Reload()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(Snapshot.KeyPath);

            if (key is not null)
            {
                foreach (var family in new[] { SettingFamily.Preference, SettingFamily.Lock, SettingFamily.Roaming })
                {
                    var setting = Capabilities.For(family);
                    if (setting is null)
                        continue;

                    var value = key.GetValue(setting.Keyword)?.ToString();
                    if (value is not null)
                        setting.CurrentCode = value;
                }
            }

            if (Snapshot.InterfaceGuid is Guid guid)
                Status = WlanApi.QueryStatus(guid);
        }
        catch (Exception ex)
        {
            Logger.Write(Strings.LogRefreshFailed.FormatWith(ex.Message));
        }
    }

    /// <summary>Writes a setting, applies it and checks its effect.</summary>
    public ApplyOutcome Apply(SettingFamily family, string code, IProgress<string>? progress = null)
    {
        Reload();

        var setting = Capabilities.For(family);
        if (setting is null)
            return new ApplyOutcome(
                ApplyStatus.Failed, Strings.NoSettingForFamily.FormatWith(SettingClassifier.FamilyName(family)));

        var choice = setting.WithCode(code);
        if (choice is null)
            return new ApplyOutcome(ApplyStatus.Failed, Strings.InvalidDriverValue);

        if (!Interop.IsElevated)
            return new ApplyOutcome(ApplyStatus.Refused, Strings.AdminRequired);

        if (family == SettingFamily.Lock && choice.Band is Band targetBand && !IsBandAvailable(targetBand))
        {
            return new ApplyOutcome(
                ApplyStatus.Refused,
                Strings.BandNotVisible.FormatWith(BandInfo.Describe(targetBand)));
        }

        var backup = CaptureBackup(Strings.ChangeDescription.FormatWith(FamilyLabel(family), choice.DisplayLabel));

        progress?.Report(Strings.ProgressWriting);
        if (!TryWrite(setting.Keyword, code, out var error))
        {
            Logger.Write(Strings.LogWriteFailed.FormatWith(setting.Keyword, code, error));
            return new ApplyOutcome(ApplyStatus.Failed, error ?? Strings.WriteRejected);
        }

        _settings.LastChange = backup;
        _settings.Save();
        Reload();

        if (family == SettingFamily.Roaming)
        {
            Logger.Write(Strings.LogRoaming.FormatWith(setting.Keyword, code, choice.DisplayLabel));
            return new ApplyOutcome(
                ApplyStatus.Applied,
                Strings.RoamingApplied.FormatWith(choice.DisplayLabel));
        }

        var desired = choice.Band;
        if (desired is null)
        {
            ApplySoftReconnect(progress);
            Logger.Write(Strings.LogWrittenNoBand.FormatWith(setting.Keyword, code));
            return new ApplyOutcome(ApplyStatus.Applied, Strings.SavedChoice.FormatWith(choice.DisplayLabel));
        }

        progress?.Report(Strings.ProgressReconnect);
        var soft = ApplySoftReconnect(progress);
        if (soft && WaitForBand(desired.Value, 12000))
        {
            Logger.Write(Strings.LogAppliedSoft.FormatWith(setting.Keyword, code, desired));
            return new ApplyOutcome(
                ApplyStatus.Applied,
                Strings.AssociatedOn.FormatWith(BandInfo.Describe(desired.Value)));
        }

        progress?.Report(Strings.ProgressRestarting);
        if (RestartAdapter() && WaitForBand(desired.Value, 30000))
        {
            Logger.Write(Strings.LogAppliedRestart.FormatWith(setting.Keyword, code, desired));
            return new ApplyOutcome(
                ApplyStatus.Applied,
                Strings.AssociatedOn.FormatWith(BandInfo.Describe(desired.Value)));
        }

        if (family == SettingFamily.Lock)
        {
            progress?.Report(Strings.ProgressRollback);
            var reverted = Restore(backup);
            RestartAdapter();
            WaitForBand(null, 30000);

            Logger.Write(Strings.LogLockReverted.FormatWith(setting.Keyword, code));
            return new ApplyOutcome(
                ApplyStatus.Reverted,
                reverted
                    ? Strings.LockReverted
                    : Strings.LockRevertFailed.FormatWith(Strings.MenuUndoLastChange));
        }

        Logger.Write(Strings.LogPrefNotObserved.FormatWith(setting.Keyword, code, Status.Band));
        return new ApplyOutcome(
            ApplyStatus.AppliedNotObserved,
            Strings.PrefNotObserved.FormatWith(DescribeBand(Status.Band)));
    }

    /// <summary>Restores the values from before the last change.</summary>
    public ApplyOutcome RevertLastChange(IProgress<string>? progress = null)
    {
        var last = _settings.LastChange;
        if (last is null)
            return new ApplyOutcome(ApplyStatus.Refused, Strings.NothingToUndo);

        if (!Interop.IsElevated)
            return new ApplyOutcome(ApplyStatus.Refused, Strings.AdminRequired);

        if (!string.Equals(last.AdapterKeyPath, Snapshot.KeyPath, StringComparison.OrdinalIgnoreCase))
            return new ApplyOutcome(ApplyStatus.Failed, Strings.UndoWrongAdapter);

        progress?.Report(Strings.ProgressRestoring);
        var ok = Restore(last);
        _settings.LastChange = null;
        _settings.Save();
        Reload();

        if (!ok)
            return new ApplyOutcome(ApplyStatus.Failed, Strings.UndoPartial);

        progress?.Report(Strings.ProgressReconnect);
        RestartAdapter();
        var connected = WaitForBand(null, 30000);

        Logger.Write(Strings.LogUndoDetail.FormatWith(last.Description));
        return new ApplyOutcome(
            ApplyStatus.Reverted,
            connected
                ? Strings.UndoRestoredConnected.FormatWith(DescribeBand(Status.Band))
                : Strings.UndoRestoredPending);
    }

    /// <summary>Reconnects without changing any setting.</summary>
    public void ReconnectNow()
    {
        Reload();
        if (Status.Connected && !string.IsNullOrEmpty(Status.ProfileName))
        {
            WlanApi.Reconnect(Snapshot.InterfaceGuid!.Value, Status.ProfileName);
            Logger.Write(Strings.LogReconnectRequested);
        }
    }

    private bool IsBandAvailable(Band band)
    {
        if (Status.Band == band)
            return true;

        if (Snapshot.InterfaceGuid is not Guid guid)
            return true;

        var visible = WlanApi.ScanBands(guid, 2500);

        // Empty scan: do not conclude, the automatic rollback is the safety net.
        if (visible.Count == 0)
            return true;

        return visible.Contains(band);
    }

    private LastChange CaptureBackup(string description)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var family in new[] { SettingFamily.Preference, SettingFamily.Lock, SettingFamily.Roaming })
        {
            var setting = Capabilities.For(family);
            if (setting is not null && setting.CurrentCode.Length > 0)
                values[setting.Keyword] = setting.CurrentCode;
        }

        return new LastChange
        {
            AdapterKeyPath = Snapshot.KeyPath,
            Description = description,
            Values = values,
        };
    }

    private bool Restore(LastChange backup)
    {
        var ok = true;
        foreach (var (keyword, value) in backup.Values)
        {
            if (!TryWrite(keyword, value, out var error))
            {
                ok = false;
                Logger.Write(Strings.LogRestoreFailed.FormatWith(keyword, value, error));
            }
        }

        return ok;
    }

    private bool TryWrite(string keyword, string code, out string? error)
    {
        error = null;

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(Snapshot.KeyPath, writable: true);
            if (key is null)
            {
                error = Strings.RegistryKeyMissing;
                return false;
            }

            key.SetValue(keyword, code, RegistryValueKind.String);
            key.Flush();

            var readBack = key.GetValue(keyword)?.ToString();
            if (!string.Equals(readBack, code, StringComparison.OrdinalIgnoreCase))
            {
                error = Strings.DriverRefusedValue.FormatWith(readBack ?? Strings.Absent);
                return false;
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = Strings.WriteDenied;
            return false;
        }
        catch (Exception ex)
        {
            error = Strings.WriteFailed.FormatWith(ex.Message);
            return false;
        }
    }

    private bool ApplySoftReconnect(IProgress<string>? progress)
    {
        if (!Status.Connected || string.IsNullOrEmpty(Status.ProfileName) || Snapshot.InterfaceGuid is not Guid guid)
            return false;

        progress?.Report(Strings.ProgressReconnectNetwork);
        var ok = WlanApi.Reconnect(guid, Status.ProfileName);
        _settings.ApplyMode = "Soft";
        _settings.Save();
        return ok;
    }

    private bool RestartAdapter()
    {
        if (string.IsNullOrWhiteSpace(Snapshot.InterfaceAlias))
        {
            Logger.Write(Strings.LogRestartImpossible);
            return false;
        }

        var alias = Snapshot.InterfaceAlias!;
        Logger.Write(Strings.LogRestarting.FormatWith(alias));

        var disabled = Interop.RunHidden(
            "netsh", $"interface set interface name=\"{alias}\" admin=disabled", 20000);
        Thread.Sleep(1200);

        var enabled = Interop.RunHidden(
            "netsh", $"interface set interface name=\"{alias}\" admin=enabled", 20000);

        _settings.ApplyMode = "Restart";
        _settings.Save();

        if (disabled != 0 || enabled != 0)
        {
            Logger.Write(Strings.LogNetshReturned.FormatWith(disabled, enabled, alias));
            return false;
        }

        return true;
    }

    private bool WaitForBand(Band? target, int timeoutMs)
    {
        if (Snapshot.InterfaceGuid is not Guid guid)
            return false;

        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var status = WlanApi.QueryStatus(guid);
            Status = status;

            if (status.Connected && (target is null || status.Band == target))
                return true;

            Thread.Sleep(700);
        }

        Status = WlanApi.QueryStatus(guid);
        return Status.Connected && (target is null || Status.Band == target);
    }

    private static string DescribeBand(Band? band) =>
        band is Band b ? BandInfo.Describe(b) : Strings.BandUnknown;
}
