#!/bin/bash
set -e

echo "[Fightarr] Entrypoint starting..."

# Handle PUID/PGID for Unraid compatibility
PUID=${PUID:-13001}
PGID=${PGID:-13001}

echo "[Fightarr] Running as UID: $PUID, GID: $PGID"

# If running as root, switch to the correct user
if [ "$(id -u)" = "0" ]; then
    echo "[Fightarr] Running as root, setting up permissions..."

    # Update fightarr user to match PUID/PGID
    groupmod -o -g "$PGID" fightarr 2>/dev/null || true
    usermod -o -u "$PUID" fightarr 2>/dev/null || true

    # Ensure directories exist and have correct permissions
    mkdir -p /config /downloads
    chown -R "$PUID:$PGID" /config /downloads /app

    echo "[Fightarr] Permissions set, switching to user fightarr..."
    exec gosu fightarr "$0" "$@"
fi

# Now running as fightarr user
echo "[Fightarr] User: $(whoami) (UID: $(id -u), GID: $(id -g))"
echo "[Fightarr] Checking /config permissions..."

# Verify /config is writable
if [ ! -w "/config" ]; then
    echo "[Fightarr] ERROR: /config is not writable!"
    echo "[Fightarr] Directory info:"
    ls -ld /config
    echo ""
    echo "[Fightarr] TROUBLESHOOTING:"
    echo "[Fightarr] 1. Check the ownership of your /mnt/user/appdata/fightarr directory on Unraid"
    echo "[Fightarr] 2. Set PUID/PGID environment variables to match your user"
    echo "[Fightarr] 3. Or run: chown -R $PUID:$PGID /mnt/user/appdata/fightarr"
    exit 1
fi

echo "[Fightarr] /config is writable - OK"
echo "[Fightarr] Starting Fightarr..."

# Start the application
cd /app
exec dotnet Fightarr.Api.dll
