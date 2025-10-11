#!/usr/bin/env python3
import os
import re

def fix_imports(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        # Fix remaining Episode helper imports
        replacements = [
            # Episode helper functions -> FightCard helpers
            (r"from 'Episode/createEpisodesFetchingSelector'", r"from 'FightCard/createFightCardsFetchingSelector'"),
            (r"from 'Episode/getFinaleTypeName'", r"from 'FightCard/getFinaleTypeName'"),
            (r"from 'Episode/getReleaseTypeName'", r"from 'FightCard/getReleaseTypeName'"),
            (r"from 'Episode/IndexerFlags'", r"from 'FightCard/IndexerFlags'"),

            # AddSeries monitoring components -> AddEvent monitoring components
            (r"from 'AddSeries/SeriesMonitoringOptionsPopoverContent'", r"from 'AddEvent/EventMonitoringOptionsPopoverContent'"),
            (r"from 'AddSeries/SeriesMonitorNewItemsOptionsPopoverContent'", r"from 'AddEvent/EventMonitorNewItemsOptionsPopoverContent'"),
            (r"from 'AddSeries/SeriesTypePopoverContent'", r"from 'AddEvent/EventTypePopoverContent'"),
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
