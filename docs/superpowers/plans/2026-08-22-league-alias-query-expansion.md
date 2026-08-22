# League Alias Query Planning and Advanced Search Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users persist and order league name forms, inspect the exact bounded query plan for a representative event, optionally stop after an accepted fully eligible strong match, and safely reuse equivalent per-indexer results.

**Architecture:** Add one pure alias helper, a typed local alias-order preference, and one structured `QueryPlan` shared by execution and preview. Preserve legacy ordering until a user deliberately saves a combined name-form order; customized plans sort specificity first and alias priority second. Execute every selected candidate by default, with an opt-in shared eligibility pipeline that stops only after an accepted grab and resumes after failure. Move result caching to the per-indexer boundary and bypass caching when `sportarrid` capability discovery is unknown.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, EF Core SQLite/PostgreSQL migrations, FluentValidation, xUnit/FluentAssertions, React, TypeScript, Vitest.

---

## Delivery Stages

This work ships as four stacked pull requests, each independently reviewable, revertable, and valuable on its own. Each stage branches from the previous stage's branch and rebases onto `upstream/dev` when the previous stage merges.

| Stage | Branch | Tasks | Standalone value |
|---|---|---|---|
| 1 | `feat/league-alias-foundations` | 1.1 – 1.5 | Fixes a live bug: team aliases split on comma only today, so pipe- and slash-separated input is stored as one alias and never matches. Adds league alias persistence, unified league identity, and the 19-token catalog. |
| 2 | `feat/league-alias-query-plan` | 2.1 – 2.3 | Introduces the structured `QueryPlan` and bounded alias expansion. Reviewable against byte-identical-output tests; no runtime behavior change for leagues without aliases. |
| 3 | `feat/per-indexer-search-cache` | 3.1 – 3.6 | Replaces unsafe whole-plan caching with typed per-indexer caching, then executes every selected query and adds the opt-in strong-match stop. Highest-risk area, reviewed in isolation. |
| 4 | `feat/advanced-league-search-settings` | 4.1 – 4.3 | Surfaces everything above: advanced settings section, draggable name-form priority, structured preview, and user documentation. |

**Stage ordering is load-bearing.** Stage 3 lands per-indexer caching and the negative TTL *before* removing the consecutive-empty early exit, because the negative TTL is what makes a season-wide search of unreleased events affordable once the baseline no longer exits early. Do not remove the early exit in an earlier stage.

## Global Constraints

- Start Stage 1 from a short-lived `feat/...` branch created from a freshly fetched `upstream/dev`; never develop on `main`. Each later stage branches from the previous stage's branch.
- Preserve all existing query strings and their order byte-for-byte when `AliasSearchOrder` is null.
- Execute every selected query when strong-match early stop is disabled; there is never a consecutive-empty early stop.
- A customized order sorts specificity/template tier first and alias-form position second; built-in forms are draggable.
- `Config.SearchEarlyStopMatchScore` defaults to 0, and manual interactive search never stops early.
- Early stop requires the same fully downloadable candidate as final selection; a failed provisional grab resumes at the next query.
- Select at most `MaxAliasExpansionPerEvent = 8` alias-expansion queries; mandatory baseline queries do not consume that budget.
- Retain `HardQueryCeiling = 50` only as a regression guard.
- Cache only per-indexer requests whose `sportarrid` capability state is known; unknown or unavailable capability data bypasses result-cache reads and writes.
- Cache a zero-result response only when the single-indexer outcome is explicitly `Succeeded`, using `SearchNegativeCacheDuration` with default 60 seconds; 0 disables it.
- Do not introduce new client interfaces or refactor Torznab/Newznab client construction as part of capability probing.
- Use FluentValidation for typed POST/PUT bodies and explicit equivalent validation on the request paths that still deserialize `JsonElement` manually.
- New shared model types live in `src/Sportarr.Data/Models/` under `namespace Sportarr.Api.Models`, matching the existing convention that places `ReleaseSearchResult` in `src/Sportarr.Data/Models/Download.cs`. Do not create a new top-level `src/Models/` folder.
- Add SQLite and PostgreSQL migrations plus the legacy SQLite startup safety net.
- Do not change match confidence thresholds, template syntax, or unrelated dependencies.

---

# Stage 1 — Alias Foundations and Token Catalog

**Branch:** `feat/league-alias-foundations` from freshly fetched `upstream/dev`.

**Delivers:** one alias parser used by both leagues and teams, three new local-only `League` columns on both providers, a single league-identity enumeration used by every grab and import gate, and an authoritative 19-token catalog.

**Does not change:** query generation, search execution, or caching.

---

### Task 1.1: Shared Alias Parsing and Persistence Contract

**Files:**
- Create: `src/Helpers/AliasField.cs`
- Create: `tests/Sportarr.Api.Tests/Helpers/AliasFieldTests.cs`
- Modify: `src/Endpoints/FollowedTeamsAndTeamsEndpoints.cs:385`

**Interfaces:**
- Produces: `AliasField.MaxUserAliasesLength`, `AliasField.Parse(string?)`, and `AliasField.Normalize(string?)`.
- `Parse` returns trimmed, non-empty, case-insensitively distinct aliases split on comma, pipe, or slash, retaining the first-seen casing.
- `Normalize` returns a comma-and-space joined string or `null`.

- [ ] **Step 1: Write parser and normalization tests**

```csharp
[Theory]
[InlineData("one,two", new[] { "one", "two" })]
[InlineData(" one | TWO / one ", new[] { "one", "TWO" })]
public void Parse_NormalizesSupportedSeparators(string raw, string[] expected) =>
    AliasField.Parse(raw).Should().Equal(expected);

[Fact]
public void Normalize_UsesStableStorageForm() =>
    AliasField.Normalize(" one | TWO / one ").Should().Be("one, TWO");
```

Also assert `Parse(null)`, `Parse("")`, and `Parse("  ,  | / ")` return empty, and that `Normalize` returns `null` for each.

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter FullyQualifiedName~AliasFieldTests`

Expected: FAIL because `AliasField` does not exist.

- [ ] **Step 3: Implement the pure helper**

```csharp
public static class AliasField
{
    public const int MaxUserAliasesLength = 512;
    private static readonly char[] Separators = [',', '|', '/'];

