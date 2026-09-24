using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Haulix.Core.Profiles;
using Haulix.Core.Sii;
using Microsoft.Win32;

namespace Haulix.Core.Ets2;

public sealed record ProfileInfo(
    string Id,
    string Name,
    string? CompanyName,
    string Path,
    string Kind,           // local | steam
    DateTime? LastSaveUtc,
    int SaveCount,
    long? Xp,
    long? DistanceKm);

public sealed record SaveInfo(string Name, string Path, DateTime SavedUtc, long SizeBytes);

public sealed record DetectionResult(
    string? GamePath,
    string? GameExe,
    string? DocumentsPath,
    IReadOnlyList<ProfileInfo> Profiles,
    bool PluginInstalled,
    string? PluginPath,
    IReadOnlyList<string> OtherTelemetryPlugins,
    string? SaveFormat,
    IReadOnlyList<string> Notes);

/// <summary>Finds the ETS2 installation, the documents folder, profiles, saves and the telemetry plugin.</summary>
public static partial class Ets2Locator
{
    private const string GameFolder = "Euro Truck Simulator 2";
    private const string SteamAppId = "227300";

    public static DetectionResult Detect(string? gamePathOverride = null, string? documentsOverride = null)
    {
        var notes = new List<string>();
        var game = ValidGamePath(gamePathOverride) ?? FindGamePath();
        if (game is null) notes.Add("ETS2 installation not found in any Steam library.");
        var docs = ValidDocumentsPath(documentsOverride) ?? FindDocumentsPath();
        if (docs is null) notes.Add("ETS2 documents folder (profiles and saves) not found.");

        var profiles = docs is null ? new List<ProfileInfo>() : ListProfiles(docs);
        string? pluginPath = null;
        var others = new List<string>();
        if (game is not null)
        {
            var pluginDir = Path.Combine(game, "bin", "win_x64", "plugins");
            if (Directory.Exists(pluginDir))
            {
                foreach (var f in Directory.GetFiles(pluginDir, "*.dll"))
                {
                    var name = Path.GetFileName(f);
                    if (name.Equals("scs-telemetry.dll", StringComparison.OrdinalIgnoreCase)) pluginPath = f;
                    else if (name.Contains("telemetry", StringComparison.OrdinalIgnoreCase)) others.Add(name);
                }
            }
            if (pluginPath is null) notes.Add("scs-telemetry.dll is not installed – live telemetry is unavailable.");
        }

        string? saveFormat = null;
        if (docs is not null)
        {
            var cfg = Path.Combine(docs, "config.cfg");
            if (File.Exists(cfg))
            {
                try
                {
                    var m = SaveFormatRegex().Match(File.ReadAllText(cfg));
                    if (m.Success) saveFormat = m.Groups[1].Value switch
                    {
                        "0" => "binary (encrypted)",
                        "2" => "text",
                        "3" => "binary",
                        var v => v,
                    };
                }
                catch (IOException) { }
            }
        }

        return new DetectionResult(game, game is null ? null : Path.Combine(game, "bin", "win_x64", "eurotrucks2.exe"),
            docs, profiles, pluginPath is not null, pluginPath, others, saveFormat, notes);
    }

    public static string? FindGamePath()
    {
        foreach (var lib in SteamLibraries())
        {
            var candidate = ValidGamePath(Path.Combine(lib, "steamapps", "common", GameFolder));
            if (candidate is not null) return candidate;
        }
        foreach (var root in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam", @"C:\Program Files\Euro Truck Simulator 2", @"C:\Games\Euro Truck Simulator 2" })
        {
            var candidate = ValidGamePath(root.EndsWith(GameFolder, StringComparison.Ordinal) ? root : Path.Combine(root, "steamapps", "common", GameFolder));
            if (candidate is not null) return candidate;
        }
        return null;
    }

    public static string? ValidGamePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return File.Exists(Path.Combine(path, "bin", "win_x64", "eurotrucks2.exe")) || File.Exists(Path.Combine(path, "base.scs"))
            ? Path.GetFullPath(path)
            : null;
    }

    public static string? FindDocumentsPath()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return ValidDocumentsPath(Path.Combine(docs, GameFolder));
    }

    public static string? ValidDocumentsPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return null;
        return Directory.Exists(Path.Combine(path, "profiles")) || Directory.Exists(Path.Combine(path, "steam_profiles"))
            ? Path.GetFullPath(path)
            : null;
    }

    public static List<ProfileInfo> ListProfiles(string documentsPath)
    {
        var list = new List<ProfileInfo>();
        foreach (var (sub, kind) in new[] { ("profiles", "local"), ("steam_profiles", "steam") })
        {
            var dir = Path.Combine(documentsPath, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var p in Directory.GetDirectories(dir))
            {
                var id = Path.GetFileName(p);
                if (!File.Exists(Path.Combine(p, "profile.sii"))) continue;
                var saves = ListSaves(p);
                string? company = null;
                long? xp = null, dist = null;
                try
                {
                    var doc = SiiDecoder.Load(Path.Combine(p, "profile.sii"));
                    var u = doc.First("user_profile");
                    company = u?.Str("company_name");
                    xp = u?.Long("cached_experience");
                    dist = u?.Long("cached_distance");
                }
                catch (Exception) { /* unreadable profile.sii: still list the profile */ }

                list.Add(new ProfileInfo(id, SaveParser.DecodeProfileName(id), company, p, kind,
                    saves.Count > 0 ? saves[0].SavedUtc : null, saves.Count, xp, dist));
            }
        }
        return list.OrderByDescending(p => p.LastSaveUtc ?? DateTime.MinValue).ToList();
    }

    public static List<SaveInfo> ListSaves(string profilePath)
    {
        var dir = Path.Combine(profilePath, "save");
        if (!Directory.Exists(dir)) return new List<SaveInfo>();
        return new DirectoryInfo(dir).EnumerateDirectories()
            .Select(d => new FileInfo(Path.Combine(d.FullName, "game.sii")))
            .Where(f => f.Exists)
            .Select(f => new SaveInfo(f.Directory!.Name, f.Directory.FullName, f.LastWriteTimeUtc, f.Length))
            .OrderByDescending(s => s.SavedUtc)
            .ToList();
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var steam = SteamRoot();
        if (steam is null) yield break;
        yield return steam;
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        string text;
        try { text = File.ReadAllText(vdf); }
        catch (IOException) { yield break; }
        foreach (Match m in VdfPathRegex().Matches(text))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    private static string? SteamRoot()
    {
        if (!OperatingSystem.IsWindows()) return null;
        return ReadSteamRegistry();
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadSteamRegistry()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                   ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        return path is null ? null : Path.GetFullPath(path.Replace('/', '\\'));
    }

    public static string SteamAppIdForGame => SteamAppId;

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex VdfPathRegex();

    [GeneratedRegex("uset\\s+g_save_format\\s+\"(\\d+)\"")]
    private static partial Regex SaveFormatRegex();
}
