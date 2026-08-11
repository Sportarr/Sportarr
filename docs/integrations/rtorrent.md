# rTorrent

Command-line torrent client, usually driven through ruTorrent or an XML-RPC endpoint.

| | |
|---|---|
| Protocol | Torrent |
| Default port | 8080 |
| Authentication | Username and password when your web frontend requires them |

## Setup

1. Expose rTorrent's XML-RPC endpoint (commonly through ruTorrent or a reverse proxy)
2. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **rTorrent**
3. Enter the host, port, URL base for your RPC mount if you use one, and credentials
4. Keep the category as `sportarr`
5. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).
