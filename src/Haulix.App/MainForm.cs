using System.Runtime.InteropServices;
using System.Text.Json;
using Haulix.Core;
using Haulix.Core.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace Haulix.App;

/// <summary>
/// The HAULIX window: a WebView2 surface showing the local UI (served from the wwwroot folder through a
/// virtual host name, no web server), bridged to <see cref="HaulixEngine"/> via JSON messages.
/// </summary>
public sealed class MainForm : Form
{
    private const string HostName = "app.haulix";
    private static readonly Color Background = Color.FromArgb(11, 12, 14);

    private readonly string _dataFolder;
    private readonly bool _startMinimized;
    private readonly WebView2 _web;
    private readonly NotifyIcon _tray;
    private HaulixEngine? _engine;
    private bool _exiting;
    private readonly NotificationOverlay _overlay = new();
    private readonly VoiceAnnouncer _voice = new();
    private AntiAfk? _antiAfk;
    private DiscordPresence? _discord;
    private static readonly bool DiscordPresenceEnabled = false;
    private HudOverlay? _hud;
    private Control? _nativeSplash;

    public MainForm(string dataFolder, bool startMinimized)
    {
        _dataFolder = dataFolder;
        _startMinimized = startMinimized;

        Text = "HAULIX";
        BackColor = Background;
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.Manual;
        Icon = LoadIcon();
        RestoreWindowBounds();

        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
        Controls.Add(_web);
        _nativeSplash = CreateNativeSplash();
        if (_nativeSplash is not null) { Controls.Add(_nativeSplash); _nativeSplash.BringToFront(); }

        _tray = new NotifyIcon { Icon = Icon, Text = "HAULIX ETS2 Logger", Visible = false };
        _tray.DoubleClick += (_, _) => BringToFrontFromOtherInstance();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open HAULIX", null, (_, _) => BringToFrontFromOtherInstance());
        menu.Items.Add("Exit", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ApplyDarkTitleBar(Handle, Background);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            _engine = new HaulixEngine(_dataFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"HAULIX could not open its local database.\n\n{ex.Message}\n\nData folder: {_dataFolder}",
                "HAULIX", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var settings = _engine.Settings.Load();
        if (_startMinimized || settings.General.StartMinimized)
        {
            WindowState = FormWindowState.Minimized;
            if (settings.General.MinimizeToTray) HideToTray();
        }

        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_dataFolder, "WebView2"),
                new CoreWebView2EnvironmentOptions("--disable-features=msSmartScreenProtection --autoplay-policy=no-user-gesture-required"));
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "HAULIX needs the Microsoft Edge WebView2 Runtime, which is included with Windows 11 and most Windows 10 installations.\n\n" +
                "Install the \"Evergreen\" WebView2 Runtime from Microsoft and start HAULIX again.",
                "HAULIX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        // The UI is local files; drop the HTTP cache so an update never runs stale scripts from the previous version.
        try { await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage); }
        catch (Exception) { /* cache clearing is best effort */ }
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
#if DEBUG
        // Development: serve the UI straight from the source tree so edits only need a reload (F5).
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));
        if (File.Exists(Path.Combine(source, "index.html"))) root = source;
