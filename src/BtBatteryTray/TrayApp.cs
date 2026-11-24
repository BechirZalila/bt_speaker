namespace BtBatteryTray;

/// <summary>
/// Application sans fenêtre : une icône par appareil Bluetooth exposant sa batterie.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    // Provisoire (étape 2) : sera remplacé à l'étape 3 par les notifications de Windows.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, NotifyIcon> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private NotifyIcon? _placeholder;

    public TrayApp()
    {
        _menu.Items.Add("Actualiser", null, (_, _) => Refresh());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Quitter", null, (_, _) => ExitThread());

        _timer.Interval = (int)RefreshInterval.TotalMilliseconds;
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Refresh();
    }

    private void Refresh()
    {
        var devices = BatteryReader.ReadAll();
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
            Update(_placeholder, null, "Aucun appareil Bluetooth avec batterie");
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
            _timer.Dispose();
            foreach (var icon in _icons.Values)
                Remove(icon);
            if (_placeholder is not null)
                Remove(_placeholder);
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
