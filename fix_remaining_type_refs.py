#!/usr/bin/env python3
import os
import re

files_to_fix = [
    '/workspaces/Fightarr/frontend/src/App/State/ParseAppState.ts',
    '/workspaces/Fightarr/frontend/src/Components/Filter/Builder/SeriesFilterBuilderRowValue.tsx',
    '/workspaces/Fightarr/frontend/src/Components/Page/Header/SeriesSearchInput.tsx',
    '/workspaces/Fightarr/frontend/src/InteractiveImport/Series/SelectSeriesModalContent.tsx',
    '/workspaces/Fightarr/frontend/src/Store/Selectors/createEventQualityProfileSelector.ts',
]

for filepath in files_to_fix:
    if not os.path.exists(filepath):
        print(f"Skipping {filepath} - not found")
        continue
        
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    original_content = content
    
    # Replace Series and Episode types
    content = re.sub(r'\bSeries\b(?!\w)', 'Event', content)
    content = re.sub(r'\bEpisode\b(?!\w)', 'FightCard', content)
    
    if content != original_content:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Fixed: {filepath}")

print("\n✅ Done fixing specific files")
