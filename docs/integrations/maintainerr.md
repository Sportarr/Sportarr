# Maintainerr

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/maintainerr.svg" alt="" width="72" height="72" />
</p>

Maintainerr automates library cleanup rules for Plex. It has native Sportarr support, so your sports library can be cleaned up with the same rule engine you use for movies and shows.

!!! info "Availability"
    Native Sportarr support shipped in [Maintainerr v3.20.0](https://github.com/Maintainerr/Maintainerr/releases/tag/v3.20.0). It requires Sportarr 4.0.1022 or later.

## Setup

1. In Maintainerr, go to **Settings > Sportarr** and add your Sportarr server (URL and API key from Sportarr's **Settings > General**)
2. Create a collection over a Shows library that holds your sports content. In the collection settings, switch **Managed by** from Sonarr to **Sportarr** and pick your Sportarr server
3. Build rules using the Sportarr rule properties (last aired, has upcoming events, file age, and the rest) and run the collection

Items resolve by exact ID against Sportarr, and deletions clean up the download client too.

## How matching works

The Sportarr metadata provider stamps every show with external IDs that Maintainerr reads from Plex. This only happens when the sports library itself uses the Sportarr agent. Adding the provider under **Settings > Metadata Agents** is not enough on its own, the library must be created with (or switched to) the Sportarr agent, and the legacy bundle agent does not stamp IDs at all.

If Maintainerr logs `Couldn't resolve a Sportarr league id for media server item ...`, the show in Plex has no Sportarr IDs on it. Check two things. First, the event files must carry a season and episode number in their names (`S2026E23` style) so Plex can match them to the agent at all. Files without one never match, and Plex logs `Match request for 'UFC' returned no metadata` for them. Second, open the show and check **Edit > Match**. If it says Plex TV Series, the library is on the wrong agent. Fix whichever applies, refresh metadata on the show, then run the collection again.

Maintainerr needs no extra step after that. It reads the show's IDs live from Plex when it evaluates rules and again when it acts, with a five minute cache at most, so the next scheduled run (or the run button on the rule) picks up the refreshed metadata. One thing to expect. If renaming the files made Plex create new items, the old ones drop out of the collection and the new ones are added fresh, so the deletion countdown starts over for them.

The debug line `No external ids resolved for N of N children of Plex collection ...` is normal for a collection of events. Sportarr stamps IDs on the show, not on individual events, and Maintainerr reads them from the show. The full ID contract is documented in [External IDs](../EXTERNAL_IDS.md).
