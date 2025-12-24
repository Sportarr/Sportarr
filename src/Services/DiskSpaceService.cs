using System.Runtime.InteropServices;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for getting disk space information.
/// Handles Docker volumes correctly by reading /proc/mounts on Linux.
/// This matches Sonarr's approach for accurate disk space reporting.
/// </summary>
public class DiskSpaceService
{
    private readonly ILogger<DiskSpaceService> _logger;

    public DiskSpaceService(ILogger<DiskSpaceService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get available free space for a given path in bytes.
    /// On Linux/Docker, finds the correct mount point for the path.
    /// </summary>
    public long? GetAvailableSpace(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return null;
            }

            // Resolve any symbolic links to get the real path
            var realPath = GetRealPath(path);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetWindowsDiskSpace(realPath).AvailableFreeSpace;
            }
            else
            {
                // Linux/macOS - find the correct mount point
                var mount = FindMountPoint(realPath);
                if (mount != null)
                {
                    return mount.AvailableFreeSpace;
                }

                // Fallback to DriveInfo if mount detection fails
                _logger.LogDebug("Mount detection failed for {Path}, falling back to DriveInfo", path);
                var driveInfo = new DriveInfo(Path.GetPathRoot(realPath) ?? "/");
                return driveInfo.AvailableFreeSpace;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get available space for {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Get total disk space for a given path in bytes.
    /// On Linux/Docker, finds the correct mount point for the path.
    /// </summary>
    public long? GetTotalSpace(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return null;
            }

            // Resolve any symbolic links to get the real path
            var realPath = GetRealPath(path);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetWindowsDiskSpace(realPath).TotalSize;
            }
            else
            {
                // Linux/macOS - find the correct mount point
                var mount = FindMountPoint(realPath);
                if (mount != null)
                {
                    return mount.TotalSize;
                }

                // Fallback to DriveInfo if mount detection fails
                _logger.LogDebug("Mount detection failed for {Path}, falling back to DriveInfo", path);
                var driveInfo = new DriveInfo(Path.GetPathRoot(realPath) ?? "/");
                return driveInfo.TotalSize;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get total space for {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Get both available and total space for a path
    /// </summary>
    public (long? AvailableFreeSpace, long? TotalSize) GetDiskSpace(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return (null, null);
            }

            // Resolve any symbolic links to get the real path
            var realPath = GetRealPath(path);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var info = GetWindowsDiskSpace(realPath);
                return (info.AvailableFreeSpace, info.TotalSize);
            }
            else
            {
                // Linux/macOS - find the correct mount point
                var mount = FindMountPoint(realPath);
                if (mount != null)
                {
                    return (mount.AvailableFreeSpace, mount.TotalSize);
                }

                // Fallback to DriveInfo if mount detection fails
                _logger.LogDebug("Mount detection failed for {Path}, falling back to DriveInfo", path);
                var driveInfo = new DriveInfo(Path.GetPathRoot(realPath) ?? "/");
                return (driveInfo.AvailableFreeSpace, driveInfo.TotalSize);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get disk space for {Path}", path);
            return (null, null);
        }
    }

    /// <summary>
    /// Resolve symbolic links to get the real path
    /// </summary>
    private string GetRealPath(string path)
    {
        try
        {
            // Get the full path and resolve any relative components
            var fullPath = Path.GetFullPath(path);

            // On Unix, resolve symlinks
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Check if the path or any of its parents are symlinks
                var info = new FileInfo(fullPath);
                if (info.LinkTarget != null)
                {
                    return Path.GetFullPath(info.LinkTarget);
                }

                var dirInfo = new DirectoryInfo(fullPath);
                if (dirInfo.LinkTarget != null)
                {
                    return Path.GetFullPath(dirInfo.LinkTarget);
                }
            }

            return fullPath;
        }
        catch
        {
            return path;
        }
    }

