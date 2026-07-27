# Sportarr External IDs

This document is the published contract for identifying Sportarr entities
outside of Sportarr itself. It covers the id forms Sportarr emits, the URI
namespace third-party tools can recognize, and the numeric aliases used
where an integration only accepts integers. Release-name tokens (how a
tracker tags an upload with the event it contains) are specified separately
in [RELEASE_NAMING.md](RELEASE_NAMING.md).

## Canonical short ids

Every league and event has a permanent short id minted by the Sportarr
metadata API (sportarr.net):

| Entity | Form | Example |
|--------|------|---------|
| League | `lg-` + number | `lg-000142` |
| Event | `ev-` + number | `ev-848683` |

The numeric part is zero-padded to six digits and grows naturally beyond
that (`lg-1234567` is valid). Short ids are stable forever, never reused,
and survive entity merges. They are the primary identity for everything in
this document.

## URI namespace

For systems that carry provider-prefixed external ids (for example the
`Guid` array in Plex metadata), Sportarr ids use the `sportarr` scheme:

```
sportarr://lg-000142
sportarr://ev-848683
```

Third-party tools that want to support Sportarr natively should recognize
this namespace. The id after the scheme is always a canonical short id.

## Numeric id aliases

Parts of the media-automation ecosystem only accept integer external ids.
For those surfaces, each entity type has a reserved integer range. The
alias is the short id's numeric part plus a fixed per-type offset:

| Entity | Offset | Example |
|--------|--------|---------|
| League | 900,000,000 | `lg-000142` → `900000142` |
| Event | 1,000,000,000 | `ev-848683` → `1000848683` |

These offsets are **frozen forever**. Media servers and downstream tools
persist the values, so the constants can never change. The alias and the
short id are mechanically convertible in both directions, which means any
integration storing one can always derive the other.

The ranges start far above every real-world id space they might share a
field with. That is deliberate. If a tool mistakenly resolves an alias
against a third-party database, the lookup finds nothing instead of
matching an unrelated entry.

Values below 900,000,000 in these fields come from installs that have not
yet synced since the short-id change and still expose a raw legacy numeric
id. They resolve correctly against that same install and disappear on its
next library sync.

## The `tvdb://` compatibility envelope

Most existing ecosystem tools only parse three external-id namespaces:
`imdb`, `tmdb`, and `tvdb`. Until native `sportarr://` support lands in
those tools, the sportarr.net Plex metadata provider also emits the league
alias inside the `tvdb` namespace:

```json
"Guid": [
  { "id": "sportarr://lg-000142" },
  { "id": "tvdb://900000142" }
]
```

To be clear about what this is and is not:

- The value is a Sportarr numeric alias. It is **not** a TVDB id, and
  nothing in the pipeline ever queries TVDB with it.
- The number sits far above real TVDB id space, so a tool that does try to
  resolve it externally gets an empty result rather than a wrong show.
- The Sonarr v3 compatibility API on a Sportarr install reports the same
  number in its `tvdbId` fields, so tools that read an id from the media
  server and look it up against the install (Maintainerr, for example)
  close the loop entirely inside Sportarr.

### Retirement policy

The envelope is a bridge, not the identity. Both forms are emitted side by
side, and the native `sportarr://` entry is the preferred one wherever a
tool supports it. Once the tools a user depends on read the native
namespace, the `tvdb://` entry can be dropped for that flow without any
migration: the digits inside both entries are the same number modulo the
frozen offset, so nothing stored anywhere needs rewriting.
