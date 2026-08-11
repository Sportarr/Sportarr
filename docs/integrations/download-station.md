# Synology Download Station

Synology's built-in downloader. Download Station handles torrents and usenet from the same NAS, but Sportarr tracks them as two connections since they are configured independently: add **Synology Download Station** for torrents and **Synology Download Station (Usenet)** separately if you want both.

| | |
|---|---|
| Protocol | Torrent, and usenet via the separate usenet entry |
| Default port | 5000 |
| Authentication | DSM username and password |

## Setup

1. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **Synology Download Station** (or the usenet variant)
2. Enter your NAS host, DSM port, and a DSM account with Download Station access
3. Keep the category as `sportarr`
4. **Test**, then **Save**
5. Repeat with the usenet variant if Download Station also handles your NZBs

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).
