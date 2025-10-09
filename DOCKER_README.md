# Fightarr - Smart PVR for Fighting Events

![Docker Pulls](https://img.shields.io/docker/pulls/fightarr/fightarr)
![Docker Image Size](https://img.shields.io/docker/image-size/fightarr/fightarr/latest)
![Docker Stars](https://img.shields.io/docker/stars/fightarr/fightarr)

Fightarr is a PVR (Personal Video Recorder) for fighting events - UFC, MMA, Boxing, and more. Forked from Sonarr, it's specifically designed for tracking and downloading fighting events automatically.

## Quick Start

### Using Docker Compose (Recommended)

```yaml
version: '3.8'

services:
  fightarr:
    image: fightarr/fightarr:latest
    container_name: fightarr
    restart: unless-stopped
    ports:
      - "1867:1867"  # Year Marquess of Queensberry Rules published
    volumes:
      - ./config:/config
      - /path/to/tv:/tv
      - /path/to/downloads:/downloads
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/New_York
      - FIGHTARR_API_URL=http://fightarr-api:3000
```

### Using Docker CLI

```bash
docker run -d \
  --name=fightarr \
  -e PUID=1000 \
  -e PGID=1000 \
  -e TZ=America/New_York \
  -e FIGHTARR_API_URL=http://your-api-url:3000 \
  -p 1867:1867 \
  -v /path/to/config:/config \
  -v /path/to/tv:/tv \
  -v /path/to/downloads:/downloads \
  --restart unless-stopped \
  fightarr/fightarr:latest
```

### Unraid

1. Go to Docker tab
2. Add Container
3. Select `fightarr/fightarr:latest` as the repository
4. Set the following:
   - Port: `1867:1867`
   - Path `/config`: `/mnt/user/appdata/fightarr`
   - Path `/tv`: `/mnt/user/media/fighting`
   - Path `/downloads`: `/mnt/user/downloads`
   - Environment `FIGHTARR_API_URL`: `http://your-api-url:3000`
5. Click Apply

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `PUID` | `1000` | User ID for file permissions |
| `PGID` | `1000` | Group ID for file permissions |
| `TZ` | `UTC` | Timezone (e.g., `America/New_York`) |
| `FIGHTARR_API_URL` | - | **Required**: URL to your Fightarr-API instance |
| `FIGHTARR__INSTANCENAME` | `Fightarr` | Instance name |
| `FIGHTARR__BRANCH` | `main` | Update branch |
| `FIGHTARR__ANALYTICS_ENABLED` | `False` | Enable analytics |

### Volumes

| Path | Description |
|------|-------------|
| `/config` | Application data and configuration |
| `/tv` | Media library location |
| `/downloads` | Download client output directory |

### Ports

| Port | Description |
|------|-------------|
| `1867` | Web UI (Year Marquess of Queensberry Rules published) |

## Complete Stack with Fightarr-API

For full functionality, you need both Fightarr and Fightarr-API:

```yaml
version: '3.8'

services:
  fightarr:
    image: fightarr/fightarr:latest
    container_name: fightarr
    restart: unless-stopped
    ports:
      - "1867:1867"  # Year Marquess of Queensberry Rules published
    volumes:
      - ./fightarr-config:/config
      - /path/to/tv:/tv
      - /path/to/downloads:/downloads
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/New_York
      - FIGHTARR_API_URL=http://fightarr-api:3000
    depends_on:
      - fightarr-api

  fightarr-api:
    image: fightarr/fightarr-api:latest
    container_name: fightarr-api
    restart: unless-stopped
    ports:
      - "3000:3000"
    volumes:
      - ./api-data:/data
    environment:
      - DATABASE_URL=postgresql://fightarr:fightarr_password@postgres:5432/fightarr_metadata
      - ADMIN_SECRET=change-this-secret
    depends_on:
      - postgres

  postgres:
    image: postgres:16-alpine
    container_name: fightarr-postgres
    restart: unless-stopped
    volumes:
      - postgres-data:/var/lib/postgresql/data
    environment:
      - POSTGRES_USER=fightarr
      - POSTGRES_PASSWORD=fightarr_password
      - POSTGRES_DB=fightarr_metadata

volumes:
  postgres-data:
```

## Supported Architectures

This image supports multiple architectures:

- `linux/amd64` - x86-64 (Most common)
- `linux/arm64` - ARM 64-bit (Raspberry Pi 4, Apple Silicon)

Docker will automatically pull the correct architecture for your system.

## Tags

| Tag | Description |
|-----|-------------|
| `latest` | Latest stable release from main branch |
| `main` | Same as latest |
| `v5-develop` | Development branch (bleeding edge) |
| `v5.x.x` | Specific version tags |

## Building from Source

```bash
git clone https://github.com/Fightarr/Fightarr.git
cd Fightarr
docker build -t fightarr/fightarr:local .
```

## Updating

### Docker Compose

```bash
docker-compose pull fightarr
docker-compose up -d fightarr
```

### Docker CLI

```bash
docker stop fightarr
docker rm fightarr
docker pull fightarr/fightarr:latest
# Run the docker run command again
```

### Unraid

Updates happen automatically if you have "Auto Update" enabled, or manually via the Docker tab.

## Health Check

The container includes a built-in health check:

```bash
docker inspect --format='{{.State.Health.Status}}' fightarr
```

## Troubleshooting

### Check Logs

```bash
docker logs fightarr
```

### Access Shell

```bash
docker exec -it fightarr /bin/bash
```

### Permissions Issues

If you have permission issues with files:

1. Find your user/group IDs:
   ```bash
   id $USER
   ```

2. Update environment variables:
   ```yaml
   environment:
     - PUID=1000  # Replace with your UID
     - PGID=1000  # Replace with your GID
   ```

### API Connection Issues

Verify Fightarr-API is accessible:

```bash
docker exec fightarr curl -f http://fightarr-api:3000/api/health
```

## Support

- **Documentation**: [GitHub Wiki](https://github.com/Fightarr/Fightarr/wiki)
- **Issues**: [GitHub Issues](https://github.com/Fightarr/Fightarr/issues)
- **Discussions**: [GitHub Discussions](https://github.com/Fightarr/Fightarr/discussions)

## License

GPL-3.0 - Same as the parent Sonarr project

## Credits

Fightarr is a fork of [Sonarr](https://github.com/Sonarr/Sonarr), customized for fighting events. All credit to the original Sonarr team for their excellent work.
