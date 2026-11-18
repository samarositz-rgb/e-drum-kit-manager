using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TD12KitManager;

/// <summary>A newer version found on the project's GitHub releases page.</summary>
public sealed record UpdateInfo(Version Version, string DownloadUrl, long Size, string PageUrl);

/// <summary>
/// Checks the latest GitHub release for a newer installer. One release carries both platforms, so the version
/// is read from the file names: EDrumKitManager-Setup-1.3.0.exe (Windows) and EDrumKitManager-1.0.0.dmg (Mac).
/// </summary>
public static class Updates
{
    const string LatestRelease = "https://api.github.com/repos/samarositz-rgb/e-drum-kit-manager/releases/latest";
    public const string ReleasesPage = "https://github.com/samarositz-rgb/e-drum-kit-manager/releases/latest";

    static readonly string SettingsFile = Path.Combine(KitStore.AppDir, "updates.txt");

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EDrumKitManager", "1"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>The newer version for this platform, or null when this one is current.</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestRelease));
        var root = doc.RootElement;
        string page = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
        var pattern = OperatingSystem.IsMacOS()
            ? new Regex(@"^EDrumKitManager-(\d+\.\d+\.\d+)\.dmg$")
            : new Regex(@"^EDrumKitManager-Setup-(\d+\.\d+\.\d+)\.exe$");
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var m = pattern.Match(asset.GetProperty("name").GetString() ?? "");
            if (!m.Success) continue;
            var version = Version.Parse(m.Groups[1].Value);
            if (version <= new Version(current.Major, current.Minor, Math.Max(current.Build, 0))) return null;
            return new UpdateInfo(version, asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("size").GetInt64(), page);
        }
        return null;
    }

    /// <summary>Downloads the installer to the temp folder and checks its size.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(update.DownloadUrl).LocalPath));
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EDrumKitManager", "1"));
            using var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(path);
            await response.Content.CopyToAsync(file);
        }
        if (new FileInfo(path).Length != update.Size)
        {
            File.Delete(path);
            throw new IOException("The download was incomplete. Please try again.");
        }
        return path;
    }

    /// <summary>Whether to check when the program starts (on unless turned off in the Help menu).</summary>
    public static bool CheckAtStartup
    {
        get => Read("startup") != "off";
        set => Write("startup", value ? "on" : "off");
    }

    /// <summary>A version the user chose not to install; the startup check stays quiet about it.</summary>
    public static string SkippedVersion
    {
        get => Read("skip") ?? "";
        set => Write("skip", value);
    }

    static string? Read(string key)
    {
        try
        {
            if (!File.Exists(SettingsFile)) return null;
            return File.ReadAllLines(SettingsFile).Select(l => l.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == key)?[1];
        }
        catch (IOException) { return null; }
    }

    static void Write(string key, string value)
    {
        var lines = File.Exists(SettingsFile) ? File.ReadAllLines(SettingsFile).Where(l => !l.StartsWith(key + "=")).ToList() : [];
        lines.Add($"{key}={value}");
        Directory.CreateDirectory(KitStore.AppDir);
        File.WriteAllLines(SettingsFile, lines);
    }
}
