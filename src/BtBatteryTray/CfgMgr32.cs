using System.Runtime.InteropServices;

namespace BtBatteryTray;

[StructLayout(LayoutKind.Sequential)]
internal struct DevPropKey
{
    public Guid FmtId;
    public uint Pid;

    public DevPropKey(string fmtId, uint pid)
    {
        FmtId = new Guid(fmtId);
        Pid = pid;
    }
}

internal static class CfgMgr32
{
    public const uint CR_SUCCESS = 0x00;
    public const uint CR_BUFFER_SMALL = 0x1A;
    public const uint CM_GETIDLIST_FILTER_PRESENT = 0x100;
    public const uint CM_LOCATE_DEVNODE_NORMAL = 0x0;

    public const uint DEVPROP_TYPE_BYTE = 0x03;
    public const uint DEVPROP_TYPE_GUID = 0x0D;
    public const uint DEVPROP_TYPE_BOOLEAN = 0x11;
    public const uint DEVPROP_TYPE_STRING = 0x12;

    // Niveau de batterie Bluetooth (en %), renseigné par Windows à partir des infos envoyées par le périphérique.
    public static readonly DevPropKey DEVPKEY_Bluetooth_Battery = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
    // Clé non documentée : vaut TRUE sur le nœud de l'appareil physique quand il est connecté.
    public static readonly DevPropKey DEVPKEY_Bluetooth_Connected = new("83DA6326-97A6-4088-9453-A1923F573B29", 15);
    public static readonly DevPropKey DEVPKEY_Device_FriendlyName = new("A45C254E-DF1C-4EFD-8020-67D146A850E0", 14);
    public static readonly DevPropKey DEVPKEY_Device_DeviceDesc = new("A45C254E-DF1C-4EFD-8020-67D146A850E0", 2);
    public static readonly DevPropKey DEVPKEY_Device_ContainerId = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C", 2);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW", CharSet = CharSet.Unicode)]
    public static extern uint CM_Get_Device_ID_List_Size(out uint length, string? filter, uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW", CharSet = CharSet.Unicode)]
    public static extern uint CM_Get_Device_ID_List(string? filter, [Out] char[] buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode)]
    public static extern uint CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW", CharSet = CharSet.Unicode)]
    public static extern uint CM_Get_DevNode_Property(
        uint devInst, in DevPropKey propertyKey, out uint propertyType,
        [Out] byte[]? buffer, ref uint bufferSize, uint flags);
}
