using System.Diagnostics;
using Microsoft.Win32;

namespace Sportarr.Windows;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _contextMenu;
    private Process? _apiProcess;
    private readonly string _apiExePath;
    private readonly string _dataPath;
    private readonly int _port = 1867;
    private bool _isStarting = false;

    public TrayApplicationContext()
    {
        // Determine paths
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _apiExePath = Path.Combine(baseDir, "Sportarr.exe");
        _dataPath = Path.Combine(baseDir, "data");

        // Try to read port from config if it exists
        var configPath = Path.Combine(_dataPath, "config.xml");
        if (File.Exists(configPath))
        {
            try
            {
                var configContent = File.ReadAllText(configPath);
                var portMatch = System.Text.RegularExpressions.Regex.Match(configContent, @"<Port>(\d+)</Port>");
                if (portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out int configPort))
                {
                    _port = configPort;
                }
            }
            catch { /* Use default port */ }
        }

        // Create context menu
        _contextMenu = new ContextMenuStrip();

        var openMenuItem = new ToolStripMenuItem("Open Sportarr", null, OnOpen);
        openMenuItem.Font = new Font(openMenuItem.Font, FontStyle.Bold);
        _contextMenu.Items.Add(openMenuItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        _contextMenu.Items.Add(new ToolStripMenuItem("Start", null, OnStart));
        _contextMenu.Items.Add(new ToolStripMenuItem("Stop", null, OnStop));
        _contextMenu.Items.Add(new ToolStripMenuItem("Restart", null, OnRestart));

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
            Text = "Sportarr"
        };

        _trayIcon.DoubleClick += OnOpen;

        // Update menu state
        _contextMenu.Opening += (s, e) => UpdateMenuState();

        // Start the API
        StartApi();
    }

    private Icon LoadIcon()
    {
        // Try to load icon from file first
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sportarr.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                return new Icon(iconPath);
            }
            catch { }
        }

        // Fall back to embedded resource or default
        try
        {
            using var stream = typeof(TrayApplicationContext).Assembly.GetManifestResourceStream("Sportarr.Windows.sportarr.ico");
            if (stream != null)
            {
                return new Icon(stream);
            }
        }
        catch { }

        // Use default application icon
        return SystemIcons.Application;
    }

    private void UpdateMenuState()
    {
        bool isRunning = IsApiRunning();

        // Start menu item
        var startItem = _contextMenu.Items[2] as ToolStripMenuItem;
        if (startItem != null)
        {
            startItem.Enabled = !isRunning && !_isStarting;
        }

        // Stop menu item
        var stopItem = _contextMenu.Items[3] as ToolStripMenuItem;
        if (stopItem != null)
        {
            stopItem.Enabled = isRunning;
        }

        // Restart menu item
        var restartItem = _contextMenu.Items[4] as ToolStripMenuItem;
        if (restartItem != null)
        {
            restartItem.Enabled = isRunning;
        }

        // Update startup checkbox
        var startupItem = _contextMenu.Items[6] as ToolStripMenuItem;
        if (startupItem != null)
        {
            startupItem.Checked = IsStartupEnabled();
        }

        // Update tooltip
        _trayIcon.Text = isRunning ? "Sportarr (Running)" : _isStarting ? "Sportarr (Starting...)" : "Sportarr (Stopped)";
    }

    private bool IsApiRunning()
    {
        if (_apiProcess == null) return false;

        try
        {
            return !_apiProcess.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private void StartApi()
    {
        if (IsApiRunning() || _isStarting) return;

        if (!File.Exists(_apiExePath))
        {
            ShowBalloon("Error", $"Sportarr.Api.exe not found at:\n{_apiExePath}", ToolTipIcon.Error);
            return;
        }

        _isStarting = true;
        UpdateMenuState();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _apiExePath,
                WorkingDirectory = Path.GetDirectoryName(_apiExePath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            _apiProcess = Process.Start(startInfo);

            if (_apiProcess != null)
            {
                _apiProcess.EnableRaisingEvents = true;
                _apiProcess.Exited += OnApiProcessExited;

                // Wait a moment then show balloon
                Task.Delay(2000).ContinueWith(_ =>
                {
                    _isStarting = false;
                    if (IsApiRunning())
                    {
                        ShowBalloon("Sportarr", $"Sportarr is running on port {_port}", ToolTipIcon.Info);
                    }
                });
            }
            else
            {
                _isStarting = false;
                ShowBalloon("Error", "Failed to start Sportarr", ToolTipIcon.Error);
            }
        }
        catch (Exception ex)
        {
            _isStarting = false;
            ShowBalloon("Error", $"Failed to start Sportarr:\n{ex.Message}", ToolTipIcon.Error);
        }
    }

    private void StopApi()
    {
        if (!IsApiRunning()) return;

        try
        {
            _apiProcess?.Kill(entireProcessTree: true);
            _apiProcess?.WaitForExit(5000);
            _apiProcess?.Dispose();
            _apiProcess = null;

            ShowBalloon("Sportarr", "Sportarr has been stopped", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ShowBalloon("Error", $"Failed to stop Sportarr:\n{ex.Message}", ToolTipIcon.Error);
        }
    }

    private void OnApiProcessExited(object? sender, EventArgs e)
    {
        _isStarting = false;
        _apiProcess?.Dispose();
        _apiProcess = null;

        // Only show notification if it wasn't a deliberate stop
        // (We set _apiProcess to null before showing the stop notification)
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = text;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(3000);
    }

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

    private void OnStart(object? sender, EventArgs e)
    {
        StartApi();
    }

    private void OnStop(object? sender, EventArgs e)
    {
        StopApi();
    }

    private void OnRestart(object? sender, EventArgs e)
    {
        StopApi();
        Task.Delay(1000).ContinueWith(_ => StartApi());
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
            var exePath = Application.ExecutablePath;
            key?.SetValue("Sportarr", $"\"{exePath}\"");

            ShowBalloon("Sportarr", "Sportarr will start automatically with Windows", ToolTipIcon.Info);
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
        // Ask for confirmation if API is running
        if (IsApiRunning())
        {
            var result = MessageBox.Show(
                "Sportarr is still running. Do you want to stop it and exit?",
                "Sportarr",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            StopApi();
        }

        // Clean up
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _contextMenu.Dispose();

        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopApi();
            _trayIcon?.Dispose();
            _contextMenu?.Dispose();
        }
        base.Dispose(disposing);
    }
}
