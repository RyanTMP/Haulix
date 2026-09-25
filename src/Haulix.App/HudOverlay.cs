using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using Haulix.Core;
using Haulix.Core.Settings;
using Haulix.Core.Telemetry;

namespace Haulix.App;

/// <summary>
/// In-game HUD (Settings → In-game HUD), in the style of VTC trackers like SpedV: a job card over the game with
/// cargo, route, progress and the rows the player picks (remaining distance, real-time ETA, deadline, speed, …).
/// Theme, accent, size, width, density, sections and position are configurable; "Place on screen" lets the
/// player drag the card anywhere. The card is click-through, never takes focus and hides while HAULIX itself is
/// in front (except during the preview and while placing it).
/// </summary>
internal sealed class HudOverlay : IDisposable
{
    private readonly HaulixEngine _engine;
    private readonly Func<bool> _appInFront;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly JobCard _card;
    private DateTime _previewUntil;
    private Screen? _screen;
    private DateTime _screenCheckedUtc;

    public HudOverlay(HaulixEngine engine, Func<bool> appInFront)
    {
        _engine = engine;
        _appInFront = appInFront;
        _card = new JobCard(SavePlacement);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>Shows the card for a few seconds (sample values when not driving) to check its position.</summary>
    public void Preview(int seconds = 10) => _previewUntil = DateTime.UtcNow.AddSeconds(seconds);

    /// <summary>Makes the card draggable on the game monitor: drag it, then double-click (or wait) to save.</summary>
    public void BeginPlacement()
    {
        _card.BeginMove();
        Tick();
    }

    public bool Placing => _card.Moving;

    private void SavePlacement(Point center, Screen screen)
    {
        var area = screen.WorkingArea;
        var x = Math.Round(Math.Clamp((center.X - area.Left) * 100.0 / area.Width, 0, 100), 1);
        var y = Math.Round(Math.Clamp((center.Y - area.Top) * 100.0 / area.Height, 0, 100), 1);
        _engine.UpdateSettings(s => { s.Hud.Position = "custom"; s.Hud.X = x; s.Hud.Y = y; });
    }

    private void Tick()
    {
        var settings = _engine.Settings.Load();
        var hud = settings.Hud;
        var preview = DateTime.UtcNow < _previewUntil || _card.Moving;
        var s = _engine.LastSnapshot;
        var live = s is not null && (DateTime.UtcNow - s.CapturedUtc).TotalSeconds < 5;
        var show = preview || (settings.General.Hud && hud.CardEnabled && live && !_appInFront() && (!hud.OnlyOnJob || s!.OnJob));
        var snap = live ? s! : Sample();
        // Layered window: at exactly 1.0 WinForms never sets the layer attributes and the window stays invisible.
        var opacity = Math.Min(0.99, _card.Moving ? 1.0 : Math.Clamp(hud.Opacity, 20, 100) / 100.0);

        if (show) _card.Update(snap, hud, settings.Appearance.Accent, GameScreen(), opacity, _engine.Notifier.German, settings.General.Units == "imperial");
        else _card.HideWidget();
    }

    /// <summary>Plausible values for the positioning preview when the game is not running.</summary>
    private static TelemetrySnapshot Sample() => new()
    {
        OnJob = true, Cargo = "Steel coils", CargoMassKg = 22_400, SourceCity = "Hamburg", DestinationCity = "Prague",
        SourceCompany = "Eurogoodies", DestinationCompany = "Posped", PlannedDistanceKm = 640, JobIncome = 14_820, SpeedKmh = 78,
        SpeedLimitKmh = 80, CruiseControl = true, CruiseControlKmh = 80, Gear = 11, GearDashboard = 11, FuelRangeKm = 640,
        FuelLitres = 412, FuelCapacity = 800, RestStopMinutes = 310, GameTimeMinutes = 8 * 1440 + 14 * 60 + 35,
        CargoDamage = 0.012, WearEngine = 0.03, WearWheels = 0.05,
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
    }

    /* ================================================================ look */

    private sealed record Palette(Color Bg, Color Border, Color Ink, Color Muted, Color Track, Color Sep);

    private static Palette Theme(string theme) => theme switch
    {
        "light" => new(Color.FromArgb(246, 247, 249), Color.FromArgb(214, 218, 224), Color.FromArgb(22, 25, 30), Color.FromArgb(98, 106, 117), Color.FromArgb(222, 226, 231), Color.FromArgb(230, 233, 237)),
        "glass" => new(Color.FromArgb(28, 33, 41), Color.FromArgb(70, 80, 95), Color.FromArgb(244, 246, 248), Color.FromArgb(160, 170, 184), Color.FromArgb(52, 60, 72), Color.FromArgb(44, 51, 62)),
        "contrast" => new(Color.Black, Color.FromArgb(120, 120, 120), Color.White, Color.FromArgb(200, 200, 200), Color.FromArgb(60, 60, 60), Color.FromArgb(50, 50, 50)),
        _ => new(Color.FromArgb(15, 17, 21), Color.FromArgb(48, 54, 64), Color.FromArgb(236, 237, 238), Color.FromArgb(128, 136, 147), Color.FromArgb(38, 43, 51), Color.FromArgb(32, 36, 43)),
    };

    private static Color AccentColor(string hudAccent, string appAccent) => (hudAccent is "" or "app" ? appAccent : hudAccent) switch
    {
        "copper" => Color.FromArgb(224, 122, 63),
        "ice" or "blue" => Color.FromArgb(124, 196, 255),
        "signal" or "white" => Color.FromArgb(232, 233, 235),
        "green" => Color.FromArgb(61, 214, 140),
        "red" => Color.FromArgb(240, 71, 79),
        "purple" => Color.FromArgb(167, 139, 250),
        _ => Color.FromArgb(255, 176, 32), // amber
    };

    private static readonly Color Ok = Color.FromArgb(61, 214, 140), Warn = Color.FromArgb(255, 138, 61), Crit = Color.FromArgb(240, 71, 79);

    /* ================================================================ job card */

    private sealed class JobCard : Form
    {
        private const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
        private const int GWL_EXSTYLE = -20;
        private static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        private sealed record Row(string Label, string Value, Color Color);

        private readonly Action<Point, Screen> _savePlacement;
        private Palette _pal = Theme("dark");
        private Color _accent = Color.FromArgb(255, 176, 32);
        private float _ui = 1;
        private int _radius;
        private bool _showHeader, _showCargo, _showRoute;
        private float _rowH = 22;
        private string _kicker = "", _cargo = "", _route = "";
        private double _progress = -1;
        private List<Row> _rows = new();
        private Font? _kickerFont, _cargoFont, _routeFont, _labelFont, _valueFont;
        private float _fontFactor;
        private Screen? _screen;

        // Placement ("Place on screen"): the card becomes draggable until the player double-clicks it.
        private bool _moving;
        private Point _dragFrom;
        private bool _dragging;
        private DateTime _moveUntil;

        public JobCard(Action<Point, Screen> savePlacement)
        {
            _savePlacement = savePlacement;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
        }

        public bool Moving => _moving;

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

        public void BeginMove()
        {
            _moving = true;
            _moveUntil = DateTime.UtcNow.AddMinutes(2);
            SetClickThrough(false);
            Cursor = Cursors.SizeAll;
        }

        private void EndMove(bool save)
        {
            if (!_moving) return;
            _moving = false;
            _dragging = false;
            Capture = false;
            Cursor = Cursors.Default;
            SetClickThrough(true);
            if (save && _screen is not null) _savePlacement(new Point(Left + Width / 2, Top + Height / 2), _screen);
            Invalidate();
        }

        private void SetClickThrough(bool on)
        {
            if (!IsHandleCreated) return;
            var ex = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, on ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_moving) SetClickThrough(false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_moving) return;
            if (e.Button == MouseButtons.Right) { EndMove(save: false); return; }
            _dragging = true;
            _dragFrom = e.Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_moving || !_dragging || _screen is null) return;
            var area = _screen.WorkingArea;
            var p = PointToScreen(e.Location);
            Location = new Point(
                Math.Clamp(p.X - _dragFrom.X, area.Left, Math.Max(area.Left, area.Right - Width)),
                Math.Clamp(p.Y - _dragFrom.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_moving && e.Button == MouseButtons.Left) EndMove(save: true);
        }

        public void Update(TelemetrySnapshot s, HudSettings hud, string appAccent, Screen screen, double opacity, bool de, bool imperial)
        {
            if (_moving && DateTime.UtcNow > _moveUntil) EndMove(save: true);
            _screen = screen;
            var scale = hud.Scale is >= 50 and <= 200 ? hud.Scale / 100f : hud.Size switch { "small" => 0.82f, "large" => 1.25f, _ => 1f };
            _ui = screen.Scale() * scale;
            _pal = Theme(hud.Theme);
            _accent = AccentColor(hud.Accent, appAccent);
            _radius = hud.Rounded ? (int)(10 * _ui) : 0;
            _rowH = hud.Density switch { "compact" => 18, "roomy" => 27, _ => 22 };
            _showHeader = hud.ShowHeader;
            _showCargo = hud.ShowCargo;
            _showRoute = hud.ShowRoute;
            EnsureFonts(scale);

            var c = de ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.GetCultureInfo("en-GB");
            string L(string en, string ger) => de ? ger : en;
            string Dist(double km) => imperial ? $"{km * 0.621371:0} mi" : km < 10 ? $"{km.ToString("0.0", c)} km" : $"{km.ToString("N0", c)} km";
            string Spd(double kmh) => imperial ? $"{kmh * 0.621371:0} mph" : $"{kmh:0} km/h";
            string Dur(double sec) { var m = (int)Math.Round(sec / 60); return m < 60 ? $"{Math.Max(1, m)} min" : $"{m / 60} h {m % 60:00} min"; }
            string Pct(double v) => $"{(v * 100).ToString("0.#", c)} %";

            var eta = s.Eta;
            var job = s.OnJob && !string.IsNullOrEmpty(s.DestinationCity);
            _kicker = _moving ? L("PLACE THE HUD", "HUD PLATZIEREN") : job ? L("CURRENT JOB", "AKTUELLER AUFTRAG") : L("FREE ROAM", "FREIE FAHRT");
            _cargo = job ? $"{s.Cargo}{(s.CargoMassKg > 0 ? $" · {(s.CargoMassKg / 1000).ToString("0.#", c)} t" : "")}" : $"{s.TruckBrand} {s.TruckName}".Trim();
            _route = job ? $"{s.SourceCity} → {s.DestinationCity}" : L("No active job", "Kein aktiver Auftrag");
            _progress = hud.ShowProgress && job && s.PlannedDistanceKm > 0 && eta is not null ? Math.Clamp(1 - eta.RemainingKm / s.PlannedDistanceKm, 0, 1) : -1;

            var rows = new List<Row>();
            if (_moving)
            {
                rows.Add(new(L("Drag", "Ziehen"), L("move the card", "Karte verschieben"), _pal.Ink));
                rows.Add(new(L("Double-click", "Doppelklick"), L("save position", "Position speichern"), _accent));
                rows.Add(new(L("Right-click", "Rechtsklick"), L("cancel", "abbrechen"), _pal.Muted));
            }
            else foreach (var key in hud.Fields.Distinct())
            {
                switch (key)
                {
                    case "remaining" when job: rows.Add(new(L("Remaining", "Verbleibend"), eta is not null ? Dist(eta.RemainingKm) : "—", _pal.Ink)); break;
                    case "etaReal" when job: rows.Add(new(L("Real-time ETA", "Echtzeit-ETA"), eta is not null ? Dur(eta.RealSeconds) : "—", _accent)); break;
                    case "arrival" when job: rows.Add(new(L("Arrival", "Ankunft"), eta is not null ? eta.ArrivalUtc.ToLocalTime().ToString("HH:mm") : "—", _pal.Ink)); break;
                    case "etaGame" when job: rows.Add(new(L("Game ETA", "Spiel-ETA"), eta is not null ? Dur(eta.GameSeconds) : "—", _pal.Ink)); break;
                    case "deadline" when job:
                    {
                        var m = eta?.DeadlineMarginGameMinutes;
                        rows.Add(new(m < 0 ? L("Late by", "Verspätung") : L("Deadline buffer", "Fristpuffer"),
                            m is null ? "—" : Dur(Math.Abs(m.Value) * 60), m is null ? _pal.Ink : m < 0 ? Crit : m < 60 ? Warn : Ok));
                        break;
                    }
                    case "income" when job: rows.Add(new(L("Income", "Einnahmen"), s.JobIncome > 0 ? string.Format(c, "{0:N0} €", s.JobIncome) : "—", Ok)); break;
                    case "company" when job: rows.Add(new(L("Destination", "Ziel"), string.IsNullOrEmpty(s.DestinationCompany) ? "—" : s.DestinationCompany, _pal.Ink)); break;
                    case "speed":
                        rows.Add(new(L("Speed", "Geschwindigkeit"), Spd(Math.Abs(s.SpeedKmh)) + (s.SpeedLimitKmh > 1 ? $"  ·  {L("limit", "Limit")} {(imperial ? s.SpeedLimitKmh * 0.621371 : s.SpeedLimitKmh):0}" : ""),
                            s.SpeedLimitKmh > 1 && s.SpeedKmh > s.SpeedLimitKmh + 5 ? Crit : _pal.Ink));
                        break;
                    case "speedLimit": rows.Add(new(L("Speed limit", "Tempolimit"), s.SpeedLimitKmh > 1 ? Spd(s.SpeedLimitKmh) : "—", _pal.Ink)); break;
                    case "cruise": rows.Add(new(L("Cruise control", "Tempomat"), s.CruiseControl ? Spd(s.CruiseControlKmh) : L("off", "aus"), s.CruiseControl ? _accent : _pal.Muted)); break;
                    case "gear":
                    {
                        var g = s.GearDashboard != 0 ? s.GearDashboard : s.Gear;
                        rows.Add(new(L("Gear", "Gang"), g > 0 ? g.ToString(c) : g < 0 ? $"R{-g}" : "N", _pal.Ink));
                        break;
                    }
                    case "fuel":
                        rows.Add(new(L("Fuel", "Kraftstoff"), s.FuelCapacity > 0 ? $"{s.FuelLitres:0} l · {Pct(s.FuelLitres / s.FuelCapacity)}" : "—",
                            s.FuelCapacity > 0 && s.FuelLitres / s.FuelCapacity < 0.15 ? Warn : _pal.Ink));
                        break;
                    case "fuelRange":
                        rows.Add(new(L("Fuel range", "Reichweite"), s.FuelRangeKm > 0 ? Dist(s.FuelRangeKm) : "—",
                            job && eta is not null && s.FuelRangeKm > 0 && s.FuelRangeKm < eta.RemainingKm ? Warn : _pal.Ink));
                        break;
                    case "rest": rows.Add(new(L("Next rest", "Nächste Pause"), s.RestStopMinutes > 0 ? Dur(s.RestStopMinutes * 60) : "—", s.RestStopMinutes is > 0 and < 60 ? Warn : _pal.Ink)); break;
                    case "damage":
                        rows.Add(new(job ? L("Cargo damage", "Frachtschaden") : L("Truck wear", "Lkw-Verschleiß"),
                            Pct(job ? s.CargoDamage : s.TruckDamage), (job ? s.CargoDamage : s.TruckDamage) >= 0.05 ? Warn : _pal.Ink));
                        break;
                    case "truckDamage": rows.Add(new(L("Truck wear", "Lkw-Verschleiß"), Pct(s.TruckDamage), s.TruckDamage >= 0.1 ? Warn : _pal.Ink)); break;
                    case "trailerDamage" when s.TrailerAttached: rows.Add(new(L("Trailer wear", "Anhänger-Verschleiß"), Pct(s.TrailerDamage), s.TrailerDamage >= 0.1 ? Warn : _pal.Ink)); break;
                    case "gameTime": { var t = s.GameTimeMinutes % 1440; rows.Add(new(L("Game time", "Spielzeit"), $"{t / 60:00}:{t % 60:00}", _pal.Ink)); break; }
                    case "clock": rows.Add(new(L("Time", "Uhrzeit"), DateTime.Now.ToString("HH:mm"), _pal.Ink)); break;
                }
            }
            _rows = rows;

            var w = (int)(Math.Clamp(hud.Width, 220, 420) * _ui);
            var head = (_showHeader ? 30 : 10) + (_showCargo ? 16 : 0) + (_showRoute ? 26 : 0);
            var h = (int)((head + (_progress >= 0 ? 22 : 0) + _rows.Count * _rowH + 10) * _ui);
            Place(screen, new Size(w, Math.Max(h, (int)(40 * _ui))), hud, opacity);
        }

        /// <summary>Applies size, corner/custom position and opacity, then repaints.</summary>
        private void Place(Screen screen, Size size, HudSettings hud, double opacity)
        {
            var area = screen.WorkingArea;
            var m = (int)(Math.Clamp(hud.Margin, 0, 200) * screen.Scale());
            int Cx() => area.Left + (area.Width - size.Width) / 2;
            int Cy() => area.Top + (area.Height - size.Height) / 2;
            var loc = hud.Position switch
            {
                "topLeft" => new Point(area.Left + m, area.Top + m),
                "topCenter" => new Point(Cx(), area.Top + m),
                "middleLeft" => new Point(area.Left + m, Cy()),
                "middleRight" => new Point(area.Right - m - size.Width, Cy()),
                "bottomLeft" => new Point(area.Left + m, area.Bottom - m - size.Height),
                "bottomCenter" => new Point(Cx(), area.Bottom - m - size.Height),
                "bottomRight" => new Point(area.Right - m - size.Width, area.Bottom - m - size.Height),
                "custom" => new Point(
                    area.Left + (int)Math.Clamp(area.Width * hud.X / 100.0 - size.Width / 2.0, 0, Math.Max(0, area.Width - size.Width)),
                    area.Top + (int)Math.Clamp(area.Height * hud.Y / 100.0 - size.Height / 2.0, 0, Math.Max(0, area.Height - size.Height))),
                _ => new Point(area.Right - m - size.Width, area.Top + m), // topRight
            };
            if (Size != size || _radius != _regionRadius)
            {
                Size = size;
                _regionRadius = _radius;
                if (_radius > 0) { using var path = Rounded(new Rectangle(0, 0, Width, Height), _radius); Region = new Region(path); }
                else Region = null;
            }
            // While the player drags the card, the mouse decides where it is.
            if (!_moving && Location != loc) Location = loc;
            else if (_moving && !Visible) Location = loc;
            if (Math.Abs(Opacity - opacity) > 0.005) Opacity = opacity;
            if (BackColor != _pal.Bg) BackColor = _pal.Bg;
            if (!Visible)
            {
                Show();
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x2 | 0x1 | 0x10 | 0x40);
                if (_moving) SetClickThrough(false);
            }
            Invalidate();
        }

