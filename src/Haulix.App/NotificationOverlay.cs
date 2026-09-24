using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Haulix.Core.Services;

namespace Haulix.App;

/// <summary>
/// Shows job notifications as small HAULIX-styled cards over the game: always on top, never takes focus,
/// click-through, stacked in a screen corner of the monitor ETS2 runs on, fading out after a few seconds.
/// Works over ETS2 in windowed / borderless fullscreen; exclusive fullscreen hides every overlay (Windows).
/// </summary>
internal sealed class NotificationOverlay
{
    private const int MaxVisible = 3;
    private readonly List<ToastForm> _open = new();
    private readonly Image? _logo;

    public NotificationOverlay()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "brand", "h-logo.png");
            if (File.Exists(path)) _logo = Image.FromFile(path);
        }
        catch (Exception) { _logo = null; }
    }

    /// <summary>Must be called on the UI thread.</summary>
    public void Show(Notice n, string corner)
    {
        while (_open.Count >= MaxVisible) _open[0].Close();
        var screen = GameScreen();
        var toast = new ToastForm(n, _logo);
        toast.FormClosed += (_, _) => { _open.Remove(toast); Layout(screen, corner); };
        _open.Add(toast);
        Layout(screen, corner);
        toast.ShowOverlay();
    }

    private void Layout(Screen screen, string corner)
    {
        var area = screen.WorkingArea;
        var margin = (int)(20 * screen.Scale());
        var top = corner.StartsWith("top", StringComparison.Ordinal);
        var right = corner.EndsWith("Right", StringComparison.Ordinal);
        var y = top ? area.Top + margin : area.Bottom - margin;
        // Newest card nearest to the corner.
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            var f = _open[i];
            var x = right ? area.Right - margin - f.Width : area.Left + margin;
            if (top) { f.Location = new Point(x, y); y += f.Height + margin / 2; }
            else { y -= f.Height; f.Location = new Point(x, y); y -= margin / 2; }
        }
    }

    /// <summary>The monitor ETS2 is on (falls back to the primary screen).</summary>
    private static Screen GameScreen()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("eurotrucks2"))
                if (p.MainWindowHandle != IntPtr.Zero) return Screen.FromHandle(p.MainWindowHandle);
        }
        catch (Exception) { /* access denied or process gone */ }
        return Screen.PrimaryScreen ?? Screen.AllScreens[0];
    }

    private sealed class ToastForm : Form
    {
        private const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private readonly Notice _n;
        private readonly Image? _logo;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
        private readonly float _s;
        private DateTime _shownUtc;
        private readonly Font _brandFont, _titleFont, _msgFont;
        private readonly int _textWidth;

        public ToastForm(Notice n, Image? logo)
        {
            _n = n;
            _logo = logo;
            _s = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).Scale();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(18, 20, 24);
            DoubleBuffered = true;
            Opacity = 0;

            _brandFont = new Font("Segoe UI Semibold", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
            _titleFont = new Font("Segoe UI Semibold", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
            _msgFont = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            var width = (int)(380 * _s);
            _textWidth = width - (int)(64 * _s);
            var titleH = TextRenderer.MeasureText(n.Title, _titleFont, new Size(_textWidth, 0), TextFormatFlags.WordBreak).Height;
            var msgH = string.IsNullOrEmpty(n.Message) ? 0 : TextRenderer.MeasureText(n.Message, _msgFont, new Size(_textWidth, 0), TextFormatFlags.WordBreak).Height;
            Size = new Size(width, (int)(40 * _s) + titleH + msgH);
            using var path = Rounded(new Rectangle(0, 0, Width, Height), (int)(8 * _s));
            Region = new Region(path);

            _timer.Tick += (_, _) => Animate();
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

        public void ShowOverlay()
        {
            Show();
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            _shownUtc = DateTime.UtcNow;
            _timer.Start();
        }

        private void Animate()
        {
            var t = (DateTime.UtcNow - _shownUtc).TotalMilliseconds;
            var life = _n.Kind == "critical" ? 9000 : 6500;
            if (t < 180) Opacity = 0.96 * t / 180;
            else if (t < life) Opacity = 0.96;
            else if (t < life + 400) Opacity = 0.96 * (1 - (t - life) / 400);
            else { _timer.Stop(); Close(); }
        }

        private Color Accent => _n.Kind switch
        {
            "success" => Color.FromArgb(61, 214, 140),
            "warning" => Color.FromArgb(255, 138, 61),
            "critical" => Color.FromArgb(240, 71, 79),
            _ => Color.FromArgb(255, 176, 32),
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var pad = (int)(16 * _s);
            using (var border = new Pen(Color.FromArgb(44, 49, 58), 1))
            using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), (int)(8 * _s)))
                g.DrawPath(border, path);
            // Slanted accent bar: the HAULIX signature.
            var bar = (int)(4 * _s);
            using (var b = new SolidBrush(Accent))
                g.FillPolygon(b, new[] { new Point(pad / 2 + bar, pad), new Point(pad / 2 + bar * 2, pad), new Point(pad / 2 + bar, Height - pad), new Point(pad / 2, Height - pad) });

            var x = (int)(40 * _s);
            var y = (int)(12 * _s);
            var logo = (int)(12 * _s);
            if (_logo is not null) g.DrawImage(_logo, new Rectangle(x, y + (int)(1 * _s), logo, logo));
            TextRenderer.DrawText(g, "HAULIX", _brandFont, new Point(x + (_logo is null ? 0 : logo + (int)(5 * _s)), y), Accent, TextFormatFlags.NoPadding);
            y += (int)(18 * _s);
            var titleSize = TextRenderer.MeasureText(_n.Title, _titleFont, new Size(_textWidth, 0), TextFormatFlags.WordBreak);
            TextRenderer.DrawText(g, _n.Title, _titleFont, new Rectangle(x, y, _textWidth, titleSize.Height), Color.FromArgb(236, 237, 238), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            y += titleSize.Height + (int)(2 * _s);
            if (!string.IsNullOrEmpty(_n.Message))
                TextRenderer.DrawText(g, _n.Message, _msgFont, new Rectangle(x, y, _textWidth, Height - y), Color.FromArgb(155, 161, 170), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); _brandFont.Dispose(); _titleFont.Dispose(); _msgFont.Dispose(); }
            base.Dispose(disposing);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            var d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}

internal static class ScreenExtensions
{
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    /// <summary>Display scale of a monitor (1.0 = 96 dpi).</summary>
    public static float Scale(this Screen s)
    {
        try
        {
            var mon = MonitorFromPoint(new Point(s.Bounds.Left + 1, s.Bounds.Top + 1), 2);
            if (GetDpiForMonitor(mon, 0, out var dpi, out _) == 0) return dpi / 96f;
        }
        catch (Exception) { /* older Windows */ }
        return 1f;
    }
}
