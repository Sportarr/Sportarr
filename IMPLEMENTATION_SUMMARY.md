# Fightarr Implementation Summary

**Date**: 2025-10-10
**Status**: Backend Complete, Frontend Planned, Not Committed

## What Has Been Completed

### ✅ 1. Core Data Models

Created fight-specific models to replace Sonarr's TV show structure:

#### **FightEvent** ([FightEvent.cs](src/NzbDrone.Core/Fights/FightEvent.cs))
Replaces Sonarr's `Series` model. Represents a fighting event (UFC 300, Bellator 301, etc.)

Key fields:
- `FightarrEventId` - ID from central Fightarr API
- `OrganizationName` - UFC, Bellator, PFL, etc.
- `EventNumber` - "300" or "202406" (year+month)
- `EventDate` - When the event takes place
- `Location`, `Venue`, `Broadcaster`
- `FightCards` - Collection of 3 card sections

#### **FightCard** ([FightCard.cs](src/NzbDrone.Core/Fights/FightCard.cs))
Replaces Sonarr's `Episode` model. Represents one card section (Early Prelims, Prelims, Main Card)

Key fields:
- `CardNumber` - 1, 2, or 3
- `CardSection` - "Early Prelims", "Prelims", "Main Card"
- `AirDateUtc` - When this card airs
- `Monitored` - Whether to download this card
- `Fights` - Collection of individual fights

#### **Fight** ([Fight.cs](src/NzbDrone.Core/Fights/Fight.cs))
New model. Represents an individual matchup between two fighters

Key fields:
- `Fighter1Id`, `Fighter1Name`, `Fighter1Record`
- `Fighter2Id`, `Fighter2Name`, `Fighter2Record`
- `WeightClass` - Heavyweight, Welterweight, etc.
- `IsTitleFight`, `IsMainEvent`
- `FightOrder` - Position on card (1=main event)
- `Result`, `Method`, `Round`, `Time` - For completed fights

#### **Fighter** ([Fighter.cs](src/NzbDrone.Core/Fights/Fighter.cs))
New model. Represents a fighter profile

Key fields:
- `FightarrFighterId` - ID from central API
- `Name`, `Nickname`
- `Wins`, `Losses`, `Draws`, `NoContests`
- `WeightClass`, `Nationality`
- `Height`, `Reach`, `ImageUrl`

### ✅ 2. API Controllers (Clean, No Versioning)

Created new `Fightarr.Api` project with clean endpoints (no `/api/v3/`):

#### **EventController** ([EventController.cs](src/Fightarr.Api/Events/EventController.cs))
```
GET  /api/events                 - List all events
GET  /api/events?upcoming=true   - Get upcoming events
GET  /api/events?organization=ufc - Filter by organization
GET  /api/events/{id}            - Get event details
POST /api/events/sync            - Sync from Fightarr.com
PUT  /api/events/{id}            - Update event (monitoring, etc.)
```

#### **FightController** ([FightController.cs](src/Fightarr.Api/Fights/FightController.cs))
```
GET /api/fights/event/{eventId}              - All fights for an event
GET /api/fights/card/{eventId}/{cardNumber}  - Fights for specific card (1-3)
GET /api/fights/upcoming                     - All upcoming fights
```

#### **FighterController** ([FighterController.cs](src/Fightarr.Api/Fighters/FighterController.cs))
```
GET /api/fighters/{id}  - Get fighter profile
```

#### **OrganizationController** ([OrganizationController.cs](src/Fightarr.Api/Organizations/OrganizationController.cs))
```
GET /api/organizations/{slug}/events  - Events for organization (e.g., /ufc/events)
```

### ✅ 3. Metadata Service

#### **FightarrMetadataService** ([FightarrMetadataService.cs](src/NzbDrone.Core/Fights/FightarrMetadataService.cs))

Connects to central Fightarr API at **`https://fightarr.com`** (hardcoded, no user configuration needed)

Methods:
- `GetUpcomingEvents()` - Fetch upcoming events from API
- `GetEvent(id)` - Get single event with full fight card
- `SearchEvents(query)` - Search for events
- `GetFighter(id)` - Get fighter profile

**Fight Card Distribution Logic:**
Automatically groups fights into 3 card sections based on `fightOrder`:
- **Main Card** (Episode 3): Top 5 fights
- **Prelims** (Episode 2): Next 4-5 fights
- **Early Prelims** (Episode 1): Remaining fights

