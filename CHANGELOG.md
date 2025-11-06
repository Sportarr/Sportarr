# Changelog

All notable changes to Fightarr will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Version scheme: `v1.X.Y` where max version is `v1.999.999`

---

## [v1.0.004] - 2025-10-13

### Fixed
- **UFC Search Results**: Fixed search endpoint to properly extract events array from Fightarr API response
- API response structure parsing - now correctly handles `{events: [...]}` wrapper object
- Search results now populate correctly when searching for organizations like "UFC"

### Changed
- Updated search endpoint to use `JsonDocument.Parse()` for flexible JSON parsing
- Improved error handling and logging for API integration

### Technical Details
- Fightarr-API returns `{"events": [...]}` instead of direct array
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
- Backend proxies search requests to `https://fightarr.net`
- Added fallback to empty array if API returns unexpected format
- Error: "Uncaught TypeError: s.map is not a function" - RESOLVED

---

## [v1.0.001] - 2025-10-13

### Added
- **Version Management System**: Centralized version tracking in `src/Version.cs`
  - App Version: User-facing version (increments with updates)
  - API Version: Stable API compatibility version (1.0.0)
- **Official Fightarr Logo**: Replaced placeholder "F" with 64x64 PNG logo from Logo/ folder
- **Hardcoded API URL**: All instances now connect to `https://fightarr.net` by default

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
- **Event Library**: Grid view of MMA events with poster images
- **Add Event Page**: Search-as-you-type functionality
  - Real-time search with 500ms debounce
  - Event cards with fight details
  - Monitor toggle and quality profile selection
  - Integration with Fightarr-API
- **Navigation Structure**:
  - Events: Main library view
  - Events Menu: Add New, Library Import, Mass Editor
  - Calendar: Upcoming events
  - Activity: Download monitoring
  - Settings: 10 configuration sections
  - System: 6 management pages
- **Backend API**:
  - SQLite database with Entity Framework Core
  - Event and fight card management
  - Quality profiles (HD 1080p, Any)
  - Tag system
  - Search proxy to Fightarr-API
- **Docker Support**:
  - PUID/PGID support for Unraid
  - Automatic permission management
  - Multi-arch builds (amd64, arm64)
- **API Endpoints**:
  - `/api/events` - Event CRUD operations
  - `/api/search/events` - Search MMA events
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
- Fight card management
- Quality profile selection
- Monitoring status tracking
- Responsive grid layouts
- Loading and error states
- Empty state messaging

---

## Release Notes

### How to Update
1. Pull the latest Docker image: `docker pull fightarr/fightarr:latest`
2. Restart your container
3. Check version in UI sidebar

### API Compatibility
- API Version remains at **v1.0.0** for stability
- All v1.0.x releases are backward compatible

### Support
- Issues: https://github.com/Fightarr/Fightarr/issues
- Documentation: https://docs.fightarr.net (coming soon)
- API: https://fightarr.net
