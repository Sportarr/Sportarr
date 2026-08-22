# League Alias Query Expansion — Pull Request Texts

Draft PR titles and bodies for the four stacked pull requests defined in
[the implementation plan](2026-08-22-league-alias-query-expansion.md), which implements
[the design](../specs/2026-08-22-league-alias-query-expansion-design.md).

**These four PRs are a chain.** Each branches from the previous one, not from `dev`. Review and merge them in order; rebase each onto `upstream/dev` as its parent merges. The stack exists because the whole change touches `EventQueryService`, `AutomaticSearchService`, `IndexerSearchService`, `LeagueEndpoints`, and `AddLeagueModal` — all large files — and reviewing the caching rewrite alongside drag-and-drop UI would serve neither well.

Each PR body opens with the same stack map so a reviewer landing on any one of them knows where they are. Update the checkmarks and PR numbers as the stack merges.

---

## Stack map (paste into every PR, adjusting the marker)

```markdown
### Stack

This is part of a four-PR chain. Each branch builds on the previous one; please review in order.

| | PR | Branch | Contents |
|---|---|---|---|
| 1 | #___ | `feat/league-alias-foundations` | Alias parsing, league alias persistence, unified league identity, token catalog |
| 2 | #___ | `feat/league-alias-query-plan` | Structured query plan, bounded league-alias expansion |
| 3 | #___ | `feat/per-indexer-search-cache` | Per-indexer result caching, full plan execution, opt-in strong-match stop |
| 4 | #___ | `feat/advanced-league-search-settings` | Advanced Search Settings UI, structured preview, docs |

**← You are here: PR 1.**
```

---

# PR 1 — `feat/league-alias-foundations`

**Title:** `fix: accept user-defined league aliases in search identity and unify the token catalog`

**Base:** `dev`

**Body:**

```markdown
<!-- stack map here, marking PR 1 -->

## Why

Sportarr already accepts several alternate league and team names when *matching* releases, but its indexer queries use a smaller, partly hardcoded set. A release can therefore be matchable but undiscoverable. This PR lays the foundation for fixing that: it gives leagues a durable local alias field and makes every league-identity check read from one enumeration.

It also fixes a bug that exists today, independent of any of the above: `PUT /api/teams/{id}/aliases` splits on comma alone, so pipe- and slash-separated input ("Man Utd | MUFC") is stored as a single alias and never matches anything.

## What changed

- **One alias parser.** New `AliasField` helper parses comma, pipe, and slash separators with trimming and case-insensitive deduplication, and normalizes to a stable storage form. Both league and team alias writes go through it. Values over 512 characters are rejected with a field-specific 400 rather than truncated.
- **Three new local-only `League` columns**, all nullable with no backfill: `UserAliases`, a typed JSON `AliasSearchOrder`, and `SearchEarlyStopMatchScoreOverride`. Local-only means the weekly metadata refresh never writes them — that is the whole reason they cannot live in `AlternateName`. Migrations for both SQLite and PostgreSQL, plus the legacy `EnsureCreated` startup safety net.
- **One league-identity enumeration.** `LeagueAliasHelper.GetMatchingAliases` returns name, upstream aliases, user aliases, and generated abbreviations, deduplicated. Every identity path now uses it — organization validation, `TitleNamesLeague`, `SeriesLabelMatchesLeague`, grab validation, import matching. The private duplicate list that knew only about `Name` and `AlternateName` is deleted.
- **Authoritative token catalog.** `BuildQueryFromTemplate` supports 19 tokens, the frontend showed 16, and the backend endpoint returned 12. All three now agree on the same 19, with a key-parity test. `{Round:00}`, `{Stage:0}`, and `{vs}` become insertable from the UI for the first time.

## Why the identity change is a correctness requirement, not a nicety

A release found through a user alias must not then fail league-identity matching because the matcher does not know that alias. Adding the alias to query generation without adding it to matching would produce results that get found and then rejected. That is why identity lands here, in the same PR as the field, rather than later with the query work.

## Not in this PR

No change to query generation, search execution, or caching. Aliases are stored and honored by the matcher; PR 2 starts searching for them.

## Testing

- Parser: separators, trimming, empties, case-insensitive dedupe.
- Round-trip of all three fields through add, get, and update.
- Metadata refresh changes `AlternateName` and leaves all three local values untouched.
- A title containing only a league user alias passes every applicable identity gate.
- Token metadata keys and replacement keys are exactly equal and cover all 19 tokens; frontend fallback matches literally.
- SQLite and PostgreSQL migrations apply cleanly; legacy startup is idempotent across two consecutive boots.
- Full backend suite, frontend lint/test/build.

## Review focus

The identity enumeration adoption in `ReleaseMatchingService` and `LibraryImportService` — confirm no second league-alias list survives anywhere.
```

