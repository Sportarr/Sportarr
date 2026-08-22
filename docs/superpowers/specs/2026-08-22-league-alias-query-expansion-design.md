# League Alias Query Planning, User Ordering, Early Stop, and Per-Indexer Result Cache

**Date:** 2026-08-22
**Status:** Revised after user review; implementation plan available

## Summary

Sportarr accepts several alternate league and team names while matching releases, but its indexer queries use only a smaller, partly hardcoded set. A release can therefore be matchable but undiscoverable.

This change makes league aliases a first-class input to query planning. It introduces a structured query plan, an 8-query alias-expansion budget, user-controlled ordering across built-in, canonical, user, and upstream name forms, a per-indexer request cache, a single backend token catalog, and an advanced league-search UI with a representative-event query viewer. Automatic search executes every selected query by default. Users may optionally configure a strong-match threshold globally and override it per league; that optimization stops only after a fully downloadable release is found, and resumes remaining queries if the provisional grab fails.

The cache is deliberately per indexer rather than merely per query. Indexer responses depend on tag eligibility, event or league `sportarrid`, category filtering, result limits, capabilities, and indexer availability. Query text alone is not a safe cache identity.

## Problems

### Matchable releases are not always searchable

`League.AlternateName` contains upstream aliases such as sponsor-branded competition names. `ReleaseMatchingService` accepts those aliases, but `EventQueryService` generally does not search for them. The same split exists for several team and hardcoded normalization sources.

Motorsport is particularly visible because only Formula 1, Formula E, and WSBK have multiple hardcoded search forms. MotoGP, NASCAR, IndyCar, WRC, and BSB cannot gain another search form without a code change.

### Users have no durable league-alias field

`LeagueEventSyncService` refreshes `League.AlternateName` from upstream metadata. User values stored there would be overwritten. League aliases therefore need a separate local-only field, equivalent to `Team.UserAliases`.

### Template-token catalogs disagree

`BuildQueryFromTemplate` supports 19 tokens. The frontend displays 16 and the backend token endpoint displays 12. `{Round:00}`, `{Stage:0}`, and `{vs}` cannot currently be inserted from the UI even though the builder accepts them.

### The result cache has unsafe and ineffective identity

Automatic search currently caches the merged results of a complete query list under the joined list. Event-specific round and location queries prevent broad season-query reuse. `IndexersQueried` is stored but not validated.

Changing to a query-only key would be unsafe. The same query can produce different responses because:

- league tags select different indexers;
- automatic and manual searches use different category-filter settings and result limits;
- automatic event searches pass an event `sportarrid`, which supporting indexers use as a real search parameter;
- enabled state, protocol compatibility, capabilities, rate limits, and temporary availability change the indexers that can be queried.

Empty results are intentionally not cached today, and the automatic-search comment claiming negative caching exists is incorrect. Section 9 reintroduces a narrow form of it, scoped to successful zero-result responses under a short TTL.

### Existing early termination can skip required fallbacks

Automatic search stops after two consecutive empty queries. With specificity-tier ordering, two empty round queries could prevent location and broad fallback queries from running. A query that survives planning and budgeting must not be silently skipped by this heuristic.

## Goals

- Search upstream and user-defined league aliases.
- Accept user-defined league aliases everywhere league aliases participate in matching or import identity.
- Preserve the complete alias-free query set and outbound ordering for existing leagues.
- Bound only the new alias expansion, with a maximum of 8 alias-expansion queries per event on top of the unbounded alias-free baseline.
- Ensure every selected query is executed or satisfied from cache by default; only an explicitly enabled, accepted strong-match grab may stop the remaining plan.
- Cache only requests that are genuinely equivalent.
- Keep user-authored templates primary and preserve at least their existing canonical variants.
- Expose query provenance, budget selection, and truncation in the UI.
- Make the backend token catalog authoritative while retaining a complete frontend fallback.
- Let users inspect every planned query in execution order for a representative past event.
- Let users interleave and reorder built-in, canonical, user, and upstream league-name forms.
- Preserve legacy query ordering until a user deliberately saves a custom alias order.
- Keep strong-match early stopping globally disabled by default, league-overridable, and incapable of stopping on a release that would not actually be downloaded.