### ✅ 4. Service Layer

#### **FightEventService** ([FightEventService.cs](src/NzbDrone.Core/Fights/FightEventService.cs))
Business logic for managing events in local database:
- `GetEvent()`, `GetAllEvents()`, `GetUpcomingEvents()`
- `SearchEvents()`, `GetEventsByOrganization()`
- `AddEvent()`, `UpdateEvent()`, `DeleteEvent()`
- `SyncWithFightarrApi()` - Syncs events from central API

#### **FightEventRepository** ([FightEventRepository.cs](src/NzbDrone.Core/Fights/FightEventRepository.cs))
Database operations for events:
- `FindByFightarrEventId()` - Find by API ID
- `GetUpcomingEvents()` - Query upcoming events
- `GetEventsByOrganization()` - Filter by organization
- `SearchEvents()` - Full-text search
- `GetEventsByDateRange()` - Date range queries

### ✅ 5. Resource Mappers

Clean JSON serialization for API responses:

#### **EventResource** ([EventResource.cs](src/Fightarr.Api/Events/EventResource.cs))
- Maps `FightEvent` → JSON response
- Includes nested `FightCard` and `Fight` data
- Extension method: `ToResource()`

#### **FighterResource** ([FighterResource.cs](src/Fightarr.Api/Fighters/FighterResource.cs))
- Maps `Fighter` → JSON response
- Computed `Record` property (e.g., "20-5-0")

### ✅ 6. Database Migrations

#### **Migration 224** ([224_add_fight_tables.cs](src/NzbDrone.Core/Datastore/Migration/224_add_fight_tables.cs))

Creates 4 new tables:
- `FightEvents` - Fighting events
- `FightCards` - Card sections (Early Prelims, Prelims, Main Card)
- `Fights` - Individual matchups
- `Fighters` - Fighter profiles

Includes indexes for performance:
- `IX_FightEvents_FightarrEventId`
- `IX_FightEvents_EventDate`
- `IX_FightEvents_Status`
- `IX_FightCards_FightEventId`
- `IX_Fights_FightCardId`
- `IX_Fighters_FightarrFighterId`

### ✅ 7. Dependency Injection

**Auto-registered** via DryIoc's `AutoAddServices`:
- All interfaces (`IFightEventService`, `IFightarrMetadataService`, etc.)
- All implementations (`FightEventService`, `FightarrMetadataService`, etc.)
- All repositories (`IFightEventRepository` → `FightEventRepository`)

No manual registration needed - follows the established Sonarr pattern.

### ✅ 8. Documentation

#### **FIGHTARR_API_INTEGRATION.md** ([FIGHTARR_API_INTEGRATION.md](FIGHTARR_API_INTEGRATION.md))
- Architecture overview
- API endpoint documentation
- Data model documentation
- Example usage
- File structure

#### **FRONTEND_MIGRATION_PLAN.md** ([FRONTEND_MIGRATION_PLAN.md](FRONTEND_MIGRATION_PLAN.md))
- Complete frontend migration strategy
- Directory structure changes
- Terminology mapping
- Component-by-component plan
- Redux store changes
- 36-52 hour estimate

#### **IMPLEMENTATION_SUMMARY.md** (this file)
- Complete summary of work completed
- What's left to do
- Git status (not committed)

## What's Left To Do

### 🔲 1. Frontend Migration (Planned)

See [FRONTEND_MIGRATION_PLAN.md](FRONTEND_MIGRATION_PLAN.md) for complete details.

**High-level tasks:**
- Rename `Series/` → `Events/`
- Rename `Episode/` → `FightCard/`
- Create new `Fights/` components
- Update Redux store and actions
- Update routing (`/series` → `/events`)
- Update API calls to new endpoints
- Update all UI text (Series → Events, Episodes → Fights)

**Estimated**: 36-52 hours

### 🔲 2. Remove Sonarr TV Dependencies

**Files/code to remove or update:**
- TVDB integration code
- TVMaze integration code
- Scene numbering logic (TV-specific)
- Season pack handling (replace with event-based)
- Episode file parsing (update for fight naming)
- Metadata providers (TVDB, TVMaze, Trakt for TV)

**Keep/Update:**
- Download clients (still needed for torrents/NZB)
- Indexers (still needed, update search logic)
- Quality profiles (still needed)
- Naming configuration (update for fights)
- Custom formats (may need fight-specific formats)

### 🔲 3. Fight-Specific Features (Future)

