# Fightarr Dockerfile - Modern minimal API build
# Builds Fightarr from source and creates a minimal runtime image
# Port 1867: Year the Marquess of Queensberry Rules were published

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

# Backend build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

ARG VERSION=1.0.0

WORKDIR /build

# Copy backend source
COPY src/Fightarr.Api.csproj ./
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

# Install runtime dependencies
RUN apt-get update && \
    apt-get install -y \
        sqlite3 \
        curl \
        ca-certificates && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Copy application first (as root to ensure permissions)
WORKDIR /app
COPY --from=builder /app ./

# Create fightarr user and set permissions
RUN groupadd -g 13001 fightarr && \
    useradd -u 13001 -g 13001 -d /config -s /bin/bash fightarr && \
    mkdir -p /config /downloads && \
    chown -R fightarr:fightarr /config /downloads /app

# Environment variables
ENV Fightarr__DataPath="/config" \
    ASPNETCORE_URLS="http://*:1867" \
    ASPNETCORE_ENVIRONMENT="Production" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Expose ports
# Port 1867: Year the Marquess of Queensberry Rules were published
EXPOSE 1867

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:1867/ping || exit 1

# Volume for configuration
VOLUME ["/config", "/downloads"]

# Switch to fightarr user
USER fightarr

# Verify the DLL exists before starting
RUN test -f /app/Fightarr.Api.dll || (echo "ERROR: Fightarr.Api.dll not found!" && exit 1)

# Start Fightarr with explicit shell wrapper for better error output
CMD ["/bin/bash", "-c", "echo '[Fightarr] Container starting...' && echo '[Fightarr] User: $(whoami)' && echo '[Fightarr] Files:' && ls -la /app/*.dll && echo '[Fightarr] Starting application...' && exec dotnet Fightarr.Api.dll"]
