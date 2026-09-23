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
3. Enter only the hostname in **Host**, without `https://` or a path. Set **Port** to the provider's XML-RPC port. For HTTPS this is often 443, not the prefilled 8080. Turn on **Use SSL** for HTTPS, and add a username and password if required
4. Set **XML-RPC Path** to the endpoint path, not a full URL. Use `/RPC2` for a root endpoint or `/rutorrent/RPC2` for an endpoint under ruTorrent. A base path such as `/rutorrent` also works because Sportarr adds `/RPC2`. Leaving the field empty uses `/rutorrent/RPC2`
5. Keep the category as `sportarr`
6. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).
