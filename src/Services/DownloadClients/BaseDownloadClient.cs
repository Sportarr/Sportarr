using System.Net.Http.Json;
using System.Text.Json;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services.DownloadClients;

/// <summary>
/// Base class for download client implementations.
/// Provides common functionality for HTTP communication, SSL handling, and error logging.
/// </summary>
public abstract class BaseDownloadClient<TClient> where TClient : class
{
    protected readonly HttpClient _httpClient;
    protected readonly ILogger<TClient> _logger;
    protected HttpClient? _customHttpClient;

    /// <summary>
    /// Name of the client for logging purposes
    /// </summary>
    protected abstract string ClientName { get; }

    protected BaseDownloadClient(HttpClient httpClient, ILogger<TClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Get HttpClient for requests - creates custom client with SSL bypass if needed
    /// </summary>
    protected virtual HttpClient GetHttpClient(DownloadClient config)
    {
        if (config.UseSsl && config.DisableSslCertificateValidation)
        {
            if (_customHttpClient == null)
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };
                _customHttpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(100) };
                OnCustomClientCreated(_customHttpClient);
            }
            return _customHttpClient;
        }

        return _httpClient;
    }

    /// <summary>
    /// Called when a custom HttpClient is created for SSL bypass.
    /// Override to add headers or configure the client.
    /// </summary>
    protected virtual void OnCustomClientCreated(HttpClient client)
    {
    }

    /// <summary>
    /// Get the base URL for API requests
    /// </summary>
    protected virtual string GetBaseUrl(DownloadClient config)
    {
        var protocol = config.UseSsl ? "https" : "http";
        var baseUrl = $"{protocol}://{config.Host}:{config.Port}";

        if (!string.IsNullOrEmpty(config.UrlBase))
        {
            baseUrl += config.UrlBase.TrimEnd('/');
        }

        return baseUrl;
    }

    /// <summary>
    /// Execute an HTTP GET request with JSON deserialization
    /// </summary>
    protected async Task<T?> GetJsonAsync<T>(DownloadClient config, string url)
    {
        try
        {
            var client = GetHttpClient(config);
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ClientName}] Failed to GET {Url}", ClientName, url);
            throw;
        }
    }

    /// <summary>
    /// Execute an HTTP POST request with JSON body
    /// </summary>
    protected async Task<HttpResponseMessage> PostJsonAsync<T>(DownloadClient config, string url, T content)
    {
        try
        {
            var client = GetHttpClient(config);
            return await client.PostAsJsonAsync(url, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ClientName}] Failed to POST to {Url}", ClientName, url);
            throw;
        }
    }

    /// <summary>
    /// Log an SSL/TLS connection error with helpful diagnostics
    /// </summary>
    protected void LogSslError(Exception ex, DownloadClient config)
    {
        _logger.LogError(ex,
            "[{ClientName}] SSL/TLS connection failed for {Host}:{Port}. " +
            "This usually means SSL is enabled in Sportarr but the port is serving HTTP, not HTTPS. " +
            "Please ensure HTTPS is enabled in {ClientName} settings, or disable SSL in Sportarr.",
            ClientName, config.Host, config.Port, ClientName);
    }

    /// <summary>
    /// Log a connection test failure
    /// </summary>
    protected void LogConnectionError(Exception ex)
    {
        _logger.LogError(ex, "[{ClientName}] Connection test failed", ClientName);
    }

    /// <summary>
    /// Handle common exceptions for connection tests
    /// </summary>
    protected bool HandleConnectionTestException(Exception ex, DownloadClient config)
    {
        if (ex is HttpRequestException httpEx &&
            httpEx.InnerException is System.Security.Authentication.AuthenticationException)
        {
            LogSslError(ex, config);
        }
        else
        {
            LogConnectionError(ex);
        }
        return false;
    }

    /// <summary>
    /// Test connection to the download client
    /// </summary>
    public abstract Task<bool> TestConnectionAsync(DownloadClient config);
}

/// <summary>
/// Common interface for torrent download clients
/// </summary>
public interface ITorrentClient
{
    Task<bool> TestConnectionAsync(DownloadClient config);
    Task<string?> AddTorrentAsync(DownloadClient config, string torrentUrl, string category);
    Task<AddDownloadResult> AddTorrentWithResultAsync(DownloadClient config, string torrentUrl, string category, string? expectedName = null);
    Task<bool> DeleteTorrentAsync(DownloadClient config, string torrentId, bool deleteFiles);
    Task<bool> PauseTorrentAsync(DownloadClient config, string torrentId);
    Task<bool> ResumeTorrentAsync(DownloadClient config, string torrentId);
    Task<DownloadClientStatus?> GetTorrentStatusAsync(DownloadClient config, string torrentId);
}

/// <summary>
/// Common interface for usenet download clients
/// </summary>
public interface IUsenetClient
{
    Task<bool> TestConnectionAsync(DownloadClient config);
    Task<string?> AddNzbAsync(DownloadClient config, string nzbUrl, string category);
    Task<bool> DeleteDownloadAsync(DownloadClient config, string nzoId, bool deleteFiles);
    Task<bool> PauseDownloadAsync(DownloadClient config, string nzoId);
    Task<bool> ResumeDownloadAsync(DownloadClient config, string nzoId);
    Task<DownloadClientStatus?> GetDownloadStatusAsync(DownloadClient config, string nzoId);
}
