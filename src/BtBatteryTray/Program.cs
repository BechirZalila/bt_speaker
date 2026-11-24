using BtBatteryTray;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var devices = BatteryReader.ReadAll();

if (devices.Count == 0)
{
    Console.WriteLine("Aucun périphérique Bluetooth ne communique son niveau de batterie.");
    return 1;
}

foreach (var device in devices)
{
    Console.WriteLine($"{device.Name} : {device.Level} %");
    if (args.Contains("--verbose"))
        Console.WriteLine($"    {device.InstanceId}");
}

return 0;
