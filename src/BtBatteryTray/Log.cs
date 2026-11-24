namespace BtBatteryTray;

/// <summary>
/// Journal de diagnostic, désactivé par défaut. Activé par l'option --log :
/// écrit dans %TEMP%\BtBatteryTray.log.
/// </summary>
internal static class Log
{
    private static readonly object Sync = new();
    private static string? _path;

    public static string? Path => _path;

    public static void Enable() =>
        _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BtBatteryTray.log");

    public static void Write(string message)
    {
        if (_path is null)
            return;
        lock (Sync)
            File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
    }
}
