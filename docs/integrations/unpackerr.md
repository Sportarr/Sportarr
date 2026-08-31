# Unpackerr

Watches the Sportarr queue for finished downloads that arrived as archives, extracts them in place, and tidies up once Sportarr has imported. Sportarr does not unpack archives for torrents on its own, so if your indexers hand you packed releases this is the piece that makes them importable.

| | |
|---|---|
| Protocol | Torrent. Usenet clients unpack during their own post-processing |
| Sportarr side | Sonarr v3 compatibility API, nothing to configure in Sportarr |
| Authentication | Sportarr API key |

## Setup

Unpackerr has no Sportarr app type, so add Sportarr under a `[[sonarr]]` block. The v3 compatibility API answers the exact calls Unpackerr makes.

```toml
[[sonarr]]
  url = "http://sportarr:1867"
  api_key = "your-sportarr-api-key"
  paths = ["/downloads"]
  protocols = "torrent"
  delete_orig = false
  delete_delay = "5m"
```

The same thing as Docker environment variables:

```
UN_SONARR_0_URL=http://sportarr:1867
UN_SONARR_0_API_KEY=your-sportarr-api-key
UN_SONARR_0_PATHS_0=/downloads
UN_SONARR_0_PROTOCOLS=torrent
```

The API key is under **Settings > General**. `paths` is the download directory as Unpackerr sees it, and Unpackerr needs read and write access to it. Both containers must reach the same files at the same paths, so mount the download volume into both.

## How the handoff works

1. The download finishes, Sportarr's import walks the folder and finds only archives, and the item is held as Import Pending instead of failing.
2. Unpackerr sees the item in the queue with status `completed`, extracts the archives into a temporary `<folder>_unpackerred` directory, moves the extracted files back into the download folder, and removes the temporary directory.
3. Sportarr's next import attempt finds the video file and imports it.
4. Once the item leaves the Sportarr queue, Unpackerr cleans up after `delete_delay`.

Sportarr allows 30 minutes for this. Inside that window a packed download does not spend its import retry budget, is never blocklisted, and is never removed from the download client, so a torrent that is still seeding is left alone. After 30 minutes the import fails and says the archives were never extracted.

Multi-part sets are handled as one archive, so `.part1.rar` through `.partN.rar` and `.rar` plus `.r00` both work.

## Path matching

Unpackerr finds the download folder by joining each configured path with the release title from the queue, giving `/downloads/<release title>`. Where the client saves a download to a folder named after the release, which is the usual case, this needs no extra configuration.

When the folder on disk carries a different name from the release title, Unpackerr falls back to the `outputPath` Sportarr reports on the queue record. Sportarr fills that in from whatever path the download client last reported for the job, so a renamed job folder still resolves. A queue item Sportarr has not yet polled since the download client learned the path carries no `outputPath` yet, and Unpackerr waits rather than acting on the wrong folder.

Unpackerr does not read Sportarr's remote path mappings. Its `paths` list is its own, and it has to name the download directory as Unpackerr sees it.

## Notes

- `protocols` defaults to `torrent`. Add `usenet` only if your usenet client is set not to unpack.
- Leave `delete_orig` at `false`. Sportarr's import handles the files, and deleting the originals early breaks a seeding torrent.
- Sportarr's API key gates `/api/*` whether or not UI authentication is switched on, so `api_key` is always required.

Verified against Unpackerr 0.16.1.
