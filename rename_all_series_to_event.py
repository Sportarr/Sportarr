#!/usr/bin/env python3
import os
import re

def rename_series_to_event(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        # Comprehensive replacements for all Series -> Event terminology
        replacements = [
            # Redux action names and constants
            (r"'seriesIndex/", r"'eventIndex/"),
            (r'"seriesIndex/', r'"eventIndex/'),
            (r'\bSET_SERIES_SORT\b', r'SET_EVENT_SORT'),
            (r'\bSET_SERIES_FILTER\b', r'SET_EVENT_FILTER'),
            (r'\bSET_SERIES_VIEW\b', r'SET_EVENT_VIEW'),
            (r'\bSET_SERIES_TABLE_OPTION\b', r'SET_EVENT_TABLE_OPTION'),
            (r'\bSET_SERIES_POSTER_OPTION\b', r'SET_EVENT_POSTER_OPTION'),
            (r'\bSET_SERIES_OVERVIEW_OPTION\b', r'SET_EVENT_OVERVIEW_OPTION'),

            # Action creator functions
            (r'\bsetSeriesSort\b', r'setEventSort'),
            (r'\bsetSeriesFilter\b', r'setEventFilter'),
            (r'\bsetSeriesView\b', r'setEventView'),
            (r'\bsetSeriesTableOption\b', r'setEventTableOption'),
            (r'\bsetSeriesPosterOption\b', r'setEventPosterOption'),
            (r'\bsetSeriesOverviewOption\b', r'setEventOverviewOption'),

            # State and selector references
            (r'state\.seriesIndex', r'state.eventIndex'),
            (r"'seriesIndex'", r"'eventIndex'"),
            (r'"seriesIndex"', r'"eventIndex"'),

            # Import paths
            (r"from 'Store/Actions/seriesIndexActions'", r"from 'Store/Actions/eventIndexActions'"),
            (r"from 'Store/Actions/seriesActions'", r"from 'Store/Actions/eventDetailActions'"),
            (r"from 'Store/Actions/seriesHistoryActions'", r"from 'Store/Actions/eventHistoryActions'"),
            (r"from 'Store/Actions/importSeriesActions'", r"from 'Store/Actions/importEventActions'"),

            # Component and type names
            (r'\bSeriesIndexAppState\b', r'EventIndexAppState'),
            (r'\bSeriesAppState\b', r'EventAppState'),
            (r'\bcreateSeriesClientSideCollectionItemsSelector\b', r'createEventClientSideCollectionItemsSelector'),
            (r'\bcreateSeriesSelector\b', r'createEventSelector'),

            # Season references should become FightCard references
            (r'\bseasonCount\b', r'fightCardCount'),
            (r'\bSeasons\b', r'FightCards'),
            (r'\bshowSeasonCount\b', r'showFightCardCount'),

            # Network/Airing -> Organization/Event Date
            (r'\bshowNetwork\b', r'showOrganization'),
            (r'\bshowPreviousAiring\b', r'showPreviousEvent'),
            (r'\bnextAiring\b', r'nextEvent'),
            (r'\bpreviousAiring\b', r'previousEvent'),
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
        '/workspaces/Fightarr/frontend/src/Store/Actions',
        '/workspaces/Fightarr/frontend/src/Events',
        '/workspaces/Fightarr/frontend/src/AddEvent',
        '/workspaces/Fightarr/frontend/src/Store/Selectors',
        '/workspaces/Fightarr/frontend/src/App/State',
    ]

    fixed_count = 0
    for directory in directories:
        if not os.path.exists(directory):
            print(f"Directory not found: {directory}")
            continue
        for root, dirs, files in os.walk(directory):
            for filename in files:
                if filename.endswith(('.tsx', '.ts', '.js', '.jsx')):
                    filepath = os.path.join(root, filename)
                    if rename_series_to_event(filepath):
                        print(f"Updated: {filepath}")
                        fixed_count += 1

    print(f"\nTotal files updated: {fixed_count}")

if __name__ == '__main__':
    main()
