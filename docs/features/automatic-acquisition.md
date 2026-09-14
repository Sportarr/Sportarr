# Automatic Acquisition Windows

## Problem

Monitoring and file retention do not express "keep my files, but stop unattended
downloads and upgrades once an event is old." Release publication-age filters use
the wrong timestamp for this intent. Retention can delete files and unmonitor
events, so it cannot safely stand in for an acquisition window.

## Behavior

Each league has independent `AutomaticMissingEnabled` and
`AutomaticUpgradesEnabled` switches plus `AutomaticMissingMaxAgeDays` and
`AutomaticUpgradeMaxAgeDays`. Both switches default to true; both ages default to
0 (unlimited), preserving existing behavior. Ages are non-negative 32-bit integers.
Age is measured from event UTC start time, not release publication or import time.
Exactly at the cutoff is eligible. Future events pass so DVR can schedule ahead;
existing source, timing, monitoring and quality restrictions remain authoritative.

The add/edit dialog offers apply-once presets: Unlimited (0/0), Recent 14 Days
(14/14), Archive (0/14), and Manual Only (both switches off). Values remain
editable independently; 14 is not hardcoded. No shared preset inheritance exists.
Archive does not enable backlog searching or select historical seasons.

Applying policy does not delete files, change monitoring or season selection, or
block existing transfers/imports. **Retention remains separate and can delete
files.** Presets never silently alter retention settings.

## Implementation

`AutomaticAcquisitionPolicy` owns eligibility and refusal reasons. Early filters
avoid excluded RSS/search/backlog candidates; fresh no-tracking database reads
recheck current settings and part-file ownership before transfer. Search candidates
are filtered before ranking so a forbidden pack/upgrade cannot hide a valid single
release. Missing versus upgrade is part-aware; a full file covers every part.

RSS/push, automatic queued searches/regrabs, backlog, delayed holds and newly
automatic DVR/catchup work use the same policy. Refused holds/scheduled automatic
recordings are cancelled with a reason, not blocklisted. Manual event searches,
grabs and manually scheduled recordings bypass only this policy. Batch APIs that
already mark work automatic keep their existing classification.

Packs cannot verify every contained event: restrictive policies conservatively
refuse automatic packs. Nullable held-release `IsPack` preserves that distinction;
legacy unknown holds are treated as unverified packs. DVR `IsAutomatic` preserves
origin through fallback rotation; legacy scheduled recordings default false because
their original source cannot be inferred safely. They are not retroactively cancelled.

Single/bulk API updates preserve omitted fields; create defaults enabled/unlimited.
Both database providers have additive migrations. SQLite ensures the historical
pending table exists before adding `IsPack`, since some historical migrations lack
discovery metadata and startup repair creates that table only after migrations.
No old migration is made newly discoverable. Generated designers/snapshots account
for 8,612 changed lines; they are full-model metadata, not thousands of new fields.

## Validation and Limits

The independent upstream branch passed 40 policy/DVR/migration-discovery tests.
Coverage includes cutoff/adjacent tick, independent windows, defaults, changed
settings across contexts, parts/packs, real RSS refusal, catchup manual validation,
reaper cancellation and SQLite SQL with/without the historical pending table.

Supporting combined-fork validation (not native CI for this standalone branch):
[1,932 tests and native ARM64 image smoke passed](https://github.com/jasjeetsuri/Sportarr/actions/runs/34069019918).
Both EF provider model checks passed. Real SQLite startup and browser save/reload
passed; desktop/mobile controls were checked at 1280x900 and 390x844. Local full
suites retain known macOS filesystem/timing failures and failures in unchanged
frontend fixtures; no thresholds were changed to conceal them.

This remains a design-review draft: PostgreSQL migration execution, historical
user-database upgrades/rollback, bulk atomicity matrix and live client/FFmpeg/provider
success-path/soak coverage are incomplete. Fresh guards are not atomic with external
I/O. There is no measured CPU-savings claim for this policy. Impact preview, bulk
UI, CI/publishing changes and resource pacing are outside this PR.

```sh
dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj \
  -c Release -p:SkipFrontendBuild=true \
  --filter 'FullyQualifiedName~AutomaticAcquisitionPolicyTests|FullyQualifiedName~Dvr|FullyQualifiedName~MigrationDiscoveryTests'
npm --prefix frontend test -- --run src/components/__tests__/AcquisitionPolicyFields.test.tsx
```

Back up configuration/database before upgrading. Restoring the matching backup
with the previous application is the rollback plan; schema downgrade is unverified.