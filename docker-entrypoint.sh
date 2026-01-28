#!/bin/bash
set -e

echo "[Sportarr] Entrypoint starting..."

# ============================================================================
# Architecture Detection and VAAPI Path Configuration
# ============================================================================
ARCH=$(uname -m)
echo "[Sportarr] Architecture: $ARCH"

# Set LIBVA_DRIVERS_PATH based on architecture (Debian multiarch paths)
if [ "$ARCH" = "x86_64" ]; then
    export LIBVA_DRIVERS_PATH=/usr/lib/x86_64-linux-gnu/dri
elif [ "$ARCH" = "aarch64" ]; then
    export LIBVA_DRIVERS_PATH=/usr/lib/aarch64-linux-gnu/dri
    # ARM doesn't have Intel QSV, use mesa driver
    export LIBVA_DRIVER_NAME=
fi

# Handle PUID/PGID/UMASK for Unraid and Docker compatibility (matching Sonarr/Radarr defaults)
PUID=${PUID:-99}
PGID=${PGID:-100}
UMASK=${UMASK:-022}

# Set umask for file creation permissions
umask "$UMASK"

# If running as root, do privileged setup then switch to user
if [ "$(id -u)" = "0" ]; then
    echo "[Sportarr] Running as root, setting up permissions..."

    # Handle timezone (TZ environment variable) - requires root
    if [ -n "$TZ" ]; then
        echo "[Sportarr] Setting timezone to: $TZ"
        if [ -f "/usr/share/zoneinfo/$TZ" ]; then
            ln -snf "/usr/share/zoneinfo/$TZ" /etc/localtime
            echo "$TZ" > /etc/timezone
        else
            echo "[Sportarr] WARNING: Timezone $TZ not found in /usr/share/zoneinfo"
        fi
    fi

    # Update sportarr user to match PUID/PGID
    groupmod -o -g "$PGID" sportarr 2>/dev/null || true
    usermod -o -u "$PUID" sportarr 2>/dev/null || true

    # Ensure directories exist and have correct permissions
    mkdir -p /config
    chown -R "$PUID:$PGID" /config /app

    # ============================================================================
    # GPU Hardware Acceleration Setup
    # Grant access to GPU devices for hardware transcoding (Intel QSV, AMD VAAPI)
    # ============================================================================

    # Check for Intel/AMD GPU devices (/dev/dri)
    if [ -d "/dev/dri" ]; then
        echo "[Sportarr] GPU devices detected in /dev/dri:"
        ls -la /dev/dri/

        # Add sportarr user to the video and render groups for GPU access
        # This is required for Intel QSV and AMD VAAPI
        if getent group video > /dev/null 2>&1; then
            usermod -aG video sportarr 2>/dev/null || true
            echo "[Sportarr] Added sportarr to 'video' group"
        fi

        if getent group render > /dev/null 2>&1; then
            usermod -aG render sportarr 2>/dev/null || true
            echo "[Sportarr] Added sportarr to 'render' group"
        fi

        # Ensure GPU devices are accessible
        # renderD128 is the main render device for Intel/AMD
        if [ -e "/dev/dri/renderD128" ]; then
            # Get the group that owns the device and add user to it
            RENDER_GID=$(stat -c '%g' /dev/dri/renderD128)
            if [ "$RENDER_GID" != "0" ]; then
                groupadd -g "$RENDER_GID" gpurender 2>/dev/null || true
                usermod -aG "$RENDER_GID" sportarr 2>/dev/null || true
                echo "[Sportarr] Added sportarr to GPU render device group (GID: $RENDER_GID)"
            fi
            chmod 666 /dev/dri/renderD128 2>/dev/null || true
            echo "[Sportarr] Intel QSV/VAAPI device available: /dev/dri/renderD128"
        fi

        # card0 may also be needed for some operations
        if [ -e "/dev/dri/card0" ]; then
            chmod 666 /dev/dri/card0 2>/dev/null || true
        fi
    else
        echo "[Sportarr] No GPU devices detected in /dev/dri"
        echo "[Sportarr] For Intel QSV, add: --device=/dev/dri:/dev/dri"
        echo "[Sportarr] For NVIDIA NVENC, add: --gpus=all"
    fi

    # Check for NVIDIA GPU (nvidia-smi available means NVIDIA runtime is active)
    if command -v nvidia-smi &> /dev/null; then
        echo "[Sportarr] NVIDIA GPU detected:"
        nvidia-smi --query-gpu=name,driver_version --format=csv,noheader 2>/dev/null || echo "[Sportarr] nvidia-smi query failed"
        echo "[Sportarr] NVIDIA NVENC hardware encoding available"
    fi

    echo "[Sportarr] Running as UID: $PUID, GID: $PGID, UMASK: $UMASK"

    # Skip user switching if PUID=0 (running as root intentionally)
    # This matches Sonarr/Radarr behavior and prevents infinite restart loop
    if [ "$PUID" = "0" ]; then
        echo "[Sportarr] PUID=0, continuing as root user..."
    else
        echo "[Sportarr] Switching to user sportarr..."
        exec gosu sportarr "$0" "$@"
    fi
fi

# Now running as sportarr user
echo "[Sportarr] User: $(whoami) (UID: $(id -u), GID: $(id -g))"

# ============================================================================
# Hardware Acceleration Detection (as user)
# Log available GPU capabilities for debugging
# ============================================================================
echo "[Sportarr] Detecting hardware acceleration capabilities..."

# Check VAAPI support
if [ -e "/dev/dri/renderD128" ]; then
    echo "[Sportarr] VAAPI device available"
    # Try to query VAAPI info if vainfo is available
    if command -v vainfo &> /dev/null; then
        echo "[Sportarr] VAAPI profiles:"
        vainfo 2>/dev/null | grep -E "VAProfile|driver" | head -10 || echo "[Sportarr] vainfo query failed"
    fi
fi

# Check if FFmpeg has hardware support
if command -v ffmpeg &> /dev/null; then
    # Check for QSV support
    if ffmpeg -hide_banner -encoders 2>/dev/null | grep -q "_qsv"; then
        echo "[Sportarr] FFmpeg Intel QSV encoders available"
    fi
    # Check for VAAPI support
    if ffmpeg -hide_banner -encoders 2>/dev/null | grep -q "_vaapi"; then
        echo "[Sportarr] FFmpeg VAAPI encoders available"
    fi
    # Check for NVENC support
    if ffmpeg -hide_banner -encoders 2>/dev/null | grep -q "_nvenc"; then
        echo "[Sportarr] FFmpeg NVIDIA NVENC encoders available"
    fi
fi

echo "[Sportarr] Checking /config permissions..."

# Verify /config is writable
if [ ! -w "/config" ]; then
    echo "[Sportarr] ERROR: /config is not writable!"
    echo "[Sportarr] Directory info:"
    ls -ld /config
    echo ""
    echo "[Sportarr] TROUBLESHOOTING:"
    echo "[Sportarr] 1. Check the ownership of your /mnt/user/appdata/sportarr directory on Unraid"
    echo "[Sportarr] 2. Set PUID/PGID environment variables to match your user"
    echo "[Sportarr] 3. Or run: chown -R $PUID:$PGID /mnt/user/appdata/sportarr"
    exit 1
fi

echo "[Sportarr] /config is writable - OK"
echo "[Sportarr] Starting Sportarr..."

# Start the application
cd /app
exec dotnet Sportarr.dll