## Non-goals

- No new template syntax or regular-expression fields on `League`.
- No changes to match confidence thresholds or `Config.MinimumMatchConfidence`.
- No attempt to merge every hardcoded team-name or search-normalization table in this change.
- No broad dependency upgrades or unrelated search refactoring.
- No analytics submission for league aliases in this change. Team aliases feed `SubmitTeamAliasSuggestionAsync` for upstream promotion; the league equivalent needs a matching upstream endpoint and is deferred.
- No early stopping for interactive manual search; manual search continues to collect the complete result set.

## Design

### 1. Persist and edit `League.UserAliases`

Add these local-only league fields:

- nullable `string? UserAliases`, using the same comma, pipe, or slash separators as `Team.UserAliases`;
- nullable typed JSON list `List<LeagueAliasOrderEntry>? AliasSearchOrder`, where null means the user has never customized ordering;
- nullable `int? SearchEarlyStopMatchScoreOverride`, where null inherits the global value, 0 explicitly disables early stop for this league, and a positive value is the league threshold.

`LeagueAliasOrderEntry` contains the form source and normalized value. Ordering reconciliation matches by normalized value because deduplication produces one effective row even when several sources contribute the same text; the stored source is diagnostic provenance, not a second identity key. A typed EF JSON conversion with a `ValueComparer` follows the existing small-list pattern used by `League.Tags` and other ordered settings. Do not add a relational child table.

Wire it through every persistence surface:

- `AddLeagueRequest` and `ToLeague()`;
- `LeagueResponse.FromLeague()`;
- the single-league settings response;
- the league update endpoint;
- add/edit modal request and response types, state initialization, and save payload;
- a labeled editable field in league settings.
- alias-order and early-stop override fields in add/get/update responses and payloads.

Normalize on write by trimming entries, removing empty values, deduplicating case-insensitively, and storing a comma-separated value. Reject values longer than `AliasField.MaxUserAliasesLength` (512 characters) rather than truncating. Define the constant once alongside the shared parser and apply it to both league and team alias writes. The column itself stays unconstrained `TEXT`; the limit is enforced in application validation. Use FluentValidation for typed POST/PUT request bodies; where the existing update endpoint still accepts `JsonElement`, perform equivalent explicit validation until that endpoint is converted to a typed request.

Add migrations to both supported migration projects:

- SQLite: `src/Sportarr.Data/Migrations/`;
- PostgreSQL: `src/Sportarr.Migrations.Postgres/Migrations/`.

All three columns are nullable with no backfill. Add matching legacy SQLite schema safety nets in `DatabaseInitializer` for databases originally created with `EnsureCreated()`. Exercise migration and startup with both providers.

Weekly metadata refresh continues to update `AlternateName` and must never write `UserAliases`.

### 2. Use one alias parser and one matching source

Extract a pure helper for parsing comma, pipe, and slash-separated aliases. Team query expansion and league query expansion use the same parser.

Update the matcher's league-alias enumeration to return, in deduplicated order:

1. `League.Name`;
2. `League.AlternateName` entries;
3. `League.UserAliases` entries;
4. existing generated abbreviations.

Every league-identity path must use that enumeration, including organization validation, `TitleNamesLeague`, `SeriesLabelMatchesLeague`, grab validation, and import matching. There must not be a second local list containing only `Name` and `AlternateName`.

This is a correctness requirement: a release found only through a user alias must not subsequently fail league-identity matching because the matcher does not know that alias.

The shared parser governs writes as well as reads. Convert the existing team-alias write path to it in this change; today it splits on comma alone, so pipe- and slash-separated input is stored as a single alias and never matches. League and team alias normalization must be the same code.

### 3. Represent generated work as a query plan

`EventQueryService` returns a `QueryPlan` internally. Existing callers that only need strings can use a compatibility method returning `plan.SelectedQueries.Select(q => q.Text)`.

Each `QueryCandidate` contains:

- final query text;
- league-name form;
- form source: `BuiltIn`, `Canonical`, `UpstreamAlias`, or `UserAlias`;
- query kind/specificity tier;
- resolved alias-order position;
- template index when applicable;
- team-alias slot when applicable;
- whether it belongs to the alias-free baseline and is therefore mandatory;
- selected/dropped state and an optional drop reason.

