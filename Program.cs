using MonitorControl.Native;
using MonitorControl.UI;

namespace MonitorControl;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var isRelaunch = args.Any(a => string.Equals(a, "--relaunch", StringComparison.OrdinalIgnoreCase));
        using var mutex = new Mutex(initiallyOwned: false, @"Local\MonitorControl_SingleInstance_Mutex");

        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(isRelaunch ? 2500 : 0, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            if (DisplayNative.WmActivateExistingInstance != 0)
            {
                DisplayNative.PostMessage(
                    DisplayNative.HwndBroadcast,
                    DisplayNative.WmActivateExistingInstance,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
            return;
        }

        try
        {
            // Visual styles come from the Common-Controls v6 entry in app.manifest.
            // Application.EnableVisualStyles() would need a temporary file that some
            // restricted environments deny, which kills a single-file build on startup.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