#endif
        core.SetVirtualHostNameToFolderMapping(HostName, root, CoreWebView2HostResourceAccessKind.DenyCors);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#endif
        core.NavigationStarting += (_, a) =>
        {
            // HAULIX never leaves its own local origin.
            if (!a.Uri.StartsWith($"https://{HostName}/", StringComparison.OrdinalIgnoreCase)) a.Cancel = true;
        };
        core.NewWindowRequested += (_, a) => a.Handled = true;
        core.WebMessageReceived += OnWebMessage;

        _engine.Push += (evt, data) => Post(new { @event = evt, data });
        // TruckersMP anti-AFK message (opt-in, see Settings → TruckersMP).
        _antiAfk = new AntiAfk(_engine);
        _antiAfk.Sent += msg => Post(new { @event = "antiAfkSent", data = new { message = msg, at = DateTime.UtcNow } });
        // Discord Rich Presence is "coming soon": the code exists but is locked in this version.
        if (DiscordPresenceEnabled) _discord = new DiscordPresence(_engine);
        _hud = new HudOverlay(_engine, () => Visible && WindowState != FormWindowState.Minimized && ActiveForm == this);
        _hud.PlacementEnded += saved => Post(new { @event = "hudPlacement", data = new { active = false, saved } });
        // Job notifications over the game while HAULIX itself is not the active window.
        _engine.Notifier.Raised += n => BeginInvoke(() =>
        {
            var ns = _engine.Settings.Load().Notifications;
            var appInFront = Visible && WindowState != FormWindowState.Minimized && ActiveForm == this;
            if (ns.Overlay && (!appInFront || n.Category == "test")) _overlay.Show(n, ns.Position);
            // A short chime first (the AFK alarm always, it is the point of that warning).
            if (ns.Sounds && (!appInFront || n.Category is "test" or "afk"))
            {
                var sound = NotificationSounds.For(n.Kind, n.Category);
                var wanted = sound switch
                {
                    "afk" => ns.SoundAfk,
                    "warning" or "critical" => ns.SoundWarnings,
                    "job" => ns.SoundJob,
                    "success" => n.Category == "job" ? ns.SoundJob : ns.SoundProgress,
                    _ => n.Category == "test" || ns.SoundProgress,
                };
                if (wanted) _voice.Chime(sound, ns);
            }
            // Spoken too when enabled: audible even when ETS2 runs in exclusive fullscreen on a single monitor.
            if (ns.Voice && (!appInFront || n.Category == "test")) _voice.Speak(n, _engine.Settings.Load().General, ns);
        });
        _engine.Start();
        // Setup choices like "Start with Windows" also need the host side (Run key).
        if (_engine.InstallerPreferencesApplied) ApplyHostSettings(_engine.Settings.Load());
        // The web start-up screen takes over as soon as the page has loaded.
        core.NavigationCompleted += (_, _) => { _nativeSplash?.Dispose(); _nativeSplash = null; };
        core.Navigate($"https://{HostName}/index.html");
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        try
        {
            msg = JsonDocument.Parse(e.WebMessageAsJson).RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }
        var id = msg.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
        var method = msg.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
        var args = msg.TryGetProperty("params", out var p) ? p : default;

        // Native dialogs must run on the UI thread; everything else runs on the thread pool.
        if (TryHandleNative(method, args, out var nativeResult, out var nativeError))
        {
            Reply(id, nativeResult, nativeError);
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var result = _engine!.Handle(method, args);
                if (method == "settings.save") BeginInvoke(() => ApplyHostSettings(_engine.Settings.Load()));
                Reply(id, result, null);
            }
            catch (Exception ex)
            {
                Reply(id, null, ex.InnerException?.Message ?? ex.Message);
            }
        });
    }

    private static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromMinutes(15) };

    private async Task DownloadAndRunUpdate(string url)
    {
        try
        {
            var target = Path.Combine(Path.GetTempPath(), "HAULIX-Update", Path.GetFileName(new Uri(url).LocalPath));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var resp = await UpdateHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(target);
                var buf = new byte[1 << 16];
                long done = 0; var lastPct = -1;
                int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    var pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != lastPct) { lastPct = pct; Post(new { @event = "updateProgress", data = new { pct, done, total } }); }
                }
            }
            Post(new { @event = "updateProgress", data = new { pct = 100, done = 0, total = 0, starting = true } });
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
            BeginInvoke(() => { _exiting = true; Close(); });
        }
        catch (Exception ex)
        {
            Post(new { @event = "updateError", data = new { message = ex.Message } });
        }
    }

    private bool TryHandleNative(string method, JsonElement args, out object? result, out string? error)
    {
        result = null;
        error = null;
        try
        {
            switch (method)
            {
                case "dialog.pickFolder":
                {
                    using var dlg = new FolderBrowserDialog
                    {
                        Description = Str(args, "title") ?? "Select folder",
                        UseDescriptionForTitle = true,
                        InitialDirectory = Str(args, "initial") ?? "",
                        ShowNewFolderButton = false,
                    };
                    result = dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
                    return true;
                }
                case "data.export":
                {
                    using var dlg = new SaveFileDialog
                    {
                        Title = "Export HAULIX data",
                        Filter = "HAULIX backup (*.haulix)|*.haulix",
                        FileName = $"haulix-{DateTime.Now:yyyy-MM-dd}.haulix",
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return true;
                    result = _engine!.Backups.Export(dlg.FileName);
                    return true;
                }
                case "update.install":
                {
                    // Download the setup attached to the GitHub release, then run it (it closes and updates HAULIX).
                    var url = Str(args, "url");
                    if (!Haulix.Core.Services.UpdateChecker.IsTrustedSetupUrl(url)) { error = "Not a HAULIX release download"; return true; }
                    _ = Task.Run(() => DownloadAndRunUpdate(url!));
                    result = true;
                    return true;
                }
                case "voice.list":
                    result = new
                    {
                        natural = PiperVoices.Catalog.Select(v => new { id = v.Id, name = v.Name, lang = v.Lang, gender = v.Gender, sizeMb = v.SizeMb, installed = PiperVoices.Installed(v.Id) }),
                        engineInstalled = PiperVoices.EngineInstalled,
                        windows = VoiceAnnouncer.WindowsVoices(),
                    };
                    return true;
                case "voice.install":
                {
                    // Download engine + voice in the background; progress goes to the UI as events.
                    var id = Str(args, "id") ?? "";
                    if (PiperVoices.Catalog.All(v => v.Id != id)) { error = "Unknown voice"; return true; }
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await PiperVoices.Install(id, (p, step) => Post(new { @event = "voiceProgress", data = new { id, pct = (int)(p * 100), step } }));
                            Post(new { @event = "voiceInstalled", data = new { id } });
                        }
                        catch (Exception ex) { Post(new { @event = "voiceError", data = new { id, message = ex.Message } }); }
                    });
                    result = true;
                    return true;
                }
                case "voice.remove":
                    PiperVoices.Remove(Str(args, "id") ?? "");
                    result = true;
                    return true;
                case "voice.test":
                {
                    var ns = _engine!.Settings.Load().Notifications;
                    var lang = VoiceAnnouncer.Language(_engine.Settings.Load().General);
                    _voice.Say(lang == "de" ? "Noch zehn Kilometer bis Prag. Ankunft in etwa neun Minuten." : "Ten kilometres left to Prague. Arriving in about nine minutes.", ns, lang);
                    result = true;
                    return true;
                }
                case "sound.test":
                {
                    // Settings → Notifications → Sounds: play one sound with the current style and volume.
                    var sound = Str(args, "sound") ?? "info";
                    if (Array.IndexOf(NotificationSounds.Kinds, sound) < 0) sound = "info";
                    _voice.Chime(sound, _engine!.Settings.Load().Notifications);
                    result = true;
                    return true;
                }
                case "hud.preview":
                    // Show the HUD for a few seconds over everything so the user can check position and size.
                    _hud?.Preview(10);
                    result = true;
                    return true;
                case "hud.place":
                    // The card becomes draggable on the game monitor; a double-click saves the position.
                    BeginInvoke(() => _hud?.BeginPlacement());
                    result = true;
                    return true;
                case "hud.placeEnd":
                {
                    // Done / Cancel in Settings → HUD while the card is being placed.
                    var save = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("save", out var sv) && sv.ValueKind == JsonValueKind.True;
                    BeginInvoke(() => _hud?.EndPlacement(save));
                    result = true;
                    return true;
                }
                case "shell.openUrl":
                {
                    // Only web links, opened in the user's browser.
                    var url = Str(args, "url") ?? "";
                    if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { error = "Only https links can be opened"; return true; }
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    result = true;
                    return true;
                }
                case "image.save":
                {
                    // Share cards: the UI renders a PNG (data URL) and the user picks where to save it.
                    var dataUrl = Str(args, "dataUrl") ?? "";
                    var comma = dataUrl.IndexOf(',');
                    if (!dataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal) || comma < 0) { error = "Invalid image"; return true; }
                    using var dlg = new SaveFileDialog
                    {
                        Title = "Save image",
                        Filter = "PNG image (*.png)|*.png",
                        FileName = Str(args, "name") ?? $"haulix-{DateTime.Now:yyyy-MM-dd}.png",
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return true;
                    File.WriteAllBytes(dlg.FileName, Convert.FromBase64String(dataUrl[(comma + 1)..]));
                    result = dlg.FileName;
                    return true;
                }
                case "image.copy":
                {
                    var dataUrl = Str(args, "dataUrl") ?? "";
                    var comma = dataUrl.IndexOf(',');
                    if (comma < 0) { error = "Invalid image"; return true; }
                    using var ms = new MemoryStream(Convert.FromBase64String(dataUrl[(comma + 1)..]));
                    using var img = Image.FromStream(ms);
                    Clipboard.SetImage(img);
                    result = true;
                    return true;
                }
                case "data.exportCsv":
                {
                    using var dlg = new SaveFileDialog
                    {
                        Title = "Export logbook as CSV",
                        Filter = "CSV (*.csv)|*.csv",
                        FileName = $"haulix-logbook-{DateTime.Now:yyyy-MM-dd}.csv",
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return true;
                    File.WriteAllText(dlg.FileName, Haulix.Core.Data.Queries.LogbookCsv(_engine!.Db, _engine.Telemetry.DemoActive));
                    result = dlg.FileName;
                    return true;
                }
                case "data.import":
                {
                    using var dlg = new OpenFileDialog
                    {
                        Title = "Import HAULIX backup",
                        Filter = "HAULIX backup (*.haulix)|*.haulix",
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return true;
                    var file = dlg.FileName;
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            _engine!.ImportArchive(file);
                            Post(new { @event = "toast", data = new { kind = "success", title = "Backup imported", message = Path.GetFileName(file) } });
                        }
                        catch (Exception ex)
                        {
                            Post(new { @event = "toast", data = new { kind = "error", title = "Import failed", message = ex.Message } });
                        }
                    });
                    result = file;
                    return true;
                }
                case "shell.openFolder":
                {
                    var path = Str(args, "path");
                    if (path is not null && Directory.Exists(path))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                    result = path is not null && Directory.Exists(path);
                    return true;
                }
                case "window.setTitle":
                    Text = Str(args, "title") ?? "HAULIX";
                    result = true;
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return true;
        }
    }

    private void ApplyHostSettings(AppSettings s)
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (run is null) return;
            if (s.General.LaunchWithWindows) run.SetValue("HAULIX", $"\"{Application.ExecutablePath}\" --minimized");
            else if (run.GetValue("HAULIX") is not null) run.DeleteValue("HAULIX");
        }
        catch (Exception)
        {
            // Registry may be locked down by policy; the setting simply has no effect then.
        }
    }

    private void Reply(long id, object? result, string? error) =>
        Post(error is null ? new { id, ok = true, result } : new { id, ok = false, error } as object);

    private void Post(object payload)
    {
        if (IsDisposed || !IsHandleCreated) return;
        string json;
        try
        {
            json = JsonSerializer.Serialize(payload, SettingsStore.Json);
        }
        catch (Exception ex)
        {
            json = JsonSerializer.Serialize(new { @event = "toast", data = new { kind = "error", title = "Internal error", message = ex.Message } });
        }
        try
        {
            BeginInvoke(() =>
            {
                if (_web.CoreWebView2 is not null && !IsDisposed) _web.CoreWebView2.PostWebMessageAsJson(json);
            });
        }
        catch (InvalidOperationException) { /* window closing */ }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && _engine?.Settings.Load().General.MinimizeToTray == true) HideToTray();
    }

    private void HideToTray()
    {
        _tray.Visible = true;
        ShowInTaskbar = false;
        Hide();
    }

    public void BringToFrontFromOtherInstance()
    {
        _tray.Visible = false;
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && _engine?.Settings.Load().General.MinimizeToTray == true)
        {
            // "Minimise to tray" keeps HAULIX logging in the background when the window is closed.
            e.Cancel = true;
            HideToTray();
            return;
        }
        SaveWindowBounds();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tray.Visible = false;
        _tray.Dispose();
        _antiAfk?.Dispose();
        _discord?.Dispose();
        _hud?.Dispose();
        _engine?.Dispose();
        base.OnFormClosed(e);
    }

    private string BoundsFile => Path.Combine(_dataFolder, "window.json");

    private void RestoreWindowBounds()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var w = Math.Min(1600, area.Width - 40);
        var h = Math.Min(960, area.Height - 40);
        Bounds = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        try
        {
            if (!File.Exists(BoundsFile)) return;
            var b = JsonSerializer.Deserialize<SavedBounds>(File.ReadAllText(BoundsFile));
            if (b is null) return;
            var rect = new Rectangle(b.X, b.Y, Math.Max(b.W, MinimumSize.Width), Math.Max(b.H, MinimumSize.Height));
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect))) Bounds = rect;
            if (b.Maximized) WindowState = FormWindowState.Maximized;
        }
        catch (Exception) { }
    }

    private void SaveWindowBounds()
    {
        try
        {
            var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            Directory.CreateDirectory(_dataFolder);
            File.WriteAllText(BoundsFile, JsonSerializer.Serialize(new SavedBounds(r.X, r.Y, r.Width, r.Height, WindowState == FormWindowState.Maximized)));
        }
        catch (Exception) { }
    }

    private sealed record SavedBounds(int X, int Y, int W, int H, bool Maximized);

    /// <summary>The H logo on the dark background while WebView2 starts, so the window is never blank.</summary>
    private static Control? CreateNativeSplash()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "brand", "h-logo.png");
        if (!File.Exists(path)) return null;
        try
        {
            using var file = Image.FromFile(path);
            var logo = new Bitmap(file, new Size(62, (int)Math.Round(62.0 * file.Height / file.Width)));
            var box = new PictureBox { Dock = DockStyle.Fill, BackColor = Background, Image = logo, SizeMode = PictureBoxSizeMode.CenterImage };
            box.Disposed += (_, _) => logo.Dispose();
            return box;
        }
        catch (Exception) { return null; }
    }

    private static Icon LoadIcon()
    {
        using var s = typeof(MainForm).Assembly.GetManifestResourceStream("Haulix.App.app.ico");
        return s is null ? SystemIcons.Application : new Icon(s);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static class Native
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void ApplyDarkTitleBar(IntPtr hwnd, Color caption)
        {
            var on = 1;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            var colorRef = caption.R | (caption.G << 8) | (caption.B << 16);
            DwmSetWindowAttribute(hwnd, 35, ref colorRef, sizeof(int)); // DWMWA_CAPTION_COLOR (Win11)
            var border = 0x2E2723; // #23272E in COLORREF (BGR)
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int)); // DWMWA_BORDER_COLOR (Win11)
        }
    }
}
