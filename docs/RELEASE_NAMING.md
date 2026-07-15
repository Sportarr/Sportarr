# Sportarr Release Naming Standard (v1.1)

A convention for embedding Sportarr's canonical event and league ids in
release names, so any release can be matched to the exact sporting event
with zero ambiguity. Sports releases have never had the equivalent of the
`{tvdb-121361}` tags that TV releases carry for Plex and the media
ecosystem. This is that standard for sports.

Release groups that adopt it get guaranteed exact matching in Sportarr,
with no dependence on how a parser interprets team names, dates, rounds,
or languages. Names tagged today remain forward-compatible forever.

## The token

Append one token to the release name:

```
{sportarr-ev-2336155}
```

The id inside the token is the event's Sportarr id, exactly as shown on
sportarr.net and in the Sportarr API. It is copy-pasteable in both
directions.

Grammar:

```
token       = "{" "sportarr" sep id "}"
id          = prefix sep digits
prefix      = "ev" | "lg"          ; reserved for future use: "ep", "sn", "tm"
sep         = "-" | "." | "_" | " "
digits      = 4*10 DIGIT

bare-id     = prefix strict-sep strict-digits   ; v1.1: accepted without braces or brand
strict-sep  = "-" | "." | "_"                   ; separator required, space not allowed
strict-digits = 6*10 DIGIT
```

