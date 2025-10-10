#!/usr/bin/env python3
import os
import re

def fix_imports_in_file(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        replacements = [
            # Fix relative imports in FightCard directory
            (r"from './EpisodeDetailsModalContent'", r"from './FightCardDetailsModalContent'"),
            (r"from './EpisodeNumber'", r"from './FightCardNumber'"),

            # Fix CSS imports
            (r"from './NoSeries\.css'", r"from './NoEvent.css'"),

            # Fix utility paths - keep them as Series since that's where they actually are
            (r"from 'Utilities/Events/filterAlternateTitles'", r"from 'Utilities/Series/filterAlternateTitles'"),
            (r"from 'Utilities/Events/getProgressBarKind'", r"from 'Utilities/Series/getProgressBarKind'"),

            # Fix Card utilities - use Season since that's what exists
            (r"from 'Card/formatCard'", r"from 'Season/formatSeason'"),

            # Fix Event imports that should be Events
            (r"from 'Event/Event'", r"from 'Events/Event'"),
            (r"from 'Event/useSeries'", r"from 'Events/useEvent'"),
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
        if not os.path.exists(directory):
            continue
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
