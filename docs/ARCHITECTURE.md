# Sportarr Architecture

Architectural conventions for Sportarr. Read this before adding new code.

## Project layout

```
src/
├── Program.cs                    # Top-level: startup orchestration only (~960 lines, target: stay under 1500)
├── Sportarr.csproj
├── Authentication/               # Auth handlers (ApiKey, Basic, Forms)
├── Constants/                    # Magic-string-free constants
│   ├── AuthenticationConstants.cs
│   └── ConfigurationKeys.cs
├── Converters/                   # JSON converters
├── Data/                         # SportarrDbContext + DbSet definitions
├── Endpoints/                    # All HTTP endpoint groups (one file per domain)
├── Exceptions/                   # SportarrException hierarchy
├── Health/                       # Health check implementations
├── Helpers/                      # Pure-function helpers (no DI)
├── Middleware/                   # Custom ASP.NET middleware
├── Migrations/                   # EF Core migrations
├── Models/                       # Domain entities + DTOs (intentionally flat for now)
│   ├── Requests/                 # Inline request records (small ones)
│   └── Metadata/                 # Metadata-related models
├── Services/                     # Business logic services (DI-registered)
├── Startup/                      # Startup orchestration helpers
│   ├── ServiceCollectionExtensions.cs    # DI registration, split by concern
│   ├── DatabaseInitializer.cs            # EF migrations + manual schema safety nets
│   └── AgentInstaller.cs                 # Plex/Jellyfin/Emby/Kodi agent file deployment
├── Validation/                   # FluentValidation custom validators (legacy)
├── Validators/                   # Request DTO validators (FluentValidation)
└── Windows/                      # Windows-specific (system tray, console hide)
```

## API versioning

See [docs/API_VERSIONING.md](API_VERSIONING.md) for the full explanation.

| Prefix | Purpose | Where it lives |
|---|---|---|
| `/api/*` | Sportarr's native API (consumed by web frontend) | All non-prefixed `*Endpoints.cs` files |
| `/api/v1/*` | Sonarr v1 compatibility shim (Prowlarr) | `Endpoints/V1ProwlarrEndpoints.cs` |
| `/api/v3/*` | Sonarr v3 compatibility shim (Decypharr/Maintainerr/ArrControl) | `Endpoints/Sonarr*.cs` |

**Never add new routes under `/api/v1/` or `/api/v3/`** — those prefixes exist solely to mirror Sonarr's contract for external consumers. Native Sportarr features go under `/api/*`.

## Adding a new endpoint

1. Pick the right file in `Endpoints/`. Endpoints group by domain, not by HTTP verb. (e.g. all `/api/iptv/*` lives in `IptvEndpoints.cs`.)
2. If no existing file fits, create a new one following the pattern below.
3. Inside the extension method, register the endpoint with `app.MapGet/MapPost/etc.`
4. If the request has a body, write a validator (see "Validation").
5. Use `ILogger<TEndpointClass>` — never `ILogger<Program>`.
6. Inject services through DI. Never reference `app.Services` from inside an endpoint handler.

### Endpoint file template

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Validators;   // only if applying WithRequestValidation

namespace Sportarr.Api.Endpoints;

public static class WidgetEndpoints
{
    public static IEndpointRouteBuilder MapWidgetEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/widgets", async (SportarrDbContext db, ILogger<WidgetEndpoints> logger) =>
        {
            // ...
            return Results.Ok(items);
        });

        app.MapPost("/api/widgets", async (CreateWidgetRequest req, SportarrDbContext db) =>
        {
            // ...
            return Results.Created($"/api/widgets/{widget.Id}", widget);
        }).WithRequestValidation<CreateWidgetRequest>();

        return app;
    }
}
```

Then wire it up once in `Program.cs`:

```csharp
app.MapWidgetEndpoints();
```

## Adding a new service

1. File goes in `src/Services/` named `WidgetService.cs`.
2. Namespace: `Sportarr.Api.Services`.
3. Register in the appropriate extension method in `Startup/ServiceCollectionExtensions.cs`:
   - Core services (config, auth, sessions, health, backup, notifications) → `AddSportarrCoreServices`
   - Indexer/search/quality logic → `AddSportarrIndexing`
   - File parsing/naming/importing → `AddSportarrFileServices`
   - IPTV/DVR/EPG → `AddSportarrIptv`
   - Hosted (background) services → `AddSportarrBackgroundServices`
4. Inject into endpoints by type — DI handles the resolution.
5. Use `ILogger<WidgetService>` for logging.

## Adding a new HttpClient

Don't `new HttpClient()` anywhere. All HttpClient usage goes through `IHttpClientFactory`. Configure in `ServiceCollectionExtensions.AddSportarrHttpClients` with appropriate `PooledConnectionLifetime`, retry policy, and timeout. Look at the existing named clients (`DownloadClient`, `IptvClient`, `IndexerClient`, `EpgClient`, `StreamProxy`, `TrashGuides`) for examples.

## Validation

All POST/PUT endpoints with a request body should validate using FluentValidation.

1. Write a validator in `src/Validators/` named `XxxRequestValidator.cs`:

```csharp
using FluentValidation;
using Sportarr.Api.Models;

