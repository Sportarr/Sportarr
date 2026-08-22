# League Alias Query Expansion, Query Budget, and Per-Query Result Cache

**Date:** 2026-08-22
**Status:** Approved, pending implementation plan

## Problem

### 1. Sportarr knows league aliases but never searches for them

`League.AlternateName` holds upstream aliases (TheSportsDB `strLeagueAlternate`) — a league canonically named "Premiership Rugby" also carries "Gallagher Premiership Rugby", the sponsor-branded name scene groups actually use. The release *matcher* accepts these via `ReleaseMatchingService.LeagueAliases()`.

The query *builder* never reads the field. So Sportarr will accept a release named "Gallagher Premiership..." and never ask an indexer for one.

### 2. Alias handling is split incoherently between matching and searching

Every alias source in the system, and where it actually applies:

| Source | Kind | Builds queries | Matches releases | User-editable |
|---|---|---|---|---|
| `Team.UserAliases` | user data | yes — capped 3, team sports only | yes | yes |
| `Team.AlternateName` | upstream | **no** | yes | no |
| `Team.ShortName` | upstream | **no** | yes | no |
| `League.AlternateName` | upstream | **no** | yes | no |
| `TeamNameVariationData` | hardcoded, 160 teams | **no** | yes | no |
| `SearchNormalizationService` circuits/demonyms | hardcoded, ~80 | **no** | yes | no |
| `LeagueNameSuffixStripper` | hardcoded rules | **no** | yes | no |
| `GetNormalizedLeagueNameForTemplate` | hardcoded, ~10 | yes | **no** | no |
| `GetMotorsportSeriesPrefix` | hardcoded, ~8 | yes | **no** | no |
| `GetMotorsportSearchPrefixes` | hardcoded, 3 series | yes | **no** | no |

`Team.UserAliases` is the only row that does both. `EventQueryService` contains zero references to `TeamNameVariationData` and zero to `SearchNormalizationService`.

Motorsport is the sharpest case. `GetMotorsportSearchPrefixes` is the league-alias concept, hardcoded and closed:

```csharp
"Formula1" => { "Formula 1", "Formula1" },
"FormulaE" => { "Formula E", "FormulaE" },
"WSBK"     => { "WSBK", "SBK" },
_          => { seriesKey }          // one form only
```

MotoGP, NASCAR, IndyCar, WRC and BSB search exactly one name form, with no way to add another short of a code edit.

### 3. Three tokens are unreachable from the UI

`BuildQueryFromTemplate` substitutes **19** tokens. The clickable buttons in `AddLeagueModal` offer **16**. Missing: `{Round:00}`, `{Stage:0}`, `{vs}`. They work if typed, but the button row reads as the available set.

Underneath: `GET /api/search/available-tokens` advertises 12, is missing seven, and **nothing consumes it** — the frontend hardcodes its own separate array. Two lists, both wrong differently, neither checked against the substituting code.

### 4. The result cache does not do what its callers believe

Two contradictions, both affecting query volume:

**Over-keying.** `AutomaticSearchService` uses `cacheKey = string.Join("", queries)`. Because motorsport queries embed round and location, the key is unique per event. The comment four lines above still claims *"Multiple events often share the same primary query (e.g., 'Formula1.2025' for all F1 races)"* — true when the key was the primary query alone, silently broken by `ca23ed32c`. Every F1 event now re-queries every indexer for the byte-identical broad `Formula 1 2026` fallback, 24 times a season, against a 300s TTL that can never hit across events.

**Phantom negative caching.** `AutomaticSearchService` line 296 claims it stores empty results "so a season-wide click storm doesn't re-query the same 20 indexers once per event for identical empty responses." `SearchResultCache.Store` refuses empty result sets outright (line 213), deliberately, to avoid negative-cache lockout. The cache's reasoning is sound; the caller's comment describes behaviour that does not exist. Consequence: unaired events — most of a season at any moment — cache nothing and are re-queried in full every backlog pass.

## Query volume today

`BuildMotorsportQueries` emits up to 5 queries per name form: round, location, title-word, country-noun, broad fallback.

| League | Name forms | Queries/event |
|---|---|---|
| F1 | 2 | **10** |
| MotoGP | 1 | **5** |

Backlog: `BacklogSearchMaxConcurrent = 3`, `BacklogSearchIntervalMinutes = 360` (4 passes/day). A 24-race season across 20 indexers is **~4,800 indexer requests per pass** at F1's current 10 queries/event — and unaired events never cache, so that repeats every pass.

This is the number the budget must govern, not the per-event figure.

## Non-goals

