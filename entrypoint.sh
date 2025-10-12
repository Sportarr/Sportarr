#!/bin/bash
set -e

# Use PUID/PGID environment variables (defaults to 13001:13001)
PUID=${PUID:-13001}
PGID=${PGID:-13001}

echo "Starting Fightarr with PUID=$PUID, PGID=$PGID"

# Update user/group IDs if running as root
if [ "$(id -u)" = "0" ]; then
    # Change fightarr user/group to match PUID/PGID
    groupmod -o -g "$PGID" fightarr 2>/dev/null || true
    usermod -o -u "$PUID" fightarr 2>/dev/null || true

    # Ensure config directory exists and has correct permissions
    mkdir -p /config
    chown -R fightarr:fightarr /config

    # Execute Fightarr as the fightarr user
    exec su-exec fightarr /app/Fightarr "$@"
else
    # Not running as root, just execute
    exec /app/Fightarr "$@"
fi