    public static IReadOnlyList<string> Parse(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    public static string? Normalize(string? value)
    {
        var aliases = Parse(value);
        return aliases.Count == 0 ? null : string.Join(", ", aliases);
    }
}
```

`RemoveEmptyEntries | TrimEntries` already drops whitespace-only entries, so do not add a redundant `Where` filter. In particular do not write `.Where(value => ...)`: a lambda parameter cannot shadow the enclosing `value` parameter and the file will not compile.

- [ ] **Step 4: Route the existing team alias write through `AliasField`**

`PUT /api/teams/{id}/aliases` currently splits on comma alone, so pipe- and slash-separated input is stored as a single alias and never matches. Replace that split with `AliasField.Normalize`, reject raw values over `AliasField.MaxUserAliasesLength` with a field-specific 400 response keyed `userAliases`, and use `AliasField.Parse` for the list passed to `SubmitTeamAliasSuggestionAsync`.

- [ ] **Step 5: Run focused tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~AliasFieldTests|FullyQualifiedName~EventQueryServiceAliasTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Helpers/AliasField.cs src/Endpoints/FollowedTeamsAndTeamsEndpoints.cs tests/Sportarr.Api.Tests/Helpers/AliasFieldTests.cs
git commit -m "fix: parse pipe and slash separated user aliases everywhere"
```

---

### Task 1.2: Persist League Search Preferences on Both Providers

**Files:**
- Modify: `src/Sportarr.Data/Models/League.cs` (entity, `AddLeagueRequest`, `LeagueResponse`)
- Modify: `src/Sportarr.Data/Data/SportarrDbContext.cs`
- Modify: `src/Services/LeagueAddService.cs`
- Modify: `src/Endpoints/LeagueEndpoints.cs`
- Create: `src/Validators/AddLeagueRequestValidator.cs`
- Create: `src/Sportarr.Data/Migrations/20260822000100_AddLeagueSearchPreferences.cs`
- Create: `src/Sportarr.Data/Migrations/20260822000100_AddLeagueSearchPreferences.Designer.cs`
- Modify: `src/Sportarr.Data/Migrations/SportarrDbContextModelSnapshot.cs`
- Create: `src/Sportarr.Migrations.Postgres/Migrations/20260822000100_AddLeagueSearchPreferences.cs`
- Create: `src/Sportarr.Migrations.Postgres/Migrations/20260822000100_AddLeagueSearchPreferences.Designer.cs`
- Modify: `src/Sportarr.Migrations.Postgres/Migrations/SportarrDbContextModelSnapshot.cs`
- Modify: `src/Startup/DatabaseInitializer.cs`
- Create: `tests/Sportarr.Api.Tests/Endpoints/LeagueSearchPreferencesTests.cs`

**Interfaces:**
- Adds nullable `League.UserAliases`, typed JSON `League.AliasSearchOrder`, and nullable `League.SearchEarlyStopMatchScoreOverride` plus matching request/response fields.
- Null alias order means never customized; null early-stop override inherits global, zero disables, and a positive integer overrides.
- Stores normalized aliases and never writes local search preferences during upstream metadata refresh.

**Scope decision — do not convert the add endpoint to typed binding.** `POST /api/leagues` buffers the body and delegates to `LeagueAddService`, which deserializes manually. Converting it to typed model binding is separate future work and would inflate this stage. Keep the existing deserialization path and invoke `AddLeagueRequestValidator` explicitly inside `LeagueAddService`, exactly as the `JsonElement` update endpoint will do.

- [ ] **Step 1: Write failing DTO and validation tests**

Cover add mapping, response mapping, pipe/slash normalization, clearing with whitespace, alias-order source/value round-trip, null/zero/positive early-stop semantics, and rejection at 513 characters. Assert the validation error key is `userAliases`. Assert the explicit validator invocation rejects an over-length alias submitted through `POST /api/leagues`, not only through the typed path.

- [ ] **Step 2: Run the focused tests and verify failure**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter FullyQualifiedName~LeagueSearchPreferencesTests`

Expected: FAIL because league user aliases are absent.

- [ ] **Step 3: Add the entity and DTO fields**

```csharp
public string? UserAliases { get; set; }
public List<LeagueAliasOrderEntry>? AliasSearchOrder { get; set; }
public int? SearchEarlyStopMatchScoreOverride { get; set; }
```

Define `LeagueNameFormSource` (`BuiltIn`, `Canonical`, `UserAlias`, `UpstreamAlias`) and `LeagueAliasOrderEntry` with `LeagueNameFormSource Source` and `string Value`, both in `src/Sportarr.Data/Models/` under `namespace Sportarr.Api.Models`. Reconcile saved positions by normalized value after effective-form deduplication; retain source as diagnostic provenance, not a second identity key.

Configure the list with the repository's existing JSON conversion plus `ValueComparer` pattern, copied from the `League.Tags` configuration in `SportarrDbContext` (`JsonSerializerOptionsProvider.Database`, `SequenceEqual` comparer, `ToList` snapshot). Do not add a relational child table.

Set `UserAliases = AliasField.Normalize(UserAliases)` in `AddLeagueRequest.ToLeague()` and copy all three fields in `LeagueResponse.FromLeague()`.

- [ ] **Step 4: Validate writes**

Create `AddLeagueRequestValidator` with:

```csharp
RuleFor(request => request.UserAliases)
    .MaximumLength(AliasField.MaxUserAliasesLength)
    .When(request => request.UserAliases is not null);
```

Validate alias-order entries with a maximum of 64 entries, defined enum source, non-empty value, and value length at most 256. Validate the early-stop override from 0 through 100, matching `ReleaseMatchScorer`'s clamp.

Invoke the validator explicitly from `LeagueAddService` after its existing deserialization, and apply equivalent explicit checks in the `JsonElement` `PUT /api/leagues/{id}` endpoint. Return a field-specific 400 in both places.

- [ ] **Step 5: Generate and inspect both EF migrations**

Run the repository's existing EF commands for the SQLite and PostgreSQL migration projects, using migration name `AddLeagueSearchPreferences`. Verify both migrations add nullable `UserAliases`, `AliasSearchOrder`, and `SearchEarlyStopMatchScoreOverride` columns with no backfill.

- [ ] **Step 6: Add the legacy SQLite safety net**

Follow the existing `pragma_table_info('Leagues')` pattern in `DatabaseInitializer` and execute only:

```sql
ALTER TABLE Leagues ADD COLUMN UserAliases TEXT NULL;
ALTER TABLE Leagues ADD COLUMN AliasSearchOrder TEXT NULL;
ALTER TABLE Leagues ADD COLUMN SearchEarlyStopMatchScoreOverride INTEGER NULL;
```

when the column is absent. The column stays unconstrained `TEXT`; the 512-character limit is application validation only.

- [ ] **Step 7: Verify metadata refresh preservation**

Extend `LeagueEventSyncService` tests with all local search preferences populated. Assert refresh changes the upstream alternate name and leaves all three local values untouched.

- [ ] **Step 8: Run focused persistence tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~LeagueSearchPreferencesTests|FullyQualifiedName~LeagueEventSyncService"`

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Sportarr.Data src/Sportarr.Migrations.Postgres src/Services/LeagueAddService.cs src/Endpoints/LeagueEndpoints.cs src/Startup/DatabaseInitializer.cs src/Validators/AddLeagueRequestValidator.cs tests/Sportarr.Api.Tests
git commit -m "feat: persist league search preferences"
```

---

### Task 1.3: Use One League Identity Enumeration Everywhere

**Files:**
- Create: `src/Helpers/LeagueAliasHelper.cs`
- Modify: `src/Services/ReleaseMatchingService.cs`
- Modify: `src/Services/LibraryImportService.cs`
- Create: `tests/Sportarr.Api.Tests/Services/LeagueUserAliasMatchingTests.cs`
- Modify: `tests/Sportarr.Api.Tests/Services/ImportLeagueIdentityTests.cs`

**Interfaces:**
- Produces: `LeagueAliasHelper.GetMatchingAliases(League)` in order: name, upstream aliases, user aliases, generated abbreviations, case-insensitively deduplicated.
- All grab/import identity paths consume this enumeration directly or via `TitleNamesLeague`/`SeriesLabelMatchesLeague`.

This is a correctness requirement, not a convenience: a release found only through a user alias must not later fail league-identity matching because the matcher does not know that alias.

- [ ] **Step 1: Write regression tests for every identity gate**

Use a league named `English Prem Rugby` with `UserAliases = "Gallagher Prem"` and release/series text containing only `Gallagher Prem`. Cover organization scoring, `TitleNamesLeague`, `SeriesLabelMatchesLeague`, grab validation, and import matching.

- [ ] **Step 2: Run the tests and verify at least the user-alias cases fail**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~LeagueUserAliasMatchingTests|FullyQualifiedName~ImportLeagueIdentityTests"`

- [ ] **Step 3: Implement and adopt the enumeration**

```csharp
public static IReadOnlyList<string> GetMatchingAliases(League league)
{
    var aliases = new List<string> { league.Name };
    aliases.AddRange(AliasField.Parse(league.AlternateName));
    aliases.AddRange(AliasField.Parse(league.UserAliases));
    // Append the existing generated Formula-style abbreviation.
    return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
```

Delete the local `Name` plus `AlternateName` list in organization validation and the private duplicate enumeration in `ReleaseMatchingService`. There must not remain a second league-alias list anywhere.

- [ ] **Step 4: Run matching and import tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~LeagueUserAliasMatchingTests|FullyQualifiedName~ImportLeagueIdentityTests|FullyQualifiedName~CrossLeagueMatchingGateTests|FullyQualifiedName~F1AbbreviationMatchingTests|FullyQualifiedName~WsbkSbkAliasMatchingTests"`

Expected: PASS with existing confidence behavior unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/Helpers/LeagueAliasHelper.cs src/Services/ReleaseMatchingService.cs src/Services/LibraryImportService.cs tests/Sportarr.Api.Tests/Services
git commit -m "fix: apply league user aliases to grab and import identity"
```

---

### Task 1.4: Make the Backend Token Catalog Authoritative

**Files:**
- Create: `src/Helpers/SearchTemplateTokens.cs`
- Modify: `src/Services/EventQueryService.cs`
- Modify: `src/Endpoints/LeagueEndpoints.cs`
- Create: `tests/Sportarr.Api.Tests/Helpers/SearchTemplateTokensTests.cs`
- Create: `frontend/src/utils/searchTemplateTokens.ts`
- Create: `frontend/src/utils/searchTemplateTokens.test.ts`

**Interfaces:**
- Produces: `SearchTemplateTokens.All` containing metadata for exactly 19 tokens.
- Produces: frontend `fallbackSearchTemplateTokens` containing the identical token-key set.

This task is independent of all alias work and can be reviewed on its own within the stage.

- [ ] **Step 1: Write backend key-parity tests**

Assert metadata keys and replacement keys are equal and contain all 19 tokens: `{League}`, `{Year}`, `{Month}`, `{Day}`, `{Round}`, `{Round:00}`, `{Round:0}`, `{Week}`, `{EventTitle}`, `{EventName}`, `{Stage}`, `{Stage:00}`, `{Stage:0}`, `{HomeTeam}`, `{AwayTeam}`, `{vs}`, `{Season}`, `{Part}`, `{EventType}`. Build a fully populated event and assert no supported token remains unsubstituted. Compare key sets directly; do not scan source text for `result.Replace` literals.

- [ ] **Step 2: Implement the canonical catalog and replacement map**

Build replacement values in `EventQueryService` keyed by the same canonical token constants, applying longer formatted keys before their shorter prefixes so `{Round:00}` is not partially consumed by `{Round}`. Leave unknown tokens visible in output so users can see mistakes in preview.

- [ ] **Step 3: Return the catalog from the endpoint**

Replace the inline 12-token array in `GET /api/search/available-tokens` with `SearchTemplateTokens.All`, and use `ILogger<LeagueEndpoints>` rather than `ILogger<Program>` while touching that endpoint.

- [ ] **Step 4: Write and implement frontend fallback tests**

Assert the frontend module exports exactly the same 19 literal token strings. Do not derive the fallback from a successful HTTP response.

- [ ] **Step 5: Run focused backend and frontend tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter FullyQualifiedName~SearchTemplateTokensTests`

Run: `cd frontend && npm test -- --run src/utils/searchTemplateTokens.test.ts`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Helpers/SearchTemplateTokens.cs src/Services/EventQueryService.cs src/Endpoints/LeagueEndpoints.cs tests/Sportarr.Api.Tests/Helpers/SearchTemplateTokensTests.cs frontend/src/utils/searchTemplateTokens.ts frontend/src/utils/searchTemplateTokens.test.ts
git commit -m "fix: unify search template token catalogs"
```

---

### Task 1.5: Stage 1 Validation and Migration Smoke Tests

**Files:**
- Modify only files required to resolve failures introduced by Tasks 1.1 – 1.4.

- [ ] **Step 1: Exercise SQLite migration and legacy startup**

Create a temporary SQLite database at an explicit `mktemp -d` path, apply migrations, start the application, and verify `PRAGMA table_info('Leagues')` contains nullable `UserAliases`, `AliasSearchOrder`, and `SearchEarlyStopMatchScoreOverride`. Repeat startup against a legacy `EnsureCreated` schema and verify the safety net is idempotent across two consecutive startups.

- [ ] **Step 2: Exercise PostgreSQL migrations**

Run the repository's PostgreSQL migration test path against a disposable test database and verify existing rows have all three new league columns set to null.

- [ ] **Step 3: Run backend validation**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true`

Expected: PASS with no new compiler warnings.

- [ ] **Step 4: Run frontend validation**

Run: `cd frontend && npm ci`

Run: `cd frontend && npm run lint`

Run: `cd frontend && npm test -- --run`

Run: `cd frontend && npm run build`

Expected: all commands PASS with no new lint warnings.

- [ ] **Step 5: Review the stage diff**

Run: `git status --short && git diff --check && git diff --stat upstream/dev...HEAD`

Expected: only scoped source, migration, test, frontend, and plan files; no build output, database, credential, or unrelated workspace files.

**Stage 1 exit criteria:** a user alias saved on a league survives reload and metadata refresh; a title naming only that alias passes every league-identity gate; team aliases accept pipe and slash separators; all 19 tokens are served by the backend and present in the frontend fallback.

---

# Stage 2 — Structured Query Plan and Bounded Alias Expansion

**Branch:** `feat/league-alias-query-plan` from Stage 1.

**Delivers:** a structured `QueryPlan` shared by execution and preview, and bounded league-alias expansion in every query builder.

**Does not change:** search execution order for leagues without aliases or a saved order, caching, or the UI. Callers continue to consume `BuildEventQueries`.

---

### Task 2.1: Introduce the Structured Query Plan and Name-Form Diagnostics

**Files:**
- Create: `src/Sportarr.Data/Models/QueryPlan.cs`
- Create: `src/Helpers/LeagueQueryForms.cs`
- Modify: `src/Services/EventQueryService.cs`
- Create: `tests/Sportarr.Api.Tests/Services/QueryPlanTests.cs`

**Interfaces:**
- Produces: `QueryPlan EventQueryService.BuildEventQueryPlan(Event evt, string? part = null, string? customTemplate = null, QueryPlanningOptions? options = null)`.
- `QueryPlanningOptions` carries unsaved user aliases, alias order, and template without mutating the tracked `League`.
- Preserves: `List<string> BuildEventQueries(...) => BuildEventQueryPlan(...).SelectedQueries.Select(query => query.Text).ToList()`.
- Defines `QueryCandidate`, `QueryKind`, `QueryDropReason`, `LeagueNameForm`, `ExcludedLeagueNameForm`, and `QueryPlanningOptions` in `src/Sportarr.Data/Models/QueryPlan.cs` under `namespace Sportarr.Api.Models`; reuses `LeagueNameFormSource` from Task 1.2.

- [ ] **Step 1: Write failing plan-shape tests**

Assert that a null saved order returns exactly the current strings and order; duplicate query text retains mandatory provenance over expansion provenance plus all contributing forms; a combined saved order interleaves all sources; the first three non-built-in forms survive while the fourth is `AliasFormLimit`; new forms append; missing stored forms are ignored; reset/null restores legacy order; and a synthetic builder regression above 50 candidates retains the first 50, sets `MandatoryInvariantViolated`, and logs at Error.

Also assert observability: ordinary alias truncation logs at **Warning** containing the league, selected count, and dropped count, and every dropped candidate carries a non-null `DropReason`.

- [ ] **Step 2: Run plan tests and verify failure**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter FullyQualifiedName~QueryPlanTests`

- [ ] **Step 3: Add the plan records and constants**

```csharp
public sealed record QueryCandidate(
    string Text,
    string LeagueNameForm,
    LeagueNameFormSource FormSource,
    QueryKind Kind,
    int SpecificityRank,
    int AliasOrderIndex,
    int? TemplateIndex,
    int? TeamAliasSlot,
    bool IsMandatory,
    bool IsSelected,
    QueryDropReason? DropReason,
    IReadOnlyList<LeagueNameForm> ContributingForms);

public sealed record QueryPlan(
    IReadOnlyList<QueryCandidate> Candidates,
    IReadOnlyList<QueryCandidate> SelectedQueries,
    IReadOnlyList<QueryCandidate> DroppedQueries,
    IReadOnlyList<ExcludedLeagueNameForm> ExcludedNameForms,
    int AliasBudgetUsed,
    int AliasBudgetLimit,
    int HardQueryCeiling,
    bool IsTruncated,
    bool MandatoryInvariantViolated);

public sealed record QueryPlanningOptions(
    string? UserAliases,
    IReadOnlyList<LeagueAliasOrderEntry>? AliasSearchOrder,
    string? SearchQueryTemplate);
```

Set `MaxAliasExpansionPerEvent = 8` and `HardQueryCeiling = 50` once in the planner.

- [ ] **Step 4: Build league query forms without altering current canonical normalization**

Return built-in/canonical forms first, then user aliases, then upstream aliases. The canonical template form keeps using `GetNormalizedLeagueNameForTemplate` exactly as today; alias strings are trimmed and otherwise used as entered, never passed through canonical-name recognition that could collapse them back to the same series key. Deduplicate before applying the three-alias cap, retain excluded aliases as `AliasFormLimit` diagnostics, and keep display text separate from final query text.

- [ ] **Step 5: Add candidate deduplication and budget selection**

Deduplicate query text case-insensitively before budgeting. Select every mandatory candidate unconditionally, select the first eight expansion candidates in builder-specific priority, append expansions after the complete baseline, and mark remaining candidates `AliasBudgetExceeded`. Log ordinary truncation at Warning with league, selected count, and dropped count.

Apply the hard ceiling only after ordinary selection; if it is exceeded, retain the first 50 in builder order, set `MandatoryInvariantViolated`, and log at Error. Exceeding it means a genuine builder regression, since the largest legitimate baseline is 10 templates × 4 team-alias slots = 40.

- [ ] **Step 6: Run plan and unchanged baseline tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~QueryPlanTests|FullyQualifiedName~MultipleSearchTemplatesTests|FullyQualifiedName~EventQueryService"`

Expected: PASS; `MultipleSearchTemplatesTests` remains unmodified.

- [ ] **Step 7: Commit**

```bash
git add src/Sportarr.Data/Models/QueryPlan.cs src/Helpers/LeagueQueryForms.cs src/Services/EventQueryService.cs tests/Sportarr.Api.Tests/Services/QueryPlanTests.cs
git commit -m "refactor: represent event searches as structured query plans"
```

---

### Task 2.2: Generate and Order Bounded Alias Queries for Every Builder

**Files:**
- Modify: `src/Services/EventQueryService.cs`
- Modify: `tests/Sportarr.Api.Tests/Services/EventQueryServiceAliasTests.cs`
- Modify: `tests/Sportarr.Api.Tests/Services/EventQueryServiceMotorsportTests.cs`
- Modify: `tests/Sportarr.Api.Tests/Services/EventQueryServiceWrestlingTests.cs`
- Create: `tests/Sportarr.Api.Tests/Services/EventQueryServiceLeagueAliasTests.cs`

**Interfaces:**
- Extends `BuildQueryFromTemplate` with `string? leagueNameOverride = null`, alongside the existing home/away overrides.
- Null `AliasSearchOrder` emits the current complete baseline followed by expansions without changing legacy order.
- A saved order emits specificity/template tier first, alias position second, and stable builder tie-breakers last.

- [ ] **Step 1: Write failing cross-builder tests**

Cover custom templates, motorsport, mapped and unmapped team sports, Premiership Rugby, wrestling, fighting, and generic fallback. Assert null-order compatibility, customized specificity-first ordering, draggable built-ins participating in the saved order, user/upstream interleaving, no home/away Cartesian product, and a maximum of eight selected expansions.

Add the 10-template × 3-team-alias-slot case: all 40 baseline queries survive, none counted against the expansion budget.

- [ ] **Step 2: Run the focused query suite and verify failures**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~EventQueryService|FullyQualifiedName~MultipleSearchTemplatesTests"`

- [ ] **Step 3: Implement the two-phase template planner**

First generate every current template/team-slot query in existing order — template index, then team-alias slot with the canonical team pair first — and mark each mandatory. Then loop template index, league alias, and canonical/team alias slots to generate optional expansions using `leagueNameOverride`. For a customized order, retain template index as the major tier, then alias position, then team slot, so alias drag order can never move a later user-authored template ahead of an earlier one.

- [ ] **Step 4: Implement default-builder alias expansion**

League aliases must be inserted where they actually affect output; re-invoking an unchanged builder with a different `leagueName` is not sufficient.

- **Motorsport:** keep the built-in prefix list and ordering, and append raw user/upstream alias forms to the search-prefix list without passing them through `GetMotorsportSeriesPrefix`. Specificity ranks: round, location, title-derived location, country noun, broad season fallback.
- **Team sports:** add `{alias} {year} {home} {away}` when both teams resolve, otherwise `{alias} {year}`. Reuse each existing team-alias slot's names; do not create a home/away Cartesian product. This is what gives Premiership Rugby-style leagues an alias query at all, since their canonical builder falls back to event-title queries.
- **Wrestling:** replace only the leading organization token in the corresponding canonical weekly-show or special-event query. If a canonical query has no leading organization token, synthesize nothing. Never infer WWE versus AEW from an arbitrary alias.
- **Fighting:** apply alias variants only to queries with a recognized leading organization token, preserving the card number/type or year suffix. Pure surname-matchup queries gain no league variant.
- **Generic fallback:** keep the normalized event-title query mandatory, then add `{alias} {year} {normalized event title}`, or `{alias} {normalized event title}` when the event has no usable year.

All currently generated built-in-form queries remain mandatory in every builder, including every canonical broad fallback. For default builders with a customized order, assign an explicit specificity rank and sort by rank, alias position, and original candidate position. A saved alias order changes execution order intentionally but never changes mandatory or budget classification.

At the null/custom ordering branch, include this compatibility comment in substance:

```csharp
// Preserve the legacy form-grouped order for leagues whose users have not
// customized alias priority. A future migration may make specificity-first
// ordering universal; until then, null means retain existing behavior.
```

- [ ] **Step 5: Run all query tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~EventQueryService|FullyQualifiedName~MultipleSearchTemplatesTests|FullyQualifiedName~BritishSuperbikeTests|FullyQualifiedName~DwcsWeekNumberingTests"`

Expected: PASS and no alias-free expected output changes.

- [ ] **Step 6: Commit**

```bash
git add src/Services/EventQueryService.cs tests/Sportarr.Api.Tests/Services
git commit -m "feat: expand event queries with bounded league aliases"
```

---

### Task 2.3: Stage 2 Validation

- [ ] **Step 1: Confirm byte-identical legacy output**

Run the full backend suite and confirm no existing expected query string or ordering assertion was modified in this stage:

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true`

Run: `git diff upstream/dev...HEAD -- tests/Sportarr.Api.Tests/Services/MultipleSearchTemplatesTests.cs`

Expected: backend suite PASS; the `MultipleSearchTemplatesTests` diff is empty.

- [ ] **Step 2: Run frontend validation**

Run: `cd frontend && npm run lint && npm test -- --run && npm run build`

Expected: PASS.

- [ ] **Step 3: Review the stage diff**

Run: `git status --short && git diff --check && git diff --stat`

**Stage 2 exit criteria:** a league with no saved alias-order preference emits exactly its pre-change query strings in the same order; no event selects more than 8 alias-expansion queries; mandatory queries are never dropped by ordinary truncation; every dropped candidate has a reason and produces the expected Warning.

---

# Stage 3 — Safe Per-Indexer Caching, Full Plan Execution, and Strong-Match Early Stop

**Branch:** `feat/per-indexer-search-cache` from Stage 2.

**Delivers:** structured single-indexer outcomes, typed per-indexer request caching with a negative TTL, removal of the consecutive-empty early exit, the opt-in strong-match stop with resume, and search instrumentation.

**Internal ordering is deliberate:** caching (3.1 – 3.2) lands before the consecutive-empty exit is removed (3.3), because the negative TTL is what absorbs season-wide search storms once every selected query runs. Do not reorder these tasks.

---

### Task 3.1: Add Explicit Single-Indexer Outcomes

**Files:**
- Create: `src/Sportarr.Data/Models/IndexerSearchOutcome.cs`
- Modify: `src/Services/IndexerSearchService.cs`
- Modify: `src/Services/Interfaces/IIndexerSearchService.cs`
- Create: `tests/Sportarr.Api.Tests/Services/IndexerSearchOutcomeTests.cs`

**Interfaces:**
- Produces: `Task<IndexerSearchOutcome> SearchIndexerWithOutcomeAsync(...)`.
- Preserves: the existing `SearchIndexerAsync(...)` interface method, reimplemented as a compatibility wrapper returning `outcome.Releases`.

- [ ] **Step 1: Write failing status tests**

```csharp
public enum IndexerSearchStatus { Succeeded, Unavailable, RateLimited, Failed }

public sealed record IndexerSearchOutcome(
    IndexerSearchStatus Status,
    List<ReleaseSearchResult> Releases);
```

Test genuine empty HTTP success as `Succeeded`, health skip as `Unavailable`, `IndexerRateLimitException` as `RateLimited`, and request/general exceptions as `Failed`. Do not broaden this into a general indexer error-model refactor.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter FullyQualifiedName~IndexerSearchOutcomeTests`

- [ ] **Step 3: Extract the structured method with minimal behavior change**

Move the existing single-indexer try/catch logic into `SearchIndexerWithOutcomeAsync`. Keep health recording, protocol assignment, indexer ID assignment, and minimum-seeder filtering unchanged. Implement `SearchIndexerAsync` as a one-line compatibility wrapper so no existing caller or interface consumer changes.

- [ ] **Step 4: Run all indexer service tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~IndexerSearchOutcomeTests|FullyQualifiedName~Indexer"`

Expected: PASS with no caller-facing list behavior change.

- [ ] **Step 5: Commit**

```bash
git add src/Sportarr.Data/Models/IndexerSearchOutcome.cs src/Services/IndexerSearchService.cs src/Services/Interfaces/IIndexerSearchService.cs tests/Sportarr.Api.Tests/Services/IndexerSearchOutcomeTests.cs
git commit -m "refactor: expose single-indexer search outcomes"
```

---

### Task 3.2: Replace Whole-Plan Cache with Safe Per-Indexer Request Caching

**Files:**
- Create: `src/Sportarr.Data/Models/IndexerSearchCacheKey.cs`
- Modify: `src/Services/SearchResultCache.cs`
- Modify: `src/Services/IndexerSearchService.cs`
- Modify: `src/Services/TorznabClient.cs`
- Modify: `src/Services/NewznabClient.cs`
- Modify: `src/Sportarr.Data/Models/Config.cs`
- Modify: `src/Sportarr.Data/Models/Settings.cs`
- Modify: `src/Endpoints/SettingsEndpoints.cs`
- Modify: `frontend/src/pages/settings/IndexersSettings.tsx`
- Create: `tests/Sportarr.Api.Tests/Services/PerIndexerSearchCacheTests.cs`
- Create: `tests/Sportarr.Api.Tests/Services/SportarrIdCapabilityCacheTests.cs`

**Interfaces:**
- `TorznabClient.GetSportarrIdSupportAsync(Indexer)` and `NewznabClient.GetSportarrIdSupportAsync(Indexer)` return `bool?`.
- `null` means bypass result-cache read/write and perform the normal live search.
- `IndexerSearchCacheKey` uses value equality and contains normalized query, indexer ID and URL, effective ID, category-filter mode, effective category set, result limit, indexer type, and minimum seeders.

- [ ] **Step 1: Write typed-key and TTL tests**

Test isolation by indexer ID, URL, effective ID, category mode/set, result limit, type, and minimum seeders. Test positive TTL, 60-second negative TTL, disabled negative TTL, exact-key invalidation, and raw release round-trip. Assert value equality is implemented on the typed key rather than ambiguous string concatenation.

- [ ] **Step 2: Write capability-state tests**

Assert known support includes the normalized `sportarrId`, known non-support uses null, and unknown support causes no cache lookup or store. Assert an unknown live result cannot be reused after capabilities later become known. Assert exceptions from cache access are treated as misses and do not prevent the live request.

- [ ] **Step 3: Run cache tests and verify failure**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~PerIndexerSearchCacheTests|FullyQualifiedName~SportarrIdCapabilityCacheTests"`

- [ ] **Step 4: Expose the tri-state support method on concrete clients**

Return `null` when the cached capabilities lookup failed, otherwise return whether `SupportedSearchParams` contains `sportarrid`. Reuse the existing static 12-hour success and 15-minute failure caches so no duplicate round-trip is added, and keep the send-time check inside `SearchAsync`. `IndexerSearchService` already constructs these clients directly; do not introduce a new client interface or DI refactor.

- [ ] **Step 5: Convert `SearchResultCache` to typed per-indexer entries**

Remove the joined-string key and the unused `IndexersQueried` list. Store raw releases plus timestamp and whether the successful response was empty. Determine expiry from `SearchCacheDuration` for non-empty results and `SearchNegativeCacheDuration` for empty successes. Cache entries hold raw release data only; event, part, quality-profile, custom-format, blocklist, retention, highlight, and match evaluation run again for the current event on every hit.

- [ ] **Step 6: Integrate cache checks inside the per-indexer task**

After indexer eligibility is resolved, determine capability support, construct the exact key only for a known support state, serve a hit, or call `SearchIndexerWithOutcomeAsync`. Store only `Succeeded` outcomes; never store unavailable, rate-limited, failed, or unknown-capability outcomes. Treat any cache exception as a miss and continue with live search. Merge cached and live raw results before the existing event/profile evaluation. Log cache hits and misses at Debug with query and indexer ID.

- [ ] **Step 7: Add the negative TTL setting**

```csharp
public int SearchNegativeCacheDuration { get; set; } = 60;
```

Expose it through settings GET/PUT, clamp at zero or greater, and add an advanced indexer setting labeled "Empty-result cache duration" with `0` documented as disabled. `Config` is the `config.xml`-backed `[XmlRoot]` type, not an EF entity, so no database migration is required.

- [ ] **Step 8: Run cache and indexer tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~PerIndexerSearchCacheTests|FullyQualifiedName~SportarrIdCapabilityCacheTests|FullyQualifiedName~IndexerSearchOutcomeTests"`

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Sportarr.Data/Models/IndexerSearchCacheKey.cs src/Services/SearchResultCache.cs src/Services/IndexerSearchService.cs src/Services/TorznabClient.cs src/Services/NewznabClient.cs src/Sportarr.Data/Models/Config.cs src/Sportarr.Data/Models/Settings.cs src/Endpoints/SettingsEndpoints.cs frontend/src/pages/settings/IndexersSettings.tsx tests/Sportarr.Api.Tests/Services
git commit -m "refactor: cache equivalent requests per indexer"
```

---

### Task 3.3: Execute Every Selected Query and Wire Search to the New Cache

**Files:**
- Modify: `src/Services/AutomaticSearchService.cs`
- Modify: `src/Endpoints/ManualEventSearchEndpoints.cs`
- Modify: `src/Services/IndexerSearchService.cs`
- Modify: `src/Services/Interfaces/IIndexerSearchService.cs`
- Create: `tests/Sportarr.Api.Tests/Services/AutomaticSearchQueryExecutionTests.cs`
- Create: `tests/Sportarr.Api.Tests/Endpoints/ManualSearchQueryExecutionTests.cs`
- Create: `tests/Sportarr.Api.Tests/Endpoints/ManualSearchForceRefreshTests.cs`

**Interfaces:**
- Automatic and generated manual search consume `QueryPlan.SelectedQueries` in displayed order.
- Custom one-off manual query remains a single explicit query and bypasses generated planning.
- Search callers no longer read or write `SearchResultCache`; `IndexerSearchService` owns cache use.
- Adds `bool forceRefresh = false` to `SearchAllIndexersAsync` on both `IIndexerSearchService` and `IndexerSearchService`.

This task folds the former "execute every query" and "wire the cache" tasks together, because they modify the same two source files and the same two test files.

- [ ] **Step 1: Write failing execution and cache-integration tests**

Configure five selected queries whose first four return empty. Assert all five calls occur in plan order and results from the fifth are merged, for automatic search and for generated manual search, with the global setting and league override both disabled.

Add integration assertions: automatic and manual searches both reuse equivalent per-indexer entries; manual category mode remains isolated from automatic category-filtered search; and force refresh bypasses and invalidates every applicable selected-query key rather than only the primary query.

- [ ] **Step 2: Run focused tests and confirm the automatic test stops after two**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~AutomaticSearchQueryExecutionTests|FullyQualifiedName~ManualSearchQueryExecutionTests|FullyQualifiedName~ManualSearchForceRefreshTests"`

- [ ] **Step 3: Remove all consecutive-empty state and breaks**

Delete `consecutiveEmptyResults`, `MaxConsecutiveEmpty`, and the associated `break` in `AutomaticSearchService`. Iterate every selected candidate. Do not add a separate expansion stop. A zero-result response must not prevent later mandatory fallback queries from running.

- [ ] **Step 4: Remove caller-level caching**

Delete `SearchResultCache` injection and whole-plan/primary-query cache handling from automatic and manual search, including the `TryGetCached(primaryQuery, ...)` call in `ManualEventSearchEndpoints`. Pass cache duration configuration and `forceRefresh` into the cache-aware indexer service path.

- [ ] **Step 5: Implement exact-key force refresh**

For each selected query and eligible known-capability indexer, invalidate the computed key before live search and do not serve a cached response. Unknown-capability indexers already bypass caching and need no invalidation.

- [ ] **Step 6: Preserve GUID deduplication and plan-aware logging**

Retain GUID deduplication while merging results. Log candidate provenance and mandatory/expansion status from the plan rather than reconstructing it from query strings. A cache hit satisfies only that exact per-indexer request and is neither an early-success signal nor a reason to skip the next candidate.

- [ ] **Step 7: Run execution and integration tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~AutomaticSearchQueryExecutionTests|FullyQualifiedName~ManualSearchQueryExecutionTests|FullyQualifiedName~ManualSearchForceRefreshTests|FullyQualifiedName~PerIndexerSearchCacheTests"`

Expected: PASS with every selected query observed.

- [ ] **Step 8: Commit**

```bash
git add src/Services/AutomaticSearchService.cs src/Endpoints/ManualEventSearchEndpoints.cs src/Services/IndexerSearchService.cs src/Services/Interfaces/IIndexerSearchService.cs tests/Sportarr.Api.Tests
git commit -m "fix: execute every selected query over safe shared indexer caching"
```

---

### Task 3.4: Add Opt-In Strong-Match Early Stop with Resume

**Files:**
- Create: `src/Services/AutomaticSearchCandidateEvaluator.cs`
- Create: `src/Sportarr.Data/Models/AutomaticSearchCandidateDecision.cs`
- Modify: `src/Services/AutomaticSearchService.cs`
- Modify: `src/Startup/ServiceCollectionExtensions.cs`
- Modify: `src/Sportarr.Data/Models/Config.cs`
- Modify: `src/Sportarr.Data/Models/Settings.cs`
- Modify: `src/Endpoints/SettingsEndpoints.cs`
- Modify: `frontend/src/pages/settings/IndexersSettings.tsx`
- Create: `tests/Sportarr.Api.Tests/Services/AutomaticSearchEarlyStopTests.cs`
- Modify: `tests/Sportarr.Api.Tests/Services/AutomaticSearchQueryExecutionTests.cs`

**Interfaces:**
- Adds `Config.SearchEarlyStopMatchScore`, default 0. `Config` is `config.xml`-backed, so no migration is required.
- Effective threshold is `league.SearchEarlyStopMatchScoreOverride ?? config.SearchEarlyStopMatchScore`; zero disables.
- Produces `AutomaticSearchCandidateEvaluator.EvaluateAsync(...)`, returning the same selected fully downloadable candidate used by final automatic selection.
- Extracts the existing grab block into one reusable `TryGrabSelectedReleaseAsync(...)` path; both threshold and end-of-plan grabs call it.

- [ ] **Step 1: Write failing setting-resolution tests**

Cover global 0 disabled, league null inheriting global, league 0 disabling a positive global, and a positive league override replacing the global value. Verify the settings endpoint clamps negative values to zero and exposes the global field. Assert `AutoGrabMinMatchScore` is untouched.

- [ ] **Step 2: Write failing execution tests**

Cover these sequences:

```text
empty, empty, eligible-below-threshold, empty, result  => every query runs
high-score-but-rejected, result                        => later query runs
eligible-at-threshold, accepted grab                   => later queries do not run
eligible-at-threshold, failed grab, later result       => search resumes at the next query
cached eligible result, accepted grab                  => same stop behavior as live
manual search with positive global/league values       => every query runs
```

Assert the resumed path does not rerun earlier queries, does not retry the failed release after each later query or at final selection, and retains GUID deduplication.

- [ ] **Step 3: Run focused tests and verify failure**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~AutomaticSearchEarlyStopTests|FullyQualifiedName~AutomaticSearchQueryExecutionTests"`

- [ ] **Step 4: Extract the shared candidate evaluator**

Move the existing automatic-only filtering and selection gates out of the post-query block without changing their order or thresholds. The evaluator must cover match score, release approval, league/event identity, parts/session/event types, blocklist, retention, minimum age, delay profile, churn guard, existing-file/upgrade rules, and download-client eligibility. Return no candidate unless the existing final path would proceed to `AddDownloadAsync`.

Carry the existing hardcoded `const int AutoGrabMinMatchScore = 50` across verbatim. It currently shadows `Config.AutoGrabMinMatchScore`; reconciling the two is out of scope here and would change grab behavior, which the design forbids.

```csharp
public sealed record AutomaticSearchCandidateDecision(
    ReleaseSearchResult? SelectedRelease,
    bool IsDownloadable,
    IReadOnlyList<ReleaseSearchResult> EligibleReleases,
    string? RejectionReason);
```

- [ ] **Step 5: Extract one grab-attempt method**

Move the existing download-client add, queue/history persistence, priority, and notification path into a focused method returning whether the client accepted the release. Preserve its existing compensation and failure recording. The normal end-of-plan path and the threshold path must call the same method, so the incremental decision cannot drift from final selection.

- [ ] **Step 6: Add the provisional stop to the automatic query loop**

After each live or cached query merges results, skip incremental evaluation when the effective threshold is zero. Otherwise evaluate accumulated results; if the selected downloadable candidate meets the threshold, attempt the grab, and finish the search only after the download client accepts it. On failure, record the existing failure state, add the release's strongest identity (torrent hash, GUID, download URL, then title plus indexer) to an attempt-local exclusion set, and continue with the next unexecuted query. The evaluator and final selection must both honor that exclusion set. Never inspect consecutive empty counts.

Log accepted stops at Information with threshold, match score, triggering query position, and skipped-query count; log failed provisional grabs and the resumed query position at Warning. Interactive manual search ignores early-stop settings entirely.

- [ ] **Step 7: Add global UI semantics**

Expose a global "Stop searching after strong match" toggle and a 1–100 numeric threshold under advanced indexer search behavior, default off; turning it on from zero proposes 90. Task 4.1 adds the league-level `Inherit global` / `Disabled` / `Custom score` editor; this task supplies the settings API contract it consumes. Keep `AutoGrabMinMatchScore` separate and unchanged.

- [ ] **Step 8: Run focused tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~AutomaticSearchEarlyStopTests|FullyQualifiedName~AutomaticSearchQueryExecutionTests|FullyQualifiedName~ManualSearchQueryExecutionTests"`

Expected: PASS; default-disabled execution still runs every selected query.

- [ ] **Step 9: Commit**

```bash
git add src/Services/AutomaticSearchCandidateEvaluator.cs src/Sportarr.Data/Models/AutomaticSearchCandidateDecision.cs src/Services/AutomaticSearchService.cs src/Startup/ServiceCollectionExtensions.cs src/Sportarr.Data/Models/Config.cs src/Sportarr.Data/Models/Settings.cs src/Endpoints/SettingsEndpoints.cs frontend/src/pages/settings/IndexersSettings.tsx tests/Sportarr.Api.Tests/Services
git commit -m "feat: add safe strong-match search stopping"
```

---

### Task 3.5: Instrument Search Volume for Rollout

**Files:**
- Modify: `src/Services/AutomaticSearchService.cs`
- Modify: `src/Services/IndexerSearchService.cs`
- Create: `tests/Sportarr.Api.Tests/Services/SearchInstrumentationTests.cs`

**Interfaces:**
- Emits per-search counts for selected candidates, dropped candidates, live per-indexer requests, positive cache hits, and negative cache hits.

The design makes these counts the basis for revisiting `MaxAliasExpansionPerEvent` and `SearchNegativeCacheDuration` after rollout, and for separating the baseline increase from removing the early exit from the increase caused by alias expansion. Without them the rollout plan has no evidence to act on.

- [ ] **Step 1: Write failing instrumentation tests**

Assert a completed automatic search emits one summary record containing selected-candidate count, dropped-candidate count, mandatory versus expansion split, live per-indexer request count, positive cache hits, and negative cache hits. Assert the mandatory and expansion counts are read from the plan, not inferred from query text.

- [ ] **Step 2: Emit the summary**

Log a single structured summary at Information at the end of each automatic search, using the repository's existing logging conventions and message-template style. Counters come from the plan for candidate figures and from the per-indexer cache path for request and hit figures. Do not add a metrics dependency.

- [ ] **Step 3: Run focused tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter FullyQualifiedName~SearchInstrumentationTests`

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Services/AutomaticSearchService.cs src/Services/IndexerSearchService.cs tests/Sportarr.Api.Tests/Services/SearchInstrumentationTests.cs
git commit -m "feat: instrument search candidate and indexer request volume"
```

---

### Task 3.6: Stage 3 Validation

- [ ] **Step 1: Confirm no consecutive-empty logic survives**

Run: `grep -rn "consecutiveEmpty\|MaxConsecutiveEmpty" src`

Expected: no matches.

- [ ] **Step 2: Run backend validation**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true`

Expected: PASS with no new compiler warnings.

- [ ] **Step 3: Run frontend validation**

Run: `cd frontend && npm run lint && npm test -- --run && npm run build`

Expected: PASS.

- [ ] **Step 4: Review the stage diff**

Run: `git status --short && git diff --check && git diff --stat`

**Stage 3 exit criteria:** every mandatory fallback is reached regardless of preceding empty results; cached results cannot cross excluded indexers, different event IDs, category modes, or result limits; failed, unavailable, and rate-limited responses stay uncached at any TTL; early stop is disabled by default, stops only after an accepted fully eligible grab, and resumes after a failed grab; per-search volume counts are emitted.

---

# Stage 4 — Advanced League Search Settings, Preview, and Documentation

**Branch:** `feat/advanced-league-search-settings` from Stage 3.

**Delivers:** the collapsed Advanced Search Settings section, draggable name-form priority, the structured query preview over a representative past event, and user documentation for everything shipped across all four stages.

---

### Task 4.1: Add Advanced League Search Settings and Structured Query Preview

**Files:**
- Modify: `src/Endpoints/LeagueEndpoints.cs`
- Create: `src/Sportarr.Data/Models/Requests/LeagueSearchPreviewRequest.cs`
- Create: `src/Validators/LeagueSearchPreviewRequestValidator.cs`
- Modify: `frontend/src/components/AddLeagueModal.tsx`
- Create: `frontend/src/components/__tests__/AddLeagueModalAliases.test.tsx`
- Create: `tests/Sportarr.Api.Tests/Endpoints/SearchTemplatePreviewTests.cs`

**Interfaces:**
- Preview accepts unsaved `userAliases`, `aliasSearchOrder`, `searchQueryTemplate`, `searchEarlyStopMatchScoreOverride`, and an optional representative event ID.
- Preview returns the chosen event ID/title/date/season plus selected and dropped candidates, excluded forms, provenance, kind, budget use/limit, truncation, and mandatory-invariant status.
- Frontend fetches backend token metadata and falls back to `fallbackSearchTemplateTokens` on failure.

**UI surface note:** `AddLeagueModal` is the edit surface as well as the add surface — it is rendered from `LeagueSearchPage` for adding and from `LeagueDetailPage` for editing an existing league. There is no separate league-edit component; the Advanced Search Settings section must render correctly in both contexts, and preview requires a saved league, so it is only available in the edit context.

- [ ] **Step 1: Write failing preview endpoint tests**

Assert the default preview returns every selected query rather than only the first, and that the custom-template preview uses the same plan. Assert unsaved aliases/order/templates affect output without changing the tracked entity. Verify event selection chooses a past event from the newest season containing any started event, ignores a synced future season, returns a stable requested event, and excludes the current event when "try another" is requested. Assert a fourth valid alias appears as `AliasFormLimit` and budget-dropped candidates include `AliasBudgetExceeded`.

- [ ] **Step 2: Return the real plan from preview**

Convert the `POST /api/leagues/{id}/search-template-preview` body from `JsonElement` to typed `LeagueSearchPreviewRequest` with FluentValidation, reusing the same alias, order, and score bounds as Task 1.2. Replace the default `FirstOrDefault` special case with `BuildEventQueryPlan` for both default and template previews.

Group events by season in application code, discard seasons with no event at or before the current time, choose the newest remaining season, and use `Random.Shared` over the eligible IDs so SQLite and PostgreSQL behave identically. Pass unsaved values through `QueryPlanningOptions`; do not mutate the EF-tracked league. Serialize provenance and reasons from plan data rather than reconstructing them from query strings.

- [ ] **Step 3: Write failing component tests**

Cover the collapsed advanced section, loading/saving aliases and order, a 512-character validation message, source badges, native drag reorder, reset-to-null, untouched-order remaining null, global inheritance/disabled/custom early-stop choices, backend token success and fallback, stable representative event, "Try another event," numbered query order, selected/dropped rendering, and the `AliasFormLimit` warning.

- [ ] **Step 4: Implement league alias state and payload wiring**

Add `userAliases`, nullable `aliasSearchOrder`, and nullable/zero/positive `searchEarlyStopMatchScoreOverride` to the existing frontend types and payloads. Move "Your aliases" and Custom Search Query Template into one collapsed "Advanced Search Settings" section containing, in order: the aliases field, the interleaved draggable search-name priority list, custom templates with the complete token picker, the early-stop override, and the query viewer.

Build the draggable list with the repository's existing native HTML drag pattern from `ProfilesSettings`/`ActivityPage`; do not add a drag-and-drop dependency. All forms including built-ins are draggable; built-in rows are not deletable. Duplicate text appears once with multiple source badges. Set an order only after an actual drag — merely opening or saving the modal must not create an override — and provide "Reset order" that clears the preference back to null.

- [ ] **Step 5: Implement token fetch and structured preview rendering**

Fetch `/api/search/available-tokens` when the modal opens; on any non-success or malformed response use the complete fallback, and treat the failure as a fallback rather than a settings-page error. Keep the returned representative event ID while edits and drags refresh the preview; clear or exclude it only for "Try another event."

Render the actual numbered execution order with provenance, specificity, mandatory/expansion status, budget usage, dropped candidates, and excluded forms. Show a read-only name-form summary after deduplication and the three-alias cap, listing aliases excluded with reason `AliasFormLimit` as not searched. When early stop is enabled, explain that later queries may be skipped only after a fully eligible match is accepted by the download client; do not claim where runtime stopping will occur. Display a prominent warning for drops, exclusions, or a mandatory-invariant violation.

- [ ] **Step 6: Run endpoint and component tests**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true --filter FullyQualifiedName~SearchTemplatePreviewTests`

Run: `cd frontend && npm test -- --run src/components/__tests__/AddLeagueModalAliases.test.tsx src/utils/searchTemplateTokens.test.ts`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Endpoints/LeagueEndpoints.cs src/Sportarr.Data/Models/Requests/LeagueSearchPreviewRequest.cs src/Validators/LeagueSearchPreviewRequestValidator.cs frontend/src/components/AddLeagueModal.tsx frontend/src/components/__tests__/AddLeagueModalAliases.test.tsx tests/Sportarr.Api.Tests/Endpoints/SearchTemplatePreviewTests.cs
git commit -m "feat: add advanced league search planning controls"
```

---

### Task 4.2: Document the New Search Behavior

**Files:**
- Create: `docs/features/search.md`
- Modify: `mkdocs.yml`

`docs/features/` currently has no search page, and this work adds three user-facing settings — league user aliases with drag-ordered priority, the empty-result cache duration, and the global strong-match stop — plus a behavior change in which every selected query now runs.

- [ ] **Step 1: Write the page**

Cover: what league aliases do and how they differ from upstream alternate names; search-name priority and the three-alias cap; the query preview and how to read provenance, mandatory versus expansion, and dropped candidates; the 8-query expansion budget; that every selected query now runs and what that means for indexer load; empty-result cache duration and when to change it; and strong-match early stop, its default-off state, league override semantics, and the fact that it stops only after a grab is accepted.

- [ ] **Step 2: Add the page to navigation**

Add `features/search.md` to the `nav` section of `mkdocs.yml` alongside the existing feature pages.

- [ ] **Step 3: Build the docs strictly**

Run: `mkdocs build --strict`

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add docs/features/search.md mkdocs.yml
git commit -m "docs: document league aliases and search behavior"
```

---

### Task 4.3: Final Validation Against the Design

**Files:**
- Modify only files required to resolve failures introduced by earlier tasks.

- [ ] **Step 1: Verify the design and implementation agree**

Check every acceptance criterion in `docs/superpowers/specs/2026-08-22-league-alias-query-expansion-design.md`, especially: no consecutive-empty code remains, default-disabled search runs all selected expansions, customized order is specificity-first, unknown capabilities bypass cache, failed provisional grabs resume, excluded aliases are visible, and all 19 tokens are insertable.

- [ ] **Step 2: Re-run the migration smoke tests**

Repeat the SQLite temporary-database, legacy `EnsureCreated` startup, and PostgreSQL migration checks from Task 1.5 against the fully stacked branch, confirming no later stage disturbed the schema.

- [ ] **Step 3: Run backend validation**

Run: `dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj -p:SkipFrontendBuild=true`

Expected: PASS with no new compiler warnings.

- [ ] **Step 4: Run frontend validation**

Run: `cd frontend && npm ci`

Run: `cd frontend && npm run lint`

Run: `cd frontend && npm test -- --run`

Run: `cd frontend && npm run build`

Expected: all commands PASS with no new lint warnings.

- [ ] **Step 5: Run strict documentation build**

Run: `mkdocs build --strict`

Expected: PASS.

- [ ] **Step 6: Review the final diff for generated or unrelated files**

Run: `git status --short && git diff --check && git diff --stat upstream/dev...HEAD`

Expected: only scoped source, migration, test, frontend, documentation, design, and plan files; no build output, database, credential, or unrelated workspace files.

**Stage 4 exit criteria:** a user can add a league alias, see it after reload, preview its queries in real execution order, reorder every name-form source, reset to legacy order, find a release named only with that alias, and pass grab and import matching — with the behavior documented.
