# Troubleshooting

Common issues and the settings that usually explain them. For anything not covered here, [Discord](https://discord.gg/YjHVWGWjjG) is the fastest route to help, and logs live under **System > Logs**.

## Downloads and imports

**Can't connect to the download client in Docker?**
Use the container name (e.g. `qbittorrent`) instead of `localhost`, and make sure both containers are on the same Docker network.

**Files not importing?**
Check that the download path is accessible from within the Sportarr container. The path your download client reports needs to be the same path Sportarr sees; if it can't be, add a remote path mapping under **Settings > Download Clients**.

**Files being moved instead of hardlinked?**
Hardlinks require the download folder and library root to be on the same filesystem (in Docker, under the same volume mount). If you want Sportarr to never move files even after seeding ends, set the download client's **Post-Import Mode** to Hardlink.

**Import happened but the file was tiny or empty?**
Sportarr refuses 0-byte and still-growing files and retries until the transfer settles, so if you see this on an old version, update. With remote seedbox mirrors, give the sync tool time to finish before expecting imports.

## Search

**A release exists on my indexer but Sportarr rejects it?**
Open the interactive search and read the rejection column. The usual suspects are the quality profile (the release's quality isn't enabled in the profile assigned to that league) and custom format scores (a format with a large negative score, like a no-release-group rule imported from movie-oriented guides, can push sports TV captures below the minimum). Sports releases often have no release group, so aggressive No-RlsGroup penalties will reject most of them.

**The same event downloaded twice?**
An event that is already downloading is not searched again, however long the transfer takes, so a second release cannot be grabbed while the first is on its way. If you do see two grabs for one event, check the Activity page for a download that failed or was removed outside Sportarr, since that frees the event to be searched again.

**Nothing found for an event you can see on the tracker?**
Check the league's quality profile allows the release's quality, and verify the indexer's categories include TV/Sport (5060). Movies categories (2000-series) help on indexers that file sports there.

## Indexers

**Indexer errors?**
Check your API keys and rate limits. Full request logs are under **System > Logs** with trace logging enabled.

## Reverse proxy

**"API routing/configuration error" toasts, or the UI half-loads?**
This almost always means the proxy is serving Sportarr's own page (or an error page) for `/api/*` requests instead of forwarding them to the app - the giveaway is the toast's description mentioning an HTML response where JSON was expected. It happens most often with a single catch-all location block that was written for a plain single-page app and never got a matching rule for `/api/`.

**Do I need to set URL Base?**
Only if Sportarr is reachable under a *subfolder* of a shared domain (e.g. `example.com/sportarr/`). Leave URL Base blank if Sportarr has its own (sub)domain and answers at the root (e.g. `sportarr.example.com/`) - that includes third-level domains. Setting URL Base to `/` has no effect either way; internally it normalizes to the same blank value.

**Minimal nginx config for Sportarr at the root of its own (sub)domain:**

```nginx
server {
    listen 443 ssl;
    server_name sportarr.example.com;

    # ssl_certificate / ssl_certificate_key / your usual TLS config here

    location / {
        proxy_pass http://sportarr:1867;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Needed for the live status hub Bazarr and similar Sonarr-style
        # consumers use (/signalr/messages); harmless to include even if
        # you don't use it.
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
    }
}
```

Because the whole domain belongs to Sportarr here, one `location /` block that proxies everything is correct - there's no separate SPA-only block to conflict with `/api/`. If you're instead putting Sportarr under a path on a domain shared with other services, give `/api/`, `/signalr/`, and the SPA path their own `location` blocks (in that order, most specific first) rather than one `location /` catching everything, and set URL Base to match the path.

**Still see errors after that?** Check that `proxy_pass` points at the container's actual network name and port (`1867`), not `localhost`, and that both containers share a Docker network - the same requirement as reaching a download client.

## IPTV DVR

**Recordings not scheduling?**
The event must be monitored, the league needs a mapped channel (or EPG/broadcaster match), and the league's **Automatic DVR scheduling** toggle must be on.
