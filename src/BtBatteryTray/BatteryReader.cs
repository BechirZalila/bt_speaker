using System.Text;
using static BtBatteryTray.CfgMgr32;

namespace BtBatteryTray;

internal sealed record BatteryDevice(string Name, string InstanceId, int Level, bool IsConnected);

/// <summary>
/// Lit le niveau de batterie que Windows a déjà en cache pour les périphériques Bluetooth.
/// Aucune communication radio n'est déclenchée : on interroge uniquement le gestionnaire de périphériques.
/// </summary>
internal static class BatteryReader
{
    public static List<BatteryDevice> ReadAll()
    {
        var withBattery = new List<(uint DevInst, string InstanceId, int Level)>();
        var physicalDevices = new Dictionary<Guid, (string? Name, bool? Connected)>();

        foreach (var instanceId in GetPresentDeviceIds())
        {
            if (!instanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase))
                continue;
            if (CM_Locate_DevNode(out var devInst, instanceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
                continue;

            // Nœud représentant l'appareil physique (ex. "P9") : on retient son nom et son état de connexion.
            if (IsPhysicalDeviceNode(instanceId) &&
                GetGuidProperty(devInst, DEVPKEY_Device_ContainerId) is { } containerId)
            {
                physicalDevices[containerId] = (GetName(devInst), GetBoolProperty(devInst, DEVPKEY_Bluetooth_Connected));
            }

            if (GetByteProperty(devInst, DEVPKEY_Bluetooth_Battery) is { } level)
                withBattery.Add((devInst, instanceId, level));
        }

        // La batterie est souvent portée par un service (ex. "P9 Hands-Free AG") :
        // on utilise alors le nom et l'état de connexion de l'appareil physique du même conteneur.
        return withBattery
            .Select(d =>
            {
                var physical = GetGuidProperty(d.DevInst, DEVPKEY_Device_ContainerId) is { } c &&
                               physicalDevices.TryGetValue(c, out var p)
                    ? p
                    : default;
                var name = physical.Name ?? GetName(d.DevInst) ?? "Périphérique inconnu";
                // Propriété absente : on suppose l'appareil connecté plutôt que de le masquer à tort.
                return new BatteryDevice(name, d.InstanceId, d.Level, physical.Connected ?? true);
            })
            .ToList();
    }

    private static bool IsPhysicalDeviceNode(string instanceId) =>
        instanceId.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase) ||
        instanceId.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> GetPresentDeviceIds()
    {
        if (CM_Get_Device_ID_List_Size(out var length, null, CM_GETIDLIST_FILTER_PRESENT) != CR_SUCCESS)
            return [];

        var buffer = new char[length];
        if (CM_Get_Device_ID_List(null, buffer, length, CM_GETIDLIST_FILTER_PRESENT) != CR_SUCCESS)
            return [];

        // Liste de chaînes séparées par '\0', terminée par un double '\0'.
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? GetName(uint devInst) =>
        GetStringProperty(devInst, DEVPKEY_Device_FriendlyName) ??
        GetStringProperty(devInst, DEVPKEY_Device_DeviceDesc);

    private static byte[]? GetRawProperty(uint devInst, DevPropKey key, uint expectedType)
    {
        uint size = 0;
        var cr = CM_Get_DevNode_Property(devInst, key, out var type, null, ref size, 0);
        if (cr != CR_BUFFER_SMALL || type != expectedType || size == 0)
            return null;

        var buffer = new byte[size];
        if (CM_Get_DevNode_Property(devInst, key, out _, buffer, ref size, 0) != CR_SUCCESS)
            return null;
        return buffer;
    }

    private static int? GetByteProperty(uint devInst, DevPropKey key) =>
        GetRawProperty(devInst, key, DEVPROP_TYPE_BYTE) is [var b, ..] ? b : null;

    // DEVPROP_BOOLEAN : 0x00 = FALSE, 0xFF = TRUE.
    private static bool? GetBoolProperty(uint devInst, DevPropKey key) =>
        GetRawProperty(devInst, key, DEVPROP_TYPE_BOOLEAN) is [var b, ..] ? b != 0 : null;

    private static Guid? GetGuidProperty(uint devInst, DevPropKey key) =>
        GetRawProperty(devInst, key, DEVPROP_TYPE_GUID) is { Length: 16 } raw ? new Guid(raw) : null;

    private static string? GetStringProperty(uint devInst, DevPropKey key)
    {
        var raw = GetRawProperty(devInst, key, DEVPROP_TYPE_STRING);
        if (raw is null)
            return null;
        var text = Encoding.Unicode.GetString(raw).TrimEnd('\0');
        return text.Length > 0 ? text : null;
    }
}
