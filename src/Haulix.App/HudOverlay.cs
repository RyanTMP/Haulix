using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Haulix.Core;
using Haulix.Core.Settings;
using Haulix.Core.Telemetry;

namespace Haulix.App;

/// <summary>
/// In-game HUD (Settings → In-game HUD), in the style of VTC trackers like SpedV: two widgets over the game –
/// a job card (cargo, route, progress, remaining distance, real-time ETA, deadline, speed, …) and a mini map with
/// the roads around the truck and the route. Each widget has its own corner/custom position and size; both are
/// click-through, never take focus and hide while HAULIX itself is in front (except during the preview).
/// </summary>
internal sealed class HudOverlay : IDisposable
{
    private readonly HaulixEngine _engine;
    private readonly Func<bool> _appInFront;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly JobCard _card = new();
    private readonly MiniMap _map;
    private DateTime _previewUntil;
    private Screen? _screen;
    private DateTime _screenCheckedUtc;

    public HudOverlay(HaulixEngine engine, Func<bool> appInFront)
    {
        _engine = engine;
        _appInFront = appInFront;
        _map = new MiniMap(engine);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>Shows both widgets for a few seconds (sample values when not driving) to check their position.</summary>
    public void Preview(int seconds = 10) => _previewUntil = DateTime.UtcNow.AddSeconds(seconds);

    private void Tick()
    {
        var settings = _engine.Settings.Load();
        var hud = settings.Hud;
        var preview = DateTime.UtcNow < _previewUntil;
        var s = _engine.LastSnapshot;
        var live = s is not null && (DateTime.UtcNow - s.CapturedUtc).TotalSeconds < 5;
        var show = preview || (settings.General.Hud && live && !_appInFront() && (!hud.OnlyOnJob || s!.OnJob));
        var snap = live ? s! : Sample();
        var screen = GameScreen();
        var de = _engine.Notifier.German;
        var imperial = settings.General.Units == "imperial";
        var opacity = Math.Clamp(hud.Opacity, 20, 100) / 100.0;

        if (show && hud.CardEnabled) _card.Update(snap, hud, screen, opacity, de, imperial);
        else _card.HideWidget();
        if (show && hud.MapEnabled) _map.Update(snap, hud, screen, opacity, live, imperial);
        else _map.HideWidget();
    }

    /// <summary>Plausible values for the positioning preview when the game is not running.</summary>
    private static TelemetrySnapshot Sample() => new()
    {
        OnJob = true, Cargo = "Steel coils", CargoMassKg = 22_400, SourceCity = "Hamburg", DestinationCity = "Prague",
        DestinationCompany = "Posped", PlannedDistanceKm = 640, SpeedKmh = 78, SpeedLimitKmh = 80, FuelRangeKm = 640,
        RestStopMinutes = 310, GameTimeMinutes = 8 * 1440 + 14 * 60 + 35, CargoDamage = 0.012,
        Eta = new Haulix.Core.Services.EtaInfo("game", 214, 10_200, 540, 19, DateTime.UtcNow.AddSeconds(540), 0, 95, true),
    };

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

    public void Dispose()
    {
        _timer.Dispose();
        _card.Dispose();
        _map.Dispose();
    }

    /* ================================================================ shared widget base */

    internal abstract class Widget : Form
    {
        private const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
        private static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        protected static readonly Color Bg = Color.FromArgb(15, 17, 21), Border = Color.FromArgb(48, 54, 64), Ink = Color.FromArgb(236, 237, 238),
            Muted = Color.FromArgb(128, 136, 147), Accent = Color.FromArgb(255, 176, 32), Ok = Color.FromArgb(61, 214, 140),
            Warn = Color.FromArgb(255, 138, 61), Crit = Color.FromArgb(240, 71, 79);

        protected float Ui = 1; // display scale × widget size

        protected Widget()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Bg;
            DoubleBuffered = true;
        }

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

        public void HideWidget() { if (Visible) Hide(); }

        /// <summary>Applies size, corner/custom position and opacity, then repaints.</summary>
        protected void Place(Screen screen, Size size, string position, double px, double py, double opacity, int radius)
        {
            var area = screen.WorkingArea;
            var m = (int)(16 * screen.Scale());
            var loc = position switch
            {
                "topLeft" => new Point(area.Left + m, area.Top + m),
                "bottomLeft" => new Point(area.Left + m, area.Bottom - m - size.Height),
                "bottomRight" => new Point(area.Right - m - size.Width, area.Bottom - m - size.Height),
                "custom" => new Point(
                    area.Left + (int)Math.Clamp(area.Width * px / 100.0 - size.Width / 2.0, 0, Math.Max(0, area.Width - size.Width)),
                    area.Top + (int)Math.Clamp(area.Height * py / 100.0 - size.Height / 2.0, 0, Math.Max(0, area.Height - size.Height))),
                _ => new Point(area.Right - m - size.Width, area.Top + m), // topRight (also old topCenter etc.)
            };
            if (Size != size)
            {
                Size = size;
                using var path = Rounded(new Rectangle(0, 0, Width, Height), radius);
                Region = new Region(path);
            }
            if (Location != loc) Location = loc;
            if (Math.Abs(Opacity - opacity) > 0.005) Opacity = opacity;
            if (!Visible)
            {
                Show();
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x2 | 0x1 | 0x10 | 0x40);
            }
            Invalidate();
        }

