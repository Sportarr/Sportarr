#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Sportarr.Windows;

/// <summary>
/// Windows system tray icon for Sportarr (Sonarr/Radarr style)
/// </summary>
public class WindowsTrayIcon : IDisposable
{
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly int _port;
    private readonly CancellationTokenSource _appShutdown;
    private bool _disposed;

    // P/Invoke to hide/show console window.
    // ShowWindow alone is unreliable on non-elevated Windows: the console is
    // owned by conhost.exe and SW_HIDE often only minimizes to the taskbar
    // instead of fully hiding. FreeConsole detaches the process from the
    // console entirely, which works regardless of elevation; AllocConsole
    // brings a fresh one back when the user wants to see logs again.
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    public WindowsTrayIcon(int port, CancellationTokenSource appShutdown)
    {
        _port = port;
        _appShutdown = appShutdown;

        // Create context menu
        _contextMenu = new ContextMenuStrip();

        var openMenuItem = new ToolStripMenuItem("Open Sportarr", null, OnOpen);
        openMenuItem.Font = new Font(openMenuItem.Font, FontStyle.Bold);
        _contextMenu.Items.Add(openMenuItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        _contextMenu.Items.Add(new ToolStripMenuItem("Show Console", null, OnShowConsole));
        _contextMenu.Items.Add(new ToolStripMenuItem("Hide Console", null, OnHideConsole));

        _contextMenu.Items.Add(new ToolStripSeparator());

        var startupMenuItem = new ToolStripMenuItem("Start with Windows", null, OnToggleStartup);
        startupMenuItem.Checked = IsStartupEnabled();
        _contextMenu.Items.Add(startupMenuItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        _contextMenu.Items.Add(new ToolStripMenuItem("Exit", null, OnExit));

        // Create tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = _contextMenu,
            Visible = true,
            Text = $"Sportarr (Port {_port})"
        };

        _trayIcon.DoubleClick += OnOpen;

        // Update menu state when opening
        _contextMenu.Opening += (s, e) => UpdateMenuState();
    }

    private Icon LoadIcon()
    {
        // Try to load icon from file
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "Icons", "favicon.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                // favicon.ico might be a PNG with wrong extension, try loading as icon first
                return new Icon(iconPath);
            }
            catch
            {
                // If it fails, it might be a PNG - fall back to default
            }
        }

        // Use default application icon
        return SystemIcons.Application;
    }

    private void UpdateMenuState()
    {
        var consoleWindow = GetConsoleWindow();
        bool isConsoleVisible = consoleWindow != IntPtr.Zero && IsWindowVisible(consoleWindow);

        // Show Console menu item
        var showItem = _contextMenu.Items[2] as ToolStripMenuItem;
        if (showItem != null)
        {
            showItem.Enabled = !isConsoleVisible;
        }

        // Hide Console menu item
        var hideItem = _contextMenu.Items[3] as ToolStripMenuItem;
        if (hideItem != null)
        {
            hideItem.Enabled = isConsoleVisible;
        }

        // Update startup checkbox
        var startupItem = _contextMenu.Items[5] as ToolStripMenuItem;
        if (startupItem != null)
        {
            startupItem.Checked = IsStartupEnabled();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private void OnOpen(object? sender, EventArgs e)
    {
        var url = $"http://localhost:{_port}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to open browser:\n{ex.Message}\n\nManually navigate to: {url}",
                "Sportarr",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnShowConsole(object? sender, EventArgs e)
    {
        ShowConsoleWindow();
    }

    private void OnHideConsole(object? sender, EventArgs e)
    {
        HideConsoleWindow();
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        if (IsStartupEnabled())
        {
            DisableStartup();
        }
        else
        {
            EnableStartup();
        }

        // Update checkbox
        if (sender is ToolStripMenuItem item)
        {
            item.Checked = IsStartupEnabled();
        }
    }

    private bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("Sportarr") != null;
        }
        catch
        {
            return false;
        }
    }

    private void EnableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (!string.IsNullOrEmpty(exePath))
            {
                // Add --tray flag so it starts minimized to tray
                key?.SetValue("Sportarr", $"\"{exePath}\" --tray");
                ShowBalloon("Sportarr", "Sportarr will start automatically with Windows", ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to enable startup:\n{ex.Message}",
                "Sportarr",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void DisableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            key?.DeleteValue("Sportarr", false);
            ShowBalloon("Sportarr", "Sportarr will no longer start automatically", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to disable startup:\n{ex.Message}",
                "Sportarr",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnExit(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to exit Sportarr?",
            "Sportarr",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            // Signal the application to shut down
            _appShutdown.Cancel();
        }
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = text;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(3000);
    }

    /// <summary>
    /// Hide the console window (for --tray mode and "Hide Console" menu item).
    /// Uses FreeConsole rather than ShowWindow(SW_HIDE) because non-elevated
    /// Windows otherwise just minimizes the console to the taskbar.
    /// </summary>
    public static void HideConsole() => HideConsoleWindow();

    /// <summary>
    /// Show the console window. Allocates a fresh console if the previous one
    /// was freed (the console history is lost but new log lines will appear).
    /// </summary>
    public static void ShowConsole() => ShowConsoleWindow();

    private static void HideConsoleWindow()
    {
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow == IntPtr.Zero) return;

        // Try the simple hide first - works on most elevated and some non-
        // elevated configurations.
        ShowWindow(consoleWindow, SW_HIDE);

        // Detach the console entirely so the window cannot end up on the
        // taskbar. After FreeConsole the process has no console; subsequent
        // Console.* writes are silently dropped until AllocConsole is called.
        // Sportarr's logs continue going to the rolling file sink regardless.
        FreeConsole();
    }

    private static void ShowConsoleWindow()
    {
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
        {
            ShowWindow(consoleWindow, SW_SHOW);
            return;
        }

        // Console was previously freed - allocate a new one and re-route
        // standard streams so any subsequent Console.WriteLine reaches it.
        if (!AllocConsole()) return;

        try
        {
            var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdErr);
        }
        catch
        {
            // Best-effort restore; if it fails the new console will still
            // appear, just without console output (file logs unaffected).
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _contextMenu.Dispose();
    }
}
#endif
