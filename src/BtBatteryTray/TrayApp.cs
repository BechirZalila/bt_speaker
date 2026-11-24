namespace BtBatteryTray;

/// <summary>
/// Application sans fenêtre : une icône par appareil Bluetooth connecté exposant sa batterie.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    // Filet de sécurité au cas où une notification serait manquée.
    private static readonly TimeSpan SafetyRefreshInterval = TimeSpan.FromMinutes(5);
    // Un changement touche souvent plusieurs nœuds à la fois : on regroupe les notifications.
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WatcherRestartDelay = TimeSpan.FromSeconds(10);
    // Le niveau doit remonter de cette marge au-dessus du seuil avant qu'une nouvelle alerte soit possible,
    // pour éviter les alertes répétées quand le niveau oscille autour du seuil.
    private const int AlertHysteresis = 5;

    private readonly Settings _settings = Settings.Load();
    private readonly Dictionary<string, NotifyIcon> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _alerted = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _devicesMenu = new("Appareils affichés");
    private readonly ToolStripMenuItem _alertMenu = new();
    private readonly ToolStripMenuItem _startupMenu = new("Lancer au démarrage de Windows");
    private readonly System.Windows.Forms.Timer _safetyTimer = new();
    private readonly System.Windows.Forms.Timer _debounceTimer = new();
    private readonly System.Windows.Forms.Timer _restartTimer = new();
    private readonly SynchronizationContext _ui;
    private readonly DeviceWatcher _watcher;
    private List<BatteryDevice> _devices = [];
    private NotifyIcon? _placeholder;

    public TrayApp()
    {
        _ui = new WindowsFormsSynchronizationContext();

        _alertMenu.Text = $"Alerte batterie faible (≤ {_settings.LowBatteryThreshold} %)";
        _alertMenu.Click += (_, _) =>
        {
            _settings.LowBatteryAlert = !_settings.LowBatteryAlert;
            _settings.Save();
            _alerted.Clear();
            Refresh("option alerte");
        };
        _startupMenu.Click += (_, _) => ToggleStartup();

        // Le sous-menu « Appareils affichés » ne doit pas être vide pour afficher sa flèche.
        _devicesMenu.DropDownItems.Add("(aucun)");
        _menu.Opening += (_, _) => UpdateMenu();
        _menu.Items.Add(_devicesMenu);
        _menu.Items.Add(_alertMenu);
        _menu.Items.Add(_startupMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Actualiser", null, (_, _) => Refresh("menu"));
        _menu.Items.Add("Quitter", null, (_, _) => ExitThread());

        _safetyTimer.Interval = (int)SafetyRefreshInterval.TotalMilliseconds;
        _safetyTimer.Tick += (_, _) => Refresh("minuterie de secours");
        _safetyTimer.Start();

        _debounceTimer.Interval = (int)DebounceDelay.TotalMilliseconds;
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            Refresh("notification Windows");
        };

        _restartTimer.Interval = (int)WatcherRestartDelay.TotalMilliseconds;
        _restartTimer.Tick += (_, _) =>
        {
            _restartTimer.Stop();
            StartWatcher();
            Refresh("redémarrage de la surveillance");
        };

        _watcher = new DeviceWatcher(
            changed: () => _ui.Post(_ => OnDeviceChanged(), null),
            aborted: () => _ui.Post(_ => OnWatcherAborted(), null));
        StartWatcher();

        Refresh("démarrage");
    }

    private void StartWatcher()
    {
        try
        {
            _watcher.Start();
            Log.Write("Surveillance des périphériques active");
        }
        catch (Exception ex)
        {
            // Sans notifications, la minuterie de secours prend le relais.
            Log.Write($"Échec de la surveillance : {ex.Message}");
            _restartTimer.Start();
        }
    }

    private void OnDeviceChanged()
    {
        // (Re)démarre le délai : une rafale de notifications ne provoque qu'une seule lecture.
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnWatcherAborted()
    {
        Log.Write("Surveillance interrompue par Windows, redémarrage programmé");
        _watcher.Stop();
        _restartTimer.Start();
    }

    private void Refresh(string reason)
    {
        _devices = BatteryReader.ReadAll();
        var visible = _devices
            .Where(d => d.IsConnected && !_settings.HiddenDevices.Contains(d.InstanceId))
            .ToList();
        Log.Write($"Actualisation ({reason}) : " +
                  (visible.Count == 0 ? "aucun appareil affiché" : string.Join(", ", visible.Select(d => $"{d.Name} {d.Level} %"))));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in visible)
        {
            seen.Add(device.InstanceId);
            if (!_icons.TryGetValue(device.InstanceId, out var icon))
            {
                icon = CreateNotifyIcon();
                _icons[device.InstanceId] = icon;
            }
            Update(icon, device.Level, $"{device.Name} : {device.Level} %");
            CheckLowBattery(device, icon);
        }

        foreach (var gone in _icons.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Remove(_icons[gone]);
            _icons.Remove(gone);
        }

        // Sans aucune icône d'appareil, on garde une icône grise pour accéder au menu.
        if (visible.Count == 0)
        {
            _placeholder ??= CreateNotifyIcon();
            Update(_placeholder, null, "Aucun appareil Bluetooth connecté avec batterie");
        }
        else if (_placeholder is not null)
        {
            Remove(_placeholder);
            _placeholder = null;
        }
    }

    private void CheckLowBattery(BatteryDevice device, NotifyIcon icon)
    {
        var threshold = _settings.LowBatteryThreshold;
        if (device.Level > threshold + AlertHysteresis)
        {
            _alerted.Remove(device.InstanceId);
            return;
        }

        if (!_settings.LowBatteryAlert || device.Level > threshold || !_alerted.Add(device.InstanceId))
            return;

        Log.Write($"Alerte batterie faible : {device.Name} {device.Level} %");
        icon.ShowBalloonTip(10_000, "Batterie faible",
            $"{device.Name} : {device.Level} %. Pense à le recharger.", ToolTipIcon.Warning);
    }

    private void UpdateMenu()
    {
        _alertMenu.Checked = _settings.LowBatteryAlert;
        _startupMenu.Checked = StartupRegistration.IsEnabled;

        _devicesMenu.DropDownItems.Clear();
        if (_devices.Count == 0)
        {
            _devicesMenu.DropDownItems.Add(new ToolStripMenuItem("(aucun appareil)") { Enabled = false });
            return;
        }

        foreach (var device in _devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var label = device.IsConnected ? $"{device.Name} ({device.Level} %)" : $"{device.Name} (déconnecté)";
            var item = new ToolStripMenuItem(label) { Checked = !_settings.HiddenDevices.Contains(device.InstanceId) };
            var id = device.InstanceId;
            item.Click += (_, _) => ToggleDevice(id);
            _devicesMenu.DropDownItems.Add(item);
        }
    }

    private void ToggleDevice(string instanceId)
    {
        if (!_settings.HiddenDevices.Remove(instanceId))
            _settings.HiddenDevices.Add(instanceId);
        _settings.Save();
        Refresh("choix des appareils");
    }

    private void ToggleStartup()
    {
        try
        {
            StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled);
        }
        catch (Exception ex)
        {
            Log.Write($"Échec de la modification du lancement au démarrage : {ex.Message}");
            MessageBox.Show($"Impossible de modifier le lancement au démarrage :\n{ex.Message}",
                "BT Battery Tray", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private NotifyIcon CreateNotifyIcon() => new() { ContextMenuStrip = _menu };

    private static void Update(NotifyIcon notifyIcon, int? level, string tooltip)
    {
        var old = notifyIcon.Icon;
        notifyIcon.Icon = BatteryIcon.Create(level);
        notifyIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        notifyIcon.Visible = true;
        old?.Dispose();
    }

    private static void Remove(NotifyIcon notifyIcon)
    {
        notifyIcon.Visible = false;
        notifyIcon.Icon?.Dispose();
        notifyIcon.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher.Dispose();
            _safetyTimer.Dispose();
            _debounceTimer.Dispose();
            _restartTimer.Dispose();
            foreach (var icon in _icons.Values)
                Remove(icon);
            if (_placeholder is not null)
                Remove(_placeholder);
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
