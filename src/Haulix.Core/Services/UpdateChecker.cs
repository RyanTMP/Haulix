using System.Text.Json;

namespace Haulix.Core.Services;

/// <summary>Result of an update check. <see cref="Available"/> is true when a newer version exists.</summary>
public sealed record UpdateInfo(bool Enabled, bool Available, string Current, string? Latest, string? Url, string? Notes, string? Error,
    string? SetupUrl = null, long SetupSize = 0);

/// <summary>
/// Update check without an own server: HAULIX asks the public GitHub API for the latest release of the HAULIX
/// repository (<see cref="Repository"/>) and compares its tag with the running version. The setup file attached
/// to the release can be downloaded and started by the desktop app. A custom manifest URL
/// ({ "version", "url", "notes" }) in Settings overrides the GitHub source.
/// </summary>
public static class UpdateChecker
{
    /// <summary>GitHub repository ("owner/name") whose releases are HAULIX updates.</summary>
    public const string Repository = "RyanTMP/Haulix";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("HAULIX-ETS2-Logger");   // required by the GitHub API
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Only files attached to a release of the HAULIX repository may be downloaded as an update.</summary>
    public static bool IsTrustedSetupUrl(string? url) =>
        !string.IsNullOrEmpty(Repository) && url is not null &&
        url.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase) &&
        url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    public static UpdateInfo Check(string? feedUrl, string current, bool enabled = true)
    {
        if (!enabled) return new UpdateInfo(false, false, current, null, null, null, null);
        if (!string.IsNullOrWhiteSpace(feedUrl)) return CheckManifest(feedUrl, current);
        if (string.IsNullOrEmpty(Repository)) return new UpdateInfo(false, false, current, null, null, null, null);
        try
        {
            var json = Http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            string? setup = null; long size = 0;
            if (r.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    setup = a.GetProperty("browser_download_url").GetString();
                    size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    break;
                }
            if (!IsTrustedSetupUrl(setup)) setup = null;
            var latest = tag.TrimStart('v', 'V');
            return new UpdateInfo(true, Compare(latest, current) > 0, current, latest,
                r.TryGetProperty("html_url", out var h) ? h.GetString() : null,
                r.TryGetProperty("body", out var b) ? b.GetString() : null, null, setup, size);
        }
        catch (Exception ex)
        {
            return new UpdateInfo(true, false, current, null, null, null, ex.Message);
        }
    }

    private static UpdateInfo CheckManifest(string feedUrl, string current)
    {
        if (!feedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new UpdateInfo(true, false, current, null, null, null, "The update URL must start with https://");
        try
        {
            using var doc = JsonDocument.Parse(Http.GetStringAsync(feedUrl).GetAwaiter().GetResult());
            var r = doc.RootElement;
            var latest = r.TryGetProperty("version", out var v) ? v.GetString() : null;
            var url = r.TryGetProperty("url", out var u) ? u.GetString() : null;
            var notes = r.TryGetProperty("notes", out var n) ? n.GetString() : null;
            if (url is not null && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) url = null;
            return new UpdateInfo(true, latest is not null && Compare(latest, current) > 0, current, latest, url, notes, null,
                IsTrustedSetupUrl(url) ? url : null);
        }
        catch (Exception ex)
        {
            return new UpdateInfo(true, false, current, null, null, null, ex.Message);
        }
    }

    /// <summary>Compares "1.2.3-beta" style versions; a release outranks a pre-release of the same number.</summary>
    public static int Compare(string a, string b)
    {
        static (Version V, string Pre) Parse(string s)
        {
            var parts = s.Trim().TrimStart('v', 'V').Split('-', 2);
            return (Version.TryParse(parts[0], out var v) ? v : new Version(0, 0), parts.Length > 1 ? parts[1].ToLowerInvariant() : "");
        }
        var (va, pa) = Parse(a);
        var (vb, pb) = Parse(b);
        var c = va.CompareTo(vb);
        if (c != 0) return c;
        if (pa == pb) return 0;
        if (pa == "") return 1;
        if (pb == "") return -1;
        return string.CompareOrdinal(pa, pb);
    }
}
