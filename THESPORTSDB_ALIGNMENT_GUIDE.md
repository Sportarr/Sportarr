# Fixing Sportarr App to Work with TheSportsDB via Sportarr-api

## PART 1: THE TRUTH ABOUT THESPORTSDB

### What TheSportsDB Actually Provides

Based on Sportarr-api's implementation (which correctly mirrors TheSportsDB V2), here are the ACTUAL endpoints available:

#### ✅ TV/Filter Endpoints (What Actually Exists)
```
GET /filter/tv/day/{date}              // TV broadcasts on specific date
GET /filter/tv/sport/{sport}           // TV broadcasts for sport (any date)
GET /filter/tv/country/{country}       // TV broadcasts in country
GET /filter/tv/channel/{channel}       // TV broadcasts on channel
GET /lookup/event_tv/{eventId}         // TV info for specific event
```

#### ❌ TV Endpoints That DON'T Exist in TheSportsDB
```
❌ GET /tv/sport/{sport}/{date}        // Sportarr app expects this - DOESN'T EXIST
❌ GET /tv/event/{eventId}             // Sportarr app expects this - DOESN'T EXIST
❌ GET /tv/date/{date}                 // Sportarr app expects this - DOESN'T EXIST
```

**The Problem:** Sportarr app is calling endpoints that TheSportsDB doesn't provide!

## PART 2: WHAT NEEDS TO BE FIXED IN SPORTARR APP

### Critical Issues in Sportarr App

#### Issue #1: Combat Sports API Must Be REMOVED 🚨

**File:** `Sportarr\src\Services\MetadataApiClient.cs`

**Current Code:**
```csharp
private const string BaseUrl = "https://sportarr.net";

// These endpoints DON'T exist in TheSportsDB:
GET /api/search?q=UFC
GET /api/events?upcoming=true
GET /api/fighters/{id}
GET /api/organizations
```

**Problem:**
- This is trying to call a "combat sports API" that doesn't exist
- Sportarr is for ALL SPORTS, not just combat sports
- TheSportsDB handles all sports uniformly

**Solution:**
- 🗑️ DELETE MetadataApiClient.cs entirely
- 🗑️ DELETE all combat sports-specific UI pages
- 🗑️ DELETE all references to fighters, organizations, fight cards

#### Issue #2: Wrong TV Schedule Endpoints 🚨

**File:** `Sportarr\src\Services\TheSportsDBClient.cs` (Lines 147-165)

**Current (WRONG) Code:**
```csharp
public async Task<List<TvScheduleItem>?> GetTVScheduleBySportDateAsync(string sport, string date)
{
    // ❌ This endpoint doesn't exist in TheSportsDB!
    var endpoint = $"tv/sport/{sport}/{date}";
    var response = await _httpClient.GetAsync(endpoint);
    //...
}
```

**Correct Code:**
```csharp
public async Task<List<TvScheduleItem>?> GetTVScheduleBySportDateAsync(string sport, string date)
{
    // ✅ Use the ACTUAL TheSportsDB endpoint structure
    var endpoint = $"filter/tv/day/{date}";  // Get all events for date
    var response = await _httpClient.GetAsync(endpoint);

    // Filter by sport in the application layer
    var allEvents = await response.Content.ReadAsAsync<TvScheduleResponse>();
    return allEvents?.Data?.Schedule?
        .Where(e => e.StrSport.Equals(sport, StringComparison.OrdinalIgnoreCase))
        .ToList();
}
```

#### Issue #3: Database Schema Includes Combat Sports 🚨

**Files:**
- `Sportarr\src\Data\Models\Fight.cs`
- `Sportarr\src\Data\Models\Fighter.cs`
- `Sportarr\src\Data\SportarrDbContext.cs`

**Problem:**
- Database has Fights and Fighters tables
- These are combat sports-specific
- Should use universal Events and Players tables

**Solution:**
- 🗑️ DELETE Fight.cs model
- 🗑️ DELETE Fighter.cs model
- 🗑️ REMOVE DbSet<Fight> Fights from context
- 🗑️ REMOVE DbSet<Fighter> Fighters from context

**Use only:**
- ✅ Events table (universal - works for all sports)
- ✅ Players table (universal - works for all sports/athletes)

#### Issue #4: Combat Sports UI Pages 🚨

**Files:**
- `Sportarr\frontend\src\pages\EventSearchPage.tsx` (combat sports search)
- `Sportarr\frontend\src\pages\FighterDetailsPage.tsx`
- Any pages calling MetadataApiClient

**Solution:**
- 🗑️ DELETE combat sports-specific pages
- ✅ KEEP TheSportsDBEventSearchPage.tsx (universal)
- ✅ KEEP TheSportsDBLeagueSearchPage.tsx (universal)

## PART 4: STEP-BY-STEP FIX PLAN FOR SPORTARR APP

### Step 1: Remove Combat Sports Dependencies 🗑️

**Delete These Files:**
- Sportarr/src/Services/MetadataApiClient.cs
- Sportarr/src/Data/Models/Fight.cs
- Sportarr/src/Data/Models/Fighter.cs
- Sportarr/frontend/src/pages/EventSearchPage.tsx
- Sportarr/frontend/src/pages/FighterDetailsPage.tsx

**Update These Files:**
```csharp
// File: Sportarr/src/Data/SportarrDbContext.cs
// REMOVE:
public DbSet<Fight> Fights { get; set; }
public DbSet<Fighter> Fighters { get; set; }
```

**Remove from Dependency Injection:**
```csharp
// File: Sportarr/src/Program.cs
// REMOVE:
builder.Services.AddHttpClient<MetadataApiClient>();
```

### Step 2: Fix TheSportsDBClient to Use Correct Endpoints ✅

