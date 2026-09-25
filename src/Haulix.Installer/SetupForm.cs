using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Haulix.Installer
{
    /// <summary>The HAULIX-styled setup window: HTML UI (embedded) bridged to <see cref="SetupEngine"/>.</summary>
    internal sealed class SetupForm : Form
    {
        private static readonly Color Background = Color.FromArgb(11, 12, 14);
        private readonly SetupMode _mode;
        private readonly string _uninstallDir;
        private readonly WebView2 _web;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private bool _busy;
        private bool _uninstalled;

        public SetupForm(SetupMode mode, string uninstallDir)
        {
            _mode = mode;
            _uninstallDir = uninstallDir;
            Text = mode == SetupMode.Install ? "HAULIX Setup" : "Uninstall HAULIX";
            BackColor = Background;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1040, 680);
            Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
            Controls.Add(_web);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var on = 1;
            DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
            var caption = Background.R | (Background.G << 8) | (Background.B << 16);
            DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                CoreWebView2Environment.SetLoaderDllFolderPath(Path.Combine(Program.TempDir, "lib"));
                // Missing on some Windows 10 PCs: install Microsoft's WebView2 Runtime first (HAULIX and this setup need it).
                if (!Prerequisites.WebView2Installed() && !Prerequisites.InstallWebView2(null, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de"))
                    throw new WebView2RuntimeNotFoundException();
                var env =await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.TempDir, "wv2"));
                await _web.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException || ex is DllNotFoundException || ex is FileNotFoundException)
            {
                var r = MessageBox.Show(this,
                    "HAULIX needs the Microsoft Edge WebView2 Runtime (included with Windows 11 and most Windows 10 PCs). Setup could not install it automatically – please check your internet connection.\n\nOpen the Microsoft download page now?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes) OpenUrl("https://developer.microsoft.com/microsoft-edge/webview2/");
                Close();
                return;
            }

            var core = _web.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("setup.haulix", Path.Combine(Program.TempDir, "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.NavigationStarting += (_, a) => { if (!a.Uri.StartsWith("https://setup.haulix/", StringComparison.OrdinalIgnoreCase)) a.Cancel = true; };
            core.NewWindowRequested += (_, a) => a.Handled = true;
            core.WebMessageReceived += OnMessage;
            core.Navigate("https://setup.haulix/installer.html");
        }

        private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg;
            try { msg = _json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); }
            catch (Exception) { return; }
            string S(string k) => msg.TryGetValue(k, out var v) && v != null ? v.ToString() : null;
            bool B(string k) => msg.TryGetValue(k, out var v) && v is bool b && b;

            switch (S("cmd"))
            {
                case "init":
                {
                    var (dir, ver) = SetupEngine.Existing();
                    var (game, plugin) = SetupEngine.DetectEts2();
                    Send("init", new Dictionary<string, object>
                    {
                        ["mode"] = _mode == SetupMode.Install ? (dir != null ? "update" : "install") : "uninstall",
                        ["version"] = SetupEngine.SetupVersion,
                        ["existingVersion"] = ver,
                        ["installDir"] = _mode == SetupMode.Uninstall ? _uninstallDir : dir ?? SetupEngine.DefaultInstallDir,
                        ["dataDir"] = SetupEngine.DataDir,
                        ["hasPayload"] = SetupEngine.HasPayload,
                        ["sizeMb"] = Math.Round(SetupEngine.PayloadSize() / 1048576.0),
                        ["appRunning"] = SetupEngine.AppRunning(),
                        ["ets2"] = game,
                        ["plugin"] = plugin,
                        ["pluginBundled"] = SetupEngine.HasPlugin,
                        ["systemLanguage"] = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                        ["ets2Running"] = System.Diagnostics.Process.GetProcessesByName("eurotrucks2").Length > 0,
                    });
                    break;
                }
                case "browse":
                {
                    using (var dlg = new FolderBrowserDialog { Description = "Choose where to install HAULIX", ShowNewFolderButton = true, SelectedPath = S("current") ?? "" })
                        if (dlg.ShowDialog(this) == DialogResult.OK) Send("browsed", dlg.SelectedPath);
                    break;
                }
                case "install":
                    Run(() =>
                    {
                        var dir = new SetupEngine().Install(S("dir"), B("desktop"), B("startMenu"), B("plugin"), Progress);
                        SetupEngine.SaveLanguage(S("language"));
                        SetupEngine.SavePreferences(S("prefs"));
                        Send("installed", dir);
                    });
                    break;
                case "uninstall":
                    Run(() =>
                    {
                        new SetupEngine().Uninstall(_uninstallDir, B("removeData"), Progress);
                        _uninstalled = true;
                        Send("uninstalled", true);
                    });
                    break;
                case "launch":
                    try { SetupEngine.Launch(S("dir")); } catch (Exception ex) { Send("error", ex.Message); }
                    Close();
                    break;
                case "openUrl":
                    var url = S("url");
                    if (url != null && url.StartsWith("https://", StringComparison.Ordinal)) OpenUrl(url);
                    break;
                case "openFile":
                    var file = Path.Combine(Program.TempDir, "ui", "LICENSE.txt");
                    if (File.Exists(file)) Process.Start(new ProcessStartInfo("notepad.exe", "\"" + file + "\""));
                    break;
                case "close":
                    Close();
                    break;
            }
        }

        private void Run(Action work)
        {
            if (_busy) return;
            _busy = true;
            Task.Run(() =>
            {
                try { work(); }
                catch (Exception ex) { Send("error", ex.Message); }
                finally { _busy = false; }
            });
        }

        private void Progress(double p, string msg) => Send("progress", new Dictionary<string, object> { ["value"] = p, ["message"] = msg });

        private void Send(string evt, object data)
        {
            var json = _json.Serialize(new Dictionary<string, object> { ["event"] = evt, ["data"] = data });
            if (IsDisposed) return;
            try { BeginInvoke((Action)(() => { if (!IsDisposed) _web.CoreWebView2?.PostWebMessageAsJson(json); })); }
            catch (InvalidOperationException) { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // never interrupt a half-finished install
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _web.Dispose();
            // The uninstaller always runs as a temp copy; remove it whether or not the user went ahead.
            if (_mode == SetupMode.Uninstall || _uninstalled) SetupEngine.ScheduleSelfDelete();
            base.OnFormClosed(e);
        }

        private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
