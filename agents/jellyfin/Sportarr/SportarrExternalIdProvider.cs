using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Sportarr
{
    /// <summary>
    /// Registers the Sportarr provider ID as a known external identifier for series (leagues).
    /// This surfaces the stored "Sportarr" provider ID (e.g. "lg-000028") as a labelled field
    /// in the Jellyfin metadata editor, alongside IMDb/TheTVDB IDs.
    /// </summary>
    public class SportarrSeriesExternalId : IExternalId
    {
        /// <summary>
        /// Gets the display label shown for this external ID in the metadata editor.
        /// </summary>
        public string ProviderName => "Sportarr";

        /// <summary>
        /// Gets the provider key. Must match the key used by SetProviderId("Sportarr", ...)
        /// so the editor field binds to the stored value.
        /// </summary>
        public string Key => "Sportarr";

        /// <summary>
        /// Gets the media type this external ID applies to, so Jellyfin routes it to the
        /// correct (series) metadata editor.
        /// </summary>
        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

        /// <summary>
        /// Gets the URL format string for linking out to the ID. The "{0}" placeholder is
        /// replaced with the stored Sportarr ID to produce a clickable link.
        /// </summary>
        public string? UrlFormatString => "https://sportarr.net/api/metadata/agents/series/{0}";

        /// <summary>
        /// Determines whether this external ID applies to the given item.
        /// </summary>
        /// <param name="item">The item to test.</param>
        /// <returns>True for <see cref="Series"/> items; otherwise false.</returns>
        public bool Supports(IHasProviderIds item) => item is Series;
    }

    /// <summary>
    /// Registers the Sportarr provider ID as a known external identifier for episodes
    /// (matches/events). This surfaces the stored "Sportarr" provider ID as a labelled field
    /// in the Jellyfin metadata editor, alongside IMDb/TheTVDB IDs.
    /// </summary>
    public class SportarrEpisodeExternalId : IExternalId
    {
        /// <summary>
        /// Gets the display label shown for this external ID in the metadata editor.
        /// </summary>
        public string ProviderName => "Sportarr";

        /// <summary>
        /// Gets the provider key. Must match the key used by SetProviderId("Sportarr", ...)
        /// so the editor field binds to the stored value.
        /// </summary>
        public string Key => "Sportarr";

        /// <summary>
        /// Gets the media type this external ID applies to, so Jellyfin routes it to the
        /// correct (episode) metadata editor.
        /// </summary>
        public ExternalIdMediaType? Type => ExternalIdMediaType.Episode;

        /// <summary>
        /// Gets the URL format string for linking out to the ID. The "{0}" placeholder is
        /// replaced with the stored Sportarr ID to produce a clickable link.
        /// </summary>
        public string? UrlFormatString => "https://sportarr.net/api/metadata/agents/episode/{0}";

        /// <summary>
        /// Determines whether this external ID applies to the given item.
        /// </summary>
        /// <param name="item">The item to test.</param>
        /// <returns>True for <see cref="Episode"/> items; otherwise false.</returns>
        public bool Supports(IHasProviderIds item) => item is Episode;
    }
}
