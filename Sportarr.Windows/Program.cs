using System.Diagnostics;

namespace Sportarr.Windows;

static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main(string[] args)
    {
        // Ensure only one instance runs at a time
        const string mutexName = "Sportarr-SingleInstance-Mutex";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "Sportarr is already running.\n\nCheck the system tray for the Sportarr icon.",
                "Sportarr",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // Run the tray application
        Application.Run(new TrayApplicationContext());

        // Release mutex on exit
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
