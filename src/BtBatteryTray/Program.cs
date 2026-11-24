using System.Runtime.InteropServices;

namespace BtBatteryTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--list"))
            return ListToConsole(args.Contains("--verbose"));

        if (args.Contains("--log"))
            Log.Enable();

        using var mutex = new Mutex(true, @"Local\BtBatteryTray", out var isFirstInstance);
        if (!isFirstInstance)
            return 0;

        ApplicationConfiguration.Initialize();
        using var app = new TrayApp();
        Application.Run(app);
        return 0;
    }

    // Mode texte (hérité de l'étape 1) : affiche les niveaux dans le terminal appelant.
    private static int ListToConsole(bool verbose)
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        var devices = BatteryReader.ReadAll();
        if (devices.Count == 0)
        {
            output.WriteLine("Aucun périphérique Bluetooth ne communique son niveau de batterie.");
            return 1;
        }

        foreach (var device in devices)
        {
            output.WriteLine($"{device.Name} : {device.Level} %{(device.IsConnected ? "" : " (déconnecté)")}");
            if (verbose)
                output.WriteLine($"    {device.InstanceId}");
        }
        return 0;
    }

    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
