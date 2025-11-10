# Changelog

All notable changes to Sportarr will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Version scheme: `v1.X.Y` where max version is `v1.999.999`

---

## [v4.0.249] - 2025-11-10

### 🌍 Universal Sports Support

This is a **major release** that transforms Sportarr from a combat sports-focused PVR to a **universal sports PVR** supporting all major sports worldwide.

### Added
- **Universal Sport Detection**: Automatic categorization for 11 major sports:
  - ⚔️ Fighting (UFC, Bellator, ONE FC, PFL, Boxing, MMA)
  - ⚽ Soccer/Football (Premier League, La Liga, Champions League, etc.)
  - 🏀 Basketball (NBA, FIBA, EuroLeague, ACB, BBL)
  - 🏈 American Football (NFL, NCAA, CFL, Super Bowl)
  - ⚾ Baseball (MLB, World Series, NPB, KBO)
  - 🏒 Ice Hockey (NHL, Stanley Cup, KHL, SHL)
  - 🎾 Tennis (Grand Slams, ATP, WTA)
  - ⛳ Golf (PGA, Masters, Ryder Cup)
  - 🏎️ Motorsport (Formula 1, NASCAR, IndyCar, MotoGP)
  - 🏉 Rugby (Six Nations, Rugby World Cup, NRL)
  - 🏏 Cricket (IPL, BBL, Test Matches, T20, ODI)
- **Comprehensive Test Suite**: 109 unit tests covering all sport detection scenarios
- **Migration Guide**: Complete documentation for upgrading from combat sports-only version
- **Supported Sports Documentation**: README section showcasing all 11 sports with popular leagues

### Changed
- **Database Schema**: Automatic migration with universal terminology:
  - `OrganizationFilter` → `LeagueFilter` (Import Lists)
  - `FightCardNfo` → `EventCardNfo` (Metadata Providers)
  - `FighterImages` → `PlayerImages` (Metadata Providers)
  - `OrganizationLogos` → `LeagueLogos` (Metadata Providers)
- **Sport Detection Logic**: Intelligent keyword-based categorization system
  - Checks both league names and event titles for sport indicators
  - Priority ordering to prevent keyword conflicts (e.g., "Formula One" vs "ONE Championship")
  - Context-aware matching (e.g., "football" → American Football or Soccer based on context)
  - Defaults to "Fighting" for backward compatibility
- **API Endpoints**: Updated to use new universal field names while maintaining compatibility

### Fixed
- **Keyword Conflicts**: Resolved detection issues between similar sport keywords:
  - "ONE Championship" (Fighting) vs "Formula One" (Motorsport)
  - "BBL Game" (Basketball - Bundesliga) vs "BBL Match" (Cricket - Big Bash League)
  - "Football" (Soccer vs American Football context)
  - "World Cup" (Soccer vs Rugby vs Cricket context)
  - "CF" in team names (Club de Fútbol) detection
- **Test Compatibility**: Updated FileNamingServiceTests for new League object model
- **Download Client Features**: Implemented missing pause/resume functionality for Transmission and rTorrent

### Migration
- **Automatic Database Migration**: Runs on first startup after upgrade
- **Zero Data Loss**: All existing events, import lists, and settings preserved
- **Backward Compatible**: Existing UFC/MMA setups continue working without changes
- **See MIGRATION_GUIDE.md** for detailed upgrade instructions

### Testing
- **214 Total Tests**: All passing
  - 109 new sport detection tests
  - 105 existing functional tests
- **Edge Cases**: Comprehensive coverage of keyword ambiguities and conflicts
- **Integration**: Verified with both ImportListService and LibraryImportService

### Technical Details
- Sport detection implemented in `DeriveEventSport()` method
- Keyword-based categorization with case-insensitive matching
- Priority-ordered sport checks to handle overlapping keywords
- Context-aware pattern matching (e.g., "bbl game" vs "bbl match")
- Reflection-based testing for private method validation

### Upgrade Notes
⚠️ **Backup Recommended**: While automatic migration is safe, backing up your database before upgrading is recommended

✅ **Zero Downtime**: Migration completes in seconds during startup

📚 **Read MIGRATION_GUIDE.md** for complete upgrade instructions and troubleshooting

---

## [v1.0.004] - 2025-10-13