    /// <summary>
    /// Find the mount point for a given path by reading /proc/mounts.
    /// Returns the mount with the longest matching path (most specific mount).
    /// This is critical for Docker volumes to get the correct disk space.
    /// </summary>
    private MountInfo? FindMountPoint(string path)
    {
        try
        {
            var mounts = GetMounts();
            if (mounts == null || mounts.Count == 0)
            {
                return null;
            }

            // Normalize path
            path = Path.GetFullPath(path);
            if (!path.EndsWith('/'))
            {
                path += '/';
            }

            // Find all mounts that contain this path, then pick the most specific (longest path)
            MountInfo? bestMount = null;
            var bestMatchLength = 0;

            foreach (var mount in mounts)
            {
                var mountPath = mount.MountPoint;
                if (!mountPath.EndsWith('/'))
                {
                    mountPath += '/';
                }

                // Check if the path starts with this mount point
                if (path.StartsWith(mountPath, StringComparison.Ordinal))
                {
                    if (mountPath.Length > bestMatchLength)
                    {
                        bestMatchLength = mountPath.Length;
                        bestMount = mount;
                    }
                }
            }

            if (bestMount != null)
            {
                _logger.LogDebug("Found mount point {MountPoint} for path {Path}", bestMount.MountPoint, path);
            }

            return bestMount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find mount point for {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Read mount information from /proc/mounts (Linux) or use DriveInfo (other platforms)
    /// </summary>
    private List<MountInfo>? GetMounts()
    {
        var mounts = new List<MountInfo>();

        try
        {
            // Try to read /proc/mounts (Linux)
            const string procMounts = "/proc/mounts";
            if (File.Exists(procMounts))
            {
                var lines = File.ReadAllLines(procMounts);
                foreach (var line in lines)
                {
                    var mount = ParseMountLine(line);
                    if (mount != null)
                    {
                        mounts.Add(mount);
                    }
                }

                _logger.LogDebug("Found {Count} mounts from /proc/mounts", mounts.Count);
                return mounts;
            }

            // Fallback: use DriveInfo for all drives
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    mounts.Add(new MountInfo
                    {
                        Device = drive.Name,
                        MountPoint = drive.RootDirectory.FullName,
                        FileSystem = drive.DriveFormat,
                        AvailableFreeSpace = drive.AvailableFreeSpace,
                        TotalSize = drive.TotalSize
                    });
                }
            }

            return mounts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get mounts");
            return null;
        }
    }

    /// <summary>
    /// Parse a line from /proc/mounts
    /// Format: device mount_point fs_type options dump pass
    /// Example: /dev/sda1 /data ext4 rw,relatime 0 0
    /// </summary>
    private MountInfo? ParseMountLine(string line)
    {
        try
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return null;
            }

            var device = parts[0];
            var mountPoint = UnescapeMountPath(parts[1]);
            var fileSystem = parts[2];

            // Skip virtual/system filesystems that don't represent real storage
            var skipFileSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sysfs", "proc", "devtmpfs", "devpts", "tmpfs", "securityfs",
                "cgroup", "cgroup2", "pstore", "debugfs", "hugetlbfs", "mqueue",
                "fusectl", "configfs", "binfmt_misc", "autofs", "overlay",
                "squashfs", "nsfs", "ramfs"
            };

            // Skip if virtual filesystem (but allow overlayfs mounts to real paths)
            if (skipFileSystems.Contains(fileSystem))
            {
                // Exception: Docker overlay mounts that represent real storage
                if (fileSystem != "overlay" || !mountPoint.StartsWith("/var/lib/docker"))
                {
                    return null;
                }
            }

            // Skip paths that are clearly not user data
            if (mountPoint.StartsWith("/proc") ||
                mountPoint.StartsWith("/sys") ||
                mountPoint.StartsWith("/dev") ||
                mountPoint.StartsWith("/run") ||
                mountPoint == "/")
            {
                // Allow root only if it's the only option
            }

            // Get disk space for this mount point
            try
            {
                var driveInfo = new DriveInfo(mountPoint);
                if (!driveInfo.IsReady)
                {
                    return null;
                }

                return new MountInfo
                {
                    Device = device,
                    MountPoint = mountPoint,
                    FileSystem = fileSystem,
                    AvailableFreeSpace = driveInfo.AvailableFreeSpace,
                    TotalSize = driveInfo.TotalSize
                };
            }
            catch
            {
                // Mount point might not be accessible
                return null;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Unescape octal sequences in mount paths (e.g., \040 for space)
    /// </summary>
    private string UnescapeMountPath(string path)
    {
        if (!path.Contains('\\'))
        {
            return path;
        }

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] == '\\' && i + 3 < path.Length &&
                char.IsDigit(path[i + 1]) && char.IsDigit(path[i + 2]) && char.IsDigit(path[i + 3]))
            {
                // Parse octal escape sequence
                var octal = path.Substring(i + 1, 3);
                var charValue = Convert.ToInt32(octal, 8);
                result.Append((char)charValue);
                i += 3;
            }
            else
            {
                result.Append(path[i]);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Get disk space on Windows using DriveInfo
    /// </summary>
    private DriveInfo GetWindowsDiskSpace(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            throw new ArgumentException("Cannot determine root for path", nameof(path));
        }

        return new DriveInfo(root);
    }

    /// <summary>
    /// Internal class to hold mount information
    /// </summary>
    private class MountInfo
    {
        public string Device { get; set; } = "";
        public string MountPoint { get; set; } = "";
        public string FileSystem { get; set; } = "";
        public long AvailableFreeSpace { get; set; }
        public long TotalSize { get; set; }
    }
}