        protected static float SizeFactor(string size) => size switch { "small" => 0.82f, "large" => 1.25f, _ => 1f };

        protected static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            var d = Math.Max(2, radius * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected void DrawFrame(Graphics g, int radius)
        {
            using var pen = new Pen(Border);
            using var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), radius);
            g.DrawPath(pen, path);
        }
    }

    /* ================================================================ job card */

    private sealed class JobCard : Widget
    {
        private sealed record Row(string Label, string Value, Color Color);

        private string _kicker = "", _cargo = "", _route = "";
        private double _progress = -1;
        private List<Row> _rows = new();
        private Font? _kickerFont, _cargoFont, _routeFont, _labelFont, _valueFont;
        private float _fontFactor;

        public void Update(TelemetrySnapshot s, HudSettings hud, Screen screen, double opacity, bool de, bool imperial)
        {
            var f = SizeFactor(hud.Size);
            Ui = screen.Scale() * f;
            EnsureFonts(f);
            string L(string en, string ger) => de ? ger : en;
            string Dist(double km) => imperial ? $"{km * 0.621371:0} mi" : km < 10 ? $"{km:0.0} km" : $"{km:0} km";
            string Spd(double kmh) => imperial ? $"{kmh * 0.621371:0} mph" : $"{kmh:0} km/h";
            string Dur(double sec) { var m = (int)Math.Round(sec / 60); return m < 60 ? $"{Math.Max(1, m)} min" : $"{m / 60} h {m % 60:00} min"; }

            var eta = s.Eta;
            var job = s.OnJob && !string.IsNullOrEmpty(s.DestinationCity);
            _kicker = job ? L("CURRENT JOB", "AKTUELLER AUFTRAG") : L("FREE ROAM", "FREIE FAHRT");
            _cargo = job ? $"{s.Cargo}{(s.CargoMassKg > 0 ? $" · {s.CargoMassKg / 1000:0.#} t" : "")}" : $"{s.TruckBrand} {s.TruckName}".Trim();
            _route = job ? $"{s.SourceCity} → {s.DestinationCity}" : L("No active job", "Kein aktiver Auftrag");
            _progress = job && s.PlannedDistanceKm > 0 && eta is not null ? Math.Clamp(1 - eta.RemainingKm / s.PlannedDistanceKm, 0, 1) : -1;

            var rows = new List<Row>();
            foreach (var key in hud.Fields.Distinct())
            {
                switch (key)
                {
                    case "remaining" when job: rows.Add(new(L("Remaining", "Verbleibend"), eta is not null ? Dist(eta.RemainingKm) : "—", Ink)); break;
                    case "etaReal" when job: rows.Add(new(L("Real-time ETA", "Echtzeit-ETA"), eta is not null ? Dur(eta.RealSeconds) : "—", Accent)); break;
                    case "arrival" when job: rows.Add(new(L("Arrival", "Ankunft"), eta is not null ? eta.ArrivalUtc.ToLocalTime().ToString("HH:mm") : "—", Ink)); break;
                    case "etaGame" when job: rows.Add(new(L("Game ETA", "Spiel-ETA"), eta is not null ? Dur(eta.GameSeconds) : "—", Ink)); break;
                    case "deadline" when job:
                    {
                        var m = eta?.DeadlineMarginGameMinutes;
                        rows.Add(new(m < 0 ? L("Late by", "Verspätung") : L("Deadline buffer", "Fristpuffer"),
                            m is null ? "—" : Dur(Math.Abs(m.Value) * 60), m is null ? Ink : m < 0 ? Crit : m < 60 ? Warn : Ok));
                        break;
                    }
                    case "speed":
                        rows.Add(new(L("Speed", "Geschwindigkeit"), Spd(Math.Abs(s.SpeedKmh)) + (s.SpeedLimitKmh > 1 ? $"  ·  {L("limit", "Limit")} {(imperial ? s.SpeedLimitKmh * 0.621371 : s.SpeedLimitKmh):0}" : ""),
                            s.SpeedLimitKmh > 1 && s.SpeedKmh > s.SpeedLimitKmh + 5 ? Crit : Ink));
                        break;
                    case "speedLimit": rows.Add(new(L("Speed limit", "Tempolimit"), s.SpeedLimitKmh > 1 ? Spd(s.SpeedLimitKmh) : "—", Ink)); break;
                    case "fuelRange":
                        rows.Add(new(L("Fuel range", "Reichweite"), s.FuelRangeKm > 0 ? Dist(s.FuelRangeKm) : "—",
                            job && eta is not null && s.FuelRangeKm > 0 && s.FuelRangeKm < eta.RemainingKm ? Warn : Ink));
                        break;
                    case "rest": rows.Add(new(L("Next rest", "Nächste Pause"), s.RestStopMinutes > 0 ? Dur(s.RestStopMinutes * 60) : "—", s.RestStopMinutes is > 0 and < 60 ? Warn : Ink)); break;
                    case "damage":
                        rows.Add(new(job ? L("Cargo damage", "Frachtschaden") : L("Truck wear", "Lkw-Verschleiß"),
                            $"{(job ? s.CargoDamage : s.TruckDamage) * 100:0.#} %", (job ? s.CargoDamage : s.TruckDamage) >= 0.05 ? Warn : Ink));
                        break;
                    case "gameTime": { var t = s.GameTimeMinutes % 1440; rows.Add(new(L("Game time", "Spielzeit"), $"{t / 60:00}:{t % 60:00}", Ink)); break; }
                }
            }
            _rows = rows;

            var w = (int)(290 * Ui);
            var h = (int)((78 + (_progress >= 0 ? 22 : 0) + _rows.Count * 22 + 12) * Ui);
            Place(screen, new Size(w, h), hud.Position, hud.X, hud.Y, opacity, (int)(10 * Ui));
        }

        private void EnsureFonts(float f)
        {
            if (_valueFont is not null && Math.Abs(_fontFactor - f) < 0.01f) return;
            foreach (var x in new[] { _kickerFont, _cargoFont, _routeFont, _labelFont, _valueFont }) x?.Dispose();
            _fontFactor = f;
            _kickerFont = new Font("Segoe UI Semibold", 7f * f);
            _cargoFont = new Font("Segoe UI", 8.5f * f);
            _routeFont = new Font("Segoe UI Semibold", 12.5f * f);
            _labelFont = new Font("Segoe UI", 8.5f * f);
            _valueFont = new Font("Segoe UI Semibold", 9f * f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_valueFont is null) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawFrame(g, (int)(10 * Ui));
            int P(float v) => (int)(v * Ui);
            // Slanted amber accent (HAULIX signature) + kicker
            using (var b = new SolidBrush(Accent))
                g.FillPolygon(b, new[] { new Point(P(16), P(14)), new Point(P(19), P(14)), new Point(P(16), P(26)), new Point(P(13), P(26)) });
            TextRenderer.DrawText(g, _kicker, _kickerFont, new Point(P(24), P(13)), Accent, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "HAULIX", _kickerFont, new Rectangle(0, P(13), Width - P(14), P(14)), Muted, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _cargo, _cargoFont, new Rectangle(P(14), P(32), Width - P(28), P(16)), Muted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _route, _routeFont, new Rectangle(P(14), P(48), Width - P(28), P(24)), Ink, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            var y = P(78);
            if (_progress >= 0)
            {
                var bar = new Rectangle(P(14), y + P(4), Width - P(28) - P(40), P(6));
                using (var track = new SolidBrush(Color.FromArgb(38, 43, 51))) g.FillRectangle(track, bar);
                using (var fill = new SolidBrush(Accent)) g.FillRectangle(fill, bar.X, bar.Y, (int)(bar.Width * _progress), bar.Height);
                TextRenderer.DrawText(g, $"{_progress * 100:0} %", _labelFont, new Rectangle(bar.Right, y, Width - bar.Right - P(14), P(16)), Muted, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                y += P(22);
            }
            using var sep = new Pen(Color.FromArgb(32, 36, 43));
            foreach (var r in _rows)
            {
                g.DrawLine(sep, P(14), y, Width - P(14), y);
                TextRenderer.DrawText(g, r.Label, _labelFont, new Rectangle(P(14), y + P(4), Width / 2, P(16)), Muted, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, r.Value, _valueFont, new Rectangle(Width / 3, y + P(3), Width * 2 / 3 - P(14), P(18)), r.Color, TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                y += P(22);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var x in new[] { _kickerFont, _cargoFont, _routeFont, _labelFont, _valueFont }) x?.Dispose();
            base.Dispose(disposing);
        }
    }

    /* ================================================================ mini map */

    private sealed class MiniMap : Widget
    {
        private readonly HaulixEngine _engine;
        private List<(float[] Points, int Class)> _roads = new();
        private float _roadsX = float.NaN, _roadsZ = float.NaN, _roadsRadius;
        private float _x, _z, _heading, _radius;
        private bool _rotate, _live;
        private float[]? _route;
        private string _footer = "";
        private Font? _small;

        public MiniMap(HaulixEngine engine) => _engine = engine;

        public void Update(TelemetrySnapshot s, HudSettings hud, Screen screen, double opacity, bool live, bool imperial)
        {
            Ui = screen.Scale() * SizeFactor(hud.MapSize);
            _small ??= new Font("Segoe UI Semibold", 8f);
            _live = live;
            _x = (float)s.X; _z = (float)s.Z;
            _heading = (float)s.HeadingDeg;
            _rotate = hud.MapRotate;
            _radius = Math.Clamp(hud.MapZoom, 1, 3) switch { 1 => 700f, 3 => 3500f, _ => 1600f };
            if (live)
            {
                // Refresh the road query when the truck moved a good part of the view or the zoom changed.
                var moved = float.IsNaN(_roadsX) || Math.Abs(_x - _roadsX) > _radius * 0.35f || Math.Abs(_z - _roadsZ) > _radius * 0.35f;
                if (moved || Math.Abs(_roadsRadius - _radius) > 1)
                {
                    _roads = _engine.Map.RoadsNear(_x, _z, _radius * 1.8f);
                    _roadsX = _x; _roadsZ = _z; _roadsRadius = _radius;
                }
                _route = _engine.Map.Route is { Error: null, Points.Length: >= 4 } r ? r.Points : null;
            }
            else { _roads = new(); _route = null; }
            var eta = s.Eta;
            _footer = eta is not null
                ? $"{(imperial ? eta.RemainingKm * 0.621371 : eta.RemainingKm):0} {(imperial ? "mi" : "km")}  ·  {Minutes(eta.RealSeconds)}"
                : "";
            var side = (int)(220 * Ui);
            Place(screen, new Size(side, side), hud.MapPosition, hud.MapX, hud.MapY, opacity, (int)(14 * Ui));
        }

        private static string Minutes(double sec) { var m = (int)Math.Round(sec / 60); return m < 60 ? $"{Math.Max(1, m)} min" : $"{m / 60} h {m % 60:00}"; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var cx = Width / 2f;
            var cy = Height * 0.58f;              // truck a bit below centre: more road ahead is visible
            var k = Width / (2f * _radius);       // px per world metre
            var h = (_rotate ? _heading : 0) * MathF.PI / 180f;
            var cos = MathF.Cos(h); var sin = MathF.Sin(h);
            PointF P(float wx, float wz)
            {
                float dx = wx - _x, dz = wz - _z;
                return new PointF(cx + (cos * dx + sin * dz) * k, cy + (-sin * dx + cos * dz) * k);
            }

            if (!_live)
            {
                // Preview without the game: a stylised crossing so the widget is visible.
                using var pen = new Pen(Color.FromArgb(67, 72, 82), 3 * Ui);
                g.DrawLine(pen, cx, 0, cx, Height); g.DrawLine(pen, 0, cy, Width, cy - 20 * Ui);
                using var route = new Pen(Accent, 3.5f * Ui) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(route, cx, cy, cx, 12 * Ui);
            }
            else
            {
                var widths = new[] { 3.2f, 2.3f, 1.6f, 1.4f };
                var colors = new[] { Color.FromArgb(78, 84, 95), Color.FromArgb(60, 65, 75), Color.FromArgb(44, 48, 56), Color.FromArgb(44, 48, 56) };
                for (var c = 3; c >= 0; c--)
                {
                    using var pen = new Pen(colors[c], widths[c] * Ui) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                    foreach (var (pts, cls) in _roads)
                    {
                        if (Math.Min(cls, 3) != c) continue;
                        var poly = new PointF[pts.Length / 2];
                        for (var i = 0; i < poly.Length; i++) poly[i] = P(pts[i * 2], pts[i * 2 + 1]);
                        if (poly.Length >= 2) g.DrawLines(pen, poly);
                    }
                }
                if (_route is { } rp)
                {
                    var poly = new List<PointF>();
                    var reach = _radius * 2.2f;
                    for (var i = 0; i < rp.Length / 2; i++)
                    {
                        if (Math.Abs(rp[i * 2] - _x) > reach || Math.Abs(rp[i * 2 + 1] - _z) > reach) { if (poly.Count >= 2) DrawRoute(g, poly); poly.Clear(); continue; }
                        poly.Add(P(rp[i * 2], rp[i * 2 + 1]));
                    }
                    if (poly.Count >= 2) DrawRoute(g, poly);
                }
            }

            // Truck arrow
            var a = _rotate ? 0 : _heading * MathF.PI / 180f;
            PointF R(float px, float py) => new(cx + px * MathF.Cos(a) - py * MathF.Sin(a), cy + px * MathF.Sin(a) + py * MathF.Cos(a));
            var s = 9 * Ui;
            var arrow = new[] { R(0, -s), R(s * 0.72f, s * 0.8f), R(0, s * 0.35f), R(-s * 0.72f, s * 0.8f) };
            using (var halo = new SolidBrush(Color.FromArgb(90, Accent))) g.FillEllipse(halo, cx - s * 1.6f, cy - s * 1.6f, s * 3.2f, s * 3.2f);
            using (var fill = new SolidBrush(Accent)) g.FillPolygon(fill, arrow);
            using (var edge = new Pen(Color.FromArgb(20, 22, 26), 1.5f)) g.DrawPolygon(edge, arrow);

            // North marker + footer (remaining · ETA)
            if (_rotate)
            {
                var n = new PointF(cx + MathF.Sin(-h) * (Width / 2f - 14 * Ui), Height / 2f - MathF.Cos(-h) * (Height / 2f - 14 * Ui));
                TextRenderer.DrawText(g, "N", _small, new Rectangle((int)(n.X - 8 * Ui), (int)(n.Y - 8 * Ui), (int)(16 * Ui), (int)(16 * Ui)), Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (_footer.Length > 0)
            {
                var fh = (int)(22 * Ui);
                using var shade = new SolidBrush(Color.FromArgb(210, 15, 17, 21));
                g.FillRectangle(shade, 0, Height - fh, Width, fh);
                TextRenderer.DrawText(g, _footer, _small, new Rectangle(0, Height - fh, Width, fh), Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            DrawFrame(g, (int)(14 * Ui));
        }

        private void DrawRoute(Graphics g, List<PointF> poly)
        {
            using var casing = new Pen(Color.FromArgb(15, 17, 21), 6.5f * Ui) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var line = new Pen(Accent, 3.6f * Ui) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            var arr = poly.ToArray();
            g.DrawLines(casing, arr);
            g.DrawLines(line, arr);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _small?.Dispose();
            base.Dispose(disposing);
        }
    }
}
