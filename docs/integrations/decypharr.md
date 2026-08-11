# Decypharr

Debrid download client bridging Real-Debrid, Torbox, and similar services into a qBittorrent-compatible API. Sportarr supports it for torrents, and experimentally for usenet through the separate **DecypharrUsenet** entry (requires the Decypharr usenet branch).

| | |
|---|---|
| Protocol | Torrent, and experimental usenet via the separate entry |
| Default port | 8282 |
| Authentication | Configured in Decypharr; Sportarr also passes its own URL and API key so Decypharr can report back |

## Setup

1. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **Decypharr**
2. Enter the host and port, plus the **Sportarr URL** and **Sportarr API key** fields so Decypharr can call back into Sportarr
3. Keep the category as `sportarr`
4. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).
