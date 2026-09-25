using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Haulix.Installer
{
    /// <summary>
    /// What HAULIX needs on the PC. HAULIX itself is self-contained (the .NET runtime is inside Haulix.exe), and the
    /// setup runs on .NET Framework 4.8, which is part of Windows 10/11. The only thing that can be missing is the
    /// Microsoft Edge WebView2 Runtime (built into Windows 11, missing on some Windows 10 PCs) – the setup
    /// downloads Microsoft's official installer and installs it for the current user.
    /// </summary>
    internal static class Prerequisites
    {
        // Microsoft's Evergreen bootstrapper (about 2 MB; it downloads the matching runtime).
        private const string WebView2Bootstrapper = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        /// <summary>Windows 10 (1809) or newer; the app manifest makes Environment.OSVersion report the real version.</summary>
        public static bool WindowsSupported()
        {
            var v = Environment.OSVersion.Version;
            return Environment.OSVersion.Platform == PlatformID.Win32NT && (v.Major > 10 || (v.Major == 10 && v.Build >= 17763));
        }

        public static bool WebView2Installed()
        {
            try { return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
            catch (Exception) { return false; }
        }

        /// <summary>Downloads and installs the WebView2 Runtime with a small progress window. True when it is installed afterwards.</summary>
        public static bool InstallWebView2(IWin32Window owner, bool german)
        {
            using (var form = new ProgressForm(german ? "HAULIX Setup – Voraussetzungen" : "HAULIX Setup – requirements",
                german ? "Microsoft Edge WebView2 Runtime wird heruntergeladen …" : "Downloading the Microsoft Edge WebView2 Runtime …"))
            {
                Exception error = null;
                form.Shown += async (_, __) =>
                {
                    try
                    {
                        var file = Path.Combine(Program.TempDir, "MicrosoftEdgeWebview2Setup.exe");
                        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                        using (var web = new WebClient())
                        {
                            web.DownloadProgressChanged += (s, e) => form.SetProgress(e.ProgressPercentage);
                            await web.DownloadFileTaskAsync(new Uri(WebView2Bootstrapper), file);
                        }
                        if (!SignedByMicrosoft(file)) throw new InvalidOperationException("The downloaded file is not signed by Microsoft.");
                        form.SetText(german ? "WebView2 Runtime wird installiert … (kann 1–2 Minuten dauern)" : "Installing the WebView2 Runtime … (can take 1–2 minutes)", marquee: true);
                        await Task.Run(() =>
                        {
                            using (var p = Process.Start(new ProcessStartInfo(file, "/silent /install") { UseShellExecute = false, CreateNoWindow = true }))
                                p.WaitForExit(10 * 60 * 1000);
                        });
                    }
                    catch (Exception ex) { error = ex; }
                    form.Close();
                };
                form.ShowDialog(owner);
                if (error != null) Debug.WriteLine("WebView2 install failed: " + error);
            }
            return WebView2Installed();
        }

        private static bool SignedByMicrosoft(string file)
        {
            try
            {
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
                if (cert.Subject.IndexOf("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase) < 0) return false;
                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    return chain.Build(cert);
                }
            }
            catch (Exception) { return false; }
        }

        /// <summary>Small dark progress window (the WebView2 UI of the setup is not available yet).</summary>
        private sealed class ProgressForm : Form
        {
            private readonly Label _label;
            private readonly ProgressBar _bar;

            public ProgressForm(string title, string text)
            {
                Text = title;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(460, 118);
                BackColor = Color.FromArgb(11, 12, 14);
                ForeColor = Color.FromArgb(236, 237, 238);
                Font = new Font("Segoe UI", 9.5f);
                Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                _label = new Label { Text = text, AutoSize = false, Location = new Point(20, 20), Size = new Size(420, 40) };
                _bar = new ProgressBar { Location = new Point(20, 70), Size = new Size(420, 14), Style = ProgressBarStyle.Continuous };
                Controls.Add(_label);
                Controls.Add(_bar);
            }

            public void SetProgress(int pct) => _bar.Value = Math.Max(0, Math.Min(100, pct));

            public void SetText(string text, bool marquee)
            {
                _label.Text = text;
                if (marquee) _bar.Style = ProgressBarStyle.Marquee;
            }
        }
    }
}