---

# PR 2 — `feat/league-alias-query-plan`

**Title:** `feat: expand event queries with bounded league aliases`

**Base:** `feat/league-alias-foundations` (PR 1)

**Body:**

```markdown
<!-- stack map here, marking PR 2 -->

**Builds on #___ (PR 1).** Review that first; this branch contains its commits.

## Why

With aliases persisted and honored by the matcher, this PR makes them a first-class input to *query planning*, so a release named only with an alias becomes discoverable rather than merely matchable.

Motorsport is the most visible case: only Formula 1, Formula E, and WSBK have multiple hardcoded search forms today. MotoGP, NASCAR, IndyCar, WRC, and BSB cannot gain another search form without a code change. Leagues like Premiership Rugby fare worse — their canonical builder falls back to event-title queries and never embeds the league name at all.

## What changed

- **`EventQueryService` now returns a structured `QueryPlan`** rather than a bare string list. Each `QueryCandidate` carries its league-name form, form source (`BuiltIn`/`Canonical`/`UserAlias`/`UpstreamAlias`), specificity tier, template index, team-alias slot, whether it is mandatory, and its selected/dropped state with a reason. The same plan will drive execution, logging, the API preview, and the UI — provenance is never reconstructed by parsing query strings later.
- **Bounded alias expansion in every builder** — motorsport, team sports, wrestling, fighting, and the generic fallback — with aliases inserted where they actually affect output rather than by re-invoking an unchanged builder with a different league name.
- **Two separate bounds.** `MaxAliasExpansionPerEvent = 8` is the operative budget and applies only to alias-expansion candidates. `HardQueryCeiling = 50` is a runaway guard that sits above any reachable legitimate configuration (largest legitimate baseline: 10 templates × 4 team-alias slots = 40), so exceeding it means a builder regression and fails loudly.

## The compatibility guarantee

**A league with no saved `AliasSearchOrder` emits exactly its pre-change query strings in exactly the same order.** Every query generated today is marked mandatory, is never counted against the expansion budget, and can never be dropped by ordinary truncation. Alias variants are expansion candidates that fill remaining slots and are appended after the complete baseline.

`MultipleSearchTemplatesTests` passes unmodified — the diff for that file is empty, and there is a validation step that asserts it.

## Behavioral surface

None yet at runtime. Callers still consume the `BuildEventQueries` compatibility wrapper, which returns `plan.SelectedQueries.Select(q => q.Text)`. Nothing reads the plan's metadata until PR 3 and PR 4. That is deliberate: it keeps this PR reviewable as "does the generated output change when it shouldn't?"

## Not in this PR

No execution, caching, or UI changes. Users cannot yet set an alias order — the field exists but nothing writes it, so every league takes the legacy-compatible path.

## Testing

- Null saved order produces byte-identical legacy output for every builder.
- Template index primary, league form secondary, team-alias slot tertiary.
- A 10-template league with three team-alias slots keeps all 40 baseline queries, none charged to the expansion budget.
- Alias-expansion candidates never exceed 8; mandatory queries are never dropped by ordinary truncation.
- Every dropped candidate has a reason and logs the expected Warning with league, selected count, and dropped count.
- No home/away Cartesian product; existing team-alias slot pairing is retained.
- Motorsport aliases are added without collapsing back through series-key normalization.
- Wrestling and fighting variants change only queries with a recognized leading organization token.

## Review focus

The two-phase template planner and the per-builder expansion rules. The question to hold throughout is: could this drop or reorder a query that exists today?
```

---

# PR 3 — `feat/per-indexer-search-cache`

**Title:** `refactor: cache per indexer, execute every selected query, and add opt-in strong-match stopping`

**Base:** `feat/league-alias-query-plan` (PR 2)

**Body:**