- No new template syntax. Alternation sugar (`(a|b)`) was considered and rejected as larger than needed.
- No regex fields on `League`. Release profiles already support regex.
- No change to the matcher, thresholds, or `Config.MinimumMatchConfidence`.
- Not unifying the hardcoded matcher tables with the query builder. Documented in §2 as a known split; out of scope.

## Design

### 1. `League.UserAliases`

Add `string? UserAliases` to `League`, mirroring `Team.UserAliases`: same comma/pipe/slash separators, same local-only contract.

Required, not merely symmetric — `LeagueEventSyncService.cs:1415` unconditionally overwrites `league.AlternateName` on every weekly refresh, so a user alias stored there would be destroyed.

Migration `AddLeagueUserAliases`. Nullable, no backfill.

### 2. League name variants

New helper: `BuildLeagueNameVariants(League?) -> List<string>`, deduped case-insensitively, in order:

1. Canonical `GetNormalizedLeagueNameForTemplate(league.Name)` — always index 0
2. `League.AlternateName` entries
3. `League.UserAliases` entries

Alias contributions cap at **3**, matching `BuildTeamAliasPairs` `maxSlots = 3`. Null or alias-free league returns one element, so the no-alias path is byte-identical to today.

### 3. Template path

`BuildQueryFromTemplate` gains `string? leagueNameOverride = null`, mirroring the existing `homeTeamName`/`awayTeamName` overrides.

Loop order: league variant outer, template inner, team-alias pair innermost. Canonical-name queries therefore all precede alias queries.

### 4. Default builder path

#### 4a. Motorsport — inject aliases into the prefix list

`GetMotorsportSearchPrefixes(seriesKey, league)` appends `AlternateName` and `UserAliases` entries to the hardcoded forms, deduped, capped at 3.

The existing `foreach (var prefix in searchPrefixes)` loop at line 415 then emits the full query set per form unchanged. Hardcoded forms stay first, preserving the deliberate "spaced form first" ordering noted at line 412.

Looping the whole builder per variant would **not** work here — every variant collapses through `GetMotorsportSeriesPrefix` to the same series key and dedups to a no-op. This is why motorsport needs its own mechanism.

#### 4b. Wrestling, fighting, team sport — loop per variant

These embed `leagueName` directly, so the dispatch site calls the builder once per league-name variant. No changes to builder internals; existing dedup absorbs collisions.

### 5. Query ordering and budget

**`MaxQueriesPerEvent = 20.`**

Not 12. F1 already emits 10, so a 12 ceiling would leave a user's alias 2 slots out of 25 — they add `F1TV`, it gets truncated away, and the feature appears not to work. 20 is justified by §6: broad and alias queries are the ones that dedupe across a season, so under per-query caching most of the marginal 8 cost nothing after the first event of a season.

This is the single number most worth revisiting after real-world use.

**Ordering — motorsport (specificity tiers).** Grouping by name form is wrong here: it lets the canonical form's broad catch-all outrank an alias's precise round query. Sort by tier, then by form index within tier (canonical forms first):

| Tier | Query kind |
|---|---|
| 1 | `{form} {year} Round{NN}` |
| 2 | `{form} {year} {Location}` |
| 3 | `{form} {year} {titleWord}` |
| 4 | `{form} {year} {countryName}` |
| 5 | `{form} {year}` (broad fallback) |

**Invariant:** the canonical form's broad fallback is always retained regardless of truncation. It is the safety net that catches unconventionally-named releases, and losing it is a strict regression.

**Ordering — template path.** Templates are explicitly user-ordered, so template index is the primary sort key and league-variant index secondary. Canonical-name queries precede alias queries, as in §3.

Truncation logs a warning naming the league and the count dropped.

### 6. Per-query result cache

Key `SearchResultCache` on the **individual query**, not the joined list.

`AutomaticSearchService` loops its queries; for each, checks the cache; on miss, runs `SearchAllIndexersAsync` for that query alone and stores that query's results; merges all results per event with the existing GUID dedup. This removes the `usedCache` / `queriesToRun.Skip(1)` special-casing entirely.

**Cache key must include the tag-filtered indexer set.** `TryGetCached` currently validates only age — not `IndexersQueried`, which it stores but never checks. Today the joined-query key makes cross-league collisions rare by accident; per-query caching would make broad queries genuinely shared and turn this latent bug live, letting a tag-restricted league receive results from indexers it is not permitted to query. Key on `(normalizedQuery, orderedIndexerIdSet)`.

**This also fixes the stale-results bug properly.** `ca23ed32c` over-keyed on the whole list so that editing a later template invalidated the entry. Per-query keying achieves the same thing precisely: each query's entry is independent, so an edited template simply runs queries that have no entry.

