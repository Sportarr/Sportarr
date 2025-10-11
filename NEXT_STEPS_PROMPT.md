# Fightarr - Next Steps Prompt for Claude

## Current Status

The Fightarr transformation from Sonarr (TV shows) to fighting sports is 95% complete:

**✅ Completed:**
- Backend: All models, services, controllers transformed to fighting domain (Compiles with 6 style warnings)
- Frontend: Deleted ALL old Sonarr directories (/Series, /Episode, /AddSeries)
- Frontend: Created new fighting sports directories (/Events, /FightCard, /AddEvent)
- Redux: Renamed all actions and state (eventActions, eventIndex, eventDetails, fightCardActions)
- Database: Migration 224 ready for fight tables

**❌ Remaining Issue:**
- Frontend has 249 TypeScript errors - all are import path errors
- Files are trying to import from deleted directories (Series/, Episode/, AddSeries/)
- Need to update all imports to point to new directories (Events/, FightCard/, AddEvent/)

## Task: Fix All Import Path Errors

### Problem
When we deleted the old Sonarr directories (/Series, /Episode, /AddSeries), ~249 files across the codebase still have imports pointing to those old locations. These files are in other parts of the app like:
- `/Components/`
- `/InteractiveImport/`
- `/InteractiveSearch/`
- `/Calendar/`
- `/Wanted/`
- `/Activity/`
- etc.

### Solution
Create and run a comprehensive Python script to update ALL import statements throughout the entire frontend codebase.

### Script to Create: `/workspaces/Fightarr/fix_all_import_paths.py`

```python
#!/usr/bin/env python3
import os
import re

def fix_imports(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        # Fix all import paths from old to new directories
        replacements = [
            # Series directory -> Events directory
            (r"from 'Series/Series'", r"from 'Events/Event'"),
            (r"from 'Series/SeriesBanner'", r"from 'Events/EventBanner'"),
            (r"from 'Series/SeriesPoster'", r"from 'Events/EventPoster'"),
            (r"from 'Series/SeriesImage'", r"from 'Events/EventImage'"),
            (r"from 'Series/SeriesGenres'", r"from 'Events/EventGenres'"),
            (r"from 'Series/SeriesStatus'", r"from 'Events/EventStatus'"),
            (r"from 'Series/SeriesTitleLink'", r"from 'Events/EventTitleLink'"),
            (r"from 'Series/useSeries'", r"from 'Events/useEvent'"),
            (r"from 'Series/NoSeries'", r"from 'Events/NoEvent'"),

            # Episode directory -> FightCard directory
            (r"from 'Episode/Episode'", r"from 'FightCard/FightCard'"),
            (r"from 'Episode/EpisodeDetailsModal'", r"from 'FightCard/FightCardDetailsModal'"),
            (r"from 'Episode/EpisodeDetailsModalContent'", r"from 'FightCard/FightCardDetailsModalContent'"),
            (r"from 'Episode/EpisodeNumber'", r"from 'FightCard/FightCardNumber'"),
            (r"from 'Episode/SeasonEpisodeNumber'", r"from 'FightCard/CardNumber'"),
            (r"from 'Episode/EpisodeStatus'", r"from 'FightCard/FightCardStatus'"),
            (r"from 'Episode/EpisodeTitleLink'", r"from 'FightCard/FightCardTitleLink'"),
            (r"from 'Episode/EpisodeFormats'", r"from 'FightCard/FightCardFormats'"),
            (r"from 'Episode/EpisodeLanguages'", r"from 'FightCard/FightCardLanguages'"),
            (r"from 'Episode/EpisodeQuality'", r"from 'FightCard/FightCardQuality'"),
            (r"from 'Episode/EpisodeSearchCell'", r"from 'FightCard/FightCardSearchCell'"),
            (r"from 'Episode/useEpisode'", r"from 'FightCard/useFightCard'"),
            (r"from 'Episode/useEpisodes'", r"from 'FightCard/useFightCards'"),
            (r"from 'Episode/episodeEntities'", r"from 'FightCard/fightCardEntities'"),

            # AddSeries directory -> AddEvent directory
            (r"from 'AddSeries/AddSeries'", r"from 'AddEvent/AddEvent'"),
            (r"from 'AddSeries/addSeriesOptionsStore'", r"from 'AddEvent/addEventOptionsStore'"),

            # Type imports
            (r"from 'Series/Series'", r"from 'Events/Event'"),
            (r"\bSeries\b(?=\s+from)", r"Event"),
            (r"\bEpisode\b(?=\s+from)", r"FightCard"),
        ]

        for pattern, replacement in replacements:
            content = re.sub(pattern, replacement, content)

        if content != original_content:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(content)
            return True
        return False
    except Exception as e:
        print(f"Error processing {filepath}: {e}")
        return False

def main():
    base_dir = '/workspaces/Fightarr/frontend/src'

    fixed_count = 0
    for root, dirs, files in os.walk(base_dir):
        # Skip node_modules
        if 'node_modules' in root:
            continue

        for filename in files:
            if filename.endswith(('.tsx', '.ts', '.js', '.jsx')):
                filepath = os.path.join(root, filename)
                if fix_imports(filepath):
                    print(f"Fixed: {filepath}")
                    fixed_count += 1

    print(f"\n✅ Total files fixed: {fixed_count}")

if __name__ == '__main__':
    main()
```

