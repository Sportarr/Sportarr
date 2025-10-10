#!/usr/bin/env python3
import os
import re

def fix_imports_in_file(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        # Fix import paths for renamed files
        replacements = [
            # Series -> Event in paths
            (r"from '(.*/)?AddNewSeries'", r"from '\1AddNewEvent'"),
            (r"from '(.*/)?AddNewSeriesModal'", r"from '\1AddNewEventModal'"),
            (r"from '(.*/)?AddNewSeriesModalContent'", r"from '\1AddNewEventModalContent'"),
            (r"from '(.*/)?AddNewSeriesSearchResult'", r"from '\1AddNewEventSearchResult'"),
            (r"from '(.*/)?ImportSeriesPage'", r"from '\1ImportEvent'"),
            (r"from '(.*/)?ImportSeries'([^P]|$)", r"from '\1ImportEvent'\2"),
            (r"from '(.*/)?SeriesIndex'", r"from '\1EventIndex'"),
            (r"from '(.*/)?SeriesDetails'", r"from '\1EventDetails'"),
            (r"from '(.*/)?SeriesDetailsPage'", r"from '\1EventDetailsPage'"),
            (r"from '(.*/)?SeriesDetailsSeason'", r"from '\1EventDetailsSeason'"),
            (r"from '(.*/)?SeriesDetailsLinks'", r"from '\1EventDetailsLinks'"),
            (r"from '(.*/)?SeriesAlternateTitles'", r"from '\1EventAlternateTitles'"),
            (r"from '(.*/)?SeriesTags'", r"from '\1EventTags'"),
            (r"from '(.*/)?SeriesProgressLabel'", r"from '\1EventProgressLabel'"),
            (r"from '(.*/)?SeriesHistoryModal'", r"from '\1EventHistoryModal'"),
            (r"from '(.*/)?SeriesHistoryModalContent'", r"from '\1EventHistoryModalContent'"),
            (r"from '(.*/)?SeriesHistoryRow'", r"from '\1EventHistoryRow'"),
            (r"from '(.*/)?SeriesIndexTable'", r"from '\1EventIndexTable'"),
            (r"from '(.*/)?SeriesIndexRow'", r"from '\1EventIndexRow'"),
            (r"from '(.*/)?SeriesIndexTableHeader'", r"from '\1EventIndexTableHeader'"),
            (r"from '(.*/)?SeriesIndexTableOptions'", r"from '\1EventIndexTableOptions'"),
            (r"from '(.*/)?SeriesStatusCell'", r"from '\1EventStatusCell'"),
            (r"from '(.*/)?SeriesIndexFooter'", r"from '\1EventIndexFooter'"),
            (r"from '(.*/)?SeriesIndexFilterModal'", r"from '\1EventIndexFilterModal'"),
            (r"from '(.*/)?SeriesIndexRefreshSeriesButton'", r"from '\1EventIndexRefreshEventButton'"),
            (r"from '(.*/)?SeriesIndexPosterSelect'", r"from '\1EventIndexPosterSelect'"),
            (r"from '(.*/)?SeriesIndexSelectFooter'", r"from '\1EventIndexSelectFooter'"),
            (r"from '(.*/)?SeriesIndexSelectAllButton'", r"from '\1EventIndexSelectAllButton'"),
            (r"from '(.*/)?SeriesIndexSelectModeButton'", r"from '\1EventIndexSelectModeButton'"),
            (r"from '(.*/)?SeriesIndexSelectModeMenuItem'", r"from '\1EventIndexSelectModeMenuItem'"),
            (r"from '(.*/)?SeriesIndexSelectAllMenuItem'", r"from '\1EventIndexSelectAllMenuItem'"),
            (r"from '(.*/)?DeleteSeriesModal'", r"from '\1DeleteEventModal'"),
            (r"from '(.*/)?DeleteSeriesModalContent'", r"from '\1DeleteEventModalContent'"),
            (r"from '(.*/)?OrganizeSeriesModal'", r"from '\1OrganizeEventModal'"),
            (r"from '(.*/)?OrganizeSeriesModalContent'", r"from '\1OrganizeEventModalContent'"),
            (r"from '(.*/)?EditSeriesModal'", r"from '\1EditEventModal'"),
            (r"from '(.*/)?EditSeriesModalContent'", r"from '\1EditEventModalContent'"),
            (r"from '(.*/)?MoveSeriesModal'", r"from '\1MoveEventModal'"),
            (r"from '(.*/)?SeriesIndexProgressBar'", r"from '\1EventIndexProgressBar'"),
            (r"from '(.*/)?SeriesIndexSortMenu'", r"from '\1EventIndexSortMenu'"),
            (r"from '(.*/)?SeriesIndexViewMenu'", r"from '\1EventIndexViewMenu'"),
            (r"from '(.*/)?SeriesIndexFilterMenu'", r"from '\1EventIndexFilterMenu'"),
            (r"from '(.*/)?SeriesIndexOverview'([^s]|$)", r"from '\1EventIndexOverview'\2"),
            (r"from '(.*/)?SeriesIndexOverviews'", r"from '\1EventIndexOverviews'"),
            (r"from '(.*/)?SeriesIndexOverviewInfo'", r"from '\1EventIndexOverviewInfo'"),
            (r"from '(.*/)?SeriesIndexOverviewInfoRow'", r"from '\1EventIndexOverviewInfoRow'"),
            (r"from '(.*/)?SeriesIndexOverviewOptionsModal'", r"from '\1EventIndexOverviewOptionsModal'"),
            (r"from '(.*/)?SeriesIndexOverviewOptionsModalContent'", r"from '\1EventIndexOverviewOptionsModalContent'"),
            (r"from '(.*/)?SeriesIndexPoster'([^s]|$)", r"from '\1EventIndexPoster'\2"),
            (r"from '(.*/)?SeriesIndexPosters'", r"from '\1EventIndexPosters'"),
            (r"from '(.*/)?SeriesIndexPosterInfo'", r"from '\1EventIndexPosterInfo'"),
            (r"from '(.*/)?SeriesIndexPosterOptionsModal'", r"from '\1EventIndexPosterOptionsModal'"),
            (r"from '(.*/)?SeriesIndexPosterOptionsModalContent'", r"from '\1EventIndexPosterOptionsModalContent'"),
            (r"from '(.*/)?createSeriesIndexItemSelector'", r"from '\1createEventIndexItemSelector'"),

            # Episode -> FightCard in paths
            (r"from '(.*/)?EpisodeRow'", r"from '\1FightCardRow'"),
            (r"from '(.*/)?EpisodeHistory'", r"from '\1FightCardHistory'"),
            (r"from '(.*/)?EpisodeHistoryRow'", r"from '\1FightCardHistoryRow'"),
            (r"from '(.*/)?EpisodeSearch'", r"from '\1FightCardSearch'"),
            (r"from '(.*/)?EpisodeSummary'", r"from '\1FightCardSummary'"),
            (r"from '(.*/)?EpisodeFileRow'", r"from '\1FightCardFileRow'"),
            (r"from '(.*/)?EpisodeAiring'", r"from '\1FightCardAiring'"),

            # CSS imports
            (r"from '(.*/)?AddNewSeries\.css'", r"from '\1AddNewEvent.css'"),
            (r"from '(.*/)?ImportSeries\.css'", r"from '\1ImportEvent.css'"),
            (r"from '(.*/)?SeriesIndex\.css'", r"from '\1EventIndex.css'"),
            (r"from '(.*/)?SeriesDetails\.css'", r"from '\1EventDetails.css'"),
            (r"from '(.*/)?SeriesDetailsSeason\.css'", r"from '\1EventDetailsSeason.css'"),
            (r"from '(.*/)?SeriesDetailsLinks\.css'", r"from '\1EventDetailsLinks.css'"),
            (r"from '(.*/)?SeriesAlternateTitles\.css'", r"from '\1EventAlternateTitles.css'"),
            (r"from '(.*/)?SeriesHistoryRow\.css'", r"from '\1EventHistoryRow.css'"),
            (r"from '(.*/)?SeriesIndexTable\.css'", r"from '\1EventIndexTable.css'"),
            (r"from '(.*/)?SeriesIndexRow\.css'", r"from '\1EventIndexRow.css'"),
            (r"from '(.*/)?SeriesIndexTableHeader\.css'", r"from '\1EventIndexTableHeader.css'"),
            (r"from '(.*/)?SeriesStatusCell\.css'", r"from '\1EventStatusCell.css'"),
            (r"from '(.*/)?SeriesIndexFooter\.css'", r"from '\1EventIndexFooter.css'"),
            (r"from '(.*/)?SeriesIndexPosterSelect\.css'", r"from '\1EventIndexPosterSelect.css'"),
            (r"from '(.*/)?SeriesIndexSelectFooter\.css'", r"from '\1EventIndexSelectFooter.css'"),
            (r"from '(.*/)?DeleteSeriesModalContent\.css'", r"from '\1DeleteEventModalContent.css'"),
            (r"from '(.*/)?OrganizeSeriesModalContent\.css'", r"from '\1OrganizeEventModalContent.css'"),
            (r"from '(.*/)?EditSeriesModalContent\.css'", r"from '\1EditEventModalContent.css'"),
            (r"from '(.*/)?MoveSeriesModal\.css'", r"from '\1MoveEventModal.css'"),
            (r"from '(.*/)?SeriesIndexProgressBar\.css'", r"from '\1EventIndexProgressBar.css'"),
            (r"from '(.*/)?SeriesIndexOverview\.css'", r"from '\1EventIndexOverview.css'"),
            (r"from '(.*/)?SeriesIndexOverviewInfo\.css'", r"from '\1EventIndexOverviewInfo.css'"),
            (r"from '(.*/)?SeriesIndexOverviewInfoRow\.css'", r"from '\1EventIndexOverviewInfoRow.css'"),
            (r"from '(.*/)?SeriesIndexPoster\.css'", r"from '\1EventIndexPoster.css'"),
            (r"from '(.*/)?SeriesIndexPosterInfo\.css'", r"from '\1EventIndexPosterInfo.css'"),
            (r"from '(.*/)?EpisodeRow\.css'", r"from '\1FightCardRow.css'"),
            (r"from '(.*/)?EpisodeHistoryRow\.css'", r"from '\1FightCardHistoryRow.css'"),
            (r"from '(.*/)?EpisodeSearch\.css'", r"from '\1FightCardSearch.css'"),
            (r"from '(.*/)?EpisodeSummary\.css'", r"from '\1FightCardSummary.css'"),
            (r"from '(.*/)?EpisodeFileRow\.css'", r"from '\1FightCardFileRow.css'"),
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
