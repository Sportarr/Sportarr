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
COPY src/Sportarr.Api.csproj ./
RUN dotnet restore

COPY src/ ./
RUN dotnet publish \
    --configuration Release \
    --output /app \
    --no-restore \
    /p:Version=${VERSION}

# Copy frontend build to wwwroot
COPY --from=frontend-builder /src/_output/UI /app/wwwroot

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Docker metadata labels
LABEL org.opencontainers.image.title="Sportarr" \
      org.opencontainers.image.description="Universal Sports PVR - Automatically track and download live sports events across all major sports" \
      org.opencontainers.image.vendor="Sportarr" \
      org.opencontainers.image.url="https://github.com/Sportarr/Sportarr" \
      org.opencontainers.image.source="https://github.com/Sportarr/Sportarr" \
      org.opencontainers.image.documentation="https://github.com/Sportarr/Sportarr/blob/main/README.md" \
      org.opencontainers.image.licenses="GPL-3.0" \
      maintainer="Sportarr"

# Unraid/Docker Hub icon URL (points to GitHub raw content)
LABEL net.unraid.docker.icon="https://raw.githubusercontent.com/Sportarr/Sportarr/main/Logo/512.png"

# Install runtime dependencies including gosu for proper user switching
RUN apt-get update && \
    apt-get install -y \
        sqlite3 \
        curl \
        ca-certificates \
        gosu && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Copy application first (as root to ensure permissions)
WORKDIR /app
COPY --from=builder /app ./

# Copy entrypoint script
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

# Create sportarr user and set permissions
RUN groupadd -g 13001 sportarr && \
    useradd -u 13001 -g 13001 -d /config -s /bin/bash sportarr && \
    mkdir -p /config /downloads && \
    chown -R sportarr:sportarr /config /downloads /app

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

# Volume for configuration
VOLUME ["/config", "/downloads"]

# Start as root to allow permission setup, entrypoint will switch to sportarr user
ENTRYPOINT ["/docker-entrypoint.sh"]
