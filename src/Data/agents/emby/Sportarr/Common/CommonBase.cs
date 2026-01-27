namespace Sportarr.Common
{
    using Sportarr;
    using MediaBrowser.Common;
    using MediaBrowser.Controller.Base;
    using MediaBrowser.Model.Logging;
    using MediaBrowser.Model.System;
    using System.Linq;

    /// <summary>
    /// Abstract base class for Sportarr plugin components that require access to the plugin instance and its configuration.
    /// Extends CommonBaseCore to provide common functionality with plugin-specific capabilities.
    /// </summary>
    public abstract class CommonBase : CommonBaseCore
    {
        /// <summary>
        /// Cached reference to the Sportarr plugin instance.
        /// </summary>
        private SportarrPlugin myPlugin;

        /// <summary>
        /// Initializes a new instance of the <see cref="CommonBase"/> class.
        /// </summary>
        /// <param name="serviceRoot">The service root providing access to Emby services.</param>
        /// <param name="logName">Optional custom name for logging. If null, uses the default log name.</param>
        protected CommonBase(IServiceRoot serviceRoot, string logName = null)
            : base(serviceRoot, logName)
        {
        }

        /// <summary>
        /// Gets the current plugin configuration options.
        /// Provides easy access to settings such as API URL and debug logging preferences.
        /// </summary>
        protected SportarrPluginOptions Options => this.Plugin.Options;

        /// <summary>
        /// Gets the Sportarr plugin instance.
        /// Lazily loads and caches the plugin reference from the application host on first access.
        /// </summary>
        /// <exception cref="Exception">Thrown when the Sportarr plugin is not loaded in the Emby server.</exception>
        protected SportarrPlugin Plugin
        {
            get
            {
                if (this.myPlugin == null)
                {
                    this.myPlugin = this.GetService<IApplicationHost>().Plugins.OfType<SportarrPlugin>().FirstOrDefault();
                    if (this.myPlugin == null)
                    {
                        throw this.GetEx(@"The {0} plugin is not loaded", SportarrPlugin.PluginName);
                    }
                }
                return this.myPlugin;
            }
        }
    }
}