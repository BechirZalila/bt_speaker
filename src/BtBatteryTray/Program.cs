using System.Runtime.InteropServices;

namespace BtBatteryTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--list"))
            return ListToConsole(args.Contains("--verbose"));

        if (args.Contains("--check-update"))
            return CheckUpdateToConsole(args.Contains("--download"));

        // Affiche seulement la fenêtre « À propos » (utile pour la vérifier sans passer par le menu).
        if (args.Contains("--about"))
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new AboutForm());
            return 0;
        }

        if (args.Contains("--log"))
            Log.Enable();

        using var mutex = new Mutex(true, @"Local\BtBatteryTray", out var isFirstInstance);
        if (!isFirstInstance)
            return 0;

        ApplicationConfiguration.Initialize();
        // --updated : relance par l'assistant après une mise à jour intégrée (confirmation par une bulle).
        // --update-now : recherche une mise à jour dès le démarrage (comme « Rechercher maintenant »).
        using var app = new TrayApp(justUpdated: args.Contains("--updated"), checkUpdateNow: args.Contains("--update-now"));
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

    // Diagnostic de la recherche de mise à jour ; --download télécharge et vérifie l'assistant sans l'installer.
    private static int CheckUpdateToConsole(bool download)
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        try
        {
            output.WriteLine($"Version installée : {UpdateChecker.CurrentVersion}" +
                             (UpdateChecker.IsInstalledBySetup ? " (assistant d'installation)" : " (hors assistant)"));
            var update = UpdateChecker.CheckAsync().GetAwaiter().GetResult();
            if (update is null)
            {
                output.WriteLine("À jour.");
                return 0;
            }
            output.WriteLine($"Version {update.Version} disponible : {update.InstallerName} ({update.Size} octets)");
            output.WriteLine($"    SHA-256 : {update.Sha256 ?? "(non fourni)"}");
            if (download)
                output.WriteLine($"Téléchargé et vérifié : {UpdateChecker.DownloadAsync(update, null).GetAwaiter().GetResult()}");
            return 0;
        }
        catch (Exception ex)
        {
            output.WriteLine($"Échec : {ex.Message}");
            return 1;
        }
    }

    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
