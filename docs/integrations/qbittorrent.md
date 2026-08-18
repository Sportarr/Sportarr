# qBittorrent

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/qbittorrent.svg" alt="" width="72" height="72" />
</p>

Free and reliable torrent client, and the most common pairing for Sportarr torrent setups.

| | |
|---|---|
| Protocol | Torrent |
| Default port | 8080 |
| Authentication | WebUI username and password |

## Setup

1. In qBittorrent, enable the WebUI under **Tools > Options > Web UI** and set a username and password
2. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **qBittorrent**
3. Enter the host, port, and your WebUI credentials
4. Keep the category as `sportarr` so Sportarr only manages its own downloads
5. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).
