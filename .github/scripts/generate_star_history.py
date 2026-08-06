#!/usr/bin/env python3
"""
Generates a self-hosted star history SVG chart from real stargazer data.

GitHub restricts the stargazers-with-timestamps endpoint to a repo's own
admins/collaborators, so the live badges third-party sites used to embed
(e.g. api.star-history.com/svg) render blank for outside viewers now. This
script runs inside the repo's own GitHub Actions workflow, where the
default GITHUB_TOKEN already has that access, fetches the real stargazer
timeline, and renders a static SVG that gets committed to the repo - no
external service, no write access handed to a third party.

Stdlib only, no pip install needed.
"""
import json
import os
import sys
import urllib.request
from datetime import datetime, timezone

REPO = os.environ["GITHUB_REPOSITORY"]
TOKEN = os.environ["GITHUB_TOKEN"]
OUT_DIR = os.environ.get("STAR_HISTORY_OUT_DIR", "assets/star-history")

API_ROOT = f"https://api.github.com/repos/{REPO}/stargazers"


def fetch_stargazer_timestamps():
    timestamps = []
    url = f"{API_ROOT}?per_page=100"
    while url:
        req = urllib.request.Request(
            url,
            headers={
                "Authorization": f"Bearer {TOKEN}",
                "Accept": "application/vnd.github.star+json",
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "sportarr-star-history-script",
            },
        )
        with urllib.request.urlopen(req) as resp:
            batch = json.loads(resp.read())
            link_header = resp.headers.get("Link", "")

        for entry in batch:
            timestamps.append(
                datetime.strptime(entry["starred_at"], "%Y-%m-%dT%H:%M:%SZ").replace(
                    tzinfo=timezone.utc
                )
            )

        url = None
        for part in link_header.split(","):
            if 'rel="next"' in part:
                url = part.split(";")[0].strip().strip("<>")

    timestamps.sort()
    return timestamps


def build_points(timestamps):
    """Cumulative star count over time, one point per star event."""
    if not timestamps:
        return []
    points = [(timestamps[0], 0)]
    for i, ts in enumerate(timestamps, start=1):
        points.append((ts, i))
    points.append((datetime.now(timezone.utc), len(timestamps)))
    return points


def render_svg(points, *, dark: bool) -> str:
    width, height = 800, 400
    pad_left, pad_right, pad_top, pad_bottom = 60, 30, 30, 40
    plot_w = width - pad_left - pad_right
    plot_h = height - pad_top - pad_bottom

    if not points:
        text_color = "#c9d1d9" if dark else "#24292f"
        return (
            f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}">'
            f'<text x="{width // 2}" y="{height // 2}" fill="{text_color}" '
            f'font-family="sans-serif" font-size="16" text-anchor="middle">'
            f"No star data yet</text></svg>"
        )

    line_color = "#58a6ff" if dark else "#0969da"
    fill_color = "rgba(88,166,255,0.15)" if dark else "rgba(9,105,218,0.12)"
    grid_color = "#30363d" if dark else "#d0d7de"
    text_color = "#c9d1d9" if dark else "#24292f"

    t0 = points[0][0].timestamp()
    t1 = points[-1][0].timestamp()
    t_span = max(t1 - t0, 1)
    max_stars = max(1, points[-1][1])

    def x_for(ts):
        return pad_left + (ts.timestamp() - t0) / t_span * plot_w

    def y_for(count):
        return pad_top + plot_h - (count / max_stars * plot_h)

    line_pts = [(x_for(ts), y_for(c)) for ts, c in points]
    line_path = "M " + " L ".join(f"{x:.1f},{y:.1f}" for x, y in line_pts)
    area_path = (
        line_path
        + f" L {line_pts[-1][0]:.1f},{pad_top + plot_h:.1f}"
        + f" L {line_pts[0][0]:.1f},{pad_top + plot_h:.1f} Z"
    )

    # Y-axis gridlines/labels (5 evenly spaced steps).
    y_gridlines = []
    for i in range(5):
        frac = i / 4
        count = round(max_stars * frac)
        y = y_for(count)
        y_gridlines.append(
            f'<line x1="{pad_left}" y1="{y:.1f}" x2="{width - pad_right}" y2="{y:.1f}" '
            f'stroke="{grid_color}" stroke-width="1" stroke-dasharray="2,3" />'
            f'<text x="{pad_left - 10}" y="{y + 4:.1f}" fill="{text_color}" '
            f'font-family="sans-serif" font-size="11" text-anchor="end">{count}</text>'
        )

    # X-axis date labels (5 evenly spaced steps).
    x_labels = []
    for i in range(5):
        frac = i / 4
        ts = datetime.fromtimestamp(t0 + frac * t_span, tz=timezone.utc)
        x = x_for(ts)
        label = ts.strftime("%b %Y")
        x_labels.append(
            f'<text x="{x:.1f}" y="{height - pad_bottom + 20}" fill="{text_color}" '
            f'font-family="sans-serif" font-size="11" text-anchor="middle">{label}</text>'
        )

    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">
  <rect width="{width}" height="{height}" fill="none" />
  {"".join(y_gridlines)}
  {"".join(x_labels)}
  <path d="{area_path}" fill="{fill_color}" stroke="none" />
  <path d="{line_path}" fill="none" stroke="{line_color}" stroke-width="2" />
  <text x="{pad_left}" y="18" fill="{text_color}" font-family="sans-serif" font-size="13" font-weight="600">
    {REPO} - {max_stars} stars
  </text>
</svg>'''


def main():
    timestamps = fetch_stargazer_timestamps()
    points = build_points(timestamps)

    os.makedirs(OUT_DIR, exist_ok=True)
    with open(os.path.join(OUT_DIR, "star-history-light.svg"), "w") as f:
        f.write(render_svg(points, dark=False))
    with open(os.path.join(OUT_DIR, "star-history-dark.svg"), "w") as f:
        f.write(render_svg(points, dark=True))

    print(f"Wrote star history charts for {REPO}: {len(timestamps)} stars", file=sys.stderr)


if __name__ == "__main__":
    main()
