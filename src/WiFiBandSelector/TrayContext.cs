using System.Reflection;
using System.Text;

namespace WiFiBandSelector;

/// <summary>Application de zone de notification : menu, icône, actions.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _menu = new() { ShowItemToolTips = true };
    private readonly AppSettings _settings;
    private List<DriverSnapshot> _snapshots = new();
    private BandController? _controller;
    private Icon? _icon;
    private bool _busy;

    public TrayContext()
    {
        _settings = AppSettings.Load();
        _menu.Opening += OnMenuOpening;
        _tray.ContextMenuStrip = _menu;
        _tray.MouseClick += OnTrayMouseClick;
        _tray.Visible = true;

        Logger.Write(
            $"Démarrage {Version} — élevé : {Interop.IsElevated}, " +
            $"exécutable : {Environment.ProcessPath}");

        Refresh();
    }

    private static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            Balloon("Une opération est déjà en cours sur la carte Wi-Fi.");
            return;
        }

        Rebuild();
    }

    private void OnTrayMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _menu.Show(Cursor.Position);
    }

    /// <summary>Découverte de la carte et rafraîchissement des valeurs.</summary>
    private void Refresh()
    {
        try
        {
            _snapshots = AdapterDiscovery.Discover();

            if (_snapshots.Count == 0)
            {
                _controller = null;
                UpdateTray();
                return;
            }

            var chosen = _snapshots.FirstOrDefault(
                             s => string.Equals(s.KeyPath, _settings.AdapterKeyPath, StringComparison.OrdinalIgnoreCase))
                         ?? _snapshots[0];

            if (_controller is null ||
                !string.Equals(_controller.Snapshot.KeyPath, chosen.KeyPath, StringComparison.OrdinalIgnoreCase))
            {
                _controller = new BandController(chosen, SettingClassifier.Classify(chosen.Params), _settings);
            }
            else
            {
                _controller.Capabilities = SettingClassifier.Classify(chosen.Params);
            }

            if (!string.Equals(_settings.AdapterKeyPath, chosen.KeyPath, StringComparison.OrdinalIgnoreCase))
            {
                _settings.AdapterKeyPath = chosen.KeyPath;
                _settings.Save();
            }

            _controller.Reload();
        }
        catch (Exception ex)
        {
            Logger.Write($"Rafraîchissement impossible : {ex}");
            _controller = null;
        }

        UpdateTray();
    }

    private void UpdateTray()
    {
        Band? band = null;
        var locked = false;
        var unsupported = true;

        if (_controller is not null)
        {
            var caps = _controller.Capabilities;
            locked = caps.Lock?.CurrentBand is not null;
            band = caps.Lock?.CurrentBand ?? caps.Preference?.CurrentBand;
            unsupported = !caps.IsSupported;
        }

        var icon = TrayIcon.Create(band, locked, unsupported);
        var previous = _icon;
        _tray.Icon = icon;
        _icon = icon;
        previous?.Dispose();

        _tray.Text = Clip(BuildTooltip(), 63);
    }

    private string BuildTooltip()
    {
        if (_controller is null)
            return "WiFiBandSelector — aucune carte Wi-Fi";

        var caps = _controller.Capabilities;
        if (!caps.IsSupported)
            return "WiFiBandSelector — carte non prise en charge";

        var lockBand = caps.Lock?.CurrentBand;
        var prefBand = caps.Preference?.CurrentBand;

        var state = lockBand is Band lb
            ? $"verrouillé {BandInfo.Describe(lb)}"
            : prefBand is Band pb
                ? $"préfère {BandInfo.Describe(pb)}"
                : "automatique";

        var association = _controller.Status.Connected
            ? $"associé {BandInfo.Describe(_controller.Status.Band ?? Band.Band24)}"
            : "non connecté";

        return $"Wi-Fi — {state} · {association}";
    }

    private string HeaderText()
    {
        if (_controller is null)
            return "Aucune carte Wi-Fi détectée";

        var caps = _controller.Capabilities;
        var lockBand = caps.Lock?.CurrentBand;
        var prefBand = caps.Preference?.CurrentBand;

        var state = lockBand is Band lb
            ? $"verrouillé sur {BandInfo.Describe(lb)}"
            : prefBand is Band pb
                ? $"préfère {BandInfo.Describe(pb)}"
                : caps.IsSupported ? "automatique" : "aucun réglage de bande";

        var association = _controller.Status.Connected
            ? $"associé : {BandInfo.Describe(_controller.Status.Band ?? Band.Band24)}"
            : "non connecté";

        var name = _snapshots.Count > 1
            ? _controller.Snapshot.InterfaceAlias ?? _controller.Snapshot.DriverDesc
            : "Wi-Fi";

        return $"{name} — {state} ({association})";
    }

    private void Rebuild()
    {
        Refresh();
        _menu.Items.Clear();

        if (_controller is null)
        {
            AddDisabled("Aucune carte Wi-Fi détectée");
            _menu.Items.Add(new ToolStripSeparator());
            Add("Diagnostic…", (_, _) => ShowDiagnostic());
            Add("Quitter", (_, _) => Quit());
            return;
        }

        var caps = _controller.Capabilities;

        AddDisabled(HeaderText());

        if (!Interop.IsElevated)
            Add("Relancer en administrateur", (_, _) => Elevate());

        if (_snapshots.Count > 1)
        {
            var cardMenu = new ToolStripMenuItem("Carte Wi-Fi");
            foreach (var snapshot in _snapshots)
            {
                var item = new ToolStripMenuItem(snapshot.DriverDesc)
                {
                    Checked = string.Equals(snapshot.KeyPath, _controller.Snapshot.KeyPath, StringComparison.OrdinalIgnoreCase),
                };
                var key = snapshot.KeyPath;
                item.Click += (_, _) => SelectAdapter(key);
                cardMenu.DropDownItems.Add(item);
            }

            _menu.Items.Add(cardMenu);
        }

        _menu.Items.Add(new ToolStripSeparator());

        if (!caps.IsSupported)
            AddDisabled("Aucun réglage de bande exposé par ce pilote");

        if (caps.Preference is not null)
        {
            AddDisabled("Bande préférée");
            var lockedBand = caps.Lock?.CurrentBand;

            foreach (var choice in caps.Preference.Choices)
            {
                var item = new ToolStripMenuItem(choice.DisplayLabel)
                {
                    Checked = string.Equals(choice.Code, caps.Preference.CurrentCode, StringComparison.OrdinalIgnoreCase),
                    Enabled = lockedBand is null,
                };

                if (lockedBand is Band b)
                    item.ToolTipText = $"Verrouillé sur {BandInfo.Describe(b)}";

                var code = choice.Code;
                item.Click += async (_, _) => await ApplyAsync(SettingFamily.Preference, code);
                _menu.Items.Add(item);
            }
        }

        if (caps.Lock is not null)
        {
            var lockMenu = new ToolStripMenuItem("Verrouiller une bande");
            foreach (var choice in caps.Lock.Choices)
            {
                var item = new ToolStripMenuItem(choice.DisplayLabel)
                {
                    Checked = string.Equals(choice.Code, caps.Lock.CurrentCode, StringComparison.OrdinalIgnoreCase),
                };
                var code = choice.Code;
                item.Click += async (_, _) => await ApplyAsync(SettingFamily.Lock, code);
                lockMenu.DropDownItems.Add(item);
            }

            _menu.Items.Add(lockMenu);
        }
        else if (caps.Preference is not null)
        {
            AddDisabled("Verrouillage non pris en charge par ce pilote");
        }

        if (caps.Roaming is not null)
        {
            var roamingMenu = new ToolStripMenuItem("Itinérance");
            foreach (var choice in caps.Roaming.Choices)
            {
                var item = new ToolStripMenuItem(choice.DisplayLabel)
                {
                    Checked = string.Equals(choice.Code, caps.Roaming.CurrentCode, StringComparison.OrdinalIgnoreCase),
                };
                var code = choice.Code;
                item.Click += async (_, _) => await ApplyAsync(SettingFamily.Roaming, code);
                roamingMenu.DropDownItems.Add(item);
            }

            var defaultChoice = caps.Roaming.FindDefault();
            if (defaultChoice is not null)
            {
                roamingMenu.DropDownItems.Add(new ToolStripSeparator());
                var item = new ToolStripMenuItem($"Valeur par défaut du pilote ({defaultChoice.DisplayLabel})");
                var code = defaultChoice.Code;
                item.Click += async (_, _) => await ApplyAsync(SettingFamily.Roaming, code);
                roamingMenu.DropDownItems.Add(item);
            }

            _menu.Items.Add(roamingMenu);
        }

        _menu.Items.Add(new ToolStripSeparator());

        var reconnect = Add("Reconnecter maintenant", (_, _) => ReconnectNow());
        reconnect.Enabled = _controller.Status.Connected;

        var startup = Add("Démarrage avec Windows", (_, _) => ToggleStartup(ScheduledTask.IsEnabled() == true));
        var installed = ScheduledTask.IsRunningFromInstallDirectory();
        var taskState = installed ? ScheduledTask.IsEnabled() : null;
        startup.Checked = taskState == true;
        startup.Enabled = taskState is not null && Interop.IsElevated;
        startup.ToolTipText = !installed
            ? "Disponible une fois l'application installée."
            : taskState is null
                ? "Tâche planifiée absente : réinstallez l'application."
                : Interop.IsElevated
                    ? "Démarre l'application à l'ouverture de session, pour tous les comptes."
                    : "Droits administrateur requis.";

        _menu.Items.Add(new ToolStripSeparator());

        var revert = Add("Annuler la dernière modification", (_, _) => _ = RevertAsync());
        revert.Enabled = _settings.LastChange is not null && Interop.IsElevated;
        if (_settings.LastChange is not null)
            revert.ToolTipText = _settings.LastChange.Describe();

        Add("Diagnostic…", (_, _) => ShowDiagnostic());
        Add("Journal…", (_, _) => Interop.ShellOpen(AppPaths.LogFile));
        Add("À propos", (_, _) => ShowAbout());
        Add("Quitter", (_, _) => Quit());
    }

    private ToolStripMenuItem Add(string text, EventHandler? onClick = null)
    {
        var item = new ToolStripMenuItem(text);
        if (onClick is not null)
            item.Click += onClick;
        _menu.Items.Add(item);
        return item;
    }

    private void AddDisabled(string text) =>
        _menu.Items.Add(new ToolStripMenuItem(text) { Enabled = false });

    private async Task ApplyAsync(SettingFamily family, string code)
    {
        if (_busy || _controller is null)
            return;

        if (!Interop.IsElevated)
        {
            Balloon("Droits administrateur requis pour modifier un pilote. Utilisez « Relancer en administrateur ».",
                ToolTipIcon.Warning);
            return;
        }

        _busy = true;
        var controller = _controller;
        var progress = new Progress<string>(message => _tray.Text = Clip("WiFiBandSelector — " + message, 63));

        try
        {
            var outcome = await Task.Run(() => controller.Apply(family, code, progress));
            Logger.Write($"Action {family}/{code} : {outcome.Status} — {outcome.Message}");
            Balloon(outcome.Message, IconFor(outcome.Status));
        }
        catch (Exception ex)
        {
            Logger.Write($"Action {family}/{code} en échec : {ex}");
            Balloon($"Erreur : {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
            UpdateTray();
        }
    }

    private async Task RevertAsync()
    {
        if (_busy || _controller is null)
            return;

        if (!Interop.IsElevated)
        {
            Balloon("Droits administrateur requis pour modifier un pilote.", ToolTipIcon.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            $"Restaurer les réglages d'avant « {_settings.LastChange?.Description} » ?",
            "WiFiBandSelector",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (confirmation != DialogResult.Yes)
            return;

        _busy = true;
        var controller = _controller;
        var progress = new Progress<string>(message => _tray.Text = Clip("WiFiBandSelector — " + message, 63));

        try
        {
            var outcome = await Task.Run(() => controller.RevertLastChange(progress));
            Logger.Write($"Annulation : {outcome.Status} — {outcome.Message}");
            Balloon(outcome.Message, IconFor(outcome.Status));
        }
        catch (Exception ex)
        {
            Logger.Write($"Annulation en échec : {ex}");
            Balloon($"Erreur : {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
            UpdateTray();
        }
    }

    private void ReconnectNow()
    {
        if (_busy || _controller is null)
            return;

        try
        {
            _controller.ReconnectNow();
            Balloon("Reconnexion demandée.");
        }
        catch (Exception ex)
        {
            Logger.Write($"Reconnexion impossible : {ex.Message}");
            Balloon($"Reconnexion impossible : {ex.Message}", ToolTipIcon.Error);
        }

        UpdateTray();
    }

    private void ToggleStartup(bool enabled)
    {
        var ok = ScheduledTask.SetEnabled(!enabled);
        Balloon(
            ok
                ? enabled ? "Démarrage avec Windows désactivé." : "Démarrage avec Windows activé."
                : "Modification de la tâche planifiée impossible.",
            ok ? ToolTipIcon.Info : ToolTipIcon.Error);
    }

    private void SelectAdapter(string keyPath)
    {
        _settings.AdapterKeyPath = keyPath;
        _settings.Save();
        _controller = null;
        Refresh();
    }

    private void Elevate()
    {
        if (Interop.TryRestartElevated())
            Quit();
        else
            Balloon("Relance en administrateur annulée.", ToolTipIcon.Warning);
    }

    private void ShowDiagnostic()
    {
        try
        {
            AppPaths.Ensure();
            var report = Diagnostics.BuildReport();
            var path = AppPaths.NewReportFile();
            File.WriteAllText(path, report, new UTF8Encoding(true));
            TryCopyToClipboard(report);

            Logger.Write($"Rapport de diagnostic écrit : {path}");
            Interop.ShowInExplorer(path);
            Balloon(
                "Rapport écrit et copié dans le presse-papiers. Envoyez-le par mail ou joignez le fichier ouvert dans l'Explorateur.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Logger.Write($"Rapport de diagnostic impossible : {ex}");
            Balloon($"Rapport impossible : {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void ShowAbout()
    {
        var text =
            $"WiFiBandSelector {Version}{Environment.NewLine}{Environment.NewLine}" +
            "Change la bande Wi-Fi de la carte en deux clics : préférence souple, verrouillage, itinérance." +
            Environment.NewLine + Environment.NewLine +
            "L'application ne fait qu'écrire les réglages que le pilote expose déjà. " +
            "Aucun pilote n'est modifié, aucune donnée n'est envoyée." +
            Environment.NewLine + Environment.NewLine +
            $"Journal : {AppPaths.LogFile}";

        MessageBox.Show(text, "À propos de WiFiBandSelector", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Quit()
    {
        try
        {
            _tray.Visible = false;
            _tray.Dispose();
            _icon?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Write($"Arrêt difficile : {ex.Message}");
        }

        ExitThread();
    }

    private void Balloon(string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try
        {
            _tray.ShowBalloonTip(6000, "WiFiBandSelector", Clip(message, 250), icon);
        }
        catch (Exception ex)
        {
            Logger.Write($"Bulle impossible : {ex.Message}");
        }
    }

    private static ToolTipIcon IconFor(ApplyStatus status) => status switch
    {
        ApplyStatus.Applied => ToolTipIcon.Info,
        ApplyStatus.AppliedNotObserved => ToolTipIcon.Warning,
        ApplyStatus.Reverted => ToolTipIcon.Warning,
        _ => ToolTipIcon.Error,
    };

    private static void TryCopyToClipboard(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch
            {
                Thread.Sleep(80);
            }
        }

        Logger.Write("Presse-papiers indisponible.");
    }

    private static string Clip(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _icon?.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }
}