`QueryPlan` contains the ordered candidate list, selected queries, dropped queries, both budget limits, and truncation status. Case-insensitive query deduplication happens before budgeting. When duplicate text has several provenances, retain the highest-priority provenance and record the other contributing forms for preview diagnostics.

The same plan drives execution, logging, API preview, and UI display. Provenance must not be reconstructed later by parsing final query strings.

### 4. League-name forms and priority

Build league forms in this order:

1. existing canonical/built-in forms required to reproduce current behavior;
2. user aliases;
3. upstream aliases.

Without a saved order, user aliases precede upstream aliases because explicit user intent should not be crowded out by poor upstream metadata. With a saved `AliasSearchOrder`, built-in, canonical, user, and upstream forms follow that combined order. Alias contributions are capped at three after case-insensitive deduplication: select the first three non-built-in forms in the effective order. Built-in forms such as `Formula 1` and `Formula1` do not consume those three slots. The plan records every otherwise-valid user or upstream alias excluded by this cap as an excluded name form with reason `AliasFormLimit`; the settings summary and preview display those exclusions instead of silently omitting them.

Duplicate text becomes one effective form with all contributing provenance recorded. New upstream or user forms absent from a saved order append to the end without disturbing saved entries. Stored forms that are temporarily absent are ignored during planning but retained in storage unless the user deliberately saves a newly reordered list.

The display form and query form are distinct. The canonical template form uses `GetNormalizedLeagueNameForTemplate`, exactly as today. Default builders keep their existing canonical normalization. Alias strings are trimmed and otherwise used as entered; they are not passed through canonical-name recognition that could collapse them back to the same series key.

An alias-free league with no saved `AliasSearchOrder` produces the same selected query strings in the same order as before this change. Merely opening or saving league settings must not create an alias-order override. A “Reset order” action clears `AliasSearchOrder` back to null and restores legacy ordering.

### 5. Template query planning

`BuildQueryFromTemplate` accepts an optional league-name override, alongside the existing home/away overrides.

When no alias-order override exists, template planning has two explicit phases so all existing work remains ahead of new expansion work:

1. Build the complete alias-free baseline in its current order: template index, then team-alias slot with the canonical team pair first.
2. Build league-alias expansion candidates: template index, then league-alias form, then team-alias slot with the canonical team pair first.

Template index remains primary within each phase because templates are explicitly user ordered. The alias-free queries produced today for every configured template and team-alias slot are marked mandatory. League-alias variants are expansion candidates and fill only remaining budget slots. Selected expansion queries are appended after the complete baseline.

When a saved alias order exists, template index remains the major tier, followed by the saved alias-form order and then team-alias slot. This makes custom-template ordering predictable without allowing alias drag order to move a later user-authored template ahead of an earlier one.

This guarantees that adding league aliases cannot make an existing template or existing team-user-alias query disappear. `MultipleSearchTemplatesTests` must pass unmodified, with additional tests for league/team cross-expansion and budgeting.

### 6. Default-builder query planning

League aliases must be inserted into a part of each builder that actually affects its output. Repeatedly invoking an unchanged builder with a different `leagueName` is not sufficient.

#### Motorsport

Keep the current built-in prefix list and its ordering. Append user and upstream alias forms directly to the search-prefix list without passing them through `GetMotorsportSeriesPrefix`.

With no alias-order override, keep the currently generated built-in queries in their existing form-grouped order and mark all of them mandatory. This preserves byte-identical legacy output. Append alias candidates after that baseline using the existing default priorities.

With a saved alias order, order all selected motorsport queries by specificity tier and then alias-form order:

1. round;
2. location;
3. title-derived location;
4. country noun;
5. broad season fallback.

All currently generated built-in-form queries remain mandatory, including every canonical broad fallback. User/upstream-only queries remain expansion candidates. A saved alias order changes execution order intentionally but does not change mandatory or budget classification.

The compatibility branch must carry a code comment explaining that legacy form-grouped ordering is intentionally preserved for users without a saved preference, while a future migration may make specificity-first ordering universal.

#### Team sports

Generate the existing canonical queries unchanged and mark them mandatory.

