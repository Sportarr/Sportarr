#!/usr/bin/env python3
import os
import re

def fix_action_imports(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        # Fix action import names
        replacements = [
            # Event actions
            (r'\bfetchSeries\b', r'fetchEvents'),
            (r'\bsaveSeries\b', r'saveEvent'),
            (r'\bdeleteSeries\b', r'deleteEvent'),
            (r'\btoggleSeriesMonitored\b', r'toggleEventMonitored'),
            (r'\btoggleSeasonMonitored\b', r'toggleFightCardMonitored'),
            (r'\bupdateSeriesMonitor\b', r'updateEventMonitor'),
            (r'\bsaveSeriesEditor\b', r'saveEventEditor'),
            (r'\bbulkDeleteSeries\b', r'bulkDeleteEvents'),
            (r'\bsetSeriesValue\b', r'setEventValue'),
            (r'\bsetSeriesSort\b', r'setEventSort'),
            (r'\bsetSeriesFilter\b', r'setEventFilter'),
            (r'\bsetSeriesTableOption\b', r'setEventTableOption'),

            # Import statement fixes
            (r"from 'AddEvent/addSeriesOptionsStore'", r"from 'AddEvent/addEventOptionsStore'"),
            (r"from './addSeriesOptionsStore'", r"from './addEventOptionsStore'"),
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
    ]

    fixed_count = 0
    for directory in directories:
        if not os.path.exists(directory):
            continue
        for root, dirs, files in os.walk(directory):
            for filename in files:
                if filename.endswith(('.tsx', '.ts', '.js', '.jsx')):
                    filepath = os.path.join(root, filename)
                    if fix_action_imports(filepath):
                        print(f"Fixed action imports in: {filepath}")
                        fixed_count += 1

    print(f"\nTotal files with fixed action imports: {fixed_count}")

if __name__ == '__main__':
    main()