**File:** `Sportarr/src/Services/TheSportsDBClient.cs`

**Replace Method:**
```csharp
// OLD (WRONG):
public async Task<List<TvScheduleItem>?> GetTVScheduleBySportDateAsync(string sport, string date)
{
    var endpoint = $"tv/sport/{sport}/{date}";  // ❌ Doesn't exist
    // ...
}

// NEW (CORRECT):
public async Task<List<TvScheduleItem>?> GetTVScheduleBySportDateAsync(string sport, string date)
{
    try
    {
        // Use TheSportsDB's ACTUAL endpoint
        var endpoint = $"filter/tv/day/{date}";
        var response = await _httpClient.GetAsync(endpoint);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError($"TV schedule API returned {response.StatusCode}");
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<TheSportsDBResponse<TvScheduleData>>();

        // Filter by sport if specified
        if (!string.IsNullOrEmpty(sport) && result?.Data?.Schedule != null)
        {
            result.Data.Schedule = result.Data.Schedule
                .Where(e => e.StrSport?.Equals(sport, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        return result?.Data?.Schedule;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"Error fetching TV schedule for {sport} on {date}");
        return null;
    }
}
```

**Add Method:**
```csharp
// Get TV info for specific event
public async Task<TvBroadcast?> GetEventTVAsync(string eventId)
{
    try
    {
        // Use TheSportsDB's ACTUAL endpoint
        var endpoint = $"lookup/event_tv/{eventId}";
        var response = await _httpClient.GetAsync(endpoint);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<TheSportsDBResponse<TvLookupData>>();
        return result?.Data?.Tv?.FirstOrDefault();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"Error fetching TV info for event {eventId}");
        return null;
    }
}
```

### Step 3: Update UI to Remove Combat Sports Features ✅

**File:** `Sportarr/frontend/src/pages/TheSportsDBEventSearchPage.tsx`

Remove sport filter options for combat sports:
```typescript
// OLD:
const sportOptions = [
  { value: 'Soccer', label: 'Soccer' },
  { value: 'Basketball', label: 'Basketball' },
  { value: 'Fighting', label: 'Fighting' },  // ❌ Remove this
  { value: 'MMA', label: 'MMA' },            // ❌ Remove this
  //...
];

// NEW:
const sportOptions = [
  { value: 'Soccer', label: 'Soccer ⚽' },
  { value: 'Basketball', label: 'Basketball 🏀' },
  { value: 'American Football', label: 'American Football 🏈' },
  { value: 'Baseball', label: 'Baseball ⚾' },
  { value: 'Ice Hockey', label: 'Ice Hockey 🏒' },
  { value: 'Tennis', label: 'Tennis 🎾' },
  { value: 'Golf', label: 'Golf ⛳' },
  { value: 'Motorsport', label: 'Motorsport 🏎️' },
  { value: 'Rugby', label: 'Rugby 🏉' },
  // Add more as needed from TheSportsDB's supported sports
];
```

### Step 4: Update Navigation to Remove Combat Sports Links ✅

**File:** `Sportarr/frontend/src/components/Navigation.tsx` (or similar)

**Remove:**
```tsx
// ❌ Remove these navigation items:
<Link to="/search/events">Search UFC/MMA Events</Link>
<Link to="/fighters">Browse Fighters</Link>
<Link to="/organizations">Organizations</Link>
```

**Keep:**
```tsx
// ✅ Keep universal sports navigation:
<Link to="/search/leagues">Search Leagues</Link>
<Link to="/tv-schedule">TV Schedule</Link>
<Link to="/browse/teams">Browse Teams</Link>
```

### Step 5: Update appsettings.json Configuration ✅

**File:** `Sportarr/src/appsettings.json`

**Remove:**
```json
{
  "MetadataApi": {
    "BaseUrl": "https://sportarr.net/api"  // ❌ Remove this
  }
}
```

**Keep:**
```json
{
  "TheSportsDB": {
    "ApiBaseUrl": "https://sportarr.net/api/v2/json"  // ✅ Keep this
  }
}
```

## PART 7: EXPECTED OUTCOMES

Once All Fixes Applied:

✅ **Single Unified Architecture:**
- One API client (TheSportsDBClient)
- One data source (TheSportsDB via Sportarr-api)
- One database schema (universal Events, Leagues, Teams, Players)

✅ **All Sports Supported Equally:**
- UFC/MMA handled same as NFL/NBA/Soccer
- No special "combat sports" logic
- Universal PVR functionality for all sports

✅ **Correct Endpoint Usage:**
- Sportarr app calls endpoints that actually exist
- No 404 errors
- Proper caching and performance

✅ **Simplified Codebase:**
- Removed ~2000+ lines of combat sports-specific code
- Easier to maintain
- Consistent behavior across all sports

✅ **PVR Functionality Works:**
- TV schedules load correctly
- Automatic search scheduling works
- Downloads triggered at correct times

## SUMMARY

### The Core Problem
Sportarr app was built with assumptions about endpoints that don't exist in TheSportsDB, and included combat sports-specific logic that contradicts the "ALL SPORTS" design.

### The Solution
1. Remove all combat sports-specific code
2. Use TheSportsDB's actual endpoint structure (`/filter/tv/day/{date}` not `/tv/sport/{sport}/{date}`)
3. Filter results in application layer when TheSportsDB doesn't support combined filters
4. Optionally add convenience endpoints in Sportarr-api that combine TheSportsDB calls

### The Result
A clean, universal sports PVR that works identically for UFC, NFL, NBA, Premier League, Formula 1, and any other sport TheSportsDB supports - because they're all treated the same way.

This plan ensures Sportarr app aligns with TheSportsDB's actual capabilities, uses Sportarr-api correctly as a caching proxy, and provides universal sports PVR functionality without special cases.