### Fixed
- **UFC Search Results**: Fixed search endpoint to properly extract events array from Sportarr API response
- API response structure parsing - now correctly handles `{events: [...]}` wrapper object
- Search results now populate correctly when searching for organizations like "UFC"

### Changed
- Updated search endpoint to use `JsonDocument.Parse()` for flexible JSON parsing
- Improved error handling and logging for API integration

### Technical Details
- Sportarr-API returns `{"events": [...]}` instead of direct array
- Backend now extracts the `events` property before returning to frontend
- Maintains backward compatibility with empty array fallback

---

## [v1.0.003] - 2025-10-13

### Added
- **Release Automation**: GitHub Actions workflow for automatic release creation
- **Manual Release Script**: `create-release.sh` for manual release management
- **Comprehensive CHANGELOG**: Full version history documentation

### Changed
- Updated frontend build with logo-64.png and latest UI changes
- Improved build process and deployment workflow

### Technical Details
- Release workflow triggers on Version.cs changes
- Extracts changelog content for release notes
- Prevents duplicate releases with version checking

---

## [v1.0.002] - 2025-10-13

### Fixed
- **Search API Endpoint**: Corrected search API call from `/search/events` to `/api/search/events`
- **Array Validation**: Added `Array.isArray()` validation to prevent `map()` errors
- **Black Screen Bug**: Fixed component crash when API returns non-array data
- Search now properly validates response format before rendering

### Technical Details
- Backend proxies search requests to `https://sportarr.net`
- Added fallback to empty array if API returns unexpected format
- Error: "Uncaught TypeError: s.map is not a function" - RESOLVED

---

## [v1.0.001] - 2025-10-13

### Added
- **Version Management System**: Centralized version tracking in `src/Version.cs`
  - App Version: User-facing version (increments with updates)
  - API Version: Stable API compatibility version (1.0.0)
- **Official Sportarr Logo**: Replaced placeholder "F" with 64x64 PNG logo from Logo/ folder
- **Hardcoded API URL**: All instances now connect to `https://sportarr.net` by default

### Changed
- Updated sidebar branding with official logo
- Removed FIGHTARR_API_URL environment variable requirement
- Console logs now show both App and API versions

---

## [v1.0.0] - 2025-10-13

### Initial Release

#### Added
- **Complete UI Redesign**: Sonarr-style left sidebar navigation
- **Red & Black Theme**: Professional dark theme with red accents
- **Event Library**: Grid view of sports events with poster images
- **Add Event Page**: Search-as-you-type functionality
  - Real-time search with 500ms debounce
  - Event cards with fight details
  - Monitor toggle and quality profile selection
  - Integration with Sportarr-API
- **Navigation Structure**:
  - Events: Main library view
  - Events Menu: Add New, Library Import, Mass Editor
  - Calendar: Upcoming events
  - Activity: Download monitoring
  - Settings: 10 configuration sections
  - System: 6 management pages
- **Backend API**:
  - SQLite database with Entity Framework Core
  - Event and league management
  - Quality profiles (HD 1080p, Any)
  - Tag system
  - Search proxy to Sportarr-API
- **Docker Support**:
  - PUID/PGID support for Unraid
  - Automatic permission management
  - Multi-arch builds (amd64, arm64)
- **API Endpoints**:
  - `/api/events` - Event CRUD operations
  - `/api/search/events` - Search sports events across all leagues
  - `/api/system/status` - System information
  - `/api/qualityprofile` - Quality profiles
  - `/api/tag` - Tag management

#### Technical Stack
- Backend: ASP.NET Core 8.0 Minimal API
- Frontend: React 19 + Vite + TailwindCSS
- Database: SQLite with Entity Framework Core 9.0
- Icons: Heroicons
- State Management: TanStack React Query
- Docker: Multi-stage builds with gosu

#### Features
- Real-time event search
- Poster image display
- Event scheduling and management
- Quality profile selection
- Monitoring status tracking
- Responsive grid layouts
- Loading and error states
- Empty state messaging

---

## Release Notes

### How to Update
1. Pull the latest Docker image: `docker pull sportarr/sportarr:latest`
2. Restart your container
3. Check version in UI sidebar

### API Compatibility
- API Version remains at **v1.0.0** for stability
- All v1.0.x releases are backward compatible

### Support
- Issues: https://github.com/Sportarr/Sportarr/issues
- Documentation: https://docs.sportarr.net (coming soon)
- API: https://sportarr.net
