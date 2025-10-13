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
COPY src/Fightarr.Api/Fightarr.Api.csproj ./
RUN dotnet restore

COPY src/Fightarr.Api/ ./
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
        ca-certificates \
        gosu && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Create fightarr user and directories
RUN groupadd -g 13001 fightarr && \
    useradd -u 13001 -g 13001 -d /config -s /bin/bash fightarr && \
    mkdir -p /config /downloads && \
    chown -R fightarr:fightarr /config /downloads

# Copy application
WORKDIR /app
COPY --from=builder /app ./

# Environment variables
ENV Fightarr__DataPath="/config" \
    ASPNETCORE_URLS="http://*:1867" \
    ASPNETCORE_ENVIRONMENT="Production"

# Expose ports
# Port 1867: Year the Marquess of Queensberry Rules were published
EXPOSE 1867

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:1867/ping || exit 1

# Volume for configuration
VOLUME ["/config", "/downloads"]

# Start as fightarr user
USER fightarr

# Start Fightarr
ENTRYPOINT ["dotnet", "Fightarr.Api.dll"]
