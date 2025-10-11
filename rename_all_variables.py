#!/usr/bin/env python3
import os
import re

def rename_variables(filepath):
    """
    Rename all series/episode variable names to event/fightCard.
    This is comprehensive - we're converting ALL TV show terminology to fighting terminology.
    """
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        replacements = [
            # Variable declarations and assignments
            (r'\bconst series\b', r'const event'),
            (r'\blet series\b', r'let event'),
            (r'\bvar series\b', r'var event'),
            (r'\bconst episode\b', r'const fightCard'),
            (r'\blet episode\b', r'let fightCard'),
            (r'\bvar episode\b', r'var episode'),

            # Function parameters
            (r'\(series:', r'(event:'),
            (r'\(series\)', r'(event)'),
            (r', series:', r', event:'),
            (r', series\)', r', event)'),
            (r'\(episode:', r'(fightCard:'),
            (r'\(episode\)', r'(fightCard)'),
            (r', episode:', r', fightCard:'),
            (r', episode\)', r', fightCard)'),

            # Destructuring
            (r'\{ series', r'{ event'),
            (r', series ', r', event '),
            (r' series\}', r' event}'),
            (r'\{ episode', r'{ fightCard'),
            (r', episode ', r', fightCard '),
            (r' episode\}', r' fightCard}'),

            # Array methods and callbacks
            (r'\.map\(\(series\)', r'.map((event)'),
            (r'\.filter\(\(series\)', r'.filter((event)'),
            (r'\.forEach\(\(series\)', r'.forEach((event)'),
            (r'\.find\(\(series\)', r'.find((event)'),
            (r'\.map\(\(episode\)', r'.map((fightCard)'),
            (r'\.filter\(\(episode\)', r'.filter((fightCard)'),
            (r'\.forEach\(\(episode\)', r'.forEach((fightCard)'),
            (r'\.find\(\(episode\)', r'.find((fightCard)'),

            # Property access (be careful here)
            (r'series\.', r'event.'),
            (r'episode\.', r'fightCard.'),

            # Common patterns
            (r'\bseries\[', r'event['),
            (r'\bepisode\[', r'fightCard['),
            (r'!series', r'!event'),
            (r'!episode', r'!fightCard'),
            (r'\?series', r'?event'),
            (r'\?episode', r'?fightCard'),

            # Return statements
            (r'return series', r'return event'),
            (r'return episode', r'return fightCard'),

            # Comparisons
            (r'series ===', r'event ==='),
            (r'series !==', r'event !=='),
            (r'series ==', r'event =='),
            (r'series !=', r'event !='),
            (r'episode ===', r'fightCard ==='),
            (r'episode !==', r'fightCard !=='),
            (r'episode ==', r'fightCard =='),
            (r'episode !=', r'fightCard !='),

            # Object properties and values
            (r': series\b', r': event'),
            (r': episode\b', r': fightCard'),

            # Spread operator
            (r'\.\.\.series', r'...event'),
            (r'\.\.\.episode', r'...fightCard'),
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

    # Process ALL TypeScript/JavaScript files
    fixed_count = 0
    for root, dirs, files in os.walk(base_dir):
        if 'node_modules' in root:
            continue

        for filename in files:
            if filename.endswith(('.tsx', '.ts', '.js', '.jsx')):
                filepath = os.path.join(root, filename)
                if rename_variables(filepath):
                    print(f"Fixed: {filepath}")
                    fixed_count += 1

    print(f"\n✅ Total files with renamed variables: {fixed_count}")

if __name__ == '__main__':
    main()
