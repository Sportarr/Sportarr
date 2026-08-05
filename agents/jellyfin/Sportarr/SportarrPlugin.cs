using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Sportarr
{
    /// <summary>
    /// Sportarr Plugin for Jellyfin.
    /// Provides sports metadata from Sportarr-API.
    /// </summary>
    public class SportarrPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public SportarrPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Gets the plugin instance.
        /// </summary>
        public static SportarrPlugin? Instance { get; private set; }

        /// <summary>
        /// Gets the plugin name.
        /// </summary>
        public override string Name => "Sportarr";

        /// <summary>
        /// Gets the plugin GUID.
        /// </summary>
        public override Guid Id => new Guid("aeb8dfd3-eb78-4785-9b14-34bfd2dfc4dd");

        /// <summary>
        /// Gets the plugin description.
        /// </summary>
        public override string Description => "Sports metadata provider powered by Sportarr-API";

        /// <summary>
        /// Gets the web pages for plugin configuration.
        /// </summary>
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
                }
            };
        }
    }

    /// <summary>
    /// Plugin configuration.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Gets or sets the Sportarr API URL.
        /// </summary>
        public string SportarrApiUrl { get; set; } = "https://sportarr.net";

        /// <summary>
        /// Gets or sets whether to enable debug logging.
        /// </summary>
        public bool EnableDebugLogging { get; set; } = false;

        /// <summary>
        /// Gets or sets whether the event date is appended to episode titles.
        /// Playoff series produce several games between the same two teams in
        /// quick succession; the date in the title is what tells them apart
        /// on playback clients that don't surface the aired-on field.
        /// </summary>
        public bool IncludeDateInEpisodeTitles { get; set; } = true;

        /// <summary>
        /// Gets or sets the image cache duration in hours.
        /// </summary>
        public int ImageCacheHours { get; set; } = 24;
    }
}
