using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace Haulix.Installer
{
    /// <summary>Per-user install/uninstall of HAULIX (no admin rights, no services, no drivers).</summary>
    internal sealed class SetupEngine
    {
        public const string AppName = "HAULIX";
        public const string AppExe = "Haulix.exe";
        // The uninstaller is kept as a data file so Haulix.exe is the only program in the install folder;
        // "Haulix.exe --uninstall" copies it to %TEMP% as an .exe and starts it.
        public const string UninstallerData = "uninstall.bin";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HAULIX";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string DefaultInstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

        public static string DataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haulix");

        public static string StartMenuShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

        public static string DesktopShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

        public static string SetupVersion =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";

        public static bool HasPayload => Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("payload.zip");

        public static long PayloadSize()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (s == null) return 0;
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                    return zip.Entries.Sum(e => e.Length);
            }
        }

        /// <summary>Existing installation from the uninstall registry entry (for updates).</summary>
        public static (string Dir, string Version) Existing()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                var dir = k?.GetValue("InstallLocation") as string;
                var ver = k?.GetValue("DisplayVersion") as string;
                return dir != null && File.Exists(Path.Combine(dir, AppExe)) ? (dir, ver) : (null, null);
            }
        }

        // The setup itself is also named Haulix.exe (the release download), so never count or close our own process.
        private static IEnumerable<Process> AppProcesses()
        {
            var self = Process.GetCurrentProcess().Id;
            return Process.GetProcessesByName("Haulix").Where(p => p.Id != self);
        }

        public static bool AppRunning() => AppProcesses().Any();

        public static void CloseApp()
        {
            foreach (var p in AppProcesses())
            {
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(6000)) { p.Kill(); p.WaitForExit(3000); }
                }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Never clean out a folder that holds other files: if the chosen folder is not empty and is not an
        /// existing HAULIX install, install into a "HAULIX" subfolder instead.
        /// </summary>
        public static string SafeInstallDir(string dir)
        {
            dir = Path.GetFullPath(dir.Trim());
            bool Usable(string d) => !Directory.Exists(d) || !Directory.EnumerateFileSystemEntries(d).Any() || File.Exists(Path.Combine(d, AppExe));
            if (Usable(dir)) return dir;
            var sub = Path.Combine(dir, AppName);
            if (Usable(sub)) return sub;
            throw new InvalidOperationException("The selected folder already contains other files. Choose an empty folder.");
        }

        public string Install(string dir, bool desktop, bool startMenu, bool plugin, Action<double, string> progress)
        {
            dir = SafeInstallDir(dir);
            progress(0.02, "Preparing");
            if (plugin && HasPlugin)
            {
                progress(0.03, "Installing ETS2 telemetry plugin");
                progress(0.04, InstallPlugin(FindEts2()));
            }
            if (AppRunning()) { progress(0.04, "Closing HAULIX"); CloseApp(); }
            Directory.CreateDirectory(dir);

            // Remove files of a previous version (user data lives in %LOCALAPPDATA%\Haulix, not here).
            progress(0.06, "Removing previous version");
            foreach (var f in Directory.GetFiles(dir)) TryDelete(f);
            foreach (var d in Directory.GetDirectories(dir)) TryDeleteDir(d);

            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (s == null) throw new InvalidOperationException("This setup does not contain the HAULIX application (development build).");
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    var total = Math.Max(1, zip.Entries.Sum(e => e.Length));
                    long done = 0;
                    var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
                    foreach (var entry in zip.Entries)
                    {
                        var target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var src = entry.Open())
                        using (var dst = File.Create(target))
                            src.CopyTo(dst);
                        done += entry.Length;
                        progress(0.08 + 0.82 * done / total, "Installing " + entry.Name);
                    }
                }
            }

            progress(0.92, "Adding uninstaller");
            File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(dir, UninstallerData), true);

            progress(0.94, "Creating shortcuts");
            var exe = Path.Combine(dir, AppExe);
            if (startMenu) CreateShortcut(StartMenuShortcut, exe, dir);
            else TryDelete(StartMenuShortcut);
            if (desktop) CreateShortcut(DesktopShortcut, exe, dir);
            else TryDelete(DesktopShortcut);

            progress(0.97, "Registering with Windows");
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "HAULIX ETS2 Logger");
                k.SetValue("DisplayVersion", SetupVersion);
                k.SetValue("Publisher", "HAULIX");
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(DirSize(dir) / 1024), RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
            progress(1, "Done");
            return dir;
        }

        public void Uninstall(string dir, bool removeData, Action<double, string> progress)
        {
            progress(0.05, "Closing HAULIX");
            CloseApp();
            progress(0.2, "Removing shortcuts");
            TryDelete(StartMenuShortcut);
            TryDelete(DesktopShortcut);
            progress(0.3, "Removing registry entries");
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch (Exception) { }
            try { using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true)) run?.DeleteValue(AppName, false); } catch (Exception) { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\HAULIX", false); } catch (Exception) { }
            progress(0.45, "Removing program files");
            if (dir != null && Directory.Exists(dir) && File.Exists(Path.Combine(dir, AppExe))) TryDeleteDir(dir, 5);
            if (removeData)
            {
                progress(0.75, "Removing HAULIX data");
                TryDeleteDir(DataDir, 5);
            }
            progress(1, "Done");
        }

        /// <summary>Hands the language picked in setup to HAULIX (read and removed on its next start).</summary>
        public static void SaveLanguage(string language)
        {
            if (string.IsNullOrEmpty(language)) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\HAULIX"))
                    k.SetValue("Language", language);
            }
            catch (Exception) { }
        }

        public static void Launch(string dir)
        {
            var exe = Path.Combine(dir, AppExe);
            if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir });
        }

        /// <summary>After uninstalling, delete the temporary uninstaller copy once it has exited.</summary>
        public static void ScheduleSelfDelete()
        {
            var self = Assembly.GetExecutingAssembly().Location;
            if (!self.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return;
            Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 3 /nobreak >nul & del /f /q \"" + self + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }

        private static void CreateShortcut(string path, string target, string workDir)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(type);
            try
            {
                dynamic lnk = shell.CreateShortcut(path);
                lnk.TargetPath = target;
                lnk.WorkingDirectory = workDir;
                lnk.IconLocation = target + ",0";
                lnk.Description = "HAULIX ETS2 Logger";
                lnk.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        private static long DirSize(string dir)
        {
            try { return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
            catch (Exception) { return 0; }
        }

        private static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { }
        }

        private static void TryDeleteDir(string dir, int attempts = 1)
        {
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    return;
                }
                catch (Exception)
                {
                    System.Threading.Thread.Sleep(600);
                }
            }
        }

        public const string PluginName = "scs-telemetry.dll";

        public static bool HasPlugin => Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("plugin/" + PluginName);

        /// <summary>
        /// Installs the bundled SCS telemetry plugin (RenCloud scs-sdk-plugin 1.12.1, MIT) into ETS2's plugin folder.
        /// Returns a short status for the UI. Other plugins in the folder are never touched.
        /// </summary>
        public static string InstallPlugin(string gameDir)
        {
            if (gameDir == null) return "ETS2 not found";
            var pluginDir = Path.Combine(gameDir, "bin", "win_x64", "plugins");
            Directory.CreateDirectory(pluginDir);
            var target = Path.Combine(pluginDir, PluginName);
            byte[] bundled;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("plugin/" + PluginName))
            {
                if (s == null) return "Plugin not bundled";
                bundled = new byte[s.Length];
                s.Read(bundled, 0, bundled.Length);
            }
            if (File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(bundled)) return "Telemetry plugin already installed";
            try
            {
                File.WriteAllBytes(target, bundled);
                return "Telemetry plugin installed";
            }
            catch (IOException)
            {
                throw new InvalidOperationException("The telemetry plugin could not be updated because ETS2 is running. Close ETS2 and run setup again.");
            }
        }

        /// <summary>ETS2 installation folder and whether the telemetry plugin is present.</summary>
        public static (bool Game, bool Plugin) DetectEts2()
        {
            var dir = FindEts2();
            return (dir != null, dir != null && File.Exists(Path.Combine(dir, "bin", "win_x64", "plugins", PluginName)));
        }

        public static string FindEts2()
        {
            try
            {
                var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (steam == null) return null;
                var libs = new System.Collections.Generic.List<string> { steam.Replace('/', '\\') };
                var vdf = Path.Combine(libs[0], "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                foreach (var lib in libs)
                {
                    var game = Path.Combine(lib, "steamapps", "common", "Euro Truck Simulator 2");
                    if (!File.Exists(Path.Combine(game, "bin", "win_x64", "eurotrucks2.exe"))) continue;
                    return game;
                }
            }
            catch (Exception) { }
            return null;
        }
    }
}
