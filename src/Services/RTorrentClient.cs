using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// rTorrent/ruTorrent XML-RPC client for Sportarr
/// Implements rTorrent XML-RPC protocol
/// </summary>
public class RTorrentClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RTorrentClient> _logger;

    public RTorrentClient(HttpClient httpClient, ILogger<RTorrentClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Test connection to rTorrent
    /// </summary>
    public async Task<bool> TestConnectionAsync(DownloadClient config)
    {
        try
        {
            ConfigureClient(config);

            // Test with system.client_version
            var response = await SendXmlRpcRequestAsync("system.client_version", Array.Empty<object>());
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Add torrent from URL
    /// NOTE: Does NOT specify directory - rTorrent uses its own configured directory
    /// This matches Sonarr/Radarr behavior
    /// </summary>
    public async Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string category)
    {
        try
        {
            ConfigureClient(config);

            // Add torrent and start it - do NOT set directory, use rTorrent's default
            var response = await SendXmlRpcRequestAsync("load.start", new object[] { "", torrentUrl });

            if (response != null)
            {
                _logger.LogInformation("[rTorrent] Torrent added from URL: {Url}", torrentUrl);

                // rTorrent doesn't return hash directly, need to get latest torrent
                await Task.Delay(500);
                var torrents = await GetTorrentsAsync(config);
                var latest = torrents?.OrderByDescending(t => t.TimeAdded).FirstOrDefault();

                return latest?.Hash;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error adding torrent");
            return null;
        }
    }

    /// <summary>
    /// Get all torrents
    /// </summary>
    public async Task<List<RTorrentTorrent>?> GetTorrentsAsync(DownloadClient config)
    {
        try
        {
            ConfigureClient(config);

            // Use d.multicall2 to get all torrents with multiple fields
            var fields = new[] { "d.hash=", "d.name=", "d.size_bytes=", "d.completed_bytes=",
                                "d.up.total=", "d.state=", "d.down.rate=", "d.up.rate=",
                                "d.directory=", "d.custom1=", "d.creation_date=" };

            var response = await SendXmlRpcRequestAsync("d.multicall2", new object[] { "", "main" }.Concat(fields).ToArray());

            if (response != null)
            {
                var torrents = ParseMulticallResponse(response);
                return torrents;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error getting torrents");
            return null;
        }
    }

    /// <summary>
    /// Get torrent by hash
    /// </summary>
    public async Task<RTorrentTorrent?> GetTorrentAsync(DownloadClient config, string hash)
    {
        var torrents = await GetTorrentsAsync(config);
        return torrents?.FirstOrDefault(t => t.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Start torrent
    /// </summary>
    public async Task<bool> StartTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, "d.start", hash);
    }

    /// <summary>
    /// Stop torrent
    /// </summary>
    public async Task<bool> StopTorrentAsync(DownloadClient config, string hash)
    {
        return await ControlTorrentAsync(config, "d.stop", hash);
    }

    /// <summary>
    /// Pause torrent (same as stop in rTorrent)
    /// </summary>
    public async Task<bool> PauseTorrentAsync(DownloadClient config, string hash)
    {
        return await StopTorrentAsync(config, hash);
    }

    /// <summary>
    /// Resume torrent (same as start in rTorrent)
    /// </summary>
    public async Task<bool> ResumeTorrentAsync(DownloadClient config, string hash)
    {
        return await StartTorrentAsync(config, hash);
    }

    /// <summary>
    /// Get torrent status for download monitoring
    /// </summary>
    public async Task<DownloadClientStatus?> GetTorrentStatusAsync(DownloadClient config, string hash)
    {
        var torrent = await GetTorrentAsync(config, hash);
        if (torrent == null)
        {
            _logger.LogWarning("[RTorrent] Torrent not found: {Hash}", hash);
            return null;
        }

        // Determine status based on state and progress
        // rTorrent state: 0=stopped, 1=started
        var isComplete = torrent.CompletedBytes >= torrent.TotalSize && torrent.TotalSize > 0;
        var isStarted = torrent.State == 1;

        string status;
        if (!isStarted)
            status = "paused";
        else if (isComplete)
            status = "completed";
        else if (torrent.DownloadRate > 0)
            status = "downloading";
        else
            status = "queued";

        // Calculate progress (0-100)
        var progress = torrent.TotalSize > 0
            ? (double)torrent.CompletedBytes / torrent.TotalSize * 100
            : 0;

        // Calculate time remaining
        TimeSpan? timeRemaining = null;
        if (torrent.DownloadRate > 0 && !isComplete)
        {
            var remainingBytes = torrent.TotalSize - torrent.CompletedBytes;
            var secondsRemaining = remainingBytes / torrent.DownloadRate;
            timeRemaining = TimeSpan.FromSeconds(secondsRemaining);
        }

        return new DownloadClientStatus
        {
            Status = status,
            Progress = progress,
            Downloaded = torrent.CompletedBytes,
            Size = torrent.TotalSize,
            TimeRemaining = timeRemaining,
            SavePath = torrent.Directory,
            ErrorMessage = null
        };
    }

    /// <summary>
    /// Delete torrent
    /// </summary>
    public async Task<bool> DeleteTorrentAsync(DownloadClient config, string hash, bool deleteFiles = false)
    {
        try
        {
            ConfigureClient(config);

            if (deleteFiles)
            {
                // Delete with files using d.erase
                var response = await SendXmlRpcRequestAsync("d.erase", new object[] { hash });
                return response != null;
            }
            else
            {
                // Just remove from client
                var response = await SendXmlRpcRequestAsync("d.close", new object[] { hash });
                return response != null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error deleting torrent");
            return false;
        }
    }

    // Private helper methods

    private void ConfigureClient(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";
        _httpClient.BaseAddress = new Uri($"{protocol}://{config.Host}:{config.Port}/RPC2");

        if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.Username}:{config.Password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    private async Task<string?> SendXmlRpcRequestAsync(string method, object[] parameters)
    {
        try
        {
            var xmlRequest = BuildXmlRpcRequest(method, parameters);
            var content = new StringContent(xmlRequest, Encoding.UTF8, "text/xml");

            var response = await _httpClient.PostAsync("", content);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            _logger.LogWarning("[rTorrent] XML-RPC request failed: {Status}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] XML-RPC request error");
            return null;
        }
    }

    private async Task<bool> ControlTorrentAsync(DownloadClient config, string method, string hash)
    {
        try
        {
            ConfigureClient(config);
            var response = await SendXmlRpcRequestAsync(method, new object[] { hash });
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error controlling torrent");
            return false;
        }
    }

    private static string BuildXmlRpcRequest(string method, object[] parameters)
    {
        var methodCall = new XElement("methodCall",
            new XElement("methodName", method),
            new XElement("params",
                parameters.Select(p => new XElement("param",
                    new XElement("value",
                        new XElement("string", p)
                    )
                ))
            )
        );

        return $"<?xml version=\"1.0\"?>{methodCall}";
    }

    private List<RTorrentTorrent> ParseMulticallResponse(string xml)
    {
        var torrents = new List<RTorrentTorrent>();

        try
        {
            var doc = XDocument.Parse(xml);
            var arrays = doc.Descendants("array").FirstOrDefault();

            if (arrays == null) return torrents;

            foreach (var data in arrays.Descendants("data"))
            {
                var values = data.Descendants("value").Select(v => v.Value).ToArray();

                if (values.Length >= 11)
                {
                    torrents.Add(new RTorrentTorrent
                    {
                        Hash = values[0],
                        Name = values[1],
                        TotalSize = long.TryParse(values[2], out var size) ? size : 0,
                        CompletedBytes = long.TryParse(values[3], out var completed) ? completed : 0,
                        TotalUploaded = long.TryParse(values[4], out var uploaded) ? uploaded : 0,
                        State = int.TryParse(values[5], out var state) ? state : 0,
                        DownloadRate = long.TryParse(values[6], out var dlRate) ? dlRate : 0,
                        UploadRate = long.TryParse(values[7], out var ulRate) ? ulRate : 0,
                        Directory = values[8],
                        Label = values[9],
                        TimeAdded = long.TryParse(values[10], out var added) ? added : 0
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[rTorrent] Error parsing multicall response");
        }

        return torrents;
    }
}

/// <summary>
/// rTorrent torrent information
/// </summary>
public class RTorrentTorrent
{
    public string Hash { get; set; } = "";
    public string Name { get; set; } = "";
    public long TotalSize { get; set; }
    public long CompletedBytes { get; set; }
    public long TotalUploaded { get; set; }
    public int State { get; set; } // 0=stopped, 1=started
    public long DownloadRate { get; set; } // bytes/s
    public long UploadRate { get; set; } // bytes/s
    public string Directory { get; set; } = "";
    public string Label { get; set; } = "";
    public long TimeAdded { get; set; } // Unix timestamp
}
