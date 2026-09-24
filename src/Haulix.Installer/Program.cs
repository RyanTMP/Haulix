using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Haulix.Installer
{
    internal static class Program
    {
        public static string TempDir;

        [STAThread]
        private static int Main(string[] args)
        {
            // WebView2 assemblies are embedded; resolve them before any type that uses them is touched.
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                var name = new AssemblyName(e.Name).Name + ".dll";
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("lib/" + name))
                {
                    if (s == null) return null;
                    var bytes = new byte[s.Length];
                    s.Read(bytes, 0, bytes.Length);
                    return Assembly.Load(bytes);
                }
            };

            var mode = SetupMode.Install;
            string uninstallDir = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--uninstall") mode = SetupMode.Uninstall;
                if (args[i] == "--uninstall-from" && i + 1 < args.Length) { mode = SetupMode.Uninstall; uninstallDir = args[++i]; }
            }

            // The uninstaller lives inside the install folder; run a temp copy so the folder can be deleted.
            var self = Assembly.GetExecutingAssembly().Location;
            if (mode == SetupMode.Uninstall && uninstallDir == null)
            {
                var dir = Path.GetDirectoryName(self);
                var copy = Path.Combine(Path.GetTempPath(), "HAULIX-Uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                File.Copy(self, copy, true);
                Process.Start(new ProcessStartInfo(copy, "--uninstall-from \"" + dir + "\"") { UseShellExecute = false });
                return 0;
            }

            TempDir = Path.Combine(Path.GetTempPath(), "HAULIX-Setup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(TempDir);
            try
            {
                ExtractResources(TempDir);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Run(mode, uninstallDir);
            }
            finally
            {
                try { Directory.Delete(TempDir, true); } catch (Exception) { }
            }
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Run(SetupMode mode, string uninstallDir) => Application.Run(new SetupForm(mode, uninstallDir));

        private static void ExtractResources(string dir)
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith("ui/", StringComparison.Ordinal) && name != "lib/WebView2Loader.dll") continue;
                var target = Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var s = asm.GetManifestResourceStream(name))
                using (var f = File.Create(target))
                    s.CopyTo(f);
            }
        }
    }

    internal enum SetupMode { Install, Uninstall }
}