**Empty results stay uncached.** `SearchResultCache`'s refusal is correct and stays. The fix is to delete the false "negative caching" comment in `AutomaticSearchService`, not to implement it — a negative cache would reintroduce the lockout the cache deliberately avoids. Unaired events therefore continue to re-query each pass; the pre-event guard (`DateTime.UtcNow < evt.EventDate`) is what limits that, not the cache.

Expected effect on a 24-race F1 season: broad forms (`Formula 1 2026`, `Formula1 2026`, plus any alias broad forms) resolve from cache for every event after the first within the TTL, so per-event uncached queries fall to roughly the round/location tiers alone.

### 7. Token list repair

- Extract `SearchTemplateTokens.All` to `src/Helpers/` — all 19 tokens with description and example. `GET /api/search/available-tokens` serves it.
- `AddLeagueModal` fetches the endpoint and renders a button per token, with a hardcoded fallback array that **must also list all 19** — a stale fallback reintroduces the bug whenever the fetch fails.
- **Acceptance:** `{Round:00}`, `{Stage:0}` and `{vs}` each render a clickable button inserting correctly at the cursor.
- **Completeness test:** run `BuildQueryFromTemplate` over a fully-populated sample event with a template containing every token in `All`, assert no unsubstituted `{...}` remains; and assert every `result.Replace("{...}")` literal in `BuildQueryFromTemplate` appears in `All`, so adding a token without listing it fails the build.

### 8. Visibility

Both surfaces, because they answer different questions.

**Alias summary panel** (league settings, read-only) — answers *"what names will be searched for this league?"* at a glance. Lists each name form with its source: built-in, upstream alias, or your alias. Includes forms contributed by the hardcoded motorsport ladder, so `Formula 1` / `Formula1` are visible as built-ins rather than appearing from nowhere.

**Annotated preview** — extends the existing `search-template-preview` endpoint response so each generated query carries the name form used and its source, plus a budget line. Answers *"what will actually be sent, and did my alias survive truncation?"*

```
Preview — Italian Grand Prix

  MotoGP 2026 Round14      form: MotoGP  (built-in)
  MGP 2026 Round14         form: MGP     (your alias)
  MotoGP 2026 Italy        form: MotoGP  (built-in)
  MGP 2026 Italy           form: MGP     (your alias)
  MotoGP 2026              form: MotoGP  (built-in)

  5 of 20 query budget used
```

The truncation warning must be visible here too — if a league exceeds the budget, the preview says which queries were dropped. That is the whole point of the panel.

## Testing

| Area | Cases |
|---|---|
| `BuildLeagueNameVariants` | canonical first; upstream + user merged; case-insensitive dedup; 3-alias cap; null league; alias-free league returns 1 element |
| Template path | alias generates variants; `queries[0]` unchanged vs. today; team-alias interaction |
| Motorsport (4a) | alias appended after hardcoded forms; MotoGP/NASCAR/IndyCar/WRC/BSB gain a form from an alias; F1 spaced-form-first preserved; alias-free league produces byte-identical queries to today |
| Other builders (4b) | wrestling, fighting, team sport expand per variant; dedup absorbs collisions |
| Ordering & budget | specificity tiers ordered correctly; alias round query outranks canonical broad fallback; **canonical broad fallback always retained under truncation**; truncation warning logged |
| Per-query cache | per-query hit/miss; results merged correctly; **tag-restricted league does not receive another league's cached results**; edited template re-queries only changed queries; empty results still uncached |
| Sync safety | metadata refresh overwrites `AlternateName`, leaves `UserAliases` intact |
| Tokens | completeness both directions; the 3 previously-unreachable tokens render as buttons; fallback array lists all 19 |
| Visibility | panel lists built-in + upstream + user forms with correct provenance; preview annotates source and reports budget and truncation |
| Migration | applies clean; existing leagues get null |

`MultipleSearchTemplatesTests.cs` must pass unmodified — it pins current template behaviour.

## Risks

- **Query volume.** Bounded by the 20 ceiling and substantially offset by §6. The honest measure is *uncached* queries per backlog pass, not queries per event; §6 is what makes those diverge.
- **`MaxQueriesPerEvent = 20` is a judgement call** made against F1's 10-query baseline. Revisit with real data.
- **Per-query cache correctness** is the highest-risk item — it makes genuinely shared cache entries where there were effectively none. The indexer-set key component is load-bearing, not defensive.
- **Motorsport prefix ordering.** Appending aliases after hardcoded forms preserves the line-412 optimization; a test pins byte-identical output for alias-free leagues.
- **Bad upstream aliases** wasting queries. Mitigated by the 3-cap and `UserAliases`. Suppressing a bad upstream alias is not supported; deferred until reported.
