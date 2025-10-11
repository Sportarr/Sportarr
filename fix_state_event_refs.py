#!/usr/bin/env python3
import re
import sys

def fix_state_event_refs(filename):
    """Fix state.event references to state.eventIndex"""
    try:
        with open(filename, 'r', encoding='utf-8') as f:
            content = f.read()

        # Fix state.event but not state.events or state.eventDetails or state.eventHistory
        # This is for EventIndex files specifically
        if 'EventIndex' in filename or 'selectPosterOptions' in filename or 'selectOverviewOptions' in filename or 'selectTableOptions' in filename:
            content = re.sub(r'state\.event(?![sH]|Details)', r'state.eventIndex', content)

        with open(filename, 'w', encoding='utf-8') as f:
            f.write(content)

        print(f"Fixed: {filename}")
        return True
    except Exception as e:
        print(f"Error processing {filename}: {e}")
        return False

if __name__ == '__main__':
    files = [
        '/workspaces/Fightarr/frontend/src/Events/Index/Posters/selectPosterOptions.ts',
        '/workspaces/Fightarr/frontend/src/Events/Index/Posters/EventIndexPosters.tsx',
        '/workspaces/Fightarr/frontend/src/Events/Index/Overview/selectOverviewOptions.ts',
        '/workspaces/Fightarr/frontend/src/Events/Index/Table/selectTableOptions.ts',
        '/workspaces/Fightarr/frontend/src/Events/Index/Table/EventIndexTable.tsx',
        '/workspaces/Fightarr/frontend/src/Events/Index/EventIndexFilterModal.tsx',
        '/workspaces/Fightarr/frontend/src/Events/Index/Select/EventIndexSelectFooter.tsx',
    ]

    for file in files:
        fix_state_event_refs(file)
