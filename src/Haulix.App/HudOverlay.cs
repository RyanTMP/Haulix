using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Haulix.Core;
using Haulix.Core.Settings;
using Haulix.Core.Telemetry;

namespace Haulix.App;

/// <summary>
/// Optional in-game HUD (Settings → In-game HUD): a bar of live values over the game – speed limit, speed,
/// remaining distance, real-time ETA, arrival clock, game ETA, deadline buffer, fuel range, rest, damage,
/// game time. Position (preset or custom), size, visibility and fields are configurable. Click-through, never
/// takes focus, hidden while HAULIX itself is in front (except during the positioning preview).
/// </summary>
internal sealed class HudOverlay : Form
{
    private const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private readonly HaulixEngine _engine;
    private readonly Func<bool> _appInFront;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 400 };
    private Font? _big, _small, _sign;
    private float _fontScale;
    private List<Cell> _cells = new();
    private DateTime _previewUntil;
    private Screen? _screen;
    private DateTime _screenCheckedUtc;

    private sealed record Cell(string Key, string Value, string Label, Color Color);

    public HudOverlay(HaulixEngine engine, Func<bool> appInFront)
    {
        _engine = engine;
        _appInFront = appInFront;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(14, 16, 19);
        DoubleBuffered = true;
        _timer.Tick += (_, _) => Update2();
        _timer.Start();
    }

    /// <summary>Shows the HUD for a few seconds (with sample values when not driving) to check its position.</summary>
    public void Preview(int seconds = 10) => _previewUntil = DateTime.UtcNow.AddSeconds(seconds);

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    private void Update2()
    {
        var settings = _engine.Settings.Load();
        var hud = settings.Hud;
        var preview = DateTime.UtcNow < _previewUntil;
        var s = _engine.LastSnapshot;
        var live = s is not null && (DateTime.UtcNow - s.CapturedUtc).TotalSeconds < 5;
        var show = preview || (settings.General.Hud && live && !_appInFront() && (!hud.OnlyOnJob || s!.OnJob));
        if (!show)
        {
            if (Visible) Hide();
            return;
        }

        _cells = BuildCells(live ? s! : Sample(), hud, settings.General.Units == "imperial", _engine.Notifier.German);
        if (_cells.Count == 0) { if (Visible) Hide(); return; }

        var screen = GameScreen();
        var dpi = screen.Scale();
        var sizeFactor = hud.Size switch { "small" => 0.8f, "large" => 1.3f, _ => 1f };
        EnsureFonts(sizeFactor);
        var h = (int)(48 * dpi * sizeFactor);
        var cellW = (int)(104 * dpi * sizeFactor);
        var signW = _cells[0].Key == "speedLimit" ? h : 0;
        var width = (int)(12 * dpi * sizeFactor) * 2 + signW + (_cells.Count - (signW > 0 ? 1 : 0)) * cellW;
        var size = new Size(width, h);
        var area = screen.Bounds;
        var margin = (int)(12 * dpi);
        var loc = hud.Position switch
        {
            "topLeft" => new Point(area.Left + margin, area.Top + margin),
            "topRight" => new Point(area.Right - margin - size.Width, area.Top + margin),
            "bottomCenter" => new Point(area.Left + (area.Width - size.Width) / 2, area.Bottom - margin - size.Height),
            "bottomLeft" => new Point(area.Left + margin, area.Bottom - margin - size.Height),
            "bottomRight" => new Point(area.Right - margin - size.Width, area.Bottom - margin - size.Height),
            "custom" => new Point(
                area.Left + (int)Math.Clamp(area.Width * hud.X / 100.0 - size.Width / 2.0, 0, area.Width - size.Width),
                area.Top + (int)Math.Clamp(area.Height * hud.Y / 100.0 - size.Height / 2.0, 0, area.Height - size.Height)),
            _ => new Point(area.Left + (area.Width - size.Width) / 2, area.Top + margin),
        };
        if (Size != size)
        {
            Size = size;
            using var path = Rounded(new Rectangle(0, 0, Width, Height), (int)(8 * dpi * sizeFactor));
            Region = new Region(path);
        }
        if (Location != loc) Location = loc;
        var opacity = Math.Clamp(hud.Opacity, 20, 100) / 100.0;
        if (Math.Abs(Opacity - opacity) > 0.005) Opacity = opacity;
        if (!Visible)
        {
            Show();
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x2 | 0x1 | 0x10 | 0x40);
        }
        Invalidate();
    }

    private static readonly Color Ink = Color.FromArgb(236, 237, 238), Accent = Color.FromArgb(255, 176, 32),
        Ok = Color.FromArgb(61, 214, 140), Warn = Color.FromArgb(255, 138, 61), Crit = Color.FromArgb(240, 71, 79);

    private static List<Cell> BuildCells(TelemetrySnapshot s, HudSettings hud, bool imperial, bool de)
    {
        string Dist(double km) => imperial ? $"{km * 0.621371:0} mi" : km < 10 ? $"{km:0.0} km" : $"{km:0} km";
        string Speed(double kmh) => imperial ? $"{kmh * 0.621371:0}" : $"{kmh:0}";
        string Dur(double seconds)
        {
            var m = (int)Math.Round(seconds / 60);
            return m < 60 ? $"{Math.Max(1, m)} min" : $"{m / 60} h {m % 60:00}";
        }
        string L(string en, string ger) => de ? ger : en;
        var eta = s.Eta;
        var cells = new List<Cell>();
        foreach (var f in hud.Fields.Distinct())
        {
            switch (f)
            {
                case "speedLimit":
                    cells.Insert(0, new Cell(f, s.SpeedLimitKmh > 1 ? Speed(s.SpeedLimitKmh) : "–", "", Ink)); // sign always first
                    break;
                case "speed":
                    cells.Add(new Cell(f, Speed(Math.Abs(s.SpeedKmh)), imperial ? "MPH" : "KM/H",
                        s.SpeedLimitKmh > 1 && s.SpeedKmh > s.SpeedLimitKmh + 5 ? Crit : Ink));
                    break;
                case "remaining":
                    cells.Add(new Cell(f, eta is not null ? Dist(eta.RemainingKm) : "—", L("REMAINING", "VERBLEIBEND"), Ink));
                    break;
                case "etaReal":
                    cells.Add(new Cell(f, eta is not null ? Dur(eta.RealSeconds) : "—", L("REAL-TIME ETA", "ECHTZEIT-ETA"), Accent));
                    break;
                case "arrival":
                    cells.Add(new Cell(f, eta is not null ? eta.ArrivalUtc.ToLocalTime().ToString("HH:mm") : "—", L("ARRIVAL", "ANKUNFT"), Ink));
                    break;
                case "etaGame":
                    cells.Add(new Cell(f, eta is not null ? Dur(eta.GameSeconds) : "—", L("GAME ETA", "SPIEL-ETA"), Ink));
                    break;
                case "deadline":
                {
                    var m = eta?.DeadlineMarginGameMinutes;
                    cells.Add(new Cell(f, m is null ? "—" : m < 0 ? $"-{Dur(-m.Value * 60)}" : Dur(m.Value * 60),
                        m < 0 ? L("LATE", "ZU SPÄT") : L("BUFFER", "PUFFER"), m is null ? Ink : m < 0 ? Crit : m < 60 ? Warn : Ok));
                    break;
                }
                case "fuelRange":
                    cells.Add(new Cell(f, s.FuelRangeKm > 0 ? Dist(s.FuelRangeKm) : "—", L("FUEL RANGE", "REICHWEITE"),
                        eta is not null && s.FuelRangeKm > 0 && s.FuelRangeKm < eta.RemainingKm ? Warn : Ink));
                    break;
                case "rest":
                    cells.Add(new Cell(f, s.RestStopMinutes > 0 ? Dur(s.RestStopMinutes * 60) : "—", L("NEXT REST", "NÄCHSTE PAUSE"),
                        s.RestStopMinutes is > 0 and < 60 ? Warn : Ink));
                    break;
                case "damage":
                    cells.Add(new Cell(f, $"{s.TruckDamage * 100:0}%", L("TRUCK WEAR", "LKW-VERSCHLEISS"), s.TruckDamage >= 0.3 ? Crit : s.TruckDamage >= 0.15 ? Warn : Ink));
                    break;
                case "gameTime":
                {
                    var t = s.GameTimeMinutes % 1440;
                    cells.Add(new Cell(f, $"{t / 60:00}:{t % 60:00}", L("GAME TIME", "SPIELZEIT"), Ink));
                    break;
                }
            }
        }
        return cells;
    }

    /// <summary>Plausible values for the positioning preview when the game is not running.</summary>
    private static TelemetrySnapshot Sample() => new()
    {
        SpeedKmh = 78, SpeedLimitKmh = 80, FuelRangeKm = 640, RestStopMinutes = 310, GameTimeMinutes = 8 * 1440 + 14 * 60 + 35, OnJob = true,
        Eta = new Haulix.Core.Services.EtaInfo("game", 214, 10_200, 540, 19, DateTime.UtcNow.AddSeconds(540), 0, 95, true),
    };

    private void EnsureFonts(float factor)
    {
        if (_big is not null && Math.Abs(_fontScale - factor) < 0.01f) return;
        _big?.Dispose(); _small?.Dispose(); _sign?.Dispose();
        _fontScale = factor;
        _big = new Font("Segoe UI Semibold", 12.5f * factor);
        _small = new Font("Segoe UI", 7.5f * factor);
        _sign = new Font("Segoe UI", 9.5f * factor, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_big is null || _cells.Count == 0) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var h = Height;
        var pad = (int)(h * 0.25);
        using (var border = new Pen(Color.FromArgb(52, 58, 68)))
        using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), h / 6))
            g.DrawPath(border, path);
        // Slanted amber accent (HAULIX signature)
        using (var b = new SolidBrush(Accent))
            g.FillPolygon(b, new[] { new Point(pad / 2 + 4, pad), new Point(pad / 2 + 7, pad), new Point(pad / 2 + 3, h - pad), new Point(pad / 2, h - pad) });

        var x = pad;
        var start = 0;
        if (_cells[0].Key == "speedLimit")
        {
            var d = h - (int)(h * 0.3);
            var sign = new Rectangle(x + 4, (h - d) / 2, d, d);
            using (var red = new Pen(Crit, Math.Max(2, d / 9f))) { g.FillEllipse(Brushes.White, sign); g.DrawEllipse(red, sign); }
            TextRenderer.DrawText(g, _cells[0].Value, _sign, sign, Color.FromArgb(18, 20, 24), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x = sign.Right + pad / 2;
            start = 1;
        }
        var remaining = _cells.Count - start;
        if (remaining == 0) return;
        var cellW = (Width - x - pad / 2) / remaining;
        for (var i = start; i < _cells.Count; i++)
        {
            var c = _cells[i];
            var top = (int)(h * 0.12);
            TextRenderer.DrawText(g, c.Value, _big, new Rectangle(x, top, cellW, (int)(h * 0.52)), c.Color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, c.Label, _small!, new Rectangle(x, top + (int)(h * 0.52), cellW, (int)(h * 0.3)), Color.FromArgb(125, 133, 143), TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            x += cellW;
        }
    }

    private Screen GameScreen()
    {
        if (_screen is not null && (DateTime.UtcNow - _screenCheckedUtc).TotalSeconds < 5) return _screen;
        _screenCheckedUtc = DateTime.UtcNow;
        _screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        try
        {
            foreach (var p in Process.GetProcessesByName("eurotrucks2"))
                if (p.MainWindowHandle != IntPtr.Zero) { _screen = Screen.FromHandle(p.MainWindowHandle); break; }
        }
        catch (Exception) { }
        return _screen;
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        var dd = Math.Max(2, radius * 2);
        p.AddArc(r.X, r.Y, dd, dd, 180, 90);
        p.AddArc(r.Right - dd, r.Y, dd, dd, 270, 90);
        p.AddArc(r.Right - dd, r.Bottom - dd, dd, dd, 0, 90);
        p.AddArc(r.X, r.Bottom - dd, dd, dd, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _big?.Dispose(); _small?.Dispose(); _sign?.Dispose(); }
        base.Dispose(disposing);
    }
}
