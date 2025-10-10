#!/usr/bin/env python3
import os
import sys

def rename_files(directory):
    renamed_count = 0

    # Walk through all directories
    for root, dirs, files in os.walk(directory):
        # Rename files
        for filename in files:
            if 'Series' in filename:
                old_path = os.path.join(root, filename)
                new_filename = filename.replace('Series', 'Event')
                new_path = os.path.join(root, new_filename)

                try:
                    os.rename(old_path, new_path)
                    print(f"Renamed: {old_path} -> {new_path}")
                    renamed_count += 1
                except Exception as e:
                    print(f"Error renaming {old_path}: {e}", file=sys.stderr)

    return renamed_count

if __name__ == '__main__':
    directory = '/workspaces/Fightarr/frontend/src/Events'
    print(f"Renaming files in {directory}...")
    count = rename_files(directory)
    print(f"\nTotal files renamed: {count}")