For each league alias, add an explicit alias query rather than merely calling `BuildTeamSportQueries` again:

- with both resolved teams: `{alias} {year} {home} {away}`;
- without both teams: `{alias} {year}`.

Where a team user-alias slot exists, generate the same league-alias shape using that slot's team names. Do not create a Cartesian product of home and away aliases; retain existing slot pairing.

This explicit path covers leagues such as Premiership Rugby whose canonical builder currently falls back to event-title queries and otherwise would never embed the league alias.

#### Wrestling

Generate the existing WWE/AEW canonical queries unchanged and mark them mandatory. For an alias form, replace only the leading organization token in the corresponding canonical weekly-show or special-event query. Do not infer WWE versus AEW from an arbitrary alias.

If a canonical wrestling query has no leading organization token, do not synthesize an alias variant from it.

#### Fighting

Generate existing title-, card-, matchup-, and organization-derived queries unchanged and mark them mandatory. Alias variants apply only to queries with a recognized leading organization token: replace that token with the alias while preserving the card number/type or year suffix. Pure surname-matchup queries do not gain league variants.

#### Generic fallback

Keep the normalized event-title query mandatory. When the event has a usable year, add `{alias} {year} {normalized event title}` for each alias; otherwise add `{alias} {normalized event title}`. These are expansion candidates.

### 7. Query budget and selection guarantees

Two separate bounds, each doing one job.

**`MaxAliasExpansionPerEvent = 8`** is the operative budget. It applies only to alias-expansion candidates, after case-insensitive deduplication. Mandatory alias-free baseline queries are never counted against it and never subject to it.

**`HardQueryCeiling = 50`** is a runaway guard, not a product decision. It sits above any reachable legitimate configuration: the largest baseline is the custom-template path at `SearchTemplateList.MaxTemplates` (10) x canonical-plus-three team-alias slots (4) = 40, and every default builder is far lower. Exceeding it means a genuine regression in a builder, so it fails loudly in tests and logs at Error in production, retaining the first 50 queries in builder order.

Selection follows these rules:

1. Select every mandatory alias-free baseline query, unconditionally.
2. Fill up to `MaxAliasExpansionPerEvent` alias candidates by builder-specific priority.
3. Without a saved order, user aliases precede upstream aliases within equal specificity; with a saved order, its interleaved form positions govern.
4. Without a saved alias order, preserve the complete alias-free baseline in its existing order, then append selected expansion candidates according to the builder-specific expansion order.
5. With a saved alias order, sort by builder specificity or template tier first, saved alias-form position second, and the builder's stable tie-breakers last. Selection and ordering are separate: reordering never changes which mandatory queries survive.

Alias truncation is ordinary and expected: a league with three aliases and ten templates will drop most of its candidates. It logs a warning containing the league, selected count, and dropped count. The plan and preview expose every dropped query and its reason.

The budget is an expansion safety bound, not permission to remove existing behavior.

### 8. Execute every selected query by default; optional strong-match stop

Remove `MaxConsecutiveEmpty` and all consecutive-empty early breaks. With strong-match early stopping disabled, every selected baseline and alias-expansion query is either served from cache or searched live, regardless of how many earlier queries returned nothing. The 8-query expansion budget is the sole normal bound on new alias work.

Retain GUID deduplication while merging results. A cache hit is neither an early-success signal nor a reason to skip the next query. A zero-result response does not prevent later mandatory fallback queries from running.

Add global `Config.SearchEarlyStopMatchScore`, default 0 (disabled), validated from 0 through the scorer's clamped maximum of 100. Enabling the toggle for the first time proposes a conservative score of 90. The effective league value is `League.SearchEarlyStopMatchScoreOverride ?? Config.SearchEarlyStopMatchScore`; a league override of 0 disables the inherited global behavior. Do not reuse `AutoGrabMinMatchScore`: the minimum score safe for automatic grabbing and the higher-confidence score worth ending discovery early are separate decisions.

When the effective value is positive, automatic search evaluates accumulated releases after each query through the same complete eligibility and selection pipeline used before a final grab. A match score alone never stops search. The selected candidate must pass every pre-download gate that would otherwise allow Sportarr to grab it, including league/event identity, quality and custom-format requirements, part/session/event-type validation, blocklist, retention, minimum age, delay profile, existing-file and upgrade rules, and download-client eligibility.

