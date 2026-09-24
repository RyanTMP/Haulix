using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Haulix.Core.Ets2;

/// <summary>
/// ETS2's display mode from config.cfg. In exclusive fullscreen Windows shows no other window on top of the
/// game, so HAULIX's notification overlay is invisible on a single monitor; borderless fullscreen looks the
/// same but lets overlays through. HAULIX can switch it (only while the game is closed, with a backup).
/// </summary>
public static partial class Ets2Display
{
    public sealed record DisplayInfo(string Mode, bool GameRunning, string? ConfigPath);

    [GeneratedRegex("""^\s*uset\s+(\w+)\s+"([^"]*)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex Line();

    public static bool GameRunning() => Process.GetProcessesByName("eurotrucks2").Length > 0;

    public static DisplayInfo Read(string? documentsPath)
    {
        var path = documentsPath is null ? null : Path.Combine(documentsPath, "config.cfg");
        if (path is null || !File.Exists(path)) return new DisplayInfo("unknown", GameRunning(), null);
        var vars = Line().Matches(File.ReadAllText(path)).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var full = vars.GetValueOrDefault("r_fullscreen") == "1";
        var borderless = vars.GetValueOrDefault("r_fullscreen_borderless") == "1";
        var mode = !full ? "windowed" : borderless ? "borderless" : "exclusive";
        return new DisplayInfo(mode, GameRunning(), path);
    }

    /// <summary>Sets borderless fullscreen. Returns an error message, or null on success.</summary>
    public static string? SetBorderless(string? documentsPath)
    {
        if (GameRunning()) return "Close ETS2 first: the game rewrites config.cfg when it exits.";
        var info = Read(documentsPath);
        if (info.ConfigPath is null) return "config.cfg was not found in the ETS2 documents folder.";
        var text = File.ReadAllText(info.ConfigPath);
        File.Copy(info.ConfigPath, info.ConfigPath + ".haulix-backup", true);
        text = SetVar(text, "r_fullscreen", "1");
        text = SetVar(text, "r_fullscreen_borderless", "1");
        File.WriteAllText(info.ConfigPath, text);
        return null;
    }

    private static string SetVar(string text, string name, string value)
    {
        var re = new Regex($"""^(\s*uset\s+{name}\s+)"[^"]*"(\s*)$""", RegexOptions.Multiline);
        return re.IsMatch(text) ? re.Replace(text, $"$1\"{value}\"$2", 1) : text.TrimEnd() + $"\nuset {name} \"{value}\"\n";
    }
}

/// <summary>Detects whether ETS2 currently runs with TruckersMP (its client module is loaded into the game).</summary>
public static class TruckersMp
{
    private static DateTime _checkedUtc;
    private static bool _active;

    public static bool Active()
    {
        if ((DateTime.UtcNow - _checkedUtc).TotalSeconds < 30) return _active;
        _checkedUtc = DateTime.UtcNow;
        _active = false;
        try
        {
            foreach (var p in Process.GetProcessesByName("eurotrucks2"))
                foreach (ProcessModule m in p.Modules)
                    if (m.ModuleName.StartsWith("core_ets2mp", StringComparison.OrdinalIgnoreCase)) { _active = true; return true; }
        }
        catch (Exception)
        {
            // Access denied or the game just exited: assume single player.
        }
        return _active;
    }
}
