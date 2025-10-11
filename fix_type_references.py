#!/usr/bin/env python3
import os
import re

def fix_type_references(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        replacements = [
            # Type declarations and usages
            (r'\bEpisode\[\]', r'FightCard[]'),
            (r'\bSeries\[\]', r'Event[]'),
            (r': Episode\b', r': FightCard'),
            (r': Series\b', r': Event'),
            (r'<Episode>', r'<FightCard>'),
            (r'<Series>', r'<Event>'),
            (r'Episode\|', r'FightCard|'),
            (r'Series\|', r'Event|'),

            # Function parameter types
            (r'\(episode:', r'(fightCard:'),
            (r'\(series:', r'(event:'),
            (r'\bconst episode:', r'const fightCard:'),
            (r'\bconst series:', r'const event:'),

            # State references
            (r'state\.importSeries', r'state.importEvents'),

            # Generic Series/Episode that should be Event/FightCard in comments and strings
            (r'// Series', r'// Event'),
            (r'// Episode', r'// FightCard'),
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

    # Focus on files that likely have type errors
    target_dirs = [
        'Activity',
        'Calendar',
        'InteractiveImport',
        'InteractiveSearch',
        'AddEvent',
        'Wanted',
        'typings',
        'Utilities'
    ]

    fixed_count = 0
    for target in target_dirs:
        target_path = os.path.join(base_dir, target)
        if not os.path.exists(target_path):
            continue

        for root, dirs, files in os.walk(target_path):
            if 'node_modules' in root:
                continue

            for filename in files:
                if filename.endswith(('.tsx', '.ts', '.js', '.jsx')):
                    filepath = os.path.join(root, filename)
                    if fix_type_references(filepath):
                        print(f"Fixed: {filepath}")
                        fixed_count += 1

    print(f"\n✅ Total files fixed: {fixed_count}")

if __name__ == '__main__':
    main()
