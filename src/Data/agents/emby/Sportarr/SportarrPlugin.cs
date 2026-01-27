namespace Sportarr
{
    using MediaBrowser.Common;
    using MediaBrowser.Common.Plugins;
    using MediaBrowser.Controller.Plugins;
    using MediaBrowser.Model.Drawing;
    using MediaBrowser.Model.Logging;
    using System;
    using System.IO;

    /// <summary>
    /// Sportarr Plugin for Emby.
    /// Provides sports metadata from Sportarr-API.
    /// </summary>
    public class SportarrPlugin : BasePluginSimpleUI<SportarrPluginOptions>, IHasThumbImage
    {
        /// <summary>Initializes a new instance of the <see cref="SportarrPlugin" /> class.</summary>
        /// <param name="applicationHost">The application host.</param>
        /// <param name="logManager">The log manager.</param>
        public SportarrPlugin(IApplicationHost applicationHost, ILogManager logManager) : base(applicationHost)
        {
            this.logger = logManager.GetLogger(this.Name);
            this.logger.Info("Plugin ({0}) is getting loaded", this.Name);
            SportarrPluginOptions.Logger = logger;
        }

        /// <summary>
        /// Sets the plugin name.
        /// </summary>
        public const string PluginName = "Sportarr";

        /// <summary>
        /// Gets the plugin GUID - same as Jellyfin for consistency.
        /// </summary>
        private readonly Guid id = new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        /// <summary>
        /// Gets the unique id.
        /// </summary>
        public override Guid Id => this.id;

        private readonly ILogger logger;
        
        /// <summary>
        /// Gets the plugin description.
        /// </summary>
        public override string Description => "Sports metadata provider powered by Sportarr-API";

        /// <summary>
        /// Gets the plugin name.
        /// </summary>
        public sealed override string Name => PluginName;

        /// 
        /// <summary>Gets the plugin options.
        /// </summary>
        public SportarrPluginOptions Options => this.GetOptions();

        /// <summary>
        /// Gets the thumb image format.
        /// </summary>
        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        /// <summary>Gets the thumb image.</summary>
        public Stream GetThumbImage()
        {
            var type = this.GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".ThumbImage.png");
        }

        /// <summary>
        /// Gets options saved message
        /// </summary>
        /// <param name="options">The Plugin Options</param>
        protected override void OnOptionsSaved(SportarrPluginOptions options)
        {
            this.logger.Info("Plugin ({0}) options have been updated.", this.Name);
        }
    }
}