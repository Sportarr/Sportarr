# Troubleshooting

Common issues and the settings that usually explain them. For anything not covered here, [Discord](https://discord.gg/YjHVWGWjjG) is the fastest route to help, and logs live under **System > Logs**.

## Downloads and imports

**Can't connect to the download client in Docker?**
Use the container name (e.g. `qbittorrent`) instead of `localhost`, and make sure both containers are on the same Docker network.

**Files not importing?**
Check that the download path is accessible from within the Sportarr container. The path your download client reports needs to be the same path Sportarr sees; if it can't be, add a remote path mapping under **Settings > Download Clients**.

**Files being moved instead of hardlinked?**
Hardlinks require the download folder and library root to be on the same filesystem (in Docker, under the same volume mount). If you want Sportarr to never move files even after seeding ends, set the download client's **Post-Import Mode** to Hardlink.

**Import says the packed archives were not extracted?**
Sportarr does not unpack archives for torrents. Point [Unpackerr](integrations/unpackerr.md) at the same download folder, or grab a release that isn't packed. Usenet downloads should be unpacked by the client's own post-processing, so check that it's enabled in SABnzbd or NZBGet. Sportarr holds a packed download as Import Pending for 30 minutes before giving up, and during that wait it never blocklists the release or removes it from the client.

**Import happened but the file was tiny or empty?**
Sportarr refuses 0-byte and still-growing files and retries until the transfer settles, so if you see this on an old version, update. With remote seedbox mirrors, give the sync tool time to finish before expecting imports.

## Drives never spin down

The full disk scan wakes every drive holding library content, and older versions ran it hourly, which is shorter than most drives' spin-down timers. The default is now 720 minutes, and upgrading applies it once to every install. If you prefer more frequent scans, lower **Disk Scan Interval** under **Settings > Download Clients**; a value you set after the upgrade is never touched again.

Day-to-day discovery does not depend on the scan. The filesystem watcher picks up changes as they happen and downloads import through the client queue, so raising the interval costs nothing on local storage. Only root folders on network shares, where the watcher cannot see writes from other machines, benefit from a shorter interval. See [Background scanning and drive activity](features/download-clients.md#background-scanning-and-drive-activity).

## Search

**Why did Sportarr not grab an event?**
Every event search leaves a record. When the newest search for an event did not grab anything, a short line under the event says what happened, for example "Searched 2h ago: every release was rejected. The most common reason was quality not allowed (14 of 15)". The line shows on the league page, in the **Missing** and **Cutoff Unmet** tabs under **Activity**, and in the manual search window. Click it to see the search history. Each search says in plain words how it ended and what you can change, how many releases were found and passed, which quality profile judged them, how each indexer answered, and why releases were rejected. The newest search also lists the releases it saw with their reasons. Sportarr keeps the last 5 searches per event for 30 days.

**A release exists on my indexer but Sportarr rejects it?**
Open the interactive search and read the rejection column. The usual suspects are the quality profile (the release's quality isn't enabled in the profile assigned to that league) and custom format scores (a format with a large negative score, like a no-release-group rule imported from movie-oriented guides, can push sports TV captures below the minimum). Sports releases often have no release group, so aggressive No-RlsGroup penalties will reject most of them.

**The same event downloaded twice?**
Background searches skip an event or part while its download is active. Choosing **Auto Search** yourself bypasses this check and can grab another release. If you see an unexpected duplicate, check the Activity page for a download that failed or was removed outside Sportarr, since that frees the event or part to be searched again.

**Nothing found for an event you can see on the tracker?**
Interactive event searches omit category filters so a release filed outside the usual sports categories can still appear. Check the rejection reasons and the league's quality profile. Automatic searches and RSS use the configured categories, so include TV/Sport (5060) and any other categories your indexer uses for sports.

**Repeated searches return the same releases?**
Sportarr reuses recent search results and preserves their indexer, release group and quality information. Matching requests to the same indexer account can also share a response within one search or RSS operation. Each configured indexer's filtering still applies. An empty or short response does not always mean the indexer has returned every matching release.

## Logs

Logs live under **System > Logs**. The main log files start with `sportarr` and hold what Sportarr did, with one summary line per event search. The decisions for each release go to separate search log files that start with `search`. Sportarr keeps up to four search log files of 5 MB each, so the main log stays small and easy to read.

When you select a log and choose **Create issue**, Sportarr builds one file for the report. It holds the recent errors, the recent warnings grouped with a count, the recent searches that did not grab, and the newest part of the log. Passwords, API keys, tokens, email addresses and public IP addresses are removed first.

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

**Recording fails at startup with a connection timed out error?**
Some providers take 15 to 25 seconds to deliver the first byte of a cold stream. In **IPTV > Options > Recording > Advanced**, keep **Read Timeout** at 0 or above the slowest start you see, and raise **Max Retry Wait** if the source refuses the stream for the first few seconds.
