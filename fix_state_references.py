#!/usr/bin/env python3
import os
import re

def fix_state_references(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        # Fix state references - be careful not to change 'events' to 'eventss'
        replacements = [
            # state.event. -> state.events.
            (r'state\.event\.', r'state.events.'),
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
                    if fix_state_references(filepath):
                        print(f"Fixed state references in: {filepath}")
                        fixed_count += 1

    print(f"\nTotal files with fixed state references: {fixed_count}")

if __name__ == '__main__':
    main()
