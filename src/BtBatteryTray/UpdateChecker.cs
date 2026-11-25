using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace BtBatteryTray;

/// <summary>Nouvelle version publiée sur GitHub, avec son assistant d'installation.</summary>
internal sealed record AvailableUpdate(
    Version Version, string PageUrl, string Notes, string InstallerUrl, string InstallerName, long Size, string? Sha256);

/// <summary>
/// Recherche des mises à jour dans les releases GitHub du projet et installation de l'assistant.
/// Une seule requête HTTPS par recherche ; aucune donnée personnelle n'est envoyée.
/// </summary>
internal static class UpdateChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/BechirZalila/bt_speaker/releases/latest";
    private const string InstallerPrefix = "BtBatteryTray-Setup-";
    // Clé de désinstallation écrite par Inno Setup (AppId de installer/BtBatteryTray.iss + « _is1 »).
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{EF2B34AB-95CE-4C01-A66F-E4D1058FA9C5}_is1";

    public static Version CurrentVersion { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version!);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"BtBatteryTray/{CurrentVersion}");
        return client;
    }

    /// <summary>
    /// Vrai si l'exe en cours est celui installé par l'assistant : on peut alors lancer la mise à jour.
    /// Sinon (install.ps1, dotnet run), on se contente d'ouvrir la page de la release.
    /// </summary>
    public static bool IsInstalledBySetup
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation") is not string location || Environment.ProcessPath is null)
                return false;
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath)!;
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),
                Path.TrimEndingDirectorySeparator(exeDir), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Renvoie la dernière release si elle est plus récente que la version en cours, sinon null.</summary>
    public static async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<Release>(cancellationToken)
                      ?? throw new InvalidDataException("Réponse GitHub vide.");

        if (!Version.TryParse(release.TagName.TrimStart('v', 'V'), out var latest))
            throw new InvalidDataException($"Numéro de version illisible : {release.TagName}");
        latest = Normalize(latest);
        if (latest <= CurrentVersion)
            return null;

        var asset = release.Assets.FirstOrDefault(a =>
            a.Name.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (asset is null)
            throw new InvalidDataException($"La release {release.TagName} ne contient pas d'assistant d'installation.");

        string? sha256 = asset.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest["sha256:".Length..]
            : null;
        return new AvailableUpdate(latest, release.HtmlUrl, release.Body ?? "", asset.DownloadUrl, asset.Name, asset.Size, sha256);
    }

    /// <summary>Télécharge l'assistant dans %TEMP% et vérifie sa taille et son empreinte SHA-256.</summary>
    public static async Task<string> DownloadAsync(AvailableUpdate update, IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BtBatteryTray-Update");
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(update.InstallerName));

        using (var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(path);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                if (update.Size > 0)
                    progress?.Report((int)(total * 100 / update.Size));
            }
        }

        var size = new FileInfo(path).Length;
        if (update.Size > 0 && size != update.Size)
            throw new InvalidDataException($"Taille inattendue ({size} octets au lieu de {update.Size}).");

        if (update.Sha256 is not null)
        {
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!string.Equals(hash, update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                stream.Close();
                File.Delete(path);
                throw new InvalidDataException("L'empreinte SHA-256 du fichier téléchargé ne correspond pas.");
            }
        }
        return path;
    }

    /// <summary>
    /// Lance l'assistant en mode silencieux (fenêtre de progression seule). Avec /UPDATE=1, il ferme
    /// l'application, laisse le lancement au démarrage tel quel et relance l'application à la fin.
    /// </summary>
    public static void StartInstaller(string installerPath) =>
        Process.Start(new ProcessStartInfo(installerPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1")
        {
            UseShellExecute = true,
        });

    public static void OpenPage(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // « 1.0.0 » et « 1.0.0.0 » doivent être égales.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private sealed class Release
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public List<Asset> Assets { get; set; } = [];
    }

    private sealed class Asset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
        [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
    }
}
