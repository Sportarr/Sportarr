# Fightarr API Integration

This document describes the new fight-focused API structure for Fightarr and how it integrates with the private Fightarr-API instance.

## Architecture Overview

Fightarr has been restructured to work specifically for combat sports:

### Data Model Transformation

| Sonarr Concept | Fightarr Concept | Example |
|----------------|------------------|---------|
| Collections/Genre | Genre | MMA, Boxing, Wrestling |
| TV Show | Event | UFC, Bellator, PFL |
| Season | Event Number | "300" (UFC 300), "202406" (June 2024) |
| Episode | Fight Card Section | Early Prelims, Prelims, Main Card |

### Key Components

1. **Fightarr Metadata API** (https://fightarr.com)
   - Central metadata service for all users
   - Next.js API with PostgreSQL/Prisma
   - Provides all fight metadata
   - Endpoints: `/api/events`, `/api/organizations`, `/api/fighters`

2. **Fightarr** (Public Application)
   - Connects directly to https://fightarr.com for metadata
   - Downloads and manages fight videos
   - Clean API endpoints without version numbers
   - No user configuration needed - works out of the box

## New API Endpoints

### Events

- **GET** `/api/events` - List all events
  - Query params: `?upcoming=true`, `?organization=ufc`, `?search=UFC 300`

- **GET** `/api/events/{id}` - Get event details with full fight card

- **POST** `/api/events/sync` - Sync events from Fightarr-API

- **PUT** `/api/events/{id}` - Update event (e.g., set monitored status)

### Fights

- **GET** `/api/fights/event/{eventId}` - Get all fights for an event

- **GET** `/api/fights/card/{eventId}/{cardNumber}` - Get fights for a specific card section
  - `cardNumber`: 1 (Early Prelims), 2 (Prelims), 3 (Main Card)

- **GET** `/api/fights/upcoming` - Get all upcoming fights across all events

### Fighters

- **GET** `/api/fighters/{id}` - Get fighter profile with career record

### Organizations

- **GET** `/api/organizations/{slug}/events` - Get events for a specific organization
  - Example: `/api/organizations/ufc/events`

## Core Models

### FightEvent
Represents a fighting event (UFC 300, Bellator 301, etc.)

```csharp
public class FightEvent : ModelBase
{
    public int FightarrEventId { get; set; }
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; }
    public string Title { get; set; }
    public string EventNumber { get; set; }
    public DateTime EventDate { get; set; }
    public string EventType { get; set; }
    public string Location { get; set; }
    public string Venue { get; set; }
    public string Status { get; set; }
    public bool Monitored { get; set; }
    public List<FightCard> FightCards { get; set; }
}
```

### FightCard
Represents one section of a fight card (replaces Episode concept)

```csharp
public class FightCard : ModelBase
{
    public int FightEventId { get; set; }
    public int CardNumber { get; set; }           // 1, 2, or 3
    public string CardSection { get; set; }       // "Early Prelims", "Prelims", "Main Card"
    public DateTime AirDateUtc { get; set; }
    public bool Monitored { get; set; }
    public List<Fight> Fights { get; set; }
}
```

### Fight
Individual matchup between two fighters

```csharp
public class Fight : ModelBase
{
    public int FightarrFightId { get; set; }
    public int Fighter1Id { get; set; }
    public string Fighter1Name { get; set; }
    public string Fighter1Record { get; set; }
    public int Fighter2Id { get; set; }
    public string Fighter2Name { get; set; }
    public string Fighter2Record { get; set; }
    public string WeightClass { get; set; }
    public bool IsTitleFight { get; set; }
    public bool IsMainEvent { get; set; }
    public int FightOrder { get; set; }
    public string Result { get; set; }
    public string Method { get; set; }
}
```

### Fighter
Fighter profile with career record

```csharp
public class Fighter : ModelBase
{
    public int FightarrFighterId { get; set; }
    public string Name { get; set; }
    public string Nickname { get; set; }
    public string WeightClass { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public string ImageUrl { get; set; }
}
```

## Fight Card Distribution Logic

Fights are automatically distributed into 3 card sections based on `fightOrder`:

1. **Main Card** (Episode 3)
   - Top 5 fights (lowest fightOrder numbers)
   - Most important fights including main event
   - Typically airs last

2. **Prelims** (Episode 2)
   - Next 4-5 fights
   - Mid-tier matchups
   - Typically 2 hours before main card

3. **Early Prelims** (Episode 1)
   - Remaining fights
   - Lower-tier matchups
   - Typically 4 hours before main card
   - Not monitored by default

## Services

### IFightarrMetadataService
Connects to Fightarr metadata API (https://fightarr.com) and fetches metadata

```csharp
public interface IFightarrMetadataService
{
    Task<List<FightEvent>> GetUpcomingEvents(string organizationSlug = null);
    Task<FightEvent> GetEvent(int eventId);
    Task<List<FightEvent>> SearchEvents(string query);
    Task<Fighter> GetFighter(int fighterId);
}
```

**API Base URL**: Hardcoded to `https://fightarr.com`

### IFightEventService
Manages fight events in local database

```csharp
public interface IFightEventService
{
    FightEvent GetEvent(int id);
    List<FightEvent> GetUpcomingEvents();
    List<FightEvent> GetEventsByOrganization(string organizationSlug);
    List<FightEvent> SearchEvents(string query);
    FightEvent AddEvent(FightEvent fightEvent);
    FightEvent UpdateEvent(FightEvent fightEvent);
    Task SyncWithFightarrApi();
}
```

## Database Schema

New tables will be created:

- `FightEvents` - Fighting events
- `FightCards` - Card sections (Early Prelims, Prelims, Main Card)
- `Fights` - Individual matchups
- `Fighters` - Fighter profiles

## Next Steps

1. **Database Migrations** - Create tables for new models
2. **Frontend Updates** - Update React components to display fights
3. **Indexer Integration** - Connect to torrent/NZB indexers for fight releases
4. **Download Client** - Configure download clients for fight videos
5. **Quality Profiles** - Define quality settings for fight videos
6. **Notification System** - Alert when new fights are available

## Example Usage

### Sync Events from Fightarr.com

```bash
curl -X POST http://localhost:1867/api/events/sync
```

### Get Upcoming Events

```bash
curl http://localhost:1867/api/events?upcoming=true
```

### Get UFC Events

```bash
curl http://localhost:1867/api/organizations/ufc/events
```

### Get Event Details

```bash
curl http://localhost:1867/api/events/123
```

## File Structure

```
src/
├── Fightarr.Api/                    # New clean API (no versioning)
│   ├── Events/
│   │   ├── EventController.cs       # /api/events
│   │   └── EventResource.cs         # Response models
│   ├── Fights/
│   │   └── FightController.cs       # /api/fights
│   ├── Fighters/
│   │   ├── FighterController.cs     # /api/fighters
│   │   └── FighterResource.cs       # Response models
│   ├── Organizations/
│   │   └── OrganizationController.cs # /api/organizations
│   └── Fightarr.Api.csproj
│
├── NzbDrone.Core/
│   ├── Fights/
│   │   ├── FightEvent.cs            # Main event model
│   │   ├── FightCard.cs             # Card section model
│   │   ├── Fight.cs                 # Individual fight
│   │   ├── Fighter.cs               # Fighter profile
│   │   ├── FightarrMetadataService.cs  # API client (https://fightarr.com)
│   │   ├── IFightEventRepository.cs
│   │   ├── FightEventRepository.cs
│   │   ├── IFightEventService.cs
│   │   └── FightEventService.cs
```

## Notes

- **No TV metadata dependencies** - All Sonarr TV show metadata has been removed
- **Clean endpoints** - No version numbers in API paths (no `/api/v3/`)
- **Central metadata API** - All users connect to https://fightarr.com for metadata (no configuration needed)
- **Fight-focused terminology** - Events, Cards, Fights instead of Shows, Seasons, Episodes
- **Automatic card distribution** - Client-side logic groups fights into 3 card sections
- **Zero configuration** - Works out of the box with central API, no user setup required

---

**Status**: Core models and API structure created. Not yet committed to git.
