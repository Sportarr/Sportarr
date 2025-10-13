# Fightarr Dockerfile - Multi-stage build for production
# Builds Fightarr from source and creates a minimal runtime image
# Port 1867: Year the Marquess of Queensberry Rules were published

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

ARG TARGETPLATFORM
ARG VERSION=5.0.0

WORKDIR /build

# Copy source code and logo resources
COPY src/ ./src/
COPY Logo/ ./Logo/

# Build backend console application and platform-specific dependencies
RUN dotnet publish src/NzbDrone.Console/Fightarr.Console.csproj \
    --configuration Release \
    --framework net8.0 \
    --output /app \
    --self-contained false \
    /p:Version=${VERSION} \
    /p:RunAnalyzers=false \
    /p:EnableAnalyzers=false \
    --verbosity quiet && \
    dotnet publish src/NzbDrone.Mono/Fightarr.Mono.csproj \
    --configuration Release \
    --framework net8.0 \
    --output /app \
    /p:Version=${VERSION} \
    /p:RunAnalyzers=false \
    /p:EnableAnalyzers=false \
    --verbosity quiet

# Frontend build stage
FROM node:20-alpine AS frontend-builder

WORKDIR /src/frontend

# Copy package files for frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --quiet

# Copy frontend source and configuration
COPY frontend/ ./

# Build using npm (outputs to ../_output/UI)
RUN npm run build

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Install runtime dependencies
RUN apt-get update && \
    apt-get install -y \
        sqlite3 \
        libmediainfo0v5 \
        ffmpeg \
        curl \
        ca-certificates \
        gosu && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Create fightarr user and directories
RUN groupadd -g 13001 fightarr && \
    useradd -u 13001 -g 13001 -d /config -s /bin/bash fightarr && \
    mkdir -p /config /tv /downloads && \
    chown -R fightarr:fightarr /config /tv /downloads

# Copy application
WORKDIR /app
COPY --from=builder /app ./
COPY --from=frontend-builder /src/_output/UI ./UI

# Environment variables
ENV FIGHTARR__INSTANCENAME="Fightarr" \
    FIGHTARR__BRANCH="main" \
    FIGHTARR__LOG__ANALYTICSENABLED="False" \
    FIGHTARR__SERVER__PORT="1867" \
    ASPNETCORE_URLS="http://*:1867" \
    XDG_CONFIG_HOME="/config/xdg"

# Expose ports
# Port 1867: Year the Marquess of Queensberry Rules were published
EXPOSE 1867

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:1867/ping || exit 1

# Volume for configuration
VOLUME ["/config", "/tv", "/downloads"]

# Copy entrypoint script
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Start Fightarr via entrypoint (handles PUID/PGID)
ENTRYPOINT ["/entrypoint.sh"]
CMD ["-nobrowser", "-data=/config"]
