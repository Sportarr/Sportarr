# Fightarr Dockerfile - Multi-stage build for production
# Builds Fightarr from source and creates a minimal runtime image

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

ARG TARGETPLATFORM
ARG VERSION=5.0.0

WORKDIR /src

# Copy solution and project files
COPY src/*.sln ./
COPY src/*/*.csproj ./
RUN for file in $(ls *.csproj); do mkdir -p ${file%.*}/ && mv $file ${file%.*}/; done

# Restore dependencies
RUN dotnet restore Fightarr.sln

# Copy source code
COPY src/ ./

# Build backend
RUN dotnet publish NzbDrone/Fightarr.csproj \
    --configuration Release \
    --output /app \
    --no-restore \
    --runtime linux-x64 \
    --self-contained false \
    /p:Version=${VERSION}

# Frontend build stage
FROM node:20-alpine AS frontend-builder

WORKDIR /src

# Copy package files from root (project uses Yarn, not npm)
COPY package.json yarn.lock .yarnrc ./
RUN corepack enable && yarn install --frozen-lockfile

# Copy frontend source and configuration
COPY frontend/ ./frontend/
COPY tsconfig.json ./

# Build using yarn (outputs to _output/UI)
RUN yarn build --env production

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Install runtime dependencies
RUN apt-get update && \
    apt-get install -y \
        sqlite3 \
        libmediainfo0v5 \
        curl \
        ca-certificates && \
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
    FIGHTARR__ANALYTICS_ENABLED="False" \
    XDG_CONFIG_HOME="/config/xdg"

# Expose ports
EXPOSE 8989

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8989/ping || exit 1

# Volume for configuration
VOLUME ["/config", "/tv", "/downloads"]

# Switch to fightarr user
USER fightarr

# Start Fightarr
ENTRYPOINT ["/app/Fightarr"]
CMD ["-nobrowser", "-data=/config"]
