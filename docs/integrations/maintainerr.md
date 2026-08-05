# Maintainerr

Maintainerr automates library cleanup rules for Plex. It has native Sportarr support, so your sports library can be cleaned up with the same rule engine you use for movies and shows.

!!! info "Availability"
    Native Sportarr support shipped in [Maintainerr v3.20.0](https://github.com/Maintainerr/Maintainerr/releases/tag/v3.20.0). It requires Sportarr 4.0.1022 or later.

## Setup

1. In Maintainerr, go to **Settings > Sportarr** and add your Sportarr server (URL and API key from Sportarr's **Settings > General**)
2. Create a collection over a Shows library that holds your sports content. In the collection settings, switch **Managed by** from Sonarr to **Sportarr** and pick your Sportarr server
3. Build rules using the Sportarr rule properties (last aired, has upcoming events, file age, and the rest) and run the collection

Items resolve by exact ID against Sportarr, and deletions clean up the download client too.

## How matching works

Sportarr's media server agents stamp every show with external IDs that Maintainerr reads from Plex. If a league won't resolve, refresh metadata on that show in Plex and trigger a sync for the league in Sportarr, then re-run the collection. The full ID contract is documented in [External IDs](../EXTERNAL_IDS.md).