```markdown
<!-- stack map here, marking PR 3 -->

**Builds on #___ (PR 2).** Review that first; this branch contains its commits.

This is the largest and highest-risk PR in the chain. The three changes in it are interdependent and the commit order inside the branch is load-bearing — see "Why these ship together" below.

## Why

Three problems, one causal chain.

**The result cache has unsafe identity.** Automatic search caches the merged results of a whole query list under the joined list, and stores an `IndexersQueried` field that is never validated. Moving to a query-only key would be worse, not better: the same query text produces genuinely different responses depending on league tags selecting different indexers, automatic versus manual category-filter settings and result limits, whether the indexer treats `sportarrid` as a real search parameter, and transient indexer availability.

**Early termination can skip required fallbacks.** Automatic search stops after two consecutive empty queries. With specificity-tier ordering, two empty round queries can prevent location and broad-season fallbacks from ever running. A query that survives planning and budgeting should not be silently skipped by a heuristic.

**Removing that heuristic costs requests** — hardest in the common case of an event that has not been released yet. So it cannot land without something to absorb the cost.

## Why these ship together, in this order

1. **Structured single-indexer outcomes** (`Succeeded`/`Unavailable`/`RateLimited`/`Failed`) make it possible to distinguish "this indexer genuinely returned nothing" from "this indexer failed" — which is the precondition for caching empties at all.
2. **Per-indexer caching with a short negative TTL** absorbs season-wide search storms across events that share broad queries.
3. **Only then** is the consecutive-empty exit removed.

Doing (3) first would leave commits on the branch where query volume is unbounded against the old whole-list cache. Please keep this ordering if the branch is rebased or split further.

## What changed

### Per-indexer request caching

Caching moves inside the indexer-search boundary, where eligibility and capabilities are already known. The key is a typed `IndexerSearchCacheKey` with value equality — not string concatenation — containing normalized query, indexer ID, indexer URL and type, effective `sportarrId`, category-filter mode, effective category set, result limit, and minimum seeders.

Consequences worth stating explicitly:

- Tag-restricted leagues consult only eligible indexers, so results from excluded indexers cannot leak in.
- Automatic and manual searches cannot share entries when category mode or result limit differs.
- Two events cannot share an ID-filtered response from an indexer that supports `sportarrid`; broad season queries *can* still be shared on indexers that do not, because their effective ID component is null.
- One failing indexer no longer prevents caching reusable results from successful ones.

**Unknown capabilities bypass the cache entirely** — no read, no write, normal live request. This avoids adding a capability-state dimension to the key and guarantees an unknown no-ID request cannot collide with a later known ID-filtered request. `GetSportarrIdSupportAsync` returns `bool?` from the existing concrete clients using their existing static capabilities cache; no new client interface, no DI refactor, no extra round-trip.

Only `Succeeded` outcomes are cached. Exceptions, timeouts, rate-limit skips, and unavailable indexers are never cached at any TTL. A successful zero-result response is cached under `SearchNegativeCacheDuration` (default 60s, 0 disables) — far below the full search TTL, so a transient outage cannot shadow real results.

Cache entries hold raw release data only. Event, part, quality-profile, custom-format, blocklist, retention, and match evaluation all run again for the current event on every hit.

### Every selected query runs

`MaxConsecutiveEmpty` and all consecutive-empty state are gone. With early stop disabled, every selected baseline and expansion query is either served from cache or searched live, regardless of how many earlier queries returned nothing. Force refresh now bypasses and invalidates the exact per-indexer keys for every selected query, not just the primary one.

### Opt-in strong-match early stop

New `Config.SearchEarlyStopMatchScore`, **default 0 = disabled**, with a per-league override where null inherits, 0 explicitly disables, and a positive value overrides. Deliberately not reusing `AutoGrabMinMatchScore`: the minimum score safe for automatic grabbing and the higher-confidence score worth ending discovery early are separate decisions.

The stop is provisional and conservative. A match score alone never stops a search. The candidate must pass **every** pre-download gate that would otherwise allow a grab, evaluated through a shared evaluator so the incremental decision cannot drift from final selection. The search finishes only once the download client *accepts* the release; if the grab fails, the release identity goes into an attempt-local exclusion set and the search resumes at the next unexecuted query rather than restarting or ending. Interactive manual search ignores the setting entirely.

### Instrumentation

Each automatic search emits one structured summary: selected and dropped candidate counts with the mandatory/expansion split, live per-indexer requests, and positive and negative cache hits. This is what makes the rollout tunable — the increase from removing the early exit needs to be measurable separately from the increase caused by alias expansion, and `MaxAliasExpansionPerEvent` and `SearchNegativeCacheDuration` should be revisited against observed live-request counts rather than candidate counts.

## Expected request volume

Honestly conditional. Identical broad queries are reused per eligible indexer only when all outbound parameters for that indexer are equivalent. **No cross-event reuse is claimed** for indexers that receive different event IDs. Volume will go up for alias-free leagues too, because the early exit is gone; the negative TTL and the 8-query expansion budget are what bound it.

## Testing

Cache correctness: isolation by indexer ID, URL, effective ID, category mode and set, result limit, type, and minimum seeders; tag-restricted leagues never receive excluded indexers' results; manual broad-category results do not leak into automatic filtered search; event-ID searches do not share across events; equivalent broad queries do share on non-`sportarrid` indexers; unknown capabilities bypass reads and writes; failures stay uncached at any TTL; zero-result successes expire at the negative TTL only; cached raw results are fully re-evaluated.

Execution: every mandatory query runs after any number of empty responses; force refresh bypasses every applicable key.

Early stop: disabled by default; a high-scoring but ineligible release never stops search; an accepted grab stops later queries; a failed grab resumes at the next query without rerunning earlier ones or retrying the failed release; cached results trigger the stop through the same path; manual search always runs the full plan.

`grep -rn "consecutiveEmpty\|MaxConsecutiveEmpty" src` returns nothing.

## Review focus

The typed cache key and its capability handling. Everything else in this PR has a test that fails loudly when wrong; a subtly incomplete cache key fails quietly by serving one event's results for another.
```

