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

### Optional root startup script

Set `SPORTARR_STARTUP_SCRIPT` to an absolute path inside the container when
another command must run before Sportarr starts. Mount the script read-only.
The container runs it once as root after its normal permission and device
setup, then switches to `PUID` and `PGID`. A failed script stops the container
so a required setup step cannot fail silently.

```yaml
services:
  sportarr:
    environment:
      - SPORTARR_STARTUP_SCRIPT=/custom/startup.sh
    volumes:
      - ./startup.sh:/custom/startup.sh:ro
```

Only configure scripts you trust. This setting intentionally grants the
mounted script root access inside the container. It is ignored when the
container itself starts as a non-root user.

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
- **Runtipi** - add the [Sportarr app store](https://github.com/Sportarr/runtipi-appstore) under **Settings > App Stores** (Runtipi v4.0.0 or later), then install Sportarr from the store list.

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
WorkingDirectory=/opt/sportarr
ExecStart=/opt/sportarr/Sportarr -data /var/lib/sportarr
Restart=on-failure
RestartSec=5

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

## Updating

**System > Updates** tells you when a new release is out. Sportarr checks
GitHub for the latest stable release. If GitHub cannot be reached, it skips
the check and tries again later.

Pick how you update in **Settings > General > Updates**:

- **Built-in**: Sportarr downloads the new version, installs it and
  restarts. See [Built-in updates](#built-in-updates). Not for Docker.
- **Docker**: pull the new image and recreate the container.
- **External**: update with your installer or package manager. Sportarr
  shows when a new version is out.
- **Script**: Sportarr runs your script when a new version is out. See
  [Update with a script](#update-with-a-script).

Older versions also offered Apt. It never installed anything, and Sportarr
now shows it as External.

Your settings and database live in the data directory, not beside the
program, so replacing the program leaves them alone. Where that directory
is depends on how you started Sportarr: the path you passed to `-data`, or
the one in `Sportarr__DataPath`, or a `data` folder next to the executable
if you set neither.

### Docker

Pull the new image and recreate the container. Nothing else changes,
because `/config` is a volume.

```bash
docker pull sportarr/sportarr:latest
docker stop sportarr && docker rm sportarr
# then run your original docker run command again
```

With Compose:

```bash
docker compose pull
docker compose up -d
```

Unraid, TrueNAS SCALE, HexOS, Runtipi and DockSTARTer handle this
themselves. Use their own update button rather than the commands above.

### Linux

Stop it, replace the files, start it again. The tarball holds only the
program, so extracting it over the install directory cannot touch your
`data` folder.

```bash
sudo systemctl stop sportarr
tar -xzf Sportarr-linux-x64-<version>.tar.gz -C /opt/sportarr
sudo systemctl start sportarr
```

Take the `linux-x64` or `linux-arm64` tarball from the
[releases page](https://github.com/Sportarr/Sportarr/releases/latest). The
version is part of the filename, so there is no fixed "latest" URL to
download from a script.

If you run Sportarr by hand rather than under systemd, stop the process
first. Extracting over a running program leaves you with a mix of two
versions.

### macOS

```bash
launchctl unload ~/Library/LaunchAgents/net.sportarr.plist
tar -xzf Sportarr-osx-arm64-<version>.tar.gz -C /Applications/Sportarr
launchctl load ~/Library/LaunchAgents/net.sportarr.plist
```

Use `osx-arm64` for Apple Silicon and `osx-x64` for Intel.

### Windows

Run the new `Sportarr-Setup-<version>.exe` over the top. It keeps your
data directory.

For the portable build, close Sportarr, extract the new
`Sportarr-win-x64-<version>.zip` over the folder, and start it again. Keep
the `data` folder if it sits inside that same folder.

### Built-in updates

Set the mechanism to **Built-in** and save. The Updates section then shows
the version you run and the latest version. Click **Install update** to
install it now. Turn on **Automatic Updates** to let Sportarr install new
versions on its own.

Sportarr downloads the package for your system from the release, checks
its size and checksum, and unpacks it in the `updates` folder of the data
directory. It makes a normal database backup, which you find under
**System > Backup**. Then it starts the update helper and stops. The helper
backs up the files it replaces, copies the new files into the install folder
and starts Sportarr again from the same folder, with the same data
directory.

The helper waits up to 10 minutes for the new version to answer. A version
that migrates the database gets up to an hour, as long as it still runs. If
the new version does not answer, the helper puts the old files back and
starts the old version. If the new version had already changed a SQLite
database, the helper also puts the database back from the backup. It cannot
do that for Postgres. Then it leaves the new version in place, because the
old version cannot run on the changed database, and the health page says
which backup to restore. The health page always shows a failed or rolled
back update. Each step is in `logs/update.log` in the data directory. After
a good update, Sportarr removes the downloaded and unpacked files a few
minutes after the start.

Sportarr does not install while a recording runs or a download is imported.
**Install update** then says so. Automatic updates wait for a quiet moment,
and not in the first 15 minutes after a start. Automatic updates do not try
a version again after it failed or was rolled back. The health page says so,
and **Install update** still installs it. Before it installs,
Sportarr checks the free space. The data directory needs room for the
download, the backup of the replaced files and a copy of the database. The
install folder needs room for the new files. If space is short, **Install
update** says how much is needed.

The built-in update needs these things:

- A release build from the releases page or the Windows installer.
  Development builds do not update themselves.
- Sportarr can write to its install folder. The Windows installer puts
  Sportarr in Program Files, so there it works for the Windows service, but
  not for the tray app of a normal user.
- No container. In Docker, pull the new image instead.
- With Postgres, `pg_dump` installed, because Sportarr backs up the
  database before each update.
- Under systemd, these lines in the `[Service]` section of the unit:

  ```ini
  KillMode=process
  Restart=on-failure
  RestartSec=5
  ```

  `Restart=always` works too. Do not list exit code 1 in
  `SuccessExitStatus` or `RestartPreventExitStatus`.

  A new version that fails restarts until the helper puts the old one
  back. systemd must not give up before that. So `RestartSec` must be at
  least 2 seconds. Also, `RestartSec` times `StartLimitBurst` must be longer
  than `StartLimitIntervalSec`, or the start limit must be off.
  `RestartSec=5` works with the systemd defaults.

  `KillMode=process` keeps the update helper running while Sportarr stops.
  It also means that `systemctl stop` stops only the Sportarr process
  itself, not other programs that it started. Sportarr stops its FFmpeg
  recordings itself when it shuts down. After the helper copies the files,
  Sportarr exits with an error code, and `Restart=on-failure` makes systemd
  start the new version.
- Built-in supports systemd system services, the Windows service and
  Sportarr started by hand. Under any other supervisor, use Script or
  External.
- Not as a systemd user service. Run Sportarr as a system service, or use
  Script or External. Started from a terminal, which puts it in a scope,
  the built-in update works and the helper starts Sportarr itself.
- Not under launchd on macOS. launchd stops the helper together with
  Sportarr. Use Script or External there. Started from a terminal, the
  built-in update works.

If something is missing, the Updates section and the health page say what
it is.

### Update with a script

Set the mechanism to **Script**, enter the path to your script and turn on
**Automatic Updates**. Sportarr checks for a new release every 15 minutes. When it
finds a newer release, it runs your script once for that version. If the
script exits with an error, Sportarr tries again the next day. If GitHub
cannot be reached, Sportarr skips the check and tries again later.

The script gets these environment variables:

| Variable | Value |
|---|---|
| `SPORTARR_UPDATE_VERSION` | The new version |
| `SPORTARR_CURRENT_VERSION` | The version that is running |
| `SPORTARR_UPDATE_URL` | The release page on GitHub |
| `SPORTARR_UPDATE_DOWNLOAD_URL` | The archive for this system, or empty if Sportarr cannot tell which one |
| `SPORTARR_INSTALL_DIR` | The folder Sportarr runs from |
| `SPORTARR_DATA_DIR` | The data directory |

To install the update, the script has to stop Sportarr. A service manager
can stop every process that Sportarr started, and that includes your
script. So let the script hand the work to something that outlives
Sportarr. On Linux, start the real work with `systemd-run`. On Windows, use
a scheduled task.

```bash
#!/bin/sh
# /opt/sportarr-update/run.sh, the path you enter in Sportarr
exec systemd-run --unit=sportarr-update --collect \
  --setenv=SPORTARR_UPDATE_DOWNLOAD_URL="$SPORTARR_UPDATE_DOWNLOAD_URL" \
  --setenv=SPORTARR_INSTALL_DIR="$SPORTARR_INSTALL_DIR" \
  /opt/sportarr-update/install.sh
```

`systemd-run` needs the right to start a system unit. Run it as root, or
allow it for the Sportarr user with a polkit rule or a sudoers line.
`--collect` removes the unit when it ends, so the next update can use the
same name. `install.sh` then does the steps under [Linux](#linux). Keep the old install
folder until the new version answers on `/ping`, so you can go back if it
does not start. Sportarr waits up to one hour for the script to end.

### After an update

Sportarr applies any database changes on first start, so the first launch
after an update can take a little longer than usual. Check **System >
Status** to confirm the version, and **System > Logs** if it does not come
back up.

Next step: the [Initial Setup](initial-setup.md) walkthrough.
