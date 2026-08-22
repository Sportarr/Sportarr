# League Alias Query Expansion + Search Token List Repair

**Date:** 2026-08-22
**Status:** Approved, pending implementation plan

## Problem

Sportarr knows league aliases but never searches for them.

`League.AlternateName` holds upstream aliases (TheSportsDB `strLeagueAlternate`) — e.g. a league canonically named "Premiership Rugby" also carries "Gallagher Premiership Rugby", the sponsor-branded name scene groups actually use. The release *matcher* accepts these: `ReleaseMatchingService.LeagueAliases()` splits the field and matches any alias.

The query *builder* never reads the field. `EventQueryService.GetNormalizedLeagueNameForTemplate()` resolves `{League}` through a hardcoded `if` ladder of ~10 abbreviations (NBA, NFL, UFC, Formula1, ...) and falls back to `leagueName.Replace(" ", "")`. So Sportarr will happily accept a release named "Gallagher Premiership..." but will never ask an indexer for one.

Teams do not have this gap: `Team.UserAliases` feeds `BuildTeamAliasPairs()`, which emits extra query variants. Teamless leagues (motorsport, fighting, snooker, darts — see `LeagueSportRules.TeamlessSports`) have no equivalent and no compensating fallback.

### Secondary problem: the token list is stale and dead

`GET /api/search/available-tokens` advertises 12 tokens. `BuildQueryFromTemplate` handles 19. Missing from the endpoint: `{Round:00}`, `{Round:0}`, `{Stage}`, `{Stage:00}`, `{Stage:0}`, `{Part}`, `{EventType}`.

Worse, **nothing consumes the endpoint**. `AddLeagueModal.tsx` hardcodes its own `searchTokens` array, which is separately incomplete (missing `{Round:00}`, `{Stage:0}`, `{vs}`). Two sources of truth, both wrong, in different ways.

## Non-goals

- No new template syntax. Alternation sugar (`(a|b)`) was considered and rejected as larger than needed; aliases are the concrete bug.
- No regex matching fields on `League`. Release profiles already support regex (`ReleaseProfileService.MatchesPattern`) for users who need it.
- No change to the matcher. It already accepts aliases correctly.
- No change to `Config.MinimumMatchConfidence` or the scoring thresholds.

## Design

### 1. `League.UserAliases`

Add `string? UserAliases` to `League`, mirroring `Team.UserAliases` exactly: same comma/pipe/slash separators, same local-only contract.

This field is required, not merely symmetric. `LeagueEventSyncService.cs:1415` unconditionally overwrites `league.AlternateName` on every weekly metadata refresh:

```csharp
if (!string.IsNullOrEmpty(fullDetails.AlternateName)) league.AlternateName = fullDetails.AlternateName;
```

Any user-entered alias stored there would be destroyed. `UserAliases` is never written by sync — the same reason `Team.UserAliases` exists.

Migration: `AddLeagueUserAliases`. Nullable column, no backfill.

### 2. League name variants

New private helper in `EventQueryService`:

```
BuildLeagueNameVariants(League? league) -> List<string>
```

Order, deduped case-insensitively:

1. Canonical `GetNormalizedLeagueNameForTemplate(league.Name)` output — **always index 0**
2. `League.AlternateName` entries, parsed with the existing alias separators
3. `League.UserAliases` entries

Alias variants (everything past index 0) cap at **3**, matching the existing `BuildTeamAliasPairs` `maxSlots = 3` precedent.

Returns a single-element list when the league is null or has no aliases, so the no-alias path is unchanged.

### 3. Template path

`BuildQueryFromTemplate` gains a `string? leagueNameOverride = null` parameter. This mirrors the existing `homeTeamName`/`awayTeamName` override parameters added for team-alias variants — an established pattern in the file, not a new one.

`BuildEventQueries` template loop becomes, in this order:

```
for each leagueVariant (canonical first):
    for each template:
        emit BuildQueryFromTemplate(template, evt, part, leagueOverride: leagueVariant)
        for each teamAliasPair:
            emit variant
```

Outer-looping on league variant (not template) is what guarantees every canonical-name query precedes every alias query.

### 4. Default builder path

