# SABnzbd

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/sabnzbd.svg" alt="" width="72" height="72" />
</p>

Open source binary newsreader, the standard choice for usenet.

| | |
|---|---|
| Protocol | Usenet |
| Default port | 8080 |
| Authentication | API key |

## Setup

1. Copy the API key from SABnzbd under **Config > General > Security**
2. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **SABnzbd**
3. Enter the host, port, and API key. If SABnzbd runs under a URL base like `/sabnzbd`, set it in **URL Base**
4. Keep the category as `sportarr`
5. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).