The threshold-triggered stop is provisional:

1. select the best currently downloadable release from all accumulated results;
2. require its `MatchScore` to meet the effective threshold;
3. attempt the grab immediately;
4. finish the search only when the download client accepts it;
5. if the grab fails or is rejected, record the existing failure state, add that release identity to an attempt-local exclusion set, and resume at the next unexecuted query rather than restarting or ending the search.

The attempt-local exclusion uses the same strongest available identity order as grab/churn handling (torrent hash, GUID, download URL, then title plus indexer). It prevents the same failed strong match from triggering again after every subsequent query or at final selection during the same search.

Extract or share the eligibility pipeline so incremental threshold checks and final selection cannot drift. Do not duplicate a shortened list of gates in the query loop. Cached results are re-evaluated for the current event and may trigger the threshold only through this same path. Interactive manual search ignores early-stop settings and always executes every selected query.

### 9. Per-indexer request cache

Replace whole-list and query-only caching with per-indexer request caching inside the indexer-search boundary, where eligibility and capabilities are already known.

For each selected query, `IndexerSearchService`:

1. resolves enabled, tag-compatible, protocol-compatible indexers using its existing selection logic;
2. determines the effective outbound parameters for each indexer;
3. checks a cache entry for that indexer/request;
4. searches misses with the existing concurrency and rate-limit controls;
5. caches successful responses per indexer, using the positive TTL for non-empty results and the short negative TTL for empty results;
6. merges cached and live raw releases, then performs the existing event/profile evaluation.

The `sportarrid` capability check currently lives inside `TorznabClient.SearchAsync` / `NewznabClient.SearchAsync`, below the layer that builds the cache key. Hoist only the answer needed by caching: expose a public `GetSportarrIdSupportAsync(Indexer)` method on those existing concrete clients, returning `true`, `false`, or `null` for unknown. `IndexerSearchService` already constructs these clients directly, so do not introduce a new client interface or DI refactor for this change. The clients keep their own send-time check; both calls use the existing static capabilities cache and add no duplicate round-trip.

The effective ID component is the normalized `sportarrId` when the indexer advertises the param, and null when it demonstrably does not. When capabilities are unknown or unavailable, bypass the result cache for that indexer: perform the live request through the existing client behavior and neither read nor write a result-cache entry. This deliberately avoids adding a capability-state dimension to the cache key and guarantees that an unknown no-ID request cannot collide with a later known ID-filtered request.

The cache key is a typed `IndexerSearchCacheKey` containing:

- normalized query;
- indexer ID;
- indexer URL and type, so editing an existing indexer cannot reuse its previous endpoint's response;
- effective `sportarrId`, or null when that indexer does not support the parameter;
- `useCategoryFilter`;
- `maxResultsPerIndexer`;
- the effective category set and any other outbound option that changes the remote request;
- indexer-level local filters already applied before caching, including `MinimumSeeders`.

Do not serialize this identity with ambiguous string concatenation. Implement value equality on the typed key.

This design has the following consequences:

- tag-restricted leagues consult only their eligible indexers, so cached results from excluded indexers cannot leak in;
- automatic and manual searches cannot share entries when their category mode or result limit differs;
- two events cannot share an ID-filtered response from an indexer supporting `sportarrid`;
- broad season queries can still be shared for indexers that do not support `sportarrid` because their effective ID component is null;
- one unavailable or failing indexer does not prevent reusable results from successful indexers being cached.

`SearchResultCache` stores the indexer ID as part of the key and no longer needs the unused `IndexersQueried` list. Cache entries contain raw release data only. Event, part, quality-profile, custom-format, blocklist, retention, highlight, and match evaluation runs again for the current event on every cache hit.

An indexer that *successfully answered with zero results* is cached under a short negative TTL (`SearchNegativeCacheDuration`, default 60s, 0 disables). Exceptions, timeouts, rate-limit skips, unavailable indexers, and partial failures are never cached at any TTL.

