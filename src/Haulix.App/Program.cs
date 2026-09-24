namespace Haulix.App;

internal static class Program
{
    private const string MutexName = "Haulix.ETS2Logger.SingleInstance";
    private const string ActivateEventName = "Haulix.ETS2Logger.Activate";

    [STAThread]
    private static void Main(string[] args)
    {
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
