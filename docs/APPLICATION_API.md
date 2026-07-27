# Sportarr Application API

The stable contract for external applications that manage indexers in
Sportarr or resolve Sportarr library items - indexer managers, library
lifecycle tools, and anything else that wants to integrate natively instead
of riding a compatibility shim. Everything on this page is part of Sportarr's
native `/api/*` surface.

**Stability guarantee:** this contract is frozen. Endpoints, field names, and
value semantics documented here only change additively (new fields, new
endpoints). An integration written against this page keeps working across
Sportarr upgrades. Where a minimum version is listed, gate your integration
on it via the version signals below.

Related: [EXTERNAL_IDS.md](EXTERNAL_IDS.md) documents the numeric id alias
contract used to resolve Sportarr library items from media-server metadata.

## Authentication and version signals

Every request carries the API key, either way:

```
X-Api-Key: <key>          (header, preferred)
?apikey=<key>             (query parameter)
```

Every API response carries the running version in a header:

```
X-Application-Version: 4.0.1022
```

`GET /api/system/status` returns identity and version in the body:

```json
{ "appName": "Sportarr", "version": "4.0.1022.1102", "isDocker": true }
```

Check `appName` to confirm you are talking to Sportarr, and compare the
version against the minimum your integration requires. Versions are numeric
dot-separated segments; compare segment-wise.

## Indexer management

Minimum version: the first release at or above 4.0.1023 (single reads, schema
templates, and typed field values ship there; list/create/update/delete/test
predate it).

### The indexer object

All read endpoints return, and write endpoints accept, this shape:

```json
{
  "id": 12,
  "name": "MyIndexer (Prowlarr)",
  "implementation": "Torznab",
  "configContract": "TorznabSettings",
  "enable": true,
  "enableRss": true,
  "enableAutomaticSearch": true,
  "enableInteractiveSearch": true,
  "priority": 25,
  "fields": [
    { "name": "baseUrl", "value": "http://prowlarr:9696/12/" },
    { "name": "apiPath", "value": "/api" },
    { "name": "apiKey", "value": "..." },
    { "name": "categories", "value": "5060,5070" },
    { "name": "minimumSeeders", "value": "1" },
    { "name": "seedRatio", "value": "" },
    { "name": "seedTime", "value": "" },
    { "name": "seasonPackSeedTime", "value": "" },
    { "name": "additionalParameters", "value": "" },
    { "name": "rejectBlocklistedTorrentHashes", "value": "False" }
  ],
  "tags": []
}
```

- `implementation` is `Newznab`, `Torznab`, `Rss`, or `BroadcasTheNet`.
  External syncs should stick to `Newznab`/`Torznab`.
- Reads serialize every field value as a string; `categories` is a
  comma-joined list of Newznab category ids.
- Writes accept JSON-typed values as well as strings: `categories` may be an
  array of ints (`[5060, 5070]`), numbers may be numbers, booleans may be
  booleans. Unknown field names are ignored, so payloads built from a newer
  schema never fail against an older Sportarr.
- An explicitly empty `categories` value clears the list; Sportarr then falls
  back to its per-sport defaults at search time.
- Seed criteria live in flat fields (`seedRatio`, `seedTime`,
  `seasonPackSeedTime`), not nested names.
- Fields a caller does not send keep their stored values on update, so user
  edits made in Sportarr's UI survive an external sync that only sends its
  own fields.

### Endpoints

| Method | Path | Behavior |
|---|---|---|
| GET | `/api/indexer` | List all indexers (transformed shape above) |
| GET | `/api/indexer/{id}` | Single indexer, 404 when unknown |
| GET | `/api/indexer/schema` | Newznab and Torznab templates with default field sets |
| POST | `/api/indexer` | Create; returns the created object including `id` |
| PUT | `/api/indexer/{id}` | Update; returns the updated object |
| DELETE | `/api/indexer/{id}` | Remove |
| POST | `/api/indexer/test` | Validate a payload by querying the target feed; 200 on success |

`POST /api/indexer/test` accepts the same object shape (an `id` is not
required) and performs a live query against the indexer's `baseUrl`, so a
sync proxy can verify Sportarr can actually reach it. Like every response,
it carries the `X-Application-Version` header.

### Recognizing your own indexers

A sync manager finds the entries it owns the same way it does in other apps:
list the indexers and match on your own signature - the `apiKey` field value
you wrote, and your proxy URL prefix in `baseUrl` (conventionally ending in
`/{yourIndexerId}/`). Store Sportarr's returned `id` per entry and use the
id-based read/update/delete endpoints for everything after creation.

### Categories

Sportarr searches sports content. `5060` (TV/Sport) is the primary Newznab
category; some sports content is filed under the Movies subcategories
(`2010`-`2060`) by certain indexers, which is why Sportarr's own defaults
include both.

Recommended default sync set for an external manager: `5060`, optionally
plus the Movies categories for indexers known to file sports there. Sportarr
tolerates over-broad category lists; its matcher filters non-sports results.

## Library item resolution

For resolving Sportarr library items (leagues/events) from media-server
metadata, see [EXTERNAL_IDS.md](EXTERNAL_IDS.md): Sportarr stamps a numeric
alias in the tvdb provider-id namespace (league alias = 900,000,000 + the
numeric part of the `lg-XXXXXX` id), and the league/event read endpoints in
[api.md](api.md) serve the details. That contract is frozen the same way
this one is.
