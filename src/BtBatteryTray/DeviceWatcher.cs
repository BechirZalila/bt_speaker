using Windows.Devices.Enumeration;
using WinRtWatcher = Windows.Devices.Enumeration.DeviceWatcher;

namespace BtBatteryTray;

/// <summary>
/// Demande à Windows de nous prévenir quand la batterie ou l'état de connexion d'un périphérique
/// Bluetooth change, ou quand un périphérique Bluetooth apparaît/disparaît.
/// Aucune scrutation : le processus dort tant qu'il ne se passe rien.
/// </summary>
internal sealed class DeviceWatcher : IDisposable
{
    // Nœuds Bluetooth présents (classique et Low Energy).
    private const string Selector =
        "System.Devices.Present:=System.StructuredQueryType.Boolean#True AND " +
        "(System.Devices.DeviceInstanceId:~<\"BTHENUM\\\" OR System.Devices.DeviceInstanceId:~<\"BTHLE\\\")";

    // Propriétés surveillées : leur modification déclenche l'événement Updated.
    private static readonly string[] WatchedProperties =
    [
        "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2", // DEVPKEY_Bluetooth_Battery
        "{83DA6326-97A6-4088-9453-A1923F573B29} 15", // DEVPKEY_Bluetooth_Connected
    ];

    private readonly Action _changed;
    private readonly Action _aborted;
    private WinRtWatcher? _watcher;
    private volatile bool _enumerationCompleted;

    /// <param name="changed">Appelé (sur un thread du pool) à chaque changement.</param>
    /// <param name="aborted">Appelé si Windows interrompt la surveillance.</param>
    public DeviceWatcher(Action changed, Action aborted)
    {
        _changed = changed;
        _aborted = aborted;
    }

    public void Start()
    {
        if (_watcher is not null)
            return;

        _enumerationCompleted = false;
        var watcher = DeviceInformation.CreateWatcher(Selector, WatchedProperties, DeviceInformationKind.Device);
        watcher.Added += (_, e) => OnChanged("ajout", e.Id);
        watcher.Updated += (_, e) => OnChanged("modification", e.Id);
        watcher.Removed += (_, e) => OnChanged("retrait", e.Id);
        watcher.EnumerationCompleted += (_, _) => _enumerationCompleted = true;
        watcher.Stopped += (sender, _) =>
        {
            if (sender.Status == DeviceWatcherStatus.Aborted)
                _aborted();
        };
        _watcher = watcher;
        watcher.Start();
    }

    public void Stop()
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher is not null &&
            watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            watcher.Stop();
        }
    }

    public void Dispose() => Stop();

    private void OnChanged(string kind, string deviceId)
    {
        // Les « Added » de l'énumération initiale ne sont pas des changements.
        if (!_enumerationCompleted)
            return;
        Log.Write($"Notification ({kind}) : {deviceId}");
        _changed();
    }
}
