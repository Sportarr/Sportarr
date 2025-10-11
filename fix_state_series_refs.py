#!/usr/bin/env python3
import os
import re

def fix_state_refs(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        replacements = [
            # State references
            (r'state\.series\.', r'state.events.'),
            (r'state\.series\b', r'state.events'),
            
            # Selector imports and names
            (r'createMultiSeriesSelector', r'createMultiEventSelector'),
            (r'createSeriesCountSelector', r'createEventCountSelector'),
            (r'createAllSeriesSelector', r'createAllEventsSelector'),
            (r'createExistingSeriesSelector', r'createExistingEventSelector'),
            (r'createImportSeriesItemSelector', r'createImportEventItemSelector'),
            
            # Import paths for selectors
            (r"from 'Store/Selectors/createMultiSeriesSelector'", r"from 'Store/Selectors/createMultiEventSelector'"),
            (r"from 'Store/Selectors/createSeriesCountSelector'", r"from 'Store/Selectors/createEventCountSelector'"),
            (r"from 'Store/Selectors/createAllSeriesSelector'", r"from 'Store/Selectors/createAllEventsSelector'"),
            (r"from 'Store/Selectors/createExistingSeriesSelector'", r"from 'Store/Selectors/createExistingEventSelector'"),
            (r"from 'Store/Selectors/createImportSeriesItemSelector'", r"from 'Store/Selectors/createImportEventItemSelector'"),
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
        if 'node_modules' in root:
            continue

        for filename in files:
            if filename.endswith(('.tsx', '.ts', '.js', '.jsx')):
                filepath = os.path.join(root, filename)
                if fix_state_refs(filepath):
                    print(f"Fixed: {filepath}")
                    fixed_count += 1

    print(f"\n✅ Total files fixed: {fixed_count}")

if __name__ == '__main__':
    main()
