#!/usr/bin/env python3
"""Snapshot league seasons from the Sportarr metadata API for the corpus test.

Usage:
    snapshot_seasons.py [--base-url URL] lg-000278 2024 lg-000079 2016 ...

Each pair writes seasons/<league>-<season>.json next to this script. The file
holds the league fields and every event of the season, with only the fields
the corpus builder reads. The schedule endpoint is the one the app uses to
load a season.
"""

import argparse
import json
import os
import sys
import urllib.parse
import urllib.request

DEFAULT_BASE_URL = "https://sportarr.net/api/v2/json"
LEAGUE_FIELDS = ("idLeague", "strLeague", "strSport", "strLeagueAlternate")
EVENT_FIELDS = (
    "idEvent", "strEvent", "strSeason", "intRound", "dateEvent", "strTime",
    "strHomeTeam", "strAwayTeam",
)


def fetch_json(url):
    request = urllib.request.Request(url, headers={"User-Agent": "sportarr-corpus-snapshot"})
    with urllib.request.urlopen(request, timeout=120) as response:
        return json.load(response)


def fetch_league(base_url, league_id):
    url = f"{base_url}/lookup/league/{urllib.parse.quote(league_id)}"
    rows = (fetch_json(url).get("data") or {}).get("lookup") or []
    if not rows:
        raise SystemExit(f"league {league_id} not found at {url}")
    return {field: rows[0].get(field) for field in LEAGUE_FIELDS}


def fetch_season(base_url, league_id, season):
    url = (f"{base_url}/schedule/league/{urllib.parse.quote(league_id)}"
           f"/{urllib.parse.quote(season)}")
    rows = (fetch_json(url).get("data") or {}).get("schedule") or []
    events = [{field: row.get(field) for field in EVENT_FIELDS} for row in rows]
    # A stable order keeps the diff small when a season is snapshotted again.
    events.sort(key=lambda e: (e["dateEvent"] or "", e["strTime"] or "", e["idEvent"] or ""))
    return events


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--base-url", default=os.environ.get("SPORTARR_API_BASE", DEFAULT_BASE_URL))
    parser.add_argument("pairs", nargs="+", help="league id and season, repeated")
    args = parser.parse_args()
    if len(args.pairs) % 2:
        parser.error("give the arguments as league id and season pairs")

    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "seasons")
    os.makedirs(out_dir, exist_ok=True)
    base_url = args.base_url.rstrip("/")
    for league_id, season in zip(args.pairs[0::2], args.pairs[1::2]):
        events = fetch_season(base_url, league_id, season)
        if not events:
            print(f"{league_id} {season}: no events, nothing written", file=sys.stderr)
            continue
        snapshot = {"league": fetch_league(base_url, league_id), "events": events}
        path = os.path.join(out_dir, f"{league_id}-{season}.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(snapshot, handle, indent=1, ensure_ascii=False)
            handle.write("\n")
        print(f"{league_id} {season}: {len(events)} events -> {os.path.relpath(path)}")


if __name__ == "__main__":
    main()