Introduce a minimal structured result at the single-indexer boundary so callers can distinguish those cases. `IndexerSearchOutcome` contains a status (`Succeeded`, `Unavailable`, `RateLimited`, or `Failed`) and the raw releases. Only `Succeeded` is cacheable, including an empty release list under the negative TTL. Preserve the existing public list-returning compatibility method for callers that do not need outcome details; the cache-aware all-indexer path uses the structured method. Do not broaden this into a general indexer error-model refactor.

This preserves the reasoning behind the current refusal to cache empties, since a transient outage must not shadow real results for the full search TTL: failures remain uncached, and 60s is far below `SearchCacheDuration`. It is what makes a season-wide search of unreleased events affordable now that the baseline no longer exits early. Preserve the configured TTL behavior for successful non-empty responses.

Manual and automatic event search must both use this cache path. Force refresh bypasses and invalidates the exact per-indexer keys for every selected query in the current plan; it must not invalidate only the primary query.

The expected request reduction is therefore conditional and honest: identical broad queries are reused per eligible indexer only when all outbound request parameters for that indexer are equivalent. No cross-event reuse is claimed for indexers receiving different event IDs.

### 10. Token catalog

Create `SearchTemplateTokens.All` under `src/Helpers/` with all 19 token metadata entries. Create the replacement map from the same canonical token keys, or test that the replacement-key set and metadata-key set are exactly equal. Avoid a source-code text scan for `result.Replace` literals.

The 19 supported tokens are:

`{League}`, `{Year}`, `{Month}`, `{Day}`, `{Round}`, `{Round:00}`, `{Round:0}`, `{Week}`, `{EventTitle}`, `{EventName}`, `{Stage}`, `{Stage:00}`, `{Stage:0}`, `{HomeTeam}`, `{AwayTeam}`, `{vs}`, `{Season}`, `{Part}`, and `{EventType}`.

`GET /api/search/available-tokens` returns the backend catalog.

`AddLeagueModal` fetches it and renders one insertion button per token. Keep a complete hardcoded frontend fallback so editing remains usable if the request fails. Export the fallback from a small frontend module and unit-test its exact token-key set, including `{Round:00}`, `{Stage:0}`, and `{vs}`. Unknown template tokens remain untouched so users can see mistakes in preview; save-time behavior is unchanged unless separately specified.

### 11. Visibility and preview

Move league search controls into one collapsed “Advanced Search Settings” section on the league edit screen. It contains, in order:

1. the editable “Your aliases” field;
2. an interleaved draggable “Search-name priority” list;
3. custom search templates and the complete token picker;
4. strong-match early stop with `Inherit global`, `Disabled`, and league-specific threshold choices;
5. the structured query viewer.

The search-name list contains built-in, canonical, upstream, and user forms with source badges. All forms, including built-ins, are draggable. Duplicate text appears once with multiple badges. Dragging marks the order customized; merely opening or saving the modal does not. “Reset order” clears the preference and restores the legacy builder order.

League settings and preview expose:

- a read-only name-form summary listing built-in, canonical, upstream, and user forms after deduplication and the three-alias cap;
- any valid aliases excluded by the three-form cap, labeled as not searched with reason `AliasFormLimit`.

The existing preview endpoint returns the structured selected and dropped candidates for each sample event. It uses the same query-plan method as real execution for both custom-template and default-builder previews; default preview must no longer return only the first generated query.

Each sample includes:

- selected query text and provenance;
- query kind/tier;
- budget used and limit;
- truncation status;
- dropped query text, provenance, and reason.

The frontend displays a clear warning whenever candidates were dropped or the mandatory-query invariant was violated.

The preview uses one representative past event. Group events by season, discard seasons with no event at or before the current time, choose the newest remaining season, and randomly select one past event from it. This naturally uses the current season after it has started and the previous season when a synced future season has not started. Select randomly in application code after loading eligible IDs so SQLite and PostgreSQL behave consistently.

Return the event ID, title, date, and season. The frontend retains that ID while aliases, ordering, templates, or the early-stop override change, so the preview does not jump between events during editing. “Try another event” requests a different past event from the same season when one exists. The preview request accepts unsaved aliases, alias order, templates, and early-stop override, and passes them as explicit planner options; it must not mutate the tracked league merely to preview changes.

