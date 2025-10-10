# Frontend Migration Plan: TV Shows → Fights

This document outlines the frontend changes needed to transform Sonarr's TV show interface into Fightarr's fight-focused interface.

## Directory Structure Changes Needed

### Current Structure (Sonarr)
```
frontend/src/
├── Series/          # TV Show pages
├── Episode/         # Episode components
├── Season/          # Season components
├── AddSeries/       # Add new TV show
├── Calendar/        # Episode calendar
└── ...
```

### Proposed Structure (Fightarr)
```
frontend/src/
├── Events/          # Fighting events (renamed from Series)
├── FightCard/       # Fight card components (renamed from Episode)
├── Fights/          # Individual fight components (new)
├── AddEvent/        # Add new event (renamed from AddSeries)
├── Calendar/        # Event calendar (update logic)
└── ...
```

## Terminology Mapping

| UI Element | Current (Sonarr) | New (Fightarr) |
|------------|------------------|----------------|
| Main list page | "Series" | "Events" |
| Detail page | "Series Details" | "Event Details" |
| Sub-items | "Episodes" | "Fight Cards" (Early Prelims/Prelims/Main Card) |
| Individual items | "Episode" | "Fight" |
| Add button | "Add Series" | "Add Event" |
| Search | "Search for Series" | "Search for Events" |
| Filter | "Season 1", "Season 2" | "Early Prelims", "Prelims", "Main Card" |
| Grid/List view | Show poster | Event poster |
| Episode title | "S01E01 - Episode Title" | "Main Card - Fighter1 vs Fighter2" |

## Key Files to Update

### 1. Routing (`frontend/src/App/`)

**App.js** or **AppRoutes.tsx** - Update routes:
- `/series` → `/events`
- `/series/:id` → `/events/:id`
- `/add/new` → `/events/add`
- `/calendar` → `/calendar` (keep, update logic)

### 2. Store/State Management (`frontend/src/Store/Actions/`)

**seriesActions.js** → **eventActions.js**
- Update API endpoints from `/api/v3/series` to `/api/events`
- Rename action types: `FETCH_SERIES` → `FETCH_EVENTS`
- Update selectors and reducers

**episodeActions.js** → **fightCardActions.js**
- Update API endpoints from `/api/v3/episode` to `/api/fights/card`
- Update data structure for fight cards

**Create new: fightActions.js**
- Actions for individual fights
- Fetch fights by event or card

### 3. Main Pages (`frontend/src/Series/` → `frontend/src/Events/`)

