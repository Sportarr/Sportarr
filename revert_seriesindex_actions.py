#!/usr/bin/env python3
import os
import re

def revert_seriesindex_actions(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        # Revert the series index actions that were incorrectly changed
        # These should remain as setSeriesX because they're from seriesIndexActions
        replacements = [
            (r'\bsetEventFilter\b', r'setSeriesFilter'),
            (r'\bsetEventSort\b', r'setSeriesSort'),
            (r'\bsetEventTableOption\b', r'setSeriesTableOption'),
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
        '/workspaces/Fightarr/frontend/src/Events/Index',
    ]

    fixed_count = 0
    for directory in directories:
        if not os.path.exists(directory):
            continue
        for root, dirs, files in os.walk(directory):
            for filename in files:
                if filename.endswith(('.tsx', '.ts', '.js', '.jsx')):
                    filepath = os.path.join(root, filename)
                    if revert_seriesindex_actions(filepath):
                        print(f"Reverted seriesIndex actions in: {filepath}")
                        fixed_count += 1

    print(f"\nTotal files reverted: {fixed_count}")

if __name__ == '__main__':
    main()