The numbered query list is the actual execution order. It updates after drag-and-drop and shows provenance, specificity, mandatory/expansion status, budget decisions, and exclusions. When early stop is enabled, the viewer explains that later queries may be skipped only after a fully eligible threshold match is accepted by the download client; it does not claim where runtime stopping will occur.

## Error handling and observability

- Reject invalid or over-length user-alias input with a field-specific 400 response.
- Log ordinary alias truncation at Warning.
- Log a mandatory-query budget violation at Error.
- Treat token-catalog fetch failure as a frontend fallback, not a settings-page failure.
- Treat cache failures as misses; search behavior remains available.
- Do not cache partial failures as empty responses.
- Log cache hits and misses at Debug with query and indexer ID; avoid logging a user alias as though it were a secret, but continue normal query logging conventions.
- Log accepted early stops at Information with threshold, match score, triggering query position, and skipped-query count. Log failed provisional grabs and resumed query position at Warning.

## Testing

### Aliases and persistence

- Parser handles comma, pipe, and slash separators, trimming, empty values, and case-insensitive deduplication.
- Pipe- and slash-separated input normalizes correctly on write for both leagues and teams.
- User aliases round-trip through add, get, update, and frontend edit state.
- Alias search order and the nullable/zero/positive early-stop override round-trip through add, get, update, and frontend edit state.
- Without a saved order, user aliases take priority over upstream aliases within the three-alias cap.
- Weekly metadata refresh changes `AlternateName` but preserves `UserAliases`.
- Values over 512 characters are rejected rather than truncated, for both leagues and teams.
- SQLite and PostgreSQL migrations apply cleanly; existing rows receive null.
- Legacy SQLite startup adds the missing column safely and idempotently.

### Matching and importing

- A title containing only a league user alias passes every applicable league-identity check.
- Grab-side and import-side league identity use the same alias enumeration.
- Existing canonical, upstream-alias, and generated-abbreviation matching remains unchanged.

### Query planning

- Leagues with null alias-order preference produce byte-identical selected query strings in the same order as today.
- Template index is primary; league form is secondary; team-alias slot is tertiary.
- Every existing template and team-user-alias query remains mandatory.
- Motorsport built-ins remain mandatory and aliases are added without series-key collapse.
- Premiership Rugby-style team leagues produce an explicit league-alias query.
- Wrestling and fighting variants change only recognized organization-prefixed queries.
- Generic fallback gains an alias query without losing its existing title query.
- Deduplication retains correct provenance.
- User aliases precede upstream aliases.
- Null alias order preserves the complete legacy query order byte-for-byte.
- A saved order interleaves every form source and sorts specificity first, alias position second.
- The first three non-built-in forms in saved order survive the alias-form cap; later forms are reported as `AliasFormLimit`.
- New forms append, absent stored forms are ignored, reset returns to null, and merely saving an untouched editor does not create an override.
- Alias-expansion candidates never exceed 8.
- A 10-template league with three team-alias slots keeps all 40 baseline queries, none of them counted against the expansion budget.
- Mandatory queries are never dropped during ordinary alias truncation.
- Every dropped candidate has a reason and produces the expected warning.

### Execution

- Every mandatory query is searched or served from cache even after two or more earlier empty responses.
- Every selected alias-expansion candidate is searched or served from cache even after any number of earlier empty responses.
- Global early stop defaults to disabled, and a null league override inherits it while zero explicitly disables it.
- With early stop disabled, every selected query runs.
- A high-scoring but ineligible release never stops search.
- A fully eligible selected release at the threshold triggers a provisional grab.
- An accepted provisional grab stops later queries; a failed provisional grab resumes at the next query.
- Manual interactive search always executes the complete selected plan.
- Cached and live results merge with GUID deduplication.
- A zero-result response does not stop later mandatory fallbacks.

### Cache correctness

