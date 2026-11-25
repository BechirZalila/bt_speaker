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
    // Première recherche de mise à jour peu après le démarrage (laisse la session s'ouvrir), puis une par jour.
    private static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    private readonly Settings _settings = Settings.Load();
    private readonly Dictionary<string, NotifyIcon> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _alerted = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _devicesMenu = new("Appareils affichés");
    private readonly ToolStripMenuItem _alertMenu = new();
    private readonly ToolStripMenuItem _startupMenu = new("Lancer au démarrage de Windows");
    private readonly ToolStripMenuItem _installUpdateMenu = new() { Visible = false };
    private readonly ToolStripSeparator _installUpdateSeparator = new() { Visible = false };
    private readonly ToolStripMenuItem _updatesMenu = new("Mises à jour");
    private readonly ToolStripMenuItem _checkUpdateMenu = new("Rechercher maintenant");
    private readonly ToolStripMenuItem _autoUpdateMenu = new("Rechercher automatiquement (une fois par jour)");
    private readonly System.Windows.Forms.Timer _safetyTimer = new();
    private readonly System.Windows.Forms.Timer _debounceTimer = new();
    private readonly System.Windows.Forms.Timer _restartTimer = new();
    private readonly System.Windows.Forms.Timer _updateTimer = new();
    private readonly SynchronizationContext _ui;
    private readonly DeviceWatcher _watcher;
    private List<BatteryDevice> _devices = [];
    private NotifyIcon? _placeholder;
    private AboutForm? _aboutForm;
    private AvailableUpdate? _update;
    private Version? _notifiedVersion;
    private bool _updateBusy;
    // Vrai quand la bulle affichée annonce une mise à jour : un clic dessus lance l'installation.
    private bool _balloonIsUpdate;

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

        _installUpdateMenu.Font = new Font(_installUpdateMenu.Font, FontStyle.Bold);
        _installUpdateMenu.Click += async (_, _) => await InstallUpdateAsync();
        _checkUpdateMenu.Click += async (_, _) => await CheckForUpdateAsync(manual: true);
        _autoUpdateMenu.Click += (_, _) => ToggleAutoUpdate();
        _updatesMenu.DropDownItems.Add(_checkUpdateMenu);
        _updatesMenu.DropDownItems.Add(_autoUpdateMenu);
        _updatesMenu.DropDownItems.Add(new ToolStripSeparator());
        _updatesMenu.DropDownItems.Add(new ToolStripMenuItem($"Version installée : {UpdateChecker.CurrentVersion}") { Enabled = false });

        // Le sous-menu « Appareils affichés » ne doit pas être vide pour afficher sa flèche.
        _devicesMenu.DropDownItems.Add("(aucun)");
        _menu.Opening += (_, _) => UpdateMenu();
        _menu.Items.Add(_installUpdateMenu);
        _menu.Items.Add(_installUpdateSeparator);
        _menu.Items.Add(_devicesMenu);
        _menu.Items.Add(_alertMenu);
        _menu.Items.Add(_startupMenu);
        _menu.Items.Add(_updatesMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Actualiser", null, (_, _) => Refresh("menu"));
        _menu.Items.Add("À propos…", null, (_, _) => ShowAbout());
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

        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = (int)UpdateCheckInterval.TotalMilliseconds;
            await CheckForUpdateAsync(manual: false);
        };
        if (_settings.CheckForUpdates)
            StartUpdateTimer();

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
        ShowBalloon(icon, "Batterie faible", $"{device.Name} : {device.Level} %. Pense à le recharger.",
            ToolTipIcon.Warning, isUpdate: false);
    }

    private void ShowBalloon(NotifyIcon? icon, string title, string text, ToolTipIcon kind, bool isUpdate)
    {
        icon ??= _icons.Values.FirstOrDefault() ?? _placeholder;
        if (icon is null)
            return;
        _balloonIsUpdate = isUpdate;
        icon.ShowBalloonTip(10_000, title, text, kind);
    }

    private void StartUpdateTimer()
    {
        _updateTimer.Stop();
        _updateTimer.Interval = (int)FirstUpdateCheckDelay.TotalMilliseconds;
        _updateTimer.Start();
    }

    private void ToggleAutoUpdate()
    {
        _settings.CheckForUpdates = !_settings.CheckForUpdates;
        _settings.Save();
        if (_settings.CheckForUpdates)
            StartUpdateTimer();
        else
            _updateTimer.Stop();
    }

    private static bool IsUpdateError(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or InvalidDataException or System.Text.Json.JsonException
            or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception;

    /// <summary>
    /// Recherche automatique : annonce une nouvelle version par une bulle (une fois par version).
    /// Recherche manuelle : propose directement l'installation, ou indique que l'application est à jour.
    /// </summary>
    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_updateBusy)
            return;
        _updateBusy = true;
        AvailableUpdate? update;
        try
        {
            update = await UpdateChecker.CheckAsync();
        }
        catch (Exception ex) when (IsUpdateError(ex))
        {
            Log.Write($"Échec de la recherche de mise à jour : {ex.Message}");
            if (manual)
                MessageBox.Show($"Impossible de rechercher les mises à jour :\n{ex.Message}",
                    "BT Battery Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        finally
        {
            _updateBusy = false;
        }

        _update = update;
        UpdateInstallMenuItem();
        Log.Write(update is null
            ? $"Recherche de mise à jour : version {UpdateChecker.CurrentVersion} à jour"
            : $"Recherche de mise à jour : version {update.Version} disponible");

        if (update is null)
        {
            if (manual)
                ShowBalloon(null, "BT Battery Tray est à jour", $"Version {UpdateChecker.CurrentVersion}.",
                    ToolTipIcon.Info, isUpdate: false);
        }
        else if (manual)
        {
            await InstallUpdateAsync();
        }
        else if (update.Version != _notifiedVersion)
        {
            _notifiedVersion = update.Version;
            ShowBalloon(null, "Mise à jour disponible",
                $"La version {update.Version} de BT Battery Tray est disponible. Clique ici pour l'installer.",
                ToolTipIcon.Info, isUpdate: true);
        }
    }

    private void UpdateInstallMenuItem()
    {
        _installUpdateMenu.Visible = _installUpdateSeparator.Visible = _update is not null;
        if (_update is not null)
            _installUpdateMenu.Text = UpdateChecker.IsInstalledBySetup
                ? $"Installer la version {_update.Version}…"
                : $"Version {_update.Version} disponible…";
    }

    /// <summary>
    /// Demande confirmation, télécharge l'assistant, le lance en mode silencieux puis quitte :
    /// l'assistant remplace l'exe et relance l'application.
    /// </summary>
    private async Task InstallUpdateAsync()
    {
        if (_update is not { } update || _updateBusy)
            return;

        var versions = $"La version {update.Version} de BT Battery Tray est disponible " +
                       $"(version installée : {UpdateChecker.CurrentVersion}).";
        if (!UpdateChecker.IsInstalledBySetup)
        {
            // Copie de développement (install.ps1, dotnet run) : l'assistant l'installerait ailleurs.
            if (MessageBox.Show($"{versions}\n\nCette copie n'a pas été installée par l'assistant d'installation. " +
                                "Ouvrir la page de téléchargement ?", "Mise à jour disponible",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                UpdateChecker.OpenPage(update.PageUrl);
            return;
        }

        var notes = update.Notes.Trim();
        if (notes.Length > 500)
            notes = notes[..500].TrimEnd() + "…";
        if (notes.Length > 0)
            notes = $"\n\nNouveautés :\n{notes}";
        if (MessageBox.Show($"{versions}{notes}\n\nL'installer maintenant ? L'application sera fermée puis relancée.",
                "Mise à jour disponible", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            return;

        _updateBusy = true;
        _installUpdateMenu.Enabled = false;
        try
        {
            var progress = new Progress<int>(p => _installUpdateMenu.Text = $"Téléchargement de la mise à jour… {p} %");
            var installer = await UpdateChecker.DownloadAsync(update, progress);
            Log.Write($"Mise à jour {update.Version} téléchargée et vérifiée, lancement de {installer}");
            UpdateChecker.StartInstaller(installer);
            ExitThread();
        }
        catch (Exception ex) when (IsUpdateError(ex))
        {
            Log.Write($"Échec de la mise à jour : {ex.Message}");
            MessageBox.Show($"La mise à jour a échoué :\n{ex.Message}", "BT Battery Tray",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _installUpdateMenu.Enabled = true;
            UpdateInstallMenuItem();
        }
        finally
        {
            _updateBusy = false;
        }
    }

    private void UpdateMenu()
    {
        _alertMenu.Checked = _settings.LowBatteryAlert;
        _startupMenu.Checked = StartupRegistration.IsEnabled;
        _autoUpdateMenu.Checked = _settings.CheckForUpdates;
        _checkUpdateMenu.Enabled = !_updateBusy;

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

    // Une seule fenêtre « À propos » à la fois : un second clic la ramène au premier plan.
    private void ShowAbout()
    {
        if (_aboutForm is { IsDisposed: false })
        {
            _aboutForm.Activate();
            return;
        }
        _aboutForm = new AboutForm();
        _aboutForm.FormClosed += (_, _) =>
        {
            _aboutForm?.Dispose();
            _aboutForm = null;
        };
        _aboutForm.Show();
        _aboutForm.Activate();
    }

    private NotifyIcon CreateNotifyIcon()
    {
        var icon = new NotifyIcon { ContextMenuStrip = _menu };
        icon.BalloonTipClicked += async (_, _) =>
        {
            if (!_balloonIsUpdate)
                return;
            _balloonIsUpdate = false;
            await InstallUpdateAsync();
        };
        icon.BalloonTipClosed += (_, _) => _balloonIsUpdate = false;
        return icon;
    }

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
            _updateTimer.Dispose();
            foreach (var icon in _icons.Values)
                Remove(icon);
            if (_placeholder is not null)
                Remove(_placeholder);
            _aboutForm?.Dispose();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
