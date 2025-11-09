# Fightarr → Sportarr Transformation Progress

## Overview
Transforming Fightarr from a combat sports-only PVR to **Sportarr**, a universal sports PVR supporting all sports through TheSportsDB V2 API.

## Architecture

```
Sportarr App → Fightarr-API (fightarr.net) → TheSportsDB V2 API
```

**Fightarr-API** acts as a caching middleware layer (similar to Sonarr's services.sonarr.tv), providing:
- Rate limit protection (TheSportsDB allows 100 req/min)
- Response caching with TTL management
- API key security
- Background sync jobs for livescores and TV schedules

## Completed Work

### ✅ Phase 1: Database Schema (COMPLETED)

Created new models for universal sports support:

#### 1. **League Model** ([src/Models/League.cs](src/Models/League.cs))
Replaces the concept of "Organization" for universal sports:
- `ExternalId` - TheSportsDB league ID
- `Name` - League name (e.g., "NFL", "Premier League", "UFC")
- `Sport` - Sport type (e.g., "Soccer", "Fighting", "Basketball")
- `Country` - League country/region
- `Monitored` - Whether league is monitored
- `QualityProfileId` - Default quality profile
- `LogoUrl`, `BannerUrl`, `PosterUrl` - League imagery
- `Website`, `FormedYear` - League metadata

#### 2. **Team Model** ([src/Models/Team.cs](src/Models/Team.cs))
Represents sports teams (for team sports):
- `ExternalId` - TheSportsDB team ID
- `Name` - Team name (e.g., "Los Angeles Lakers")
- `ShortName` - Abbreviation (e.g., "LAL")
- `LeagueId` - Foreign key to League
- `Sport` - Sport type
- `Stadium`, `StadiumLocation`, `StadiumCapacity` - Venue info
- `BadgeUrl`, `JerseyUrl`, `BannerUrl` - Team imagery
- `PrimaryColor`, `SecondaryColor` - Team colors
- `FormedYear`, `Website` - Team metadata

#### 3. **Player Model** ([src/Models/Player.cs](src/Models/Player.cs))
Represents athletes/players:
- `ExternalId` - TheSportsDB player ID
- `Name`, `FirstName`, `LastName`, `Nickname`
- `Sport` - Sport type
- `TeamId` - Current team (nullable if free agent/retired)
- `Position` - Player position (e.g., "Forward", "Quarterback", "Fighter")
- `Nationality`, `BirthDate`, `Birthplace`
- `Height`, `Weight`, `Number` - Physical stats
- `PhotoUrl`, `ActionPhotoUrl`, `BannerUrl` - Player imagery
- `Dominance` - Preferred foot/stance
- Combat sports specific: `WeightClass`, `Record`, `Stance`, `Reach`

#### 4. **Event Model Updates** ([src/Models/Event.cs](src/Models/Event.cs))
Enhanced to support both combat sports AND team sports:

**New Fields:**
- `ExternalId` - TheSportsDB event ID
- `Sport` - Sport type (defaults to "Fighting" for backwards compatibility)
- `LeagueId` - Foreign key to League
- `HomeTeamId` / `AwayTeamId` - Team references (for team sports)
- `Season` - Season identifier (e.g., "2024", "2024-25")
- `Round` - Round/week number (e.g., "Week 10", "Quarterfinals")
- `Broadcast` - TV broadcast information (network, channel, streaming)
- `HomeScore` / `AwayScore` - Final scores (for completed team sports events)
- `Status` - Event status (Scheduled, Live, Completed, Postponed, Cancelled)

**Backwards Compatibility:**
- `Organization` field kept but now optional (use `LeagueId` instead)
- `Fights` collection maintained for combat sports
- `FightCards` collection maintained (similar to Sonarr's episodes)

#### 5. **Database Configuration** ([src/Data/FightarrDbContext.cs](src/Data/FightarrDbContext.cs))

Added DbSets:
- `DbSet<League> Leagues`
- `DbSet<Team> Teams`
- `DbSet<Player> Players`

Added Entity Configurations:
- League: Indexed on `ExternalId`, `Sport`, `Name+Sport` composite
- Team: Indexed on `ExternalId`, `Sport`, `LeagueId`
- Player: Indexed on `ExternalId`, `Sport`, `TeamId`
- Event: Added indexes for `Sport`, `LeagueId`, `ExternalId`, `Status`

Foreign Key Relationships:
- Event → League (SetNull on delete)
- Event → HomeTeam (SetNull on delete)
- Event → AwayTeam (SetNull on delete)
- Team → League (SetNull on delete)
- Player → Team (SetNull on delete)

### ✅ Phase 2: EF Core Migration (COMPLETED)

Created migration: `AddUniversalSportsSupport`

**Migration includes:**
- New tables: Leagues, Teams, Players
- Event table alterations: new columns for Sport, LeagueId, HomeTeamId, AwayTeamId, Season, Round, Broadcast, Status, HomeScore, AwayScore, ExternalId
- All indexes and foreign key constraints
- Organization field made nullable (backwards compatibility)

**To apply migration:**
```bash
cd src
dotnet ef database update
```

### ✅ Phase 3: TheSportsDB Client Service (COMPLETED)

Created **TheSportsDBClient** ([src/Services/TheSportsDBClient.cs](src/Services/TheSportsDBClient.cs))

This service consumes Fightarr-API endpoints and provides:

#### Search Endpoints
- `SearchLeagueAsync(query)` - Search leagues by name
- `SearchTeamAsync(query)` - Search teams by name
- `SearchPlayerAsync(query)` - Search players by name
- `SearchEventAsync(query)` - Search events/games by name

#### Lookup Endpoints
- `LookupLeagueAsync(id)` - Get league by TheSportsDB ID
- `LookupTeamAsync(id)` - Get team by TheSportsDB ID
- `LookupPlayerAsync(id)` - Get player by TheSportsDB ID
- `LookupEventAsync(id)` - Get event by TheSportsDB ID

#### Schedule Endpoints
- `GetTeamNext10Async(teamId)` - Next 10 events for a team
- `GetTeamPrev10Async(teamId)` - Previous 10 events for a team
- `GetLeagueSeasonAsync(leagueId, season)` - All events for a league season

#### TV Schedule Endpoints (CRITICAL)
- `GetEventTVScheduleAsync(eventId)` - TV broadcast info for event
- `GetTVScheduleByDateAsync(date)` - All TV broadcasts for a date
- `GetTVScheduleBySportDateAsync(sport, date)` - TV broadcasts for sport on date

**Why TV Schedule is Critical:**
Just like Sonarr monitors TV air times to trigger automatic searches, Sportarr needs to know when games/events will be broadcast to time automatic searches correctly. The TV schedule tells us:
- When to expect releases (broadcast time + buffer)
- Which network/channel (for release naming)
- Which streaming services have rights (for indexer filtering)

#### Livescore Endpoints
- `GetLivescoreBySportAsync(sport)` - Live scores for a sport
- `GetLivescoreByLeagueAsync(leagueId)` - Live scores for a league

#### All Data Endpoints
- `GetAllLeaguesAsync()` - All available leagues
- `GetAllSportsAsync()` - All available sports
- `GetAllCountriesAsync()` - All countries

**Configuration:**
- Registered as HttpClient with Polly retry policy (3 retries, exponential backoff)
- Base URL configured in `appsettings.json`: `https://fightarr.net/api/v2/json`
- Automatic retry on transient HTTP errors

**Supporting Types:**
- `TheSportsDBResponse<T>` - Generic response wrapper
- `TVSchedule` - TV broadcast schedule information
- `Sport` - Sport definition
- `Country` - Country definition

### ✅ Phase 5: Backend API Endpoints (COMPLETED)

Created comprehensive REST API endpoints for leagues and teams in [src/Program.cs](src/Program.cs):

#### League Endpoints (Lines 3854-4009)
- ✅ `GET /api/leagues` - List all leagues (with optional sport filter)
- ✅ `GET /api/leagues/{id}` - Get league details with event stats
- ✅ `GET /api/leagues/search/{query}` - Search TheSportsDB for leagues
- ✅ `POST /api/leagues` - Add league to library
- ✅ `PUT /api/leagues/{id}` - Update league settings
- ✅ `DELETE /api/leagues/{id}` - Remove league (with event validation)

#### Team Endpoints (Lines 4011-4104)
- ✅ `GET /api/teams` - List all teams (with optional league/sport filters)
- ✅ `GET /api/teams/{id}` - Get team details with event stats
- ✅ `GET /api/teams/search/{query}` - Search TheSportsDB for teams

All endpoints integrate with TheSportsDBClient service to fetch data from Fightarr-API.

**Build Status**: ✅ Backend compiles successfully (9 warnings, 0 errors)

### ✅ Phase 6: Frontend TypeScript Types (COMPLETED)

Updated [frontend/src/types/index.ts](frontend/src/types/index.ts) with universal sports support:

#### Event Interface Enhanced (Lines 1-33)
Added new sports fields while maintaining backwards compatibility:
- `externalId` - TheSportsDB event ID
- `sport` - Sport type (e.g., "Soccer", "Fighting", "Basketball")
- `leagueId` / `league` - League relationship
- `homeTeamId` / `homeTeam` - Home team (for team sports)
- `awayTeamId` / `awayTeam` - Away team (for team sports)
- `season` - Season identifier (e.g., "2024", "2024-25")
- `round` - Round/week number (e.g., "Week 10", "Quarterfinals")
- `broadcast` - TV broadcast information
- `homeScore` / `awayScore` - Final scores (for completed games)
- `status` - Event status (Scheduled, Live, Completed, Postponed, Cancelled)

#### New Interfaces Added
- **League** (Lines 141-161) - Universal league/competition entity
  - Replaces Organization concept for all sports
  - Includes logos, banners, sport classification
  - Event count statistics

- **Team** (Lines 163-193) - Team entities for team sports
  - League relationship
  - Stadium information
  - Team colors, badges, jerseys
  - Home/away event statistics

- **Player** (Lines 195-226) - Athletes/players
  - Sport-agnostic position field
  - Team relationship
  - Physical stats (height, weight, number)
  - Combat sports specific: weight class, record, stance, reach

**Backwards Compatibility**: Organization interface preserved for combat sports (Lines 120-138)

**Build Status**: ✅ Frontend builds successfully

## Pending Work

### 🔄 Phase 7: Frontend UI Components (NEXT)

Transform UI for universal sports:
1. **Leagues Page** (replaces/supplements Organizations page)
   - Grid view of monitored leagues
   - Sport filter (Soccer, Fighting, Basketball, etc.)
   - League cards with logo, sport badge, event count
   - Add league modal with league search

2. **Events/Calendar Page**
   - Filter by sport and league
   - Show team matchups for team sports
   - Show fighter matchups for combat sports
   - Display TV broadcast information
   - Color code by sport

3. **Add Event Modal**
   - Sport selector dropdown
   - League search (from TheSportsDB)
   - Team search for team sports
   - Event search for existing events
   - TV schedule display

4. **Settings**
   - Fightarr-API URL configuration
   - Sport preferences
   - Default quality profiles per sport

### 🔍 Phase 7: Search Integration

Adapt indexer search for sports:
1. **Query Building**
   - Combat sports: "UFC 293" or "Bellator 300"
   - Team sports: "Lakers Celtics 2024-01-15" or "Premier League Matchday 20"
   - Include league, teams, date in search queries

2. **Release Parsing**
   - Detect sport type from release name
   - Extract team names, league, date
   - Handle various naming conventions

3. **TV Schedule Integration**
   - Schedule automatic searches based on broadcast time
   - Search X minutes after broadcast starts
   - Re-search for quality upgrades

### 📅 Phase 8: TV Schedule & Calendar

Implement TV schedule integration:
1. **Background Sync Job**
   - Fetch TV schedules daily from Fightarr-API
   - Update Event.Broadcast field
   - Trigger automatic searches at broadcast time

2. **Calendar View**
   - Show upcoming events with TV info
   - Color code by sport
   - Filter by league, sport, team
   - Display broadcast network/channel

3. **Automatic Search Timing**
   - Use TV schedule to determine search time
   - Search immediately after broadcast starts
   - Re-search 30min, 1hr, 2hr, 4hr, 8hr, 24hr later for upgrades

### ✅ Phase 9: Testing & Validation

1. **Database Migration Testing**
   - Verify existing combat sports data still works
   - Test new League, Team, Player tables
   - Verify foreign key relationships

2. **API Testing**
   - Test TheSportsDB client endpoints
   - Verify caching through Fightarr-API
   - Test rate limit handling

3. **Frontend Testing**
   - Test league browsing and adding
   - Test team sport event adding
   - Test combat sport backwards compatibility
   - Test calendar view with TV schedules

4. **Integration Testing**
   - End-to-end: Add league → Browse events → Add event → Automatic search → Download → Import
   - Test quality upgrades based on TV schedule
   - Test multiple sports simultaneously

## Key Design Decisions

### 1. **Backwards Compatibility**
- `Organization` field kept in Event model (made optional)
- `Sport` defaults to "Fighting" if not specified
- Existing combat sports data continues to work
- `Fights` and `FightCards` collections maintained

### 2. **League vs Organization**
- League is the new primary entity (like Sonarr's Series)
- Organization kept for legacy combat sports support
- Organizations can be migrated to Leagues over time

### 3. **Team Sports Support**
- Events can reference HomeTeam and AwayTeam
- Scores tracked separately (HomeScore, AwayScore)
- Season and Round tracking for league context

### 4. **Sport-Agnostic Fields**
- Player model has `Position` field that adapts to sport
  - Combat sports: "Fighter"
  - Soccer: "Forward", "Midfielder", "Goalkeeper"
  - Basketball: "Point Guard", "Center"
  - Football: "Quarterback", "Running Back"
- `Dominance` field adapts to sport
  - Combat sports: "Orthodox", "Southpaw"
  - Soccer: "Right-footed", "Left-footed"
  - Others: "Right", "Left", "Both"

### 5. **TV Schedule Priority**
- TV schedule is CRITICAL (not optional)
- Enables automatic search timing (like Sonarr's air time monitoring)
- Provides broadcast network info for release naming
- Synced regularly via Fightarr-API background jobs

## Data Flow

### Adding a Team Sport Event (e.g., Lakers vs Celtics game)

1. **User Action**: User searches for "Lakers vs Celtics" in Add Event modal
2. **Search**: Frontend calls `/api/search/event/Lakers vs Celtics`
3. **Backend**: TheSportsDBClient calls Fightarr-API → `https://fightarr.net/api/v2/json/search/event/Lakers%20vs%20Celtics`
4. **Fightarr-API**:
   - Checks cache (2 hours TTL for searches)
   - If cache miss: Calls TheSportsDB V2 API
   - Caches response and returns
5. **Backend**: Converts TheSportsDB event data to Sportarr Event model
6. **Frontend**: Displays search results with team logos, TV info, date
7. **User Action**: User clicks "Add to Library"
8. **Backend**:
   - Creates Event record with LeagueId, HomeTeamId, AwayTeamId
   - Fetches TV schedule for event
   - Schedules automatic search at broadcast time

### Automatic Search Triggered by TV Schedule

1. **Background Job**: Checks TV schedule every hour
2. **Detects**: Game starting in 5 minutes (based on Event.Broadcast data)
3. **Triggers**: Automatic search via IndexerSearchService
4. **Search Query**: "Lakers Celtics 2024-01-15" + league + quality filters
5. **Indexers**: Return releases matching the query
6. **ReleaseEvaluator**: Scores releases based on quality, format, indexer priority
7. **Decision**: Best release sent to download client
8. **Monitor**: EnhancedDownloadMonitorService tracks download progress
9. **Import**: On completion, FileImportService imports to library
10. **Upgrade Check**: Reschedules searches for quality upgrades (30min, 1hr, 2hr, 4hr, 8hr, 24hr)

## Configuration

### appsettings.json
```json
{
  "TheSportsDB": {
    "ApiBaseUrl": "https://fightarr.net/api/v2/json"
  }
}
```

### Environment Variables (Docker)
```yaml
environment:
  - THESPORTSDB__APIBASEURL=https://fightarr.net/api/v2/json
```

## Next Steps

1. ✅ Apply database migration: `dotnet ef database update`
2. 🔄 Create backend API endpoints for leagues, teams, events
3. 📋 Update frontend TypeScript types
4. 🎨 Build Leagues page UI
5. 🎨 Update Add Event modal for sports
6. 🔍 Adapt search for sports naming conventions
7. 📅 Implement TV schedule background sync
8. ✅ Test end-to-end with multiple sports

## Timeline Estimate

- **Week 1**: Backend API endpoints for leagues/teams/events (Phase 4)
- **Week 2**: Frontend types and Leagues page UI (Phases 5-6)
- **Week 3**: Search integration and TV schedule sync (Phases 7-8)
- **Week 4**: Testing, bug fixes, polish (Phase 9)

**Total: ~4 weeks for complete Sportarr transformation**

## Testing Checklist

- [ ] Existing combat sports events still display correctly
- [ ] Can add a new UFC event (combat sports)
- [ ] Can browse and add leagues (Soccer, Basketball, etc.)
- [ ] Can search for teams (Lakers, Real Madrid, etc.)
- [ ] Can add team sport events (Lakers vs Celtics)
- [ ] TV schedule displays on event details
- [ ] Automatic search triggers at broadcast time
- [ ] Quality upgrades work correctly
- [ ] Calendar view shows all sports
- [ ] Sport filters work (Fighting, Soccer, Basketball, etc.)
- [ ] Download/import works for all sports
- [ ] Metadata files generate correctly for all sports

## Migration Path for Existing Users

1. **Before Update**: Backup database
2. **Update**: Pull new version, apply migrations
3. **Backwards Compatibility**: Existing combat sports events continue to work
4. **Optional Migration**: Organizations can be converted to Leagues
5. **Gradual Adoption**: Users can add new sports gradually while keeping combat sports

---

**Generated**: 2025-11-09
**Status**: In Progress - Phase 3 Complete, Phase 4 Next
