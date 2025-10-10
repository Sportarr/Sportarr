#!/usr/bin/env python3
import os
import re

def fix_imports_in_file(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        # Comprehensive list of all import fixes needed
        replacements = [
            # AddNewEvent files and imports
            (r"from '(.*/)?AddNewSeries'", r"from '\1AddNewEvent'"),
            (r"from '(.*/)?AddNewSeriesModal'", r"from '\1AddNewEventModal'"),
            (r"from '(.*/)?AddNewSeriesModalContent'", r"from '\1AddNewEventModalContent'"),
            (r"from '(.*/)?AddNewSeriesSearchResult'", r"from '\1AddNewEventSearchResult'"),

            # ImportEvent files and imports
            (r"from '(.*/)?ImportSeries'([^PM]|$)", r"from '\1ImportEvent'\2"),
            (r"from '(.*/)?ImportSeriesPage'", r"from '\1ImportEvent'"),
            (r"from '(.*/)?ImportSeriesTable'", r"from '\1ImportEventTable'"),
            (r"from '(.*/)?ImportSeriesRow'", r"from '\1ImportEventRow'"),
            (r"from '(.*/)?ImportSeriesHeader'", r"from '\1ImportEventHeader'"),
            (r"from '(.*/)?ImportSeriesFooter'", r"from '\1ImportEventFooter'"),
            (r"from '(.*/)?ImportSeriesSelected'", r"from '\1ImportEventSelected'"),
            (r"from '(.*/)?ImportSeriesSelectSeries'", r"from '\1ImportEventSelectSeries'"),
            (r"from '(.*/)?ImportSeriesSearchResult'", r"from '\1ImportEventSearchResult'"),
            (r"from '(.*/)?ImportSeriesTitle'", r"from '\1ImportEventTitle'"),
            (r"from '(.*/)?ImportSeriesSelectFolder'", r"from '\1ImportEventSelectFolder'"),

            # Events directory components
            (r"from 'Events/SeriesPoster'", r"from 'Events/EventPoster'"),
            (r"from 'Events/SeriesGenres'", r"from 'Events/EventGenres'"),
            (r"from 'Events/SeriesStatus'", r"from 'Events/EventStatus'"),
            (r"from 'Events/useSeries'", r"from 'Events/useEvent'"),
            (r"from 'Event/SeriesPoster'", r"from 'Events/EventPoster'"),
            (r"from 'Event/SeriesGenres'", r"from 'Events/EventGenres'"),

            # FightCard components (from Episode)
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

            # Paths fixes
            (r"from 'Events/MoveSeries/MoveEventModal'", r"from 'Events/MoveEvent/MoveEventModal'"),
            (r"from 'Utilities/Event/filterAlternateTitles'", r"from 'Utilities/Events/filterAlternateTitles'"),
            (r"from 'AddEvent/AddSeries'", r"from 'AddEvent/AddEvent'"),
            (r"from 'AddSeries/AddSeries'", r"from 'AddEvent/AddEvent'"),

            # CSS imports - AddNewEvent
            (r"from '(.*/)?AddNewSeries\.css'", r"from '\1AddNewEvent.css'"),
            (r"from '(.*/)?AddNewSeriesModalContent\.css'", r"from '\1AddNewEventModalContent.css'"),
            (r"from '(.*/)?AddNewSeriesSearchResult\.css'", r"from '\1AddNewEventSearchResult.css'"),

            # CSS imports - ImportEvent
            (r"from '(.*/)?ImportSeries\.css'", r"from '\1ImportEvent.css'"),
            (r"from '(.*/)?ImportSeriesTable\.css'", r"from '\1ImportEventTable.css'"),
            (r"from '(.*/)?ImportSeriesRow\.css'", r"from '\1ImportEventRow.css'"),
            (r"from '(.*/)?ImportSeriesHeader\.css'", r"from '\1ImportEventHeader.css'"),
            (r"from '(.*/)?ImportSeriesFooter\.css'", r"from '\1ImportEventFooter.css'"),
            (r"from '(.*/)?ImportSeriesSelected\.css'", r"from '\1ImportEventSelected.css'"),
            (r"from '(.*/)?ImportSeriesSelectSeries\.css'", r"from '\1ImportEventSelectSeries.css'"),
            (r"from '(.*/)?ImportSeriesSearchResult\.css'", r"from '\1ImportEventSearchResult.css'"),
            (r"from '(.*/)?ImportSeriesTitle\.css'", r"from '\1ImportEventTitle.css'"),
            (r"from '(.*/)?ImportSeriesSelectFolder\.css'", r"from '\1ImportEventSelectFolder.css'"),
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