**SeriesIndex/** → **EventIndex/**
- Event grid/list view
- Update columns: Organization, Event Number, Date, Location
- Filter by organization (UFC, Bellator, etc.)
- Sort by event date

**SeriesDetails/** → **EventDetails/**
- Event poster and details
- Organization logo
- Event number, date, location, venue
- List of fight cards (3 cards per event)
- Replace season tabs with card tabs

**SeriesEditor/** → **EventEditor/**
- Edit event monitoring status
- Quality profile assignment
- Download settings

### 4. Episode Components (`frontend/src/Episode/` → `frontend/src/FightCard/`)

**EpisodeSummary.js** → **FightCardSummary.js**
- Display card section (Early Prelims, Prelims, Main Card)
- Show list of fights in the card
- Fighter names and records

**EpisodeRow.js** → **FightRow.js**
- Display fight: "Fighter1 vs Fighter2"
- Weight class, title fight indicator
- Fight order/position

### 5. New Components (`frontend/src/Fights/`)

Create new components for individual fights:

**FightDetails.js**
- Fighter 1 name, record, image
- Fighter 2 name, record, image
- Weight class, rounds
- Title fight indicator
- Result, method, round, time (if completed)

**FighterCard.js**
- Fighter profile widget
- Record: 20-5-0 (W-L-D)
- Nickname
- Weight class

### 6. Calendar (`frontend/src/Calendar/`)

**CalendarPage.js**
- Update to show events instead of episodes
- Group by event date
- Show fight cards for each event
- Color code by organization

**CalendarEvent.js**
- Display event info on calendar
- Organization name
- Number of fights
- Click to expand fight cards

### 7. Add/Import (`frontend/src/AddSeries/` → `frontend/src/AddEvent/`)

**AddSeriesSearchConnector.js** → **AddEventSearchConnector.js**
- Search for events from Fightarr API
- Display upcoming events
- Filter by organization

**AddSeriesItem.js** → **AddEventItem.js**
- Event search result card
- Event poster, title, date, location
- "Add Event" button

### 8. Search/Filter Components

**SeriesIndexFilterModal** → **EventIndexFilterModal**
- Filter by organization (UFC, Bellator, PFL, etc.)
- Filter by event type (PPV, Fight Night, etc.)
- Filter by date range
- Filter by monitored status

**SeriesIndexSortMenu** → **EventIndexSortMenu**
- Sort by event date
- Sort by organization
- Sort by title
- Sort by number of fights

### 9. Type Definitions (`frontend/src/typings/`)

**Series.ts** → **Event.ts**
```typescript
interface Event {
  id: number;
  fightarrEventId: number;
  organizationId: number;
  organizationName: string;
  title: string;
  eventNumber: string;
  eventDate: Date;
  location: string;
  venue: string;
  status: string;
  monitored: boolean;
  posterUrl: string;
  fightCards: FightCard[];
}
```

**Episode.ts** → **FightCard.ts**
```typescript
interface FightCard {
  id: number;
  fightEventId: number;
  cardNumber: number; // 1, 2, or 3
  cardSection: string; // "Early Prelims", "Prelims", "Main Card"
  airDateUtc: Date;
  monitored: boolean;
  fights: Fight[];
}
```

**New: Fight.ts**
```typescript
interface Fight {
  id: number;
  fightarrFightId: number;
  fighter1Id: number;
  fighter1Name: string;
  fighter1Record: string;
  fighter2Id: number;
  fighter2Name: string;
  fighter2Record: string;
  weightClass: string;
  isTitleFight: boolean;
  isMainEvent: boolean;
  fightOrder: number;
  result?: string;
  method?: string;
}
```

**New: Fighter.ts**
```typescript
interface Fighter {
  id: number;
  fightarrFighterId: number;
  name: string;
  nickname: string;
  weightClass: string;
  wins: number;
  losses: number;
  draws: number;
  imageUrl: string;
}
```

### 10. API Client (`frontend/src/Utilities/createAjaxRequest.js`)

Update API base paths:
- Ensure requests go to new endpoints (`/api/events`, `/api/fights`, `/api/fighters`)
- Remove references to TVDB, TVMaze, etc.

### 11. UI Text Updates

**strings.json** or inline text:
- "Add Series" → "Add Event"
- "Series Editor" → "Event Editor"
- "Episodes" → "Fights"
- "Season" → "Card"
- "Series" → "Events"
- "TV" → "Fight"

### 12. Icons and Images

- Replace TV show icons with fight/MMA icons
- Update empty state illustrations
- Add organization logos (UFC, Bellator, etc.)

## API Endpoint Mapping

### Current Sonarr Endpoints
- `GET /api/v3/series` - List all series
- `GET /api/v3/series/{id}` - Get series details
- `GET /api/v3/episode` - List episodes
- `POST /api/v3/series` - Add new series

### New Fightarr Endpoints
- `GET /api/events` - List all events
- `GET /api/events/{id}` - Get event details
- `GET /api/fights/event/{id}` - List fights for event
- `GET /api/fights/card/{eventId}/{cardNumber}` - List fights for specific card
- `POST /api/events` - Add new event
- `POST /api/events/sync` - Sync from Fightarr API

## Redux Store Structure Changes

### Current State Shape
```javascript
{
  series: {
    items: [...],
    isLoading: false
  },
  episodes: {
    items: [...],
    isLoading: false
  }
}
```

### New State Shape
```javascript
{
  events: {
    items: [...],
    isLoading: false
  },
  fightCards: {
    items: [...],
    isLoading: false
  },
  fights: {
    items: [...],
    isLoading: false
  },
  fighters: {
    items: [...],
    isLoading: false
  }
}
```

## Implementation Strategy

### Phase 1: Backend First (Completed ✅)
- ✅ Created fight models (FightEvent, FightCard, Fight, Fighter)
- ✅ Created API controllers (`/api/events`, `/api/fights`, `/api/fighters`)
- ✅ Created metadata service to connect to Fightarr API
- ✅ Created database migrations

### Phase 2: Store/State (Next)
1. Rename `seriesActions.js` → `eventActions.js`
2. Update all action creators to use new API endpoints
3. Update reducers for event state
4. Create `fightCardActions.js` and `fightActions.js`
5. Update selectors to work with events instead of series

### Phase 3: Core Components
1. Rename `Series/` → `Events/`
2. Update `EventIndex` to display events in grid/list
3. Update `EventDetails` to show fight cards
4. Create `FightCard/` components
5. Create `Fights/` components

### Phase 4: Secondary Features
1. Update calendar to show events
2. Update add event flow
3. Update search and filters
4. Update settings pages

### Phase 5: Polish
1. Update all UI text and labels
2. Add fight-specific icons
3. Add organization logos
4. Update empty states
5. Update help text and tooltips

## Testing Checklist

- [ ] Event list page displays correctly
- [ ] Event details page shows fight cards
- [ ] Can expand/collapse fight cards
- [ ] Can monitor/unmonitor events
- [ ] Can add new events
- [ ] Calendar shows events correctly
- [ ] Search works for events
- [ ] Filters work (organization, date, etc.)
- [ ] Sorting works correctly
- [ ] Fight card sections display correctly
- [ ] Individual fights show fighter info
- [ ] API sync works
- [ ] No console errors
- [ ] No broken links/routes

## Migration Notes

- Keep the existing Sonarr code structure where possible
- Use find/replace carefully for terminology changes
- Test thoroughly - many interconnected components
- Frontend rebuild required after changes
- May need to clear browser cache/localStorage

## Estimated Effort

- **Store/Actions**: ~8-12 hours
- **Core Components**: ~16-20 hours
- **Secondary Features**: ~8-12 hours
- **Polish/Testing**: ~4-8 hours
- **Total**: ~36-52 hours

## Next Steps

1. Start with Phase 2 (Store/State) - foundation for everything
2. Create new action files alongside old ones
3. Test API integration before updating UI
4. Update components incrementally
5. Keep old components until new ones are tested

---

**Status**: Backend completed. Frontend migration pending.
**Last Updated**: 2025-10-10
