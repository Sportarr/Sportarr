#!/usr/bin/env python3
import os
import re

def update_imports(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        # Update imports for renamed files
        replacements = [
            # Selector imports
            (r"from 'Store/Selectors/createSeriesClientSideCollectionItemsSelector'",
             r"from 'Store/Selectors/createEventClientSideCollectionItemsSelector'"),
            (r"from 'Store/Selectors/createSeriesSelector'",
             r"from 'Store/Selectors/createEventSelector'"),
            (r"from 'Store/Selectors/createSeriesQualityProfileSelector'",
             r"from 'Store/Selectors/createEventQualityProfileSelector'"),

            # State imports
            (r"from 'App/State/SeriesAppState'", r"from 'App/State/EventAppState'"),

            # Action imports (already done but ensure consistency)
            (r"from 'Store/Actions/seriesIndexActions'", r"from 'Store/Actions/eventIndexActions'"),
            (r"from 'Store/Actions/seriesActions'", r"from 'Store/Actions/eventDetailActions'"),
            (r"from 'Store/Actions/seriesHistoryActions'", r"from 'Store/Actions/eventHistoryActions'"),
            (r"from 'Store/Actions/importSeriesActions'", r"from 'Store/Actions/importEventActions'"),
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
        for filename in files:
            if filename.endswith(('.tsx', '.ts', '.js', '.jsx')):
                filepath = os.path.join(root, filename)
                if update_imports(filepath):
                    print(f"Updated imports in: {filepath}")
                    fixed_count += 1

    print(f"\nTotal files with updated imports: {fixed_count}")

if __name__ == '__main__':
    main()