namespace Sportarr.Api.Validators;

public class AddWidgetRequestValidator : AbstractValidator<AddWidgetRequest>
{
    public AddWidgetRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Url).NotEmpty().Must(u => Uri.TryCreate(u, UriKind.Absolute, out _));
    }
}
```

2. Register the validator (one-liner): `AddValidatorsFromAssembly` in `AddSportarrValidation()` already auto-discovers it.

3. Apply to the endpoint: `.WithRequestValidation<AddWidgetRequest>()` chained after `MapPost`.

## Logging conventions

- **Logger type**: `ILogger<TheClassYouAreIn>`. Never `ILogger<Program>`.
- **Log prefixes**: bracketed domain prefix in the message: `logger.LogInformation("[IPTV] Adding source: {Name}", name)`. This makes log filtering effective.
- **Levels**:
  - `Error` — unexpected exception with stack trace; user-impacting failure
  - `Warning` — recoverable problem, retry succeeded, fallback engaged
  - `Information` — meaningful state changes (created, scheduled, deleted, synced)
  - `Debug` — verbose diagnostic detail; default level filters this out
  - `Trace` — never use unless you're debugging a specific issue

## Constants instead of magic strings

- HTTP headers, cookie names, query param names → `Constants/AuthenticationConstants.cs`
- Config keys, env var names → `Constants/ConfigurationKeys.cs`

If you find yourself hardcoding `"X-Api-Key"` or `"Sportarr__DataPath"`, add it to constants instead.

## Database

- All DB access through `SportarrDbContext` (injected). Do not inject `IDbContextFactory<>` unless you need parallel/concurrent operations (e.g. background services with Task.WhenAll over indexers).
- Migrations live in `src/Migrations/`. Add new migrations with `dotnet ef migrations add <Name>`.
- For schema safety nets that protect legacy `EnsureCreated()` databases, add to `Startup/DatabaseInitializer.cs` following the existing `pragma_table_info` pattern.
- Entity classes (Domain) and request/response DTOs both live in `src/Models/`. Domain entities have `[Key]`, navigation properties, and matching `DbSet<T>` in `SportarrDbContext`. Request/response DTOs are POCOs.

## Middleware

Custom middleware lives in `src/Middleware/`. Order matters — see `Program.cs` for the pipeline. The current order:

1. CORS (`UseCors`)
2. Exception handling (`UseExceptionHandling`) — catches everything below it
3. Request logging (`UseRequestLogging`) — logs every inbound request
4. Version header (`UseVersionHeader`) — adds Sportarr version response header
5. Authentication (`UseAuthentication` then `UseAuthorization` then `UseDynamicAuthentication`)

If you add middleware, place it correctly relative to these — usually right after `UseRequestLogging` if it needs to see all requests.

## Helpers vs Services

- **Helpers** (`src/Helpers/`): pure static functions, no state, no DI. Single-purpose. Examples: `HlsRewriter`, `PartRelevanceHelper`, `TennisLeagueHelper`. Call them statically: `Helpers.X.Method(args)`.
- **Services** (`src/Services/`): stateful, DI-registered, often async, often touch the database or HTTP. Inject and call methods.

If a helper needs `ILogger`, accept it as a parameter. Don't promote a helper to a service unless it grows to need DI for other reasons.

## What stays in Program.cs

Program.cs holds *only* startup orchestration. As of this writing it contains:
- Command-line argument parsing
- Data path resolution (Windows ACL + recovery logic)
- Serilog configuration
- Configuration overlays (`builder.Configuration[...] = ...`)
- One-line DI registration calls (via `AddSportarr*` extension methods)
- One-line endpoint registration calls (`app.MapXxxEndpoints()`)
- Middleware pipeline (`app.UseXxx()`)
- The `app.Run()` block + try/catch + Windows tray loop

**Do not add new endpoints, services, validators, or helpers to Program.cs.** Add them to their dedicated folders.

## Commit message conventions

Two-part format: a clean conventional-commit subject line **plus a thorough body** explaining the change. The subject line keeps the release-notes bot happy; the body tells the next developer (or future-you) what changed and why.

### Subject line

Conventional commits, lowercase, no scopes. Allowed types seen in history: `fix:`, `feat:`, `chore:`, `refactor:`, `ci:`, `docs:`, `perf:`. Keep under ~80 characters.

```
fix: plugin download buttons fail with unauthorized error
feat: add flexible nba patterns
refactor: extract iptv endpoints out of program.cs
ci: add weekly trivy scan
```

Never use `fix(scope):` syntax. Never capitalize after the colon. This applies to merge commits too — never default to GitHub's "Merge pull request #X into Y" format.

### Body

Below the subject (separated by a blank line), write **one or two paragraphs in prose** describing:
- **What** the commit actually changes (more concretely than the subject line conveys).
- **Why** it changes — the bug being fixed, the user-visible improvement, the architectural motivation, or the constraint that forced the change.

For commits that touch multiple files or add multiple features, end the body with a `Changes:` bullet list naming each meaningful piece. Bullets should be short — one line each — and ordered by importance, not by file path.

Skip the body only for genuinely trivial commits (typo fixes, single-line lint cleanups). Anything where a future reviewer would reasonably ask "why did this change?" deserves a body.

### Example: feature commit with multiple pieces

```
feat: add external download detection for transmission, rtorrent, and nzbget