- Entries are isolated by indexer ID, effective `sportarrId`, category-filter mode, result limit, and category/outbound options.
- A tag-restricted league never receives a cached result from an excluded indexer.
- Manual broad-category results do not leak into automatic category-filtered search.
- Event-ID searches do not share entries across events on an indexer supporting `sportarrid`.
- Equivalent broad queries do share entries on an indexer that does not support `sportarrid`.
- Editing one template invalidates only the resulting changed request keys naturally; unchanged queries remain reusable.
- Force refresh bypasses every selected query's applicable entries.
- Failed, unavailable, and rate-limited responses remain uncached at any TTL.
- A successful zero-result response is cached only for the negative TTL and never for the full search TTL.
- An indexer with unknown or unavailable caps does not share cache entries across events.
- An indexer with unknown or unavailable caps bypasses result-cache reads and writes entirely.
- Cached raw results are fully re-evaluated for the current event and quality profile.

### Tokens and visibility

- Backend token metadata and replacement keys are exactly equal and contain all 19 tokens.
- A fully populated event leaves no supported token unsubstituted.
- Frontend fallback contains exactly the same 19 token strings.
- `{Round:00}`, `{Stage:0}`, and `{vs}` render and insert at the cursor.
- Name-form summary reports correct sources after cap/deduplication.
- Name-form summary and preview show aliases excluded by the three-form cap with reason `AliasFormLimit`.
- Default and template previews use the real plan and show selected, dropped, provenance, and budget data.
- Representative-event selection uses a past event from the newest started season, remains stable during edits, and can choose another event on request.
- Drag-and-drop immediately updates the numbered specificity-first preview without saving the league.
- Advanced Search Settings contains aliases, priority, templates, early-stop override, and query viewer in one collapsible section.

Run the existing backend and frontend validation suites, including `MultipleSearchTemplatesTests`, without weakening existing assertions.

## Rollout and risk

The main risk remains increased query volume. Alias expansion adds at most 8 queries per event, existing alias-free query sets are preserved, and every selected query runs by default. Per-indexer caching offsets repeated equivalent requests without pretending event-ID-filtered searches are shareable. Users who deliberately customize alias order may also deliberately change query execution order; untouched leagues retain legacy ordering.

Removing consecutive-empty early exits can increase live requests even for alias-free leagues, and it does so hardest in the common case of an event that has not been released yet. This is an intentional correctness tradeoff: by default, if a query is selected, Sportarr runs it. Two things bound the cost: the negative TTL on successful zero-result responses, which absorbs season-wide search storms across events sharing broad queries, and the 8-query alias-expansion budget. An explicitly enabled accepted strong-match grab may stop the remainder. Measure the baseline increase separately from alias expansion so the effect is visible during rollout.

Instrument the number of selected candidates, live per-indexer requests, positive and negative cache hits, and dropped candidates. Revisit `MaxAliasExpansionPerEvent` and `SearchNegativeCacheDuration` using observed live-request counts rather than candidate counts alone.

Per-indexer caching is the highest-risk implementation area. Typed request identity and tests for event IDs, manual/automatic modes, tags, and capabilities are load-bearing requirements.

Strong-match early stop is opt-in and disabled globally by default. Its highest risk is divergence between the incremental “downloadable now” decision and the final grab pipeline. A shared eligibility/selection component, accepted-grab-only termination, and resume-on-failure tests are load-bearing requirements.

Bad upstream aliases may still waste remaining expansion slots. User aliases take priority, and the preview makes the selected forms visible. Suppressing individual upstream aliases remains deferred until a real need is reported.

## Acceptance criteria

The change is complete when:

1. A user can add a league alias, see it after reload, preview its queries, find a release named only with that alias, and pass grab/import matching.
2. A league with no saved alias-order preference emits exactly its pre-change query strings in the same order.
3. No event selects more than 8 alias-expansion queries, and normal truncation never removes an existing alias-free query.
4. Automatic search reaches every mandatory fallback regardless of preceding empty results.
5. Cached results cannot cross excluded indexers, different event IDs, category modes, or result limits.
6. All 19 template tokens are served by the backend and insertable from the frontend.
7. SQLite, PostgreSQL, legacy startup, backend tests, frontend tests, lint, and production build pass.
8. A user can reorder all name-form sources, preview specificity-first execution for the representative event, reset to legacy order, and retain the preference after reload.
9. Strong-match early stop is disabled by default, can be globally enabled and overridden per league, stops only after an accepted fully eligible grab, and resumes remaining queries after a failed grab.