**Indexer Integration:**
- Update search queries for fight releases
- Parse fight titles (UFC 300 Main Card, etc.)
- Handle different release formats

**File Naming:**
- Create fight-specific naming tokens
- Example: `{Organization} {EventNumber} - {CardSection}`
- Output: `UFC 300 - Main Card.mkv`

**Quality Profiles:**
- Potentially fight-specific quality settings
- Different profiles for different card sections

**Notifications:**
- Alert when new events are added
- Alert when fights are downloaded
- Customizable per organization

## Architecture Highlights

### Central Metadata API

All Fightarr instances connect to **https://fightarr.com** for metadata:
- No user configuration required
- Works out of the box
- Single source of truth for fight data
- Update domain in `FightarrMetadataService.cs:27` when ready

### Data Flow

```
Fightarr.com API
      ↓
FightarrMetadataService
      ↓
FightEventService
      ↓
FightEventRepository
      ↓
SQLite Database
      ↓
API Controllers
      ↓
React Frontend
```

### Clean API Design

No version numbers in Fightarr API paths:
- ✅ `/api/events`
- ✅ `/api/fights`
- ✅ `/api/fighters`
- ❌ `/api/v3/events` (Sonarr style)

### Fight Card Distribution

Automatic grouping into 3 episodes:
1. **Early Prelims** (Episode 1) - Remaining fights, 4 hours before
2. **Prelims** (Episode 2) - Next 4-5 fights, 2 hours before
3. **Main Card** (Episode 3) - Top 5 fights, main event time

## File Inventory

### New Files Created (Not Committed)

**Core Models:**
- `src/NzbDrone.Core/Fights/FightEvent.cs`
- `src/NzbDrone.Core/Fights/FightCard.cs`
- `src/NzbDrone.Core/Fights/Fight.cs`
- `src/NzbDrone.Core/Fights/Fighter.cs`

**Services:**
- `src/NzbDrone.Core/Fights/FightarrMetadataService.cs`
- `src/NzbDrone.Core/Fights/IFightEventService.cs`
- `src/NzbDrone.Core/Fights/FightEventService.cs`
- `src/NzbDrone.Core/Fights/IFightEventRepository.cs`
- `src/NzbDrone.Core/Fights/FightEventRepository.cs`

**API Controllers:**
- `src/Fightarr.Api/Fightarr.Api.csproj`
- `src/Fightarr.Api/Events/EventController.cs`
- `src/Fightarr.Api/Events/EventResource.cs`
- `src/Fightarr.Api/Fights/FightController.cs`
- `src/Fightarr.Api/Fighters/FighterController.cs`
- `src/Fightarr.Api/Fighters/FighterResource.cs`
- `src/Fightarr.Api/Organizations/OrganizationController.cs`

**Database:**
- `src/NzbDrone.Core/Datastore/Migration/224_add_fight_tables.cs`

**Documentation:**
- `FIGHTARR_API_INTEGRATION.md`
- `FRONTEND_MIGRATION_PLAN.md`
- `IMPLEMENTATION_SUMMARY.md`

### Modified Files

None yet - all work is new files only.

## Git Status

**Branch**: main
**Status**: Clean (no commits)
**Untracked files**: All files listed above

**User instruction**: "dont commit any changes just yet, but i have a few ideas of changes i want to do pieces at a time then committ all at once."

## Next Steps

When ready to continue:

1. **Review all created files** - Ensure architecture matches vision
2. **Test API integration** - Once Fightarr.com domain is ready
3. **Start frontend migration** - Follow FRONTEND_MIGRATION_PLAN.md
4. **Remove TV dependencies** - Clean up Sonarr-specific code
5. **Test end-to-end** - Full workflow from API to UI
6. **Commit all changes** - Single comprehensive commit

## Technology Stack

- **Backend**: C# / .NET 8
- **Frontend**: React + TypeScript
- **Database**: SQLite (via FluentMigrator)
- **DI Container**: DryIoc
- **HTTP Client**: NzbDrone.Common.Http
- **Serialization**: System.Text.Json
- **Logging**: NLog

## API Base URL

Currently hardcoded to: **`https://fightarr.com`**

Location: `src/NzbDrone.Core/Fights/FightarrMetadataService.cs:27`

```csharp
private const string API_BASE_URL = "https://fightarr.com";
```

Update this constant when your domain is ready.

---

**Summary**: Backend implementation complete and ready for testing. Frontend migration planned. No commits made per user request.