`BuildMotorsportQueries`, `BuildWrestlingQueries`, `BuildFightingQueries`, and `BuildTeamSportQueries` all accept `leagueName` as a parameter and derive everything from it (`GetMotorsportSeriesPrefix(leagueName)`, etc.).

So the dispatch site calls the selected builder once per league-name variant, passing the variant as `leagueName`. **No changes to builder internals.** Existing dedup on the shared `queries` list absorbs collisions.

Known limitation, accepted: motorsport maps `leagueName` through `GetMotorsportSeriesPrefix` to a series key with its own hardcoded prefix list, so aliases resolving to the same series key produce identical queries and dedup away. Alias expansion is effectively a no-op for motorsport. This is correct behaviour — that builder already carries its own naming variants — and it still helps every sport whose builder embeds the league name directly.

### 5. Query budget

`MaxQueriesPerEvent = 12`, applied to the final list, truncating from the tail.

Priority order falls out of the loop nesting in §3 — it is not separate logic. Because league variant is the *outer* loop, the list is grouped by league name, and within each group by template:

```
canonical:  t1, t1+ta1, t1+ta2, t1+ta3, t2, t2+ta1, ...
alias 1:    t1, t1+ta1, ...
alias 2:    ...
alias 3:    ...
```

The guarantee that matters: **every canonical-name query precedes every alias query**, so truncation sheds alias variants before it touches anything generated today.

Truncation logs a warning naming the count dropped and the league.

This also retroactively bounds the current uncapped worst case of `10 templates × 4 team-alias slots = 40` queries per event.

**Cost model.** Each query fans out to every matching indexer, max 5 concurrent, ~2s + jitter per indexer host (`RateLimitService`), subject to per-indexer `QueryLimit`. `MaxConsecutiveEmpty = 2` early-bails and `SearchResultCache` absorbs repeats, but neither bounds the case where queries return results — hence the explicit ceiling.

**Cache impact.** `AutomaticSearchService` keys the result cache on `string.Join("", queries)`. Adding alias variants changes the key for any league with aliases, causing a one-time cache miss on first search after upgrade. Correct behaviour; note in release notes.

### 6. Token list repair

- Extract `SearchTemplateTokens.All` to `src/Helpers/`, listing all 19 tokens with description and example. `GET /api/search/available-tokens` serves it.
- `AddLeagueModal.tsx` fetches the endpoint on open, retaining its current hardcoded array as an offline fallback.
- **Completeness test** (this is what prevents a third staleness regression): build a template string containing every token in `SearchTemplateTokens.All`, run `BuildQueryFromTemplate` against a fully-populated sample event, assert no unsubstituted `{...}` remains. Separately assert every `result.Replace("{...}"` literal in `BuildQueryFromTemplate` appears in `All`.

### 7. UI

Text input for `UserAliases` in `AddLeagueModal`, adjacent to the search template section. Helper text: these names feed both searching and matching.

Alias visibility comes free — the existing **Preview** button calls `BuildEventQueries`, so it will show alias-expanded queries per event. No new token, no new panel.

## Testing

| Area | Cases |
|---|---|
| `BuildLeagueNameVariants` | canonical first; upstream + user merged; case-insensitive dedup; 3-alias cap; null league; no-alias league returns 1 element |
| Template path | alias generates variants; `queries[0]` unchanged vs. today for every existing league; team-alias interaction |
| Default path | all four builders expand; motorsport dedups to no-op |
| Budget | truncates at 12; drops alias variants before canonical; warning logged |
| Sync safety | metadata refresh overwrites `AlternateName`, leaves `UserAliases` intact |
| Tokens | completeness test both directions |
| Migration | applies clean; existing leagues get null |

Existing `MultipleSearchTemplatesTests.cs` must continue to pass unmodified — it pins the current template behaviour.

## Risks

- **Indexer load.** Bounded by the 12-query ceiling, which is stricter than today's effective 40.
- **Bad upstream aliases** wasting queries. Mitigated: `UserAliases` lets users add what works; the 3-cap bounds the damage. Suppressing a bad upstream alias is not supported and is deferred until someone reports it.
- **Cache churn** on first search post-upgrade. One-time, documented.