        private int _regionRadius = -1;

        private static GraphicsPath Rounded(Rectangle r, int radius)
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
            int P(float v) => (int)(v * _ui);

            // Frame (dashed accent while placing)
            using (var pen = new Pen(_moving ? _accent : _pal.Border, _moving ? 2 : 1) { DashStyle = _moving ? DashStyle.Dash : DashStyle.Solid })
            {
                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                if (_radius > 0) { using var path = Rounded(rect, _radius); g.DrawPath(pen, path); }
                else g.DrawRectangle(pen, rect);
            }

            var y = P(10);
            if (_showHeader)
            {
                // Slanted accent (HAULIX signature) + kicker
                using (var b = new SolidBrush(_accent))
                    g.FillPolygon(b, new[] { new Point(P(16), P(14)), new Point(P(19), P(14)), new Point(P(16), P(26)), new Point(P(13), P(26)) });
                TextRenderer.DrawText(g, _kicker, _kickerFont, new Point(P(24), P(13)), _accent, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "HAULIX", _kickerFont, new Rectangle(0, P(13), Width - P(14), P(14)), _pal.Muted, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                y = P(32);
            }
            if (_showCargo)
            {
                TextRenderer.DrawText(g, _cargo, _cargoFont, new Rectangle(P(14), y, Width - P(28), P(16)), _pal.Muted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                y += P(16);
            }
            if (_showRoute)
            {
                TextRenderer.DrawText(g, _route, _routeFont, new Rectangle(P(14), y, Width - P(28), P(24)), _pal.Ink, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                y += P(26);
            }
            if (_progress >= 0)
            {
                var bar = new Rectangle(P(14), y + P(6), Width - P(28) - P(40), P(6));
                using (var track = new SolidBrush(_pal.Track)) g.FillRectangle(track, bar);
                using (var fill = new SolidBrush(_accent)) g.FillRectangle(fill, bar.X, bar.Y, (int)(bar.Width * _progress), bar.Height);
                TextRenderer.DrawText(g, $"{_progress * 100:0} %", _labelFont, new Rectangle(bar.Right, y + P(2), Width - bar.Right - P(14), P(16)), _pal.Muted, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                y += P(22);
            }
            using var sep = new Pen(_pal.Sep);
            var first = true;
            foreach (var r in _rows)
            {
                if (!first || _showHeader || _showCargo || _showRoute || _progress >= 0) g.DrawLine(sep, P(14), y, Width - P(14), y);
                first = false;
                var ty = y + (int)((_rowH * _ui - _labelFont!.Height) / 2);
                TextRenderer.DrawText(g, r.Label, _labelFont, new Rectangle(P(14), ty, Width / 2, P(16)), _pal.Muted, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, r.Value, _valueFont, new Rectangle(Width / 3, ty - 1, Width * 2 / 3 - P(14), P(18)), r.Color, TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                y += (int)(_rowH * _ui);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var x in new[] { _kickerFont, _cargoFont, _routeFont, _labelFont, _valueFont }) x?.Dispose();
            base.Dispose(disposing);
        }
    }
}
