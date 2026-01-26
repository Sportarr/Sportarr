# Sportarr Dockerfile - Modern minimal API build
# Builds Sportarr from source and creates a minimal runtime image
# Port 1867: Sportarr default port

# Frontend build stage
FROM node:20-alpine AS frontend-builder

WORKDIR /src/frontend

# Copy package files for frontend
COPY frontend/package.json frontend/package-lock.json frontend/.npmrc ./
RUN npm ci --quiet

# Copy frontend source and configuration
COPY frontend/ ./

# Build using npm (outputs to ../_output/UI)
RUN npm run build

# Backend build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

ARG VERSION=1.0.0

WORKDIR /build

# Copy backend source
COPY src/Sportarr.csproj ./
RUN dotnet restore

COPY src/ ./
RUN dotnet publish \
    --configuration Release \
    --output /app \
    --no-restore \
    -p:SkipFrontendBuild=true \
    /p:Version=${VERSION}

# Copy frontend build to wwwroot
COPY --from=frontend-builder /src/_output/UI /app/wwwroot

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Docker metadata labels
LABEL org.opencontainers.image.title="Sportarr" \
      org.opencontainers.image.description="Universal Sports PVR - Organize and manage your sports media library" \
      org.opencontainers.image.vendor="Sportarr" \
      org.opencontainers.image.url="https://github.com/Sportarr/Sportarr" \
      org.opencontainers.image.source="https://github.com/Sportarr/Sportarr" \
      org.opencontainers.image.documentation="https://github.com/Sportarr/Sportarr/blob/main/README.md" \
      org.opencontainers.image.licenses="GPL-3.0" \
      maintainer="Sportarr"

# Unraid/Docker Hub icon URL (points to GitHub raw content)
LABEL net.unraid.docker.icon="https://raw.githubusercontent.com/Sportarr/Sportarr/main/Logo/512.png"

# ============================================================================
# Hardware Acceleration Drivers Installation
# Supports: Intel QSV, AMD/Intel VAAPI, NVIDIA NVENC (host runtime required)
# Architecture-aware: Intel packages only installed on amd64 (x86_64)
# ============================================================================

# Enable non-free and non-free-firmware repositories for Intel drivers (amd64 only)
# Required for intel-media-va-driver-non-free (HEVC encoding on 8th gen+)
RUN if [ "$(dpkg --print-architecture)" = "amd64" ]; then \
        sed -i 's/Components: main/Components: main contrib non-free non-free-firmware/' /etc/apt/sources.list.d/debian.sources; \
    fi

# Install runtime dependencies including hardware acceleration drivers
# Architecture-aware installation:
# - amd64: Full Intel QSV + VAAPI + OpenCL support
# - arm64: VAAPI only (no Intel-specific packages)
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        # Core dependencies (all architectures)
        sqlite3 \
        curl \
        ca-certificates \
        gosu \
        # FFmpeg with hardware acceleration
        ffmpeg \
        # VAAPI (Video Acceleration API) - works on all architectures
        libva2 \
        libva-drm2 \
        va-driver-all \
        mesa-va-drivers \
        # Debugging tools
        vainfo && \
    # Intel-specific packages (amd64 only)
    # These packages don't exist for arm64 architecture
    if [ "$(dpkg --print-architecture)" = "amd64" ]; then \
        apt-get install -y --no-install-recommends \
            # Intel Quick Sync Video (QSV)
            # intel-media-va-driver-non-free: Modern driver for 8th gen+ (HEVC/AV1)
            # i965-va-driver: Legacy driver for 6th/7th gen (H.264)
            intel-media-va-driver-non-free \
            i965-va-driver \
            # Intel Media SDK / oneVPL runtime (QSV framework)
            # libmfx-gen1.2: Modern oneVPL implementation for 8th gen+ (recommended)
            # libmfx1: Legacy Intel Media SDK for older processors
            libmfx-gen1.2 \
            libmfx1 \
            libvpl2 \
            # OpenCL support (Intel GPU compute)
            intel-opencl-icd \
            ocl-icd-libopencl1; \
    fi && \
    # Cleanup to reduce image size
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* /tmp/* /var/tmp/*

# ============================================================================
# GPU Environment Configuration
# ============================================================================
# LIBVA_DRIVER_NAME: Selects the VA-API driver
#   - iHD: Intel Media Driver (8th gen+, recommended for amd64)
#   - i965: Intel i965 driver (legacy, 6th/7th gen)
#   - radeonsi: AMD GPUs
# LIBVA_DRIVERS_PATH: Set at runtime in entrypoint based on architecture
# ============================================================================
ENV LIBVA_DRIVER_NAME=iHD

# Copy application first (as root to ensure permissions)
WORKDIR /app
COPY --from=builder /app ./

# Copy entrypoint script
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

# Create sportarr user and set permissions
RUN groupadd -g 13001 sportarr && \
    useradd -u 13001 -g 13001 -d /config -s /bin/bash sportarr && \
    mkdir -p /config && \
    chown -R sportarr:sportarr /config /app

# Environment variables
ENV Sportarr__DataPath="/config" \
    ASPNETCORE_URLS="http://*:1867" \
    ASPNETCORE_ENVIRONMENT="Production" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    PUID=99 \
    PGID=100 \
    UMASK=022

# Expose ports
# Port 1867: Sportarr default port
EXPOSE 1867

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:1867/ping || exit 1

VOLUME ["/config"]

# Start as root to allow permission setup, entrypoint will switch to sportarr user
ENTRYPOINT ["/docker-entrypoint.sh"]
