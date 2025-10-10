#!/usr/bin/env python3
import os
import sys

def rename_files(directory):
    renamed_count = 0
    rename_operations = []

    # First, collect all rename operations
    for root, dirs, files in os.walk(directory):
        for filename in files:
            new_filename = filename
            changed = False

            if 'ImportSeries' in filename:
                new_filename = new_filename.replace('ImportSeries', 'ImportEvent')
                changed = True

            if changed:
                old_path = os.path.join(root, filename)
                new_path = os.path.join(root, new_filename)
                rename_operations.append((old_path, new_path))

    # Then perform renames
    for old_path, new_path in rename_operations:
        try:
            os.rename(old_path, new_path)
            print(f"Renamed: {old_path} -> {new_path}")
            renamed_count += 1
        except Exception as e:
            print(f"Error renaming {old_path}: {e}", file=sys.stderr)

    return renamed_count

if __name__ == '__main__':
    directory = '/workspaces/Fightarr/frontend/src/AddEvent/ImportEvent'
    print(f"Renaming files in {directory}...")
    count = rename_files(directory)
    print(f"\nTotal files renamed: {count}")
