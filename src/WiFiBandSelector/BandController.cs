using Microsoft.Win32;

namespace WiFiBandSelector;

public enum ApplyStatus
{
    /// <summary>Écrit et observé (ou sans effet observable attendu).</summary>
    Applied,

    /// <summary>Écrit, mais la carte ne suit pas la préférence demandée.</summary>
    AppliedNotObserved,

    /// <summary>Écrit puis annulé automatiquement, faute de connexion.</summary>
    Reverted,

    /// <summary>Refusé avant toute écriture.</summary>
    Refused,

    /// <summary>Erreur technique.</summary>
    Failed,
}

public sealed record ApplyOutcome(ApplyStatus Status, string Message)
{
    public bool Ok => Status is ApplyStatus.Applied or ApplyStatus.AppliedNotObserved;
}

/// <summary>
/// Lecture et écriture effectives des réglages : sauvegarde, écriture vérifiée,
/// application (reconnexion douce puis redémarrage de la carte), retour arrière.
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

    /// <summary>État radio réel, relu à chaque rafraîchissement.</summary>
    public WlanStatus Status { get; private set; } = WlanStatus.Disconnected;

    public Band? AssociatedBand => Status.Band;

    public static string FamilyLabel(SettingFamily family) => family switch
    {
        SettingFamily.Preference => "Préférence",
        SettingFamily.Lock => "Verrouillage",
        SettingFamily.Roaming => "Itinérance",
        _ => family.ToString(),
    };

    /// <summary>Relit les valeurs du registre et l'état radio. Jamais de cache durable.</summary>
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
            Logger.Write($"Rafraîchissement impossible : {ex.Message}");
        }
    }

    /// <summary>Écrit un réglage, l'applique et vérifie son effet.</summary>
    public ApplyOutcome Apply(SettingFamily family, string code, IProgress<string>? progress = null)
    {
        Reload();

        var setting = Capabilities.For(family);
        if (setting is null)
            return new ApplyOutcome(ApplyStatus.Failed, $"Ce pilote n'expose pas de réglage de {FamilyLabel(family).ToLowerInvariant()}.");

        var choice = setting.WithCode(code);
        if (choice is null)
            return new ApplyOutcome(ApplyStatus.Failed, "Cette valeur n'existe pas dans le pilote.");

        if (!Interop.IsElevated)
            return new ApplyOutcome(ApplyStatus.Refused, "Droits administrateur requis pour modifier un pilote.");

        if (family == SettingFamily.Lock && choice.Band is Band targetBand && !IsBandAvailable(targetBand))
        {
            return new ApplyOutcome(
                ApplyStatus.Refused,
                $"{BandInfo.Describe(targetBand)} n'apparaît pas dans l'analyse des réseaux. " +
                "Le verrou n'a pas été appliqué, pour ne pas couper la connexion.");
        }

        var backup = CaptureBackup($"{FamilyLabel(family)} : {choice.DisplayLabel}");

        progress?.Report("Écriture du réglage…");
        if (!TryWrite(setting.Keyword, code, out var error))
        {
            Logger.Write($"Échec d'écriture {setting.Keyword}={code} : {error}");
            return new ApplyOutcome(ApplyStatus.Failed, error ?? "Écriture refusée par le pilote.");
        }

        _settings.LastChange = backup;
        _settings.Save();
        Reload();

        if (family == SettingFamily.Roaming)
        {
            Logger.Write($"Itinérance {setting.Keyword}={code} ({choice.DisplayLabel})");
            return new ApplyOutcome(
                ApplyStatus.Applied,
                $"Itinérance réglée sur « {choice.DisplayLabel} ». Aucune reconnexion nécessaire.");
        }

        var desired = choice.Band;
        if (desired is null)
        {
            ApplySoftReconnect(progress);
            Logger.Write($"{setting.Keyword}={code} écrit sans bande cible");
            return new ApplyOutcome(ApplyStatus.Applied, $"« {choice.DisplayLabel} » enregistré.");
        }

        progress?.Report("Reconnexion…");
        var soft = ApplySoftReconnect(progress);
        if (soft && WaitForBand(desired.Value, 12000))
        {
            Logger.Write($"{setting.Keyword}={code} appliqué par reconnexion douce, bande {desired}");
            return new ApplyOutcome(
                ApplyStatus.Applied,
                $"Associé en {BandInfo.Describe(desired.Value)}.");
        }

        progress?.Report("Redémarrage de la carte Wi-Fi…");
        if (RestartAdapter() && WaitForBand(desired.Value, 30000))
        {
            Logger.Write($"{setting.Keyword}={code} appliqué après redémarrage, bande {desired}");
            return new ApplyOutcome(
                ApplyStatus.Applied,
                $"Associé en {BandInfo.Describe(desired.Value)}.");
        }

        if (family == SettingFamily.Lock)
        {
            progress?.Report("Retour arrière…");
            var reverted = Restore(backup);
            RestartAdapter();
            WaitForBand(null, 30000);

            Logger.Write($"Verrou {setting.Keyword}={code} annulé : association non rétablie");
            return new ApplyOutcome(
                ApplyStatus.Reverted,
                reverted
                    ? "La carte n'a pas retrouvé de connexion sur cette bande : réglage annulé et connexion rétablie."
                    : "La carte n'a pas retrouvé de connexion sur cette bande. Le réglage n'a pas pu être annulé automatiquement : utilisez « Annuler la dernière modification ».");
        }

        Logger.Write($"Préférence {setting.Keyword}={code} non observée (bande {Status.Band})");
        return new ApplyOutcome(
            ApplyStatus.AppliedNotObserved,
            $"Préférence enregistrée, mais la carte reste associée en {DescribeBand(Status.Band)}. " +
            "Le pilote décide, le signal est peut-être insuffisant sur l'autre bande.");
    }

    /// <summary>Restaure les valeurs d'avant la dernière modification.</summary>
    public ApplyOutcome RevertLastChange(IProgress<string>? progress = null)
    {
        var last = _settings.LastChange;
        if (last is null)
            return new ApplyOutcome(ApplyStatus.Refused, "Aucune modification à annuler.");

        if (!Interop.IsElevated)
            return new ApplyOutcome(ApplyStatus.Refused, "Droits administrateur requis pour modifier un pilote.");

        if (!string.Equals(last.AdapterKeyPath, Snapshot.KeyPath, StringComparison.OrdinalIgnoreCase))
            return new ApplyOutcome(ApplyStatus.Failed, "La modification sauvegardée concerne une autre carte.");

        progress?.Report("Restauration des réglages…");
        var ok = Restore(last);
        _settings.LastChange = null;
        _settings.Save();
        Reload();

        if (!ok)
            return new ApplyOutcome(ApplyStatus.Failed, "Certaines valeurs n'ont pas pu être restaurées. Consultez le journal.");

        progress?.Report("Reconnexion…");
        RestartAdapter();
        var connected = WaitForBand(null, 30000);

        Logger.Write($"Annulation de « {last.Description} »");
        return new ApplyOutcome(
            ApplyStatus.Reverted,
            connected
                ? $"Réglages précédents restaurés (associé en {DescribeBand(Status.Band)})."
                : "Réglages précédents restaurés. La connexion n'est pas encore revenue : la carte se reconnectera d'elle-même.");
    }

    /// <summary>Reconnecte sans changer de réglage.</summary>
    public void ReconnectNow()
    {
        Reload();
        if (Status.Connected && !string.IsNullOrEmpty(Status.ProfileName))
        {
            WlanApi.Reconnect(Snapshot.InterfaceGuid!.Value, Status.ProfileName);
            Logger.Write("Reconnexion demandée par l'utilisateur");
        }
    }

    private bool IsBandAvailable(Band band)
    {
        if (Status.Band == band)
            return true;

        if (Snapshot.InterfaceGuid is not Guid guid)
            return true;

        var visible = WlanApi.ScanBands(guid, 2500);

        // Analyse vide : on ne conclut pas, le retour arrière automatique protège.
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
                Logger.Write($"Restauration de {keyword}={value} impossible : {error}");
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
                error = "Clé de la carte introuvable ou non modifiable.";
                return false;
            }

            key.SetValue(keyword, code, RegistryValueKind.String);
            key.Flush();

            var readBack = key.GetValue(keyword)?.ToString();
            if (!string.Equals(readBack, code, StringComparison.OrdinalIgnoreCase))
            {
                error = "Le pilote a refusé la valeur (relue : " + (readBack ?? "absente") + ").";
                return false;
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = "Écriture refusée : l'application n'est pas élevée.";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Écriture impossible : {ex.Message}";
            return false;
        }
    }

    private bool ApplySoftReconnect(IProgress<string>? progress)
    {
        if (!Status.Connected || string.IsNullOrEmpty(Status.ProfileName) || Snapshot.InterfaceGuid is not Guid guid)
            return false;

        progress?.Report("Reconnexion au réseau…");
        var ok = WlanApi.Reconnect(guid, Status.ProfileName);
        _settings.ApplyMode = "Soft";
        _settings.Save();
        return ok;
    }

    private bool RestartAdapter()
    {
        if (string.IsNullOrWhiteSpace(Snapshot.InterfaceAlias))
        {
            Logger.Write("Redémarrage impossible : alias d'interface inconnu");
            return false;
        }

        var alias = Snapshot.InterfaceAlias!;
        Logger.Write($"Redémarrage de « {alias} »");

        var disabled = Interop.RunHidden(
            "netsh", $"interface set interface name=\"{alias}\" admin=disabled", 20000);
        Thread.Sleep(1200);

        var enabled = Interop.RunHidden(
            "netsh", $"interface set interface name=\"{alias}\" admin=enabled", 20000);

        _settings.ApplyMode = "Restart";
        _settings.Save();

        if (disabled != 0 || enabled != 0)
        {
            Logger.Write($"netsh a retourné {disabled}/{enabled} pour « {alias} »");
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
        band is Band b ? BandInfo.Describe(b) : "une bande inconnue";
}
