# Frontend Migration Progress

**Status**: Components Copied, In Progress
**Date**: 2025-10-10

## ✅ Completed

### Redux Store (100%)
- ✅ eventActions.js - Complete event state management
- ✅ fightCardActions.js - Fight card state
- ✅ fightActions.js - Individual fight state
- ✅ Registered in store index

### Component Structure (70%)
- ✅ Copied `/Series` → `/Events` directory
- ✅ Renamed all Series* files to Event* files
- ✅ Updated imports from `Series/` to `Events/`
- ✅ Updated action imports from `seriesActions` to `eventActions`
- ✅ Copied `/Episode` → `/FightCard` directory
- ✅ Renamed all Episode* files to FightCard* files

## 🔄 In Progress

### Component Content Updates Needed
The files have been copied and renamed, but still contain old logic that references:
- Series terminology
- Episode terminology
- Season concepts
- TVDB/TVMaze references

**Estimated remaining work**: 20-30 hours of find/replace and logic updates across ~200+ component files

## 📋 Remaining Tasks

### High Priority
1. **Global Find/Replace** across all Events/ and FightCard/ components:
   - `series` → `event` (variable names)
   - `Series` → `Event` (type names)
   - `episode` → `fightCard` (variable names)
   - `Episode` → `FightCard` (type names)
   - `season` → `card` or remove
   - `tvdb` → remove
   - `episodeNumber` → `cardNumber`

2. **Update App Routing**
   - Find main routing file
   - Change `/series` → `/events`
   - Change `/series/:id` → `/events/:id`
   - Update route params throughout

3. **Create Fights Components** (New directory)
   - FightRow.tsx - Display individual fight
   - FightDetails.tsx - Fighter matchup details
   - FighterCard.tsx - Fighter profile widget

4. **Update Calendar**
   - Update to display events instead of episodes
   - Update data fetching logic

5. **Rename AddSeries → AddEvent**
   - Copy directory
   - Update search logic
   - Update API endpoints

## 📊 File Inventory

### Created Directories
- `/frontend/src/Events/` - 10 subdirectories, ~50+ files
- `/frontend/src/FightCard/` - 4 subdirectories, ~40+ files

### Files Still Referencing Old Terminology
Due to the scope (200+ files), a comprehensive sed/awk script or batch rename tool is recommended for the remaining find/replace operations.

## 🎯 Recommended Next Steps

### Option 1: Manual Updates (Thorough but Slow)
- Manually update each component file
- Test each component as you go
- Estimated: 20-30 hours

### Option 2: Batch Script (Faster but Risky)
- Create comprehensive find/replace script
- Run across all files at once
- Fix any breaking changes after
- Estimated: 8-12 hours + debugging

### Option 3: Hybrid Approach (Recommended)
- Use batch scripts for simple renames
- Manually update complex logic
- Test incrementally
- Estimated: 12-16 hours

## 🚧 Known Issues

1. **Imports still reference old paths** in some nested components
2. **Type definitions** need updating (Series.ts → Event.ts)
3. **Redux selectors** in components need updating
4. **API endpoint calls** hardcoded in components need changing
5. **Translation keys** may need updating

## 💡 Strategy Moving Forward

Given the large scope, recommend:
1. Commit current progress
2. Create a comprehensive sed script for bulk replacements
3. Test build after each major batch
4. Fix compilation errors iteratively
5. Test UI functionality once build succeeds

---

**Current Estimate**: 12-20 hours remaining for full frontend migration
