#!/usr/bin/env python3
import re

files = [
    '/workspaces/Fightarr/frontend/src/Events/Index/EventIndexFilterModal.tsx',
    '/workspaces/Fightarr/frontend/src/Events/Index/Overview/selectOverviewOptions.ts',
    '/workspaces/Fightarr/frontend/src/Events/Index/Posters/EventIndexPosters.tsx',
    '/workspaces/Fightarr/frontend/src/Events/Index/Posters/selectPosterOptions.ts',
    '/workspaces/Fightarr/frontend/src/Events/Index/Table/EventIndexTable.tsx',
    '/workspaces/Fightarr/frontend/src/Events/Index/Table/selectTableOptions.ts',
]

for filepath in files:
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        # Fix eventIndexIndex typo
        content = re.sub(r'eventIndexIndex', 'eventIndex', content)

        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)

        print(f"Fixed: {filepath}")
    except Exception as e:
        print(f"Error fixing {filepath}: {e}")
