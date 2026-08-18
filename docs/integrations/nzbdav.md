# NZBdav

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/nzbdav.svg" alt="" width="72" height="72" />
</p>

Usenet streaming via WebDAV, exposed through a SABnzbd-compatible API, so completed downloads mount instead of occupying local disk.

| | |
|---|---|
| Protocol | Usenet |
| Default port | 3000 |
| Authentication | API key (SABnzbd-compatible) |

## Setup

1. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **NZBdav**
2. Enter the host, port, and API key
3. Keep the category as `sportarr`
4. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).