### Steps to Execute

1. **Create the script:**
   ```bash
   # Script is already written above, save it to fix_all_import_paths.py
   ```

2. **Run the script:**
   ```bash
   cd /workspaces/Fightarr
   python3 fix_all_import_paths.py
   ```

3. **Test the build:**
   ```bash
   cd frontend
   yarn build
   ```
   - Should go from 249 errors down to ~0-50 errors
   - Remaining errors will likely be type mismatches or missing exports

4. **Fix any remaining TypeScript type errors:**
   - Check for `Series` type references that should be `Event`
   - Check for `Episode` type references that should be `FightCard`
   - Update interface names in components

5. **Test backend build:**
   ```bash
   cd /workspaces/Fightarr
   dotnet build src/Fightarr.sln
   ```
   - Should compile successfully (may have 6 StyleCop warnings - safe to ignore)

6. **Commit progress:**
   ```bash
   git add -A
   git commit -m "Frontend: Fix all import paths from Series/Episode to Events/FightCard

   - Updated 200+ import statements across entire codebase
   - Fixed imports in Components, InteractiveImport, Calendar, Wanted, etc.
   - All files now reference new fighting sports directories
   - Build errors reduced from 249 to [X]

   🤖 Generated with [Claude Code](https://claude.com/claude-code)

   Co-Authored-By: Claude <noreply@anthropic.com>"

   git push origin main
   ```

## Expected Results

After running these steps:
- Frontend build should have 0-50 errors (down from 249)
- All imports will point to correct fighting sports directories
- Application should be very close to running
- May need minor type fixes for remaining errors

## Quick Commands Summary

```bash
# 1. Run the import fixer
cd /workspaces/Fightarr
python3 fix_all_import_paths.py

# 2. Test frontend
cd frontend && yarn build

# 3. Test backend
cd /workspaces/Fightarr && dotnet build src/Fightarr.sln

# 4. If builds succeed, start the app
cd /workspaces/Fightarr/_output && ./Fightarr

# 5. Access UI at http://localhost:1867
```

## Notes

- The script is comprehensive but may need minor adjustments if new import patterns are found
- Focus on getting imports pointing to the right directories first
- Type errors can be fixed afterwards (they're usually simpler)
- The backend should already compile successfully
- Database migration will run automatically on first startup

## Context for Claude

You are continuing work on Fightarr, a fork of Sonarr (TV show tracker) that's being transformed into a fighting sports event tracker. We've completed the major refactoring:

1. Backend is fully transformed (Events, FightCards, Fights models)
2. Frontend directories are renamed (Events/, FightCard/, AddEvent/)
3. Redux store is restructured (eventActions, eventIndex, etc.)
4. Old Sonarr directories were deleted (/Series, /Episode, /AddSeries)

The ONLY remaining issue is ~249 import path errors where files throughout the codebase are trying to import from the old deleted directories. We just need to update these imports to point to the new fighting sports directories.

This is a straightforward find-replace task across the entire codebase.