---

# PR 4 — `feat/advanced-league-search-settings`

**Title:** `feat: add advanced league search settings and structured query preview`

**Base:** `feat/per-indexer-search-cache` (PR 3)

**Body:**

```markdown
<!-- stack map here, marking PR 4 -->

**Builds on #___ (PR 3).** Review that first; this branch contains its commits. This is the last PR in the chain, and the one that makes everything in PRs 1–3 visible and controllable.

## Why

Up to this point the machinery works but is invisible. Users can store aliases but cannot see which queries they produce, cannot influence priority, and cannot tell when an alias was dropped by the expansion budget or the three-form cap. Bad upstream metadata can quietly consume expansion slots with nothing surfacing that it happened.

## What changed

### Advanced Search Settings

League search controls collect into one collapsed section containing, in order: "Your aliases", the interleaved draggable search-name priority list, custom search templates with the complete 19-token picker, the strong-match early-stop override, and the structured query viewer.

The priority list shows built-in, canonical, upstream, and user forms with source badges. All forms are draggable, including built-ins; built-in rows are not deletable. Duplicate text appears once with multiple badges. Built with the repo's existing native HTML drag pattern from `ProfilesSettings`/`ActivityPage` — no new dependency.

**Ordering stays legacy until a user deliberately drags something.** Merely opening or saving the modal does not create an override, and "Reset order" clears the preference back to null. This is what preserves PR 2's byte-identical compatibility guarantee for every league nobody has customized.

Note: `AddLeagueModal` is the edit surface as well as the add surface — rendered from `LeagueSearchPage` for adding and `LeagueDetailPage` for editing. The section renders in both; preview needs a saved league, so it appears only when editing.

### Structured query preview

The preview endpoint now returns the real `QueryPlan` for both default-builder and custom-template leagues. Previously the default path returned only the first generated query, which meant the preview did not show what search actually did.

The viewer shows the actual numbered execution order with provenance, specificity tier, mandatory versus expansion status, budget used and limit, dropped candidates with reasons, and any aliases excluded by the three-form cap labeled `AliasFormLimit` — so a wasted slot is visible rather than silent. Drag-and-drop updates the numbered order immediately, without saving the league.

Preview accepts unsaved aliases, order, templates, and early-stop override as explicit planner options and **does not mutate the tracked league**.

### Representative event selection

Group events by season, discard seasons with no event at or before now, take the newest remaining season, pick one past event from it at random in application code (so SQLite and PostgreSQL behave identically). This naturally uses the current season once it has started and the previous season when a synced future season has not. The chosen event is retained while aliases, ordering, templates, and the override change, so the preview does not jump around during editing; "Try another event" requests a different past event from the same season.

### Documentation

New `docs/features/search.md`, since there was no search page and this chain adds three user-facing settings plus a behavior change in which every selected query now runs.

## Testing

Backend: default preview returns every selected query rather than only the first; unsaved values affect output without changing the tracked entity; event selection prefers the newest started season, ignores synced future seasons, is stable across requests, and excludes the current event on "try another"; a fourth valid alias reports `AliasFormLimit` and budget drops report `AliasBudgetExceeded`.

Frontend: collapsed section; alias load/save; 512-character validation message; source badges; native drag reorder; reset-to-null; an untouched order staying null; the three early-stop choices; token fetch success and fallback; stable representative event; numbered execution order; selected and dropped rendering; the exclusion warning.

Plus the full stack validation: backend suite, frontend lint/test/build, `mkdocs build --strict`, both migration providers, and legacy startup re-verified against the fully stacked branch.

## Review focus

That the order override is only ever created by a real drag. If opening or saving the modal writes an order, every league silently switches to specificity-first execution and PR 2's compatibility guarantee is gone.
```
