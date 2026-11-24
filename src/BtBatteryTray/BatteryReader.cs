using System.Text;
using static BtBatteryTray.CfgMgr32;

namespace BtBatteryTray;

internal sealed record BatteryDevice(string Name, string InstanceId, int Level);

/// <summary>
/// Lit le niveau de batterie que Windows a déjà en cache pour les périphériques Bluetooth.
/// Aucune communication radio n'est déclenchée : on interroge uniquement le gestionnaire de périphériques.
/// </summary>
internal static class BatteryReader
{
    public static List<BatteryDevice> ReadAll()
    {
        var withBattery = new List<(uint DevInst, string InstanceId, int Level)>();
        var deviceNames = new Dictionary<Guid, string>();

        foreach (var instanceId in GetPresentDeviceIds())
        {
            if (!instanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase))
                continue;
            if (CM_Locate_DevNode(out var devInst, instanceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
                continue;

            // Nœud représentant l'appareil physique (ex. "P9") : on retient son nom par conteneur.
            if (IsPhysicalDeviceNode(instanceId) &&
                GetGuidProperty(devInst, DEVPKEY_Device_ContainerId) is { } containerId &&
                GetName(devInst) is { } name)
            {
                deviceNames[containerId] = name;
            }

            if (GetByteProperty(devInst, DEVPKEY_Bluetooth_Battery) is { } level)
                withBattery.Add((devInst, instanceId, level));
        }

        // La batterie est souvent portée par un service (ex. "P9 Hands-Free AG") :
        // on affiche alors le nom de l'appareil physique du même conteneur.
        return withBattery
            .Select(d =>
            {
                var name = GetGuidProperty(d.DevInst, DEVPKEY_Device_ContainerId) is { } c &&
                           deviceNames.TryGetValue(c, out var n)
                    ? n
                    : GetName(d.DevInst) ?? "Périphérique inconnu";
                return new BatteryDevice(name, d.InstanceId, d.Level);
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
