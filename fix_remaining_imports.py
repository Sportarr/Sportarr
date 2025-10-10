#!/usr/bin/env python3
import os
import re

def fix_imports_in_file(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        # Fix remaining Series/Episode component imports
        replacements = [
            # Events directory components
            (r"from 'Events/SeriesPoster'", r"from 'Events/EventPoster'"),
            (r"from 'Events/SeriesGenres'", r"from 'Events/EventGenres'"),
            (r"from 'Events/SeriesStatus'", r"from 'Events/EventStatus'"),
            (r"from 'Events/useSeries'", r"from 'Events/useEvent'"),
            (r"from 'Event/SeriesPoster'", r"from 'Events/EventPoster'"),
            (r"from 'Event/SeriesGenres'", r"from 'Events/EventGenres'"),

            # FightCard directory components (from Episode)
            (r"from 'FightCard/EpisodeFormats'", r"from 'FightCard/FightCardFormats'"),
            (r"from 'FightCard/EpisodeNumber'", r"from 'FightCard/FightCardNumber'"),
            (r"from 'FightCard/EpisodeSearchCell'", r"from 'FightCard/FightCardSearchCell'"),
            (r"from 'FightCard/EpisodeStatus'", r"from 'FightCard/FightCardStatus'"),
            (r"from 'FightCard/EpisodeTitleLink'", r"from 'FightCard/FightCardTitleLink'"),
            (r"from 'Episode/EpisodeFormats'", r"from 'FightCard/FightCardFormats'"),
            (r"from 'Episode/EpisodeNumber'", r"from 'FightCard/FightCardNumber'"),
            (r"from 'Episode/EpisodeSearchCell'", r"from 'FightCard/FightCardSearchCell'"),
            (r"from 'Episode/EpisodeStatus'", r"from 'FightCard/FightCardStatus'"),
            (r"from 'Episode/EpisodeTitleLink'", r"from 'FightCard/FightCardTitleLink'"),

            # MoveEvent modal path
            (r"from 'Events/MoveSeries/MoveEventModal'", r"from 'Events/MoveEvent/MoveEventModal'"),

            # Filter utilities
            (r"from 'Utilities/Event/filterAlternateTitles'", r"from 'Utilities/Events/filterAlternateTitles'"),

            # AddEvent imports
            (r"from './Import/ImportSeries'([^P]|$)", r"from './Import/ImportEvent'\1"),
            (r"from 'AddEvent/AddSeries'", r"from 'AddEvent/AddEvent'"),
            (r"from 'AddSeries/AddSeries'", r"from 'AddEvent/AddEvent'"),
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
    directories = [
        '/workspaces/Fightarr/frontend/src/AddEvent',
        '/workspaces/Fightarr/frontend/src/Events',
        '/workspaces/Fightarr/frontend/src/FightCard',
    ]

    fixed_count = 0
    for directory in directories:
        for root, dirs, files in os.walk(directory):
            for filename in files:
                if filename.endswith(('.tsx', '.ts', '.js', '.jsx')):
                    filepath = os.path.join(root, filename)
                    if fix_imports_in_file(filepath):
                        print(f"Fixed imports in: {filepath}")
                        fixed_count += 1

    print(f"\nTotal files with fixed imports: {fixed_count}")

if __name__ == '__main__':
    main()