Parsers (including Sportarr's) treat the token case-insensitively and
accept lenient variants for pipelines that strip braces or rewrite
separators. All of these resolve identically:

```
{sportarr-ev-2336155}
{SPORTARR-EV-2336155}
sportarr.ev.2336155
{ev-2336155}
ev-2336155
```

The canonical form groups should emit is the first one.

The bare short form (`ev-2336155` with no braces and no brand, added in
v1.1) has nothing marking it as a token, so its rules are stricter than
the other forms. The separator between the prefix and the digits is
required and must be a dash, dot, or underscore, and the id must be at
least 6 digits long. Every Sportarr id satisfies this because ids are
zero-padded to a minimum of 6 digits. Years and the other numbers that
occur naturally in release names are shorter or attached to letters, so
the bare form cannot collide with ordinary title text. `ev-2026`,
`ev 2338110`, and `ev2338110` do not parse as tokens; `ev-2338110` and
`ev.2338110` do.

## Which id to use

**Single-event releases use the event id (`ev-`).** One event id resolves
everything: the sport, league, season, episode number, date, teams, and
round all come from the event record. Do not add league or sport ids to a
single-event release. Redundant ids can only ever disagree with each
other.

**Pack releases use the league id (`lg-`).** A release spanning multiple
events (a week pack, a round pack, a full-season pack) cannot name one
event. Tag it with the league id and put the season and round in the
name. Sportarr resolves the individual events from there.

**Multi-part events (fight cards) use the event id plus the part name.**
Prelims, Early Prelims, and Main Card are parts of one event. Tag every
part with the same event id and keep the part name in the title text. A
part-level id prefix (`ep-`) is reserved for a future spec revision;
until then the textual part name is authoritative.

## Recommended name structure

The token guarantees the match. The rest of the name is for humans and
for tools that don't know the standard yet, so keep it readable:

```
<League>.<Date or Season/Round>.<Event Title>.<Part>.<Quality>.<Source>.<Codec>-<GROUP>{sportarr-ev-XXXXXXX}
```

Dates are ISO ordered, year first: `2026-07-10` or `2026.07.10`. Never
day-first or month-first. Ambiguous date ordering is one of the largest
sources of sports mismatches in the wild.

Examples:

```
FIFA.World.Cup.2026-07-10.Quarter.Final.Spain.vs.Belgium.1080p.WEB.h264-GROUP{sportarr-ev-2336155}
Formula.1.2026.Round.12.British.Grand.Prix.Qualifying.2160p.WEB.h265-GROUP{sportarr-ev-2338110}
UFC.319.Prelims.2026-08-16.720p.WEB.h264-GROUP{sportarr-ev-2340001}
UFC.319.Main.Card.2026-08-16.720p.WEB.h264-GROUP{sportarr-ev-2340001}
WWE.Monday.Night.Raw.2026-07-06.1080p.WEB.h264-GROUP{sportarr-ev-2339870}
MLB.2026-07-04.Yankees.vs.Red.Sox.1080p.WEB.h264-GROUP{sportarr-ev-2337555}
EPL.2026-27.Round.15.Pack.1080p.WEB.h264-GROUP{sportarr-lg-000123}
```

## Embedded file tag

The id can also live inside the file itself, as a Matroska global tag
named `SPORTARR`. This is the strongest carrier of all: it survives any
rename, move, torrent or usenet round-trip, and remux (mkvmerge copies
global tags by default). Groups that already embed IMDB/TMDB tags in
their remuxes can add it to the same tags.xml.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Tags>
  <Tag>
    <Targets>
      <TargetTypeValue>50</TargetTypeValue>
    </Targets>
    <Simple>
      <Name>SPORTARR</Name>
      <String>ev-2338110</String>
    </Simple>
  </Tag>
</Tags>
```

```
mkvpropedit release.mkv --tags global:tags.xml
```

Rules, chosen to match the Matroska tagging spec and how tools actually
read tags back:

- Tag name is `SPORTARR`, uppercase (Matroska tag names use capital
  letters). The value is the canonical id, lowercase, nothing else.
- `TargetTypeValue` is `50` (the movie/episode level, correct for a
  single event file). Do not add a `TargetType` string element next to
  it. ffprobe prefixes the key with it ("MOVIE/SPORTARR"), which some
  readers will miss.
- Leave the tag language at its default. A language suffix changes the
  key some tools see.
- One-liner alternative without mkvtoolnix, at mux time:
  `ffmpeg -i in.mkv -c copy -metadata SPORTARR=ev-2338110 out.mkv`.
- Verify with `ffprobe -show_format` (the tag appears under format
  tags) or MediaInfo (General section), exactly like an embedded IMDB
  tag.
- MKV only. MP4 has no mapping for Matroska simple tags, so for
  non-MKV containers the filename token carries the id.
- Sportarr reads the embedded tag on import and scan and treats it with
  the same authority as a filename token (the filename token wins if
  both are present and disagree). Note that media servers do not
  currently read embedded ids for matching, so keep the token in the
  release name too; the embedded tag is the recovery path for files
  whose names get mangled downstream.

## Finding ids

Every event and league page on sportarr.net displays its id. The API
supports lookups by league and date for automation, so a release module
can resolve the event id at cut time from the fixture it is recording.
Ids exist for future events as soon as the fixture is scheduled, so
releases can be tagged the moment an event airs.

## For trackers and indexers

Trackers can make the id a first-class, searchable field. The full
integration is three pieces, all of them standard newznab/torznab
mechanics:

**Upload form field.** Accept a "Sportarr ID" on sports uploads,
validated with `^(ev|lg)-\d{6,10}$`. The id can be verified (and the
form autofilled with the event's name, league, date, and venue) via the
public lookup API, no key required:

```
GET https://sportarr.net/api/public/v1/events/ev-2338110
GET https://sportarr.net/api/public/v1/leagues/lg-000123
```

Anonymous access is rate limited per IP, which comfortably covers
upload-time validation. Trackers doing higher-volume work (catalog
backfills, bulk verification) can request a partner API key; sent as an
`X-API-Key` header, it replaces the IP limits with the key's own larger
per-minute and per-day budget, reported back in `X-RateLimit-*`
response headers with `Retry-After` on 429.

**Response attribute.** Emit the id on search results as an extended
attribute, in the standard attr namespace for your protocol:

```xml
<torznab:attr name="sportarrid" value="ev-2338110" />
```

Sportarr reads this attribute (torznab and newznab) and treats it as
authoritative for matching, the same as an id token in the release
name. The in-name token takes precedence if both are present.

**Search parameter.** Advertise `sportarrid` in your caps document's
`supportedParams` and accept it as a query parameter:

```xml
<search available="yes" supportedParams="q,sportarrid" />
```

Sportarr checks caps and only sends `sportarrid=ev-XXXXXXX` to servers
that advertise it, the same capability negotiation the ecosystem
already uses for ids like tvdbid, so enabling it can never break
anything. When the parameter is present,
treat it as the sole selector and ignore `q`; the id is exact, and the
text query is only there for servers that don't know the parameter.

One honest caveat for the current ecosystem: apps connected to trackers
through Prowlarr won't see the id parameter or attribute yet, because
Prowlarr only relays fields it knows about. Trackers connected to
Sportarr directly (native torznab support) get the full integration
today. Prowlarr support is a standard param addition of the kind it has
taken before (tmdbid, doubanid), and this spec is written so that hop
needs no changes on the tracker side when it lands.

## Stability guarantees

These are the commitments that make the standard safe to build on:

- Ids are immutable and never reused. An id printed in a release name
  today resolves to the same event permanently.
- When duplicate records are merged on our side, the old id becomes an
  alias that resolves to the surviving record forever. A tagged release
  never orphans.
- The token grammar is versioned. Revisions are additive only (new
  prefixes, never changed meaning), so a v1 name is valid under every
  future revision.

## What Sportarr does with the token

A recognized id is the strongest matching signal Sportarr has. It maps
the release directly to the event, bypassing fuzzy title matching
entirely. It is validated, not blindly trusted: the id must resolve,
and an id naming a different event is a definitive rejection no matter
how similar the titles read. A league id on a pack works the same way
at league level: it can't confirm a single event by itself, but a pack
tagged for a different league is rejected outright.

When a release or file carries the id in more than one place, the
precedence is: token in the name first, then the indexer's sportarrid
attribute (for releases) or the embedded SPORTARR file tag (for files
on disk). Sportarr's own renamer can also stamp the token into library
filenames, so imports, rescans, and manual file moves match exactly for
the life of the file.
