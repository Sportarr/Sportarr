namespace Sportarr
{
    using Emby.Web.GenericEdit;
    using Emby.Web.GenericEdit.Validation;
    using MediaBrowser.Model.Logging;
    using System.ComponentModel;
    using System.Net.Http;
    using System.Text.Json;

    /// <summary>
    /// Configuration options for the Sportarr plugin.
    /// Manages connection settings and validation for the Sportarr API integration.
    /// </summary>
    public class SportarrPluginOptions : EditableOptionsBase
    {
        /// <summary>
        /// Gets or sets the logger instance used for logging validation events and errors.
        /// This is set by the main plugin class during initialization.
        /// </summary>
        public static ILogger Logger { get; set; }

        /// <summary>
        /// Gets the title displayed in the configuration editor.
        /// </summary>
        public override string EditorTitle => "Sportarr Configuration";

        /// <summary>
        /// Gets the description displayed in the configuration editor.
        /// </summary>
        public override string EditorDescription => "Configure the connection to your Sportarr-API instance. This plugin fetches sports metadata (leagues, events, images) from Sportarr-API instead of external databases.\n";

        /// <summary>
        /// Gets or sets the base URL of the Sportarr API instance.
        /// Defaults to https://sportarr.net but can be changed for local instances.
        /// </summary>
        [DisplayName("Sportarr API URL:")]
        [Description("The URL of the Sportarr API (default: https://sportarr.net). Only change if you're running a local instance.")]
        [MediaBrowser.Model.Attributes.Required]
        public string txtApiUrl { get; set; } = "https://sportarr.net";

        /// <summary>
        /// Gets or sets a value indicating whether debug logging is enabled.
        /// When enabled, verbose logging information is written to the Emby logs.
        /// </summary>
        [DisplayName("Enable Debug Logging:")]
        [Description("Enable verbose logging for troubleshooting. Check Emby logs for [Sportarr] entries.")]
        [MediaBrowser.Model.Attributes.Required]
        public bool chkDebugLogging { get; set; } = false;

        /// <summary>
        /// Validates the plugin configuration options.
        /// Checks that the API URL is properly formatted and that the Sportarr API is reachable and healthy.
        /// </summary>
        /// <param name="context">The validation context used to report validation errors.</param>
        protected override void Validate(ValidationContext context)
        {
            // Validate URL format
            if (!Uri.TryCreate(txtApiUrl, UriKind.Absolute, out Uri uriResult) ||
                (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
            {
                context.AddValidationError("txtApiUrl", "Please enter a valid HTTP or HTTPS URL.");
                return;
            }

            // Validate URL is reachable and returns valid health response
            try
            {
                var healthResponse = CheckUrlReachable(txtApiUrl).GetAwaiter().GetResult();
                if (healthResponse == null)
                {
                    context.AddValidationError("txtApiUrl", "Unable to reach the Sportarr API at the specified URL. Please verify the URL is correct and the service is running.");
                }
                else if (healthResponse.Status != "healthy")
                {
                    context.AddValidationError("txtApiUrl", $"Sportarr API returned unhealthy status: {healthResponse.Status}");
                }
                else
                {
                    Logger?.Info($"[Sportarr] API Url successfully validated --> {txtApiUrl} // {healthResponse.Status} // {healthResponse.Version} // {healthResponse.Build} // {healthResponse.Timestamp}");
                }
            }
            catch (JsonException)
            {
                context.AddValidationError("txtApiUrl", "The URL responded but did not return a valid Sportarr API health response.");
            }
            catch (Exception ex)
            {
                Logger?.Error($"[Sportarr] Error validating API URL: {ex}");
                context.AddValidationError("txtApiUrl", $"Error connecting to Sportarr API: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the Sportarr API is reachable at the specified URL by calling the health endpoint.
        /// </summary>
        /// <param name="url">The base URL of the Sportarr API instance.</param>
        /// <returns>
        /// A <see cref="SportarrHealthResponse"/> object containing the health status if successful; 
        /// otherwise, null if the API is unreachable or returns an unsuccessful status code.
        /// </returns>
        static async Task<SportarrHealthResponse> CheckUrlReachable(string url)
        {
            var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr-Emby-Client/1.0");

            var healthCheckUrl = url.TrimEnd('/') + "/api/health";
            var response = await httpClient.GetAsync(healthCheckUrl);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            var healthResponse = System.Text.Json.JsonSerializer.Deserialize<SportarrHealthResponse>(jsonString);

            return healthResponse;
        }
    }
}