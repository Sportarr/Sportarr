# Motorsport live status fixtures

Real responses from the motorsport timing sources (the series' own services, and ESPN for F1 and IndyCar), fetched with curl on
2026-11-17 between 20:24 and 20:33 UTC. They are trimmed to the parts the
providers read. Field names, values and time formats are unchanged.

| File | Source | Notes |
|---|---|---|
| `motogp-events-2026.json` | `https://api.motogp.pulselive.com/motogp/v1/events?seasonYear=2026` | Season calendar. Kept to the Thailand, Australia, Qatar and Portugal Grands Prix. Thailand has a Moto2 race listed as `RAC2`. Australia has cancelled races. |
| `motogp-event-2026-qatar.json` | `https://api.motogp.pulselive.com/motogp/v1/events/{id}` | Qatar Grand Prix, 6 to 8 November. Every session `FINISHED`. |
| `motogp-event-2026-portugal.json` | same, Portugal id | Portugal Grand Prix, 20 to 22 November. Every session `NOT-STARTED`. |
| `wsbk-rounds-2026.json` | `https://api.pulselive.worldsbk.com/wsbk-events/v1/seasons/2026/rounds` | Kept to the FRA, EST and JER rounds. The round code in the next URL is `attributes.source_id`, such as `JER`, not the id `2026-JER`. |
| `wsbk-sessions-2026-FRA.json` | `.../seasons/2026/rounds/FRA/sessions` | Magny-Cours. SBK Race 1 and SSP Race 2 were red-flagged. The stopped part is `Race 1 - Red Flag` and the restart keeps `Race 1`. Kept to SBK, SSP and SPB. |
| `wsbk-sessions-2026-EST.json` | `.../rounds/EST/sessions` | Estoril. SSP Race 2 was red-flagged. |
| `wsbk-sessions-2026-JER.json` | `.../rounds/JER/sessions` | Jerez, last round, no red flags. |
| `espn-{f1,irl}-range-{from}-{to}.json` | `https://site.api.espn.com/apis/site/v2/sports/racing/{f1,irl}/scoreboard?dates={from}-{to}` | Fetched 2026-11-18 03:34 UTC with the exact ranges the provider sends. Singapore (sprint weekend, `post`), Las Vegas (`pre`), Monterey and the Milwaukee doubleheader. Competitors removed. |
| `bsb-results-2026.html` | `https://www.tsl-timing.com/Results/bsb` | Kept to the races list. Testing days are left out. |
| `bsb-event-264204.html` | `https://www.tsl-timing.com/event/264204` | Round 11, Brands Hatch. Kept to the championship lists. |

What the captures showed:

- MotoGP and WorldSBK report `NOT-STARTED`, `CURRENT` and `FINISHED` per
  session. MotoGP also reports `CANCELLED`. No capture caught a session in
  `CURRENT`, because no session ran on the capture day. The issue records
  `CURRENT` for the Moto2 race of the Qatar Grand Prix on 2026-11-08.
- MotoGP gives no finish time. A race has the same `date_start` and
  `date_end`. Offsets are written without a colon, as `+0300`.
- WorldSBK gives `effective_end_date_utc` once a session ends.
- ESPN gives `status.type.state` as `pre` or `post` in these captures, and
  no finish time. No ESPN capture has a `completed_at` field.
- ESPN keys each event on US Eastern days. A one-day `dates=20261122`
  returned nothing for the Las Vegas race at 04:00 UTC on 22 November.
  `20261121` returned the whole weekend. A range such as
  `20260904-20260906` leaves out its last day (it was
  empty for a race on 6 September), while `20260906-20260906` returned it.
  The provider asks for the UTC day before to the UTC day after.
- No server sent gzip without a request on the capture day. The gzip test
  builds its own body.
- DTM (`https://api.dtm.com/data?query=events`) gives a calendar with an
  `eventCategory` such as `DTM 2026` or `Ended`, and no session status.
  `?query=eventDetails&slug=...` returned `{"events":[]}`. DTM is not
  covered.
- From this host, the pulselive servers reset IPv6 connections. IPv4 works.
