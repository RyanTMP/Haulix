namespace Haulix.App;

internal static class Program
{
    private const string MutexName = "Haulix.ETS2Logger.SingleInstance";
    private const string ActivateEventName = "Haulix.ETS2Logger.Activate";

    [STAThread]
    private static void Main(string[] args)
    {
        // Windows "Uninstall" calls Haulix.exe --uninstall: the uninstaller is stored as uninstall.bin so Haulix.exe
        // stays the only program in the folder; run it from a temp copy so the whole folder can be removed.
        if (args.Contains("--uninstall"))
        {
            var dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var data = Path.Combine(dir, "uninstall.bin");
            if (!File.Exists(data))
            {
                MessageBox.Show("The HAULIX uninstaller (uninstall.bin) is missing. Reinstall HAULIX, then uninstall it.", "HAULIX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var copy = Path.Combine(Path.GetTempPath(), $"HAULIX-Uninstall-{Guid.NewGuid():N}"[..24] + ".exe");
            File.Copy(data, copy, true);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(copy, $"--uninstall-from \"{dir}\"") { UseShellExecute = false });
            return;
        }

        using var mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            // Another HAULIX window is open: ask it to come to the front.
            try
            {
                using var activate = EventWaitHandle.OpenExisting(ActivateEventName);
                activate.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            return;
        }

        using var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        ApplicationConfiguration.Initialize();

        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haulix");
        var form = new MainForm(dataFolder, startMinimized: args.Contains("--minimized"));

        var waiter = ThreadPool.RegisterWaitForSingleObject(activateEvent, (_, _) =>
        {
            if (form.IsHandleCreated) form.BeginInvoke(form.BringToFrontFromOtherInstance);
        }, null, Timeout.Infinite, false);

        Application.Run(form);
        waiter.Unregister(null);
    }
}
