#!/usr/bin/env python3
import os
import re

def fix_imports_in_file(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        replacements = [
            # Series components in Events directory
            (r"from './SeriesImage'", r"from './EventImage'"),
            (r"from 'Events/SeriesImage'", r"from 'Events/EventImage'"),
            (r"from 'Events/SeriesBanner'", r"from 'Events/EventBanner'"),
            (r"from 'Events/SeriesTitleLink'", r"from 'Events/EventTitleLink'"),
            (r"from 'Events/NoSeries'", r"from 'Events/NoEvent'"),

            # Episode components references
            (r"from './EpisodeDetailsModal'", r"from './FightCardDetailsModal'"),
            (r"from './EpisodeQuality'", r"from './FightCardQuality'"),
            (r"from 'FightCard/EpisodeDetailsModal'", r"from 'FightCard/FightCardDetailsModal'"),
            (r"from 'FightCard/EpisodeLanguages'", r"from 'FightCard/FightCardLanguages'"),
            (r"from 'FightCard/EpisodeQuality'", r"from 'FightCard/FightCardQuality'"),
            (r"from 'FightCard/SeasonEpisodeNumber'", r"from 'FightCard/CardNumber'"),
            (r"from 'FightCard/useEpisode'", r"from 'FightCard/useFightCard'"),

            # CSS file imports
            (r"from './EpisodeNumber\.css'", r"from './FightCardNumber.css'"),
            (r"from './EpisodeSearchCell\.css'", r"from './FightCardSearchCell.css'"),
            (r"from './EpisodeStatus\.css'", r"from './FightCardStatus.css'"),
            (r"from './EpisodeTitleLink\.css'", r"from './FightCardTitleLink.css'"),

            # Utilities paths
            (r"from 'Utilities/Event/filterAlternateTitles'", r"from 'Utilities/Events/filterAlternateTitles'"),
            (r"from 'Utilities/Event/getProgressBarKind'", r"from 'Utilities/Events/getProgressBarKind'"),
            (r"from 'Card/formatSeason'", r"from 'Card/formatCard'"),

            # AddEvent directory
            (r"from './AddSeries'([^/]|$)", r"from './AddEvent'\1"),
            (r"from 'AddEvent/AddSeries'", r"from 'AddEvent/AddEvent'"),
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
