# Deluge

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/deluge.svg" alt="" width="72" height="72" />
</p>

Lightweight torrent client.

| | |
|---|---|
| Protocol | Torrent |
| Default port | 8112 |
| Authentication | WebUI password |

## Setup

1. Enable the Deluge WebUI (the `deluge-web` service) and set its password
2. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **Deluge**
3. Enter the host, port, and WebUI password
4. Keep the category as `sportarr`
5. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).
