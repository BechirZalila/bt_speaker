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

    private readonly Dictionary<string, NotifyIcon> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _safetyTimer = new();
    private readonly System.Windows.Forms.Timer _debounceTimer = new();
    private readonly System.Windows.Forms.Timer _restartTimer = new();
    private readonly SynchronizationContext _ui;
    private readonly DeviceWatcher _watcher;
    private NotifyIcon? _placeholder;

    public TrayApp()
    {
        _ui = new WindowsFormsSynchronizationContext();

        _menu.Items.Add("Actualiser", null, (_, _) => Refresh("menu"));
        _menu.Items.Add(new ToolStripSeparator());
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
        var devices = BatteryReader.ReadAll().Where(d => d.IsConnected).ToList();
        Log.Write($"Actualisation ({reason}) : " +
                  (devices.Count == 0 ? "aucun appareil" : string.Join(", ", devices.Select(d => $"{d.Name} {d.Level} %"))));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices)
        {
            seen.Add(device.InstanceId);
            if (!_icons.TryGetValue(device.InstanceId, out var icon))
            {
                icon = CreateNotifyIcon();
                _icons[device.InstanceId] = icon;
            }
            Update(icon, device.Level, $"{device.Name} : {device.Level} %");
        }

        foreach (var gone in _icons.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Remove(_icons[gone]);
            _icons.Remove(gone);
        }

        // Sans aucun appareil, on garde une icône grise pour pouvoir quitter l'application.
        if (devices.Count == 0)
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
