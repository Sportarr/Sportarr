# Sportarr API Versioning

Sportarr exposes three families of HTTP routes. They look like API versions but they serve different purposes.

| Prefix | Purpose | Stability guarantee | Used by |
|---|---|---|---|
| `/api/*` | **Sportarr's native API** | Internal contract; current = v1 semantics. Breaking changes only with a coordinated frontend release. | Sportarr web frontend |
| `/api/v1/*` | **Sonarr v1 compatibility shim** | Frozen — must match Sonarr's v1 API contract byte-for-byte. | Prowlarr |
| `/api/v3/*` | **Sonarr v3 compatibility shim** | Frozen — must match Sonarr's v3 API contract byte-for-byte. | Decypharr, Maintainerr, ArrControl, dashboards configured with the Sonarr template |

## Why both v1 and v3?

Sonarr itself ships both versions of its public API:

- `v1` covers the indexer-management surface (`/api/v1/indexer`, `/api/v1/system/status`) which Prowlarr uses to push indexers into Sportarr.
- `v3` covers the catalog surface (`/api/v3/series`, `/api/v3/episodefile`, `/api/v3/calendar`) which downstream consumers use to read state.

Because these are *compatibility shims*, the contracts are owned by Sonarr, not us. We do not version them ourselves — when Sonarr ships a v4, we'd add `/api/v4/*` shims for the relevant endpoints, leaving `/api/v3/*` in place.

## Why is `/api/*` not versioned?

Because it has a single consumer (the Sportarr frontend) and we ship the frontend in lockstep with the backend. There is no stability promise to make. If we ever publish a public Sportarr SDK that third parties build against, that's the moment to introduce `/api/v2/*` and freeze `/api/v1/*` semantics.

## Where each family lives in the codebase

| Prefix | Source files |
|---|---|
| `/api/*` | All non-prefixed extension methods in `src/Endpoints/` (e.g. `EventEndpoints.cs`, `LeagueEndpoints.cs`, `IptvEndpoints.cs`) |
| `/api/v1/*` | `src/Endpoints/V1ProwlarrEndpoints.cs` |
| `/api/v3/*` | `src/Endpoints/Sonarr*.cs` (`SonarrSeriesEndpoints.cs`, `SonarrCalendarEndpoint.cs`, `SonarrEpisodeFileEndpoints.cs`, `SonarrIndexerEndpoints.cs`, `SonarrCommandEndpoints.cs`, `SonarrConfigEndpoints.cs`, `SonarrSystemEndpoints.cs`, `SonarrDownloadClientEndpoint.cs`) |

## Note on Prowlarr first-class support

As of this writing, Sportarr is not a first-class app type in Prowlarr. Users add Sportarr as if it were a Sonarr instance, which works because we emulate Sonarr's v1 indexer-management contract.

The long-term path is to submit a PR to Prowlarr adding Sportarr as a native app type. Until then, the v1 emulation is the supported integration. Keep `/api/v1/*` byte-compatible with Sonarr's v1 contract — Prowlarr's payloads expect specific field names and shapes (`baseUrl`, `apiKey`, `categories`, etc.), and breaking those breaks every existing Prowlarr → Sportarr connection in the wild.

## Don't add new routes under `/api/v1/` or `/api/v3/`

Those prefixes are reserved for Sonarr-emulation routes that an external consumer (Prowlarr/Decypharr/Maintainerr) actually expects to find. Native Sportarr features go under `/api/*` even if they happen to share a domain (e.g. our own `/api/indexer/*` lives next to the v1 shim at `/api/v1/indexer/*`).
