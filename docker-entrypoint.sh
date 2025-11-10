#!/bin/bash
set -e

echo "[Sportarr] Entrypoint starting..."

# Handle PUID/PGID for Unraid compatibility
PUID=${PUID:-13001}
PGID=${PGID:-13001}

echo "[Sportarr] Running as UID: $PUID, GID: $PGID"

# If running as root, switch to the correct user
if [ "$(id -u)" = "0" ]; then
    echo "[Sportarr] Running as root, setting up permissions..."

    # Update fightarr user to match PUID/PGID
    groupmod -o -g "$PGID" fightarr 2>/dev/null || true
    usermod -o -u "$PUID" fightarr 2>/dev/null || true

    # Ensure directories exist and have correct permissions
    mkdir -p /config /downloads
    chown -R "$PUID:$PGID" /config /downloads /app

    echo "[Sportarr] Permissions set, switching to user fightarr..."
    exec gosu fightarr "$0" "$@"
fi

# Now running as fightarr user
echo "[Sportarr] User: $(whoami) (UID: $(id -u), GID: $(id -g))"
echo "[Sportarr] Checking /config permissions..."

# Verify /config is writable
if [ ! -w "/config" ]; then
    echo "[Sportarr] ERROR: /config is not writable!"
    echo "[Sportarr] Directory info:"
    ls -ld /config
    echo ""
    echo "[Sportarr] TROUBLESHOOTING:"
    echo "[Sportarr] 1. Check the ownership of your /mnt/user/appdata/fightarr directory on Unraid"
    echo "[Sportarr] 2. Set PUID/PGID environment variables to match your user"
    echo "[Sportarr] 3. Or run: chown -R $PUID:$PGID /mnt/user/appdata/fightarr"
    exit 1
fi

echo "[Sportarr] /config is writable - OK"
echo "[Sportarr] Starting Sportarr..."

# Start the application
cd /app
exec dotnet Sportarr.Api.dll
