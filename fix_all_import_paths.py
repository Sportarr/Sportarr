#!/usr/bin/env python3
import os
import re

def fix_imports(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content

        # Fix all import paths from old to new directories
        replacements = [
            # Series directory -> Events directory
            (r"from 'Series/Series'", r"from 'Events/Event'"),
            (r"from 'Series/SeriesBanner'", r"from 'Events/EventBanner'"),
            (r"from 'Series/SeriesPoster'", r"from 'Events/EventPoster'"),
            (r"from 'Series/SeriesImage'", r"from 'Events/EventImage'"),
            (r"from 'Series/SeriesGenres'", r"from 'Events/EventGenres'"),
            (r"from 'Series/SeriesStatus'", r"from 'Events/EventStatus'"),
            (r"from 'Series/SeriesTitleLink'", r"from 'Events/EventTitleLink'"),
            (r"from 'Series/useSeries'", r"from 'Events/useEvent'"),
            (r"from 'Series/NoSeries'", r"from 'Events/NoEvent'"),

            # Episode directory -> FightCard directory
            (r"from 'Episode/Episode'", r"from 'FightCard/FightCard'"),
            (r"from 'Episode/EpisodeDetailsModal'", r"from 'FightCard/FightCardDetailsModal'"),
            (r"from 'Episode/EpisodeDetailsModalContent'", r"from 'FightCard/FightCardDetailsModalContent'"),
            (r"from 'Episode/EpisodeNumber'", r"from 'FightCard/FightCardNumber'"),
            (r"from 'Episode/SeasonEpisodeNumber'", r"from 'FightCard/CardNumber'"),
            (r"from 'Episode/EpisodeStatus'", r"from 'FightCard/FightCardStatus'"),
            (r"from 'Episode/EpisodeTitleLink'", r"from 'FightCard/FightCardTitleLink'"),
            (r"from 'Episode/EpisodeFormats'", r"from 'FightCard/FightCardFormats'"),
            (r"from 'Episode/EpisodeLanguages'", r"from 'FightCard/FightCardLanguages'"),
            (r"from 'Episode/EpisodeQuality'", r"from 'FightCard/FightCardQuality'"),
            (r"from 'Episode/EpisodeSearchCell'", r"from 'FightCard/FightCardSearchCell'"),
            (r"from 'Episode/useEpisode'", r"from 'FightCard/useFightCard'"),
            (r"from 'Episode/useEpisodes'", r"from 'FightCard/useFightCards'"),
            (r"from 'Episode/episodeEntities'", r"from 'FightCard/fightCardEntities'"),

            # AddSeries directory -> AddEvent directory
            (r"from 'AddSeries/AddSeries'", r"from 'AddEvent/AddEvent'"),
            (r"from 'AddSeries/addSeriesOptionsStore'", r"from 'AddEvent/addEventOptionsStore'"),

            # Type imports
            (r"from 'Series/Series'", r"from 'Events/Event'"),
            (r"\bSeries\b(?=\s+from)", r"Event"),
            (r"\bEpisode\b(?=\s+from)", r"FightCard"),
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
        # Skip node_modules
        if 'node_modules' in root:
            continue

        for filename in files:
            if filename.endswith(('.tsx', '.ts', '.js', '.jsx')):
                filepath = os.path.join(root, filename)
                if fix_imports(filepath):
                    print(f"Fixed: {filepath}")
                    fixed_count += 1

    print(f"\n✅ Total files fixed: {fixed_count}")

if __name__ == '__main__':
    main()
