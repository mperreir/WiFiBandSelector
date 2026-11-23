using System.Reflection;
using System.Text;
using WiFiBandSelector.Resources;

namespace WiFiBandSelector;

/// <summary>Notification-area application: menu, icon and actions.</summary>
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

        Logger.Write(Strings.LogStartup.FormatWith(Version, Interop.IsElevated, Environment.ProcessPath));

        Refresh();
    }

    private static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            Balloon(Strings.BusyBalloon);
            return;
        }

        Rebuild();
    }

    private void OnTrayMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        // ContextMenuStrip.Show() creates an unactivated popup: clicking outside does
        // not dismiss it. Right-click goes through NotifyIcon.ShowContextMenu(), which
        // activates the menu correctly; use the same path here.
        var show = typeof(NotifyIcon).GetMethod(
            "ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

        if (show is null)
        {
            // Fallback if this internal member changes in a future version.
            _menu.Show(Cursor.Position);
            return;
        }

        show.Invoke(_tray, null);
    }

    /// <summary>Discovers the adapter and refreshes values.</summary>
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
            Logger.Write(Strings.LogRefreshFailed.FormatWith(ex));
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
            return Strings.TrayNoAdapter;

        var caps = _controller.Capabilities;
        if (!caps.IsSupported)
            return Strings.TrayUnsupported;

        var lockBand = caps.Lock?.CurrentBand;
        var prefBand = caps.Preference?.CurrentBand;

        var state = lockBand is Band lb
            ? Strings.TrayStateLocked.FormatWith(BandInfo.Describe(lb))
            : prefBand is Band pb
                ? Strings.TrayStatePrefers.FormatWith(BandInfo.Describe(pb))
                : Strings.TrayStateAutomatic;

        var association = _controller.Status.Connected
            ? Strings.TrayAssocConnected.FormatWith(BandInfo.Describe(_controller.Status.Band ?? Band.Band24))
            : Strings.TrayAssocOffline;

        return Strings.TrayTooltip.FormatWith(state, association);
    }

    private string HeaderText()
    {
        if (_controller is null)
            return Strings.HeaderNoAdapter;

        var caps = _controller.Capabilities;
        var lockBand = caps.Lock?.CurrentBand;
        var prefBand = caps.Preference?.CurrentBand;

        var state = lockBand is Band lb
            ? Strings.HeaderLocked.FormatWith(BandInfo.Describe(lb))
            : prefBand is Band pb
                ? Strings.HeaderPrefers.FormatWith(BandInfo.Describe(pb))
                : caps.IsSupported ? Strings.TrayStateAutomatic : Strings.HeaderNoBandSetting;

        var association = _controller.Status.Connected
            ? Strings.HeaderAssociated.FormatWith(BandInfo.Describe(_controller.Status.Band ?? Band.Band24))
            : Strings.TrayAssocOffline;

        var name = _snapshots.Count > 1
            ? _controller.Snapshot.InterfaceAlias ?? _controller.Snapshot.DriverDesc
            : "Wi-Fi";

        return Strings.HeaderFormat.FormatWith(name, state, association);
    }

    private void Rebuild()
    {
        Refresh();
        _menu.Items.Clear();

        if (_controller is null)
        {
            AddDisabled(Strings.HeaderNoAdapter);
            _menu.Items.Add(new ToolStripSeparator());
            Add(Strings.MenuDiagnostic, (_, _) => ShowDiagnostic());
            Add(Strings.MenuQuit, (_, _) => Quit());
            return;
        }

        var caps = _controller.Capabilities;

        AddDisabled(HeaderText());

        if (!Interop.IsElevated)
            Add(Strings.MenuRestartAdmin, (_, _) => Elevate());

        if (_snapshots.Count > 1)
        {
            var cardMenu = new ToolStripMenuItem(Strings.MenuAdapters);
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
            AddDisabled(Strings.MenuNoBandExposed);

        if (caps.Preference is not null)
        {
            AddDisabled(Strings.MenuPreferredBand);
            var lockedBand = caps.Lock?.CurrentBand;

            foreach (var choice in caps.Preference.Choices)
            {
                var item = new ToolStripMenuItem(choice.DisplayLabel)
                {
                    Checked = string.Equals(choice.Code, caps.Preference.CurrentCode, StringComparison.OrdinalIgnoreCase),
                    Enabled = lockedBand is null,
                };

                if (lockedBand is Band b)
                    item.ToolTipText = Strings.MenuLockedOn.FormatWith(BandInfo.Describe(b));

                var code = choice.Code;
                item.Click += async (_, _) => await ApplyAsync(SettingFamily.Preference, code);
                _menu.Items.Add(item);
            }
        }

        if (caps.Lock is not null)
        {
            var lockMenu = new ToolStripMenuItem(Strings.MenuLockBand);
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
            AddDisabled(Strings.MenuLockUnsupported);
        }

        if (caps.Roaming is not null)
        {
            var roamingMenu = new ToolStripMenuItem(Strings.MenuRoaming);
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
                var item = new ToolStripMenuItem(Strings.MenuDriverDefault.FormatWith(defaultChoice.DisplayLabel));
                var code = defaultChoice.Code;
                item.Click += async (_, _) => await ApplyAsync(SettingFamily.Roaming, code);
                roamingMenu.DropDownItems.Add(item);
            }

            _menu.Items.Add(roamingMenu);
        }

        _menu.Items.Add(new ToolStripSeparator());

        var reconnect = Add(Strings.MenuReconnectNow, (_, _) => ReconnectNow());
        reconnect.Enabled = _controller.Status.Connected;

        var startup = Add(Strings.MenuStartWithWindows, (_, _) => ToggleStartup(ScheduledTask.IsEnabled() == true));
        var installed = ScheduledTask.IsRunningFromInstallDirectory();
        var taskState = installed ? ScheduledTask.IsEnabled() : null;
        startup.Checked = taskState == true;
        startup.Enabled = taskState is not null && Interop.IsElevated;
        startup.ToolTipText = !installed
            ? Strings.StartupTipNotInstalled
            : taskState is null
                ? Strings.StartupTipNoTask
                : Interop.IsElevated
                    ? Strings.StartupTipEnabled
                    : Strings.StartupTipAdmin;

        _menu.Items.Add(new ToolStripSeparator());

        var revert = Add(Strings.MenuUndoLastChange, (_, _) => _ = RevertAsync());
        revert.Enabled = _settings.LastChange is not null && Interop.IsElevated;
        if (_settings.LastChange is not null)
            revert.ToolTipText = _settings.LastChange.Describe();

        Add(Strings.MenuDiagnostic, (_, _) => ShowDiagnostic());
        Add(Strings.MenuLog, (_, _) => Interop.ShellOpen(AppPaths.LogFile));
        Add(Strings.MenuAbout, (_, _) => ShowAbout());
        Add(Strings.MenuQuit, (_, _) => Quit());
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
            Balloon(Strings.AdminRequiredWithMenu.FormatWith(Strings.MenuRestartAdmin), ToolTipIcon.Warning);
            return;
        }

        _busy = true;
        var controller = _controller;
        var progress = new Progress<string>(message => _tray.Text = Clip(Strings.ProgressPrefix.FormatWith(message), 63));

        try
        {
            var outcome = await Task.Run(() => controller.Apply(family, code, progress));
            Logger.Write(Strings.LogAction.FormatWith(family, code, outcome.Status, outcome.Message));
            Balloon(outcome.Message, IconFor(outcome.Status));
        }
        catch (Exception ex)
        {
            Logger.Write(Strings.LogActionFailed.FormatWith(family, code, ex));
            Balloon(Strings.ErrorPrefix.FormatWith(ex.Message), ToolTipIcon.Error);
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
            Balloon(Strings.AdminRequired, ToolTipIcon.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            Strings.UndoConfirm.FormatWith(_settings.LastChange?.Description ?? string.Empty),
            Strings.AppTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (confirmation != DialogResult.Yes)
            return;

        _busy = true;
        var controller = _controller;
        var progress = new Progress<string>(message => _tray.Text = Clip(Strings.ProgressPrefix.FormatWith(message), 63));

        try
        {
            var outcome = await Task.Run(() => controller.RevertLastChange(progress));
            Logger.Write(Strings.LogUndo.FormatWith(outcome.Status, outcome.Message));
            Balloon(outcome.Message, IconFor(outcome.Status));
        }
        catch (Exception ex)
        {
            Logger.Write(Strings.LogUndoFailed.FormatWith(ex));
            Balloon(Strings.ErrorPrefix.FormatWith(ex.Message), ToolTipIcon.Error);
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
            Balloon(Strings.ReconnectRequested);
        }
        catch (Exception ex)
        {
            Logger.Write(Strings.LogReconnectImpossible.FormatWith(ex.Message));
            Balloon(Strings.ReconnectFailed.FormatWith(ex.Message), ToolTipIcon.Error);
        }

        UpdateTray();
    }

    private void ToggleStartup(bool enabled)
    {
        var ok = ScheduledTask.SetEnabled(!enabled);
        Balloon(
            ok
                ? enabled ? Strings.StartupDisabled : Strings.StartupEnabled
                : Strings.StartupFailed,
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
            Balloon(Strings.ElevationCancelled, ToolTipIcon.Warning);
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

            Logger.Write(Strings.LogReportWritten.FormatWith(path));
            Interop.ShowInExplorer(path);
            Balloon(Strings.DiagnosticCopied, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Logger.Write(Strings.LogReportFailed.FormatWith(ex));
            Balloon(Strings.DiagnosticFailed.FormatWith(ex.Message), ToolTipIcon.Error);
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            Strings.AboutText.FormatWith(Version, AppPaths.LogFile),
            Strings.AboutTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
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
            Logger.Write(Strings.LogShutdown.FormatWith(ex.Message));
        }

        ExitThread();
    }

    private void Balloon(string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try
        {
            _tray.ShowBalloonTip(6000, Strings.AppTitle, Clip(message, 250), icon);
        }
        catch (Exception ex)
        {
            Logger.Write(Strings.LogBalloonFailed.FormatWith(ex.Message));
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

        Logger.Write(Strings.ClipboardUnavailable);
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
