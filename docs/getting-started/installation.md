# Installation

Most people should run Sportarr with Docker. Native builds are available for Windows, macOS, and Linux if you prefer.

## Docker (recommended)

```yaml
version: "3.8"
services:
  sportarr:
    image: sportarr/sportarr:latest
    container_name: sportarr
    environment:
      - PUID=99
      - PGID=100
      - UMASK=022
      - TZ=America/New_York  # Optional: set your timezone
    volumes:
      - /path/to/sportarr/config:/config
      - /path/to/data:/data
    ports:
      - 1867:1867
    restart: unless-stopped
```

The `/config` volume stores your database and settings. The `/data` volume is your media library root folder. Keep downloads under the same mount so imports can hardlink instead of copying.

After starting the container, open the web UI at `http://your-server-ip:1867`.

Or with `docker run`:

```bash
docker run -d \
  --name=sportarr \
  -e PUID=99 \
  -e PGID=100 \
  -e UMASK=022 \
  -e TZ=America/New_York \
  -p 1867:1867 \
  -v /path/to/sportarr/config:/config \
  -v /path/to/data:/data \
  --restart unless-stopped \
  sportarr/sportarr:latest
```

### Image tags

| Tag | Purpose |
|---|---|
| `latest` | Stable releases (recommended) |
| `dev` | Rolling development builds |

## App catalogs and installers

- **TrueNAS SCALE** - Sportarr is in the community apps train. Search for "sportarr" on the Apps screen; catalog updates track new releases automatically.
- **HexOS** - Sportarr is in the curated apps catalog with the same one-click install.
- **Unraid** - search "sportarr" in Community Applications. Official templates live at [Sportarr/unraid-templates](https://github.com/Sportarr/unraid-templates).
- **DockSTARTer** - Sportarr is a built-in app template. Run `ds -a sportarr` or enable it in the `ds` menu. See the [DockSTARTer guide](../integrations/dockstarter.md).

## Windows, Linux, and macOS

Download the latest release from the [releases page](https://github.com/Sportarr/Sportarr/releases/latest):

| Platform | Options |
|---|---|
| Windows | **Installer** (`Sportarr-Setup.exe`) installs it for you, or **Portable** (`win-x64.zip`) runs from a folder with no install |
| macOS | **Apple Silicon** (`osx-arm64`) for M-series Macs, or **Intel** (`osx-x64`) for older Macs |
| Linux | **x64** (`linux-x64`) for most servers, or **ARM64** (`linux-arm64`) for Raspberry Pi and ARM boxes |

By default, configuration is stored in a `data` subdirectory next to the executable. You can specify a custom location with the `-data` argument:

```bash
# Windows
Sportarr.exe -data C:\ProgramData\Sportarr

# Linux/macOS
./Sportarr -data /var/lib/sportarr
```

Or set the `Sportarr__DataPath` environment variable:

```bash
# Linux/macOS
export Sportarr__DataPath=/var/lib/sportarr
./Sportarr

# Windows PowerShell
$env:Sportarr__DataPath = "C:\ProgramData\Sportarr"
.\Sportarr.exe
```

Priority order: command-line `-data` argument, then the environment variable, then the default `./data`.

## Run at startup

Docker installs restart on their own through the `restart: unless-stopped`
policy in the compose example above. For bare-metal installs, set it up per
platform:

### Windows

The installer (`Sportarr-Setup.exe`) offers two options for this:

- **Start Sportarr when Windows starts** (checked by default) adds a Startup
  shortcut that launches Sportarr in the system tray when you log in.
- **Install as Windows Service** runs Sportarr in the background from boot,
  before anyone logs in. Pick this for a headless server or a machine other
  people also use.

Already installed without the option you want? Re-run the installer and tick
it. Your data directory and settings are kept.

Using the portable zip instead? Create the service yourself from an
elevated prompt:

```powershell
sc create Sportarr binPath= "\"C:\Sportarr\Sportarr.exe\" --service" start= auto
sc start Sportarr
```

Or press Win+R, run `shell:startup`, and drop in a shortcut to
`Sportarr.exe --tray` for the tray-at-login behavior.

### Linux

Create a systemd unit:

```ini
# /etc/systemd/system/sportarr.service
[Unit]
Description=Sportarr
After=network-online.target
Wants=network-online.target

[Service]
User=sportarr
ExecStart=/opt/sportarr/Sportarr -data /var/lib/sportarr
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Then enable it:

```bash
sudo systemctl enable --now sportarr
```

### macOS

Create a launchd agent at `~/Library/LaunchAgents/net.sportarr.plist`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>net.sportarr</string>
    <key>ProgramArguments</key>
    <array>
        <string>/Applications/Sportarr/Sportarr</string>
        <string>-data</string>
        <string>/Users/YOURNAME/.sportarr</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <dict>
        <key>SuccessfulExit</key>
        <false/>
    </dict>
</dict>
</plist>
```

Then load it:

```bash
launchctl load ~/Library/LaunchAgents/net.sportarr.plist
```

Next step: the [Initial Setup](initial-setup.md) walkthrough.
