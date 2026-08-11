# Aria2

Lightweight multi-protocol downloader; Sportarr drives it over its RPC interface for torrents.

| | |
|---|---|
| Protocol | Torrent |
| Default port | 6800 |
| Authentication | RPC secret token |

## Setup

1. Start aria2 with RPC enabled and a secret token (`--enable-rpc --rpc-secret=...`)
2. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **Aria2**
3. Enter the host, port, and the secret token in the API Key field
4. Keep the category as `sportarr`
5. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).
