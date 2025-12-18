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

    // P/Invoke to hide/show console window
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
        {
            ShowWindow(consoleWindow, SW_SHOW);
        }
    }

    private void OnHideConsole(object? sender, EventArgs e)
    {
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
        {
            ShowWindow(consoleWindow, SW_HIDE);
        }
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
    /// Hide the console window (for --tray mode)
    /// </summary>
    public static void HideConsole()
    {
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
        {
            ShowWindow(consoleWindow, SW_HIDE);
        }
    }

    /// <summary>
    /// Show the console window
    /// </summary>
    public static void ShowConsole()
    {
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
        {
            ShowWindow(consoleWindow, SW_SHOW);
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
