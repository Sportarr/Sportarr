# NZBGet

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/nzbget.svg" alt="" width="72" height="72" />
</p>

Efficient usenet downloader with a small resource footprint.

| | |
|---|---|
| Protocol | Usenet |
| Default port | 6789 |
| Authentication | Control username and password |

## Setup

1. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **NZBGet**
2. Enter the host, port, and the control username and password from NZBGet's settings (`ControlUsername` / `ControlPassword`)
3. Keep the category as `sportarr`
4. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).