Adds GetAllDownloadsByCategoryAsync to the three download client adapters
that previously had no support for it. Without this method, the
EnhancedDownloadMonitorService background poll skipped these clients
entirely, meaning torrents/NZBs added directly to the client (via its web
UI, another arr app, or scripts) were never discovered by Sportarr and
required manual library import.

Closes the parity gap with QBittorrent, Deluge, and SABnzbd, which already
implemented this. UTorrent remains unimplemented as a separate gap.

Changes:
- TransmissionClient: filter by torrent download_dir matching config.Directory
- RTorrentClient: filter by d.custom1 label, falling back to directory match
- NzbGetClient: filter by Category on both queue and history
- DownloadClientService: route the three new client types in the switch
```

### Example: fix commit with a clear root cause

```
fix: match download ids case-insensitively so sportarr grabs aren't flagged as external

The three lookup HashSets in DetectExternalDownloadsAsync — knownDownloadIds,
pendingDownloadIds, grabbedDownloadIds — were created with the default
StringComparer.Ordinal (case-sensitive). qBittorrent and SABnzbd can return
identifiers in different cases between the initial add response and later
listing/poll responses, so the HashSet missed the match and Sportarr-grabbed
downloads re-appeared in the Activity page as "external" PendingImports.

Hash-based fallback was already case-insensitive, so this only manifested
when TorrentInfoHash was null (pre-Decypharr setups, usenet clients).

Changes:
- knownDownloadIds, pendingDownloadIds, grabbedDownloadIds now use
  StringComparer.OrdinalIgnoreCase
- knownHashes (already OrdinalIgnoreCase) left unchanged
```

### Example: trivial commit (no body needed)

```
fix: typo in dvr settings tooltip
```

### What stays out of commit bodies

- Don't reference issue tracker IDs in the body unless you actually need them — the PR description is the place for that.
- Don't list filenames as "Modified files:". Bullets should describe behavior changes, not paths.
- Don't write "Co-Authored-By: Claude" or any AI attribution.

## Branch & release flow

- All changes go to `dev` unless explicitly stated otherwise.
- Releases: squash-merge `dev` → `main` with a curated changelog as the commit message (that becomes the GitHub release notes).
- After release: reset `dev` to match `main`, then cherry-pick any new dev-only commits.
- Always `cd /projects/sportarr` before any git command. The parent `/projects` is a separate repo.

## Testing expectations

- Unit tests in `tests/Sportarr.Api.Tests/`. Currently covers parsers (file naming, quality, sport detection) and release evaluation.
- New service logic that has branching/state should have at least one test.
- Integration test infrastructure is not yet in place — adding it is tracked as future work.

## When in doubt

Match existing patterns. The codebase post-refactor is consistent — find a similar concern (e.g. another endpoint group, another validator, another service) and copy its shape.
