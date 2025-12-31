# Sportarr Plugin for Emby

A metadata provider plugin for Emby that fetches sports metadata from the Sportarr-API.

## Features

- **Series Provider**: Matches sports leagues/events to official metadata from sportarr.net
- **Episode Provider**: Provides episode-level metadata for individual sports events
- **Image Provider**: Fetches posters, banners, and fanart for sports content

## Installation

### Manual Installation

1. Download the latest `Emby.Plugins.Sportarr.dll` from the [releases page](https://github.com/Sportarr/Sportarr/releases)
2. Copy the DLL to your Emby plugins directory:
   - **Linux**: `/var/lib/emby/plugins/`
   - **Windows**: `C:\Users\<username>\AppData\Roaming\Emby-Server\plugins\`
   - **Docker**: Mount to `/config/plugins/` in your container
3. Restart Emby Server

### Building from Source

```bash
cd agents/emby/Sportarr
dotnet build -c Release
```

The compiled DLL will be in `bin/Release/netstandard2.1/Emby.Plugins.Sportarr.dll`

## Configuration

1. Go to Emby Dashboard → Plugins → Sportarr
2. Configure the Sportarr API URL (default: https://sportarr.net)
3. Optionally enable debug logging for troubleshooting

## Usage

1. Create a TV Shows library for your sports content
2. In Library Settings → Metadata Downloaders, enable "Sportarr"
3. Move "Sportarr" to the top of the priority list
4. Refresh library metadata

## Requirements

- Emby Server 4.8 or later
- .NET Standard 2.1 compatible runtime

## Differences from Jellyfin Plugin

This is a separate plugin built specifically for Emby. Key differences:

- Uses `MediaBrowser.Server.Core` NuGet package instead of `Jellyfin.Controller`
- Uses `IHttpClient` and `HttpResponseInfo` instead of `IHttpClientFactory` and `HttpResponseMessage`
- Uses `ProviderIdDictionary` instead of `Dictionary<string, string>` for provider IDs
- Uses `LibraryOptions` parameter in `GetImages` method

## Troubleshooting

### Plugin not loading
- Ensure you're using the Emby-specific plugin, not the Jellyfin version
- Check Emby logs for error messages
- Verify the plugin is in the correct directory

### Metadata not matching
- Enable debug logging in plugin settings
- Check Emby logs for `[Sportarr]` entries
- Ensure your folder naming matches the league/event names

## License

Same license as Sportarr main project.
