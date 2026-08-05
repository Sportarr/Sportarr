#!/usr/bin/env python3
"""Regenerate the agents/kodi/repo/ directory: addons.xml, addons.xml.md5,
and each addon's versioned zip (repo/<addon-id>/<addon-id>-<version>.zip -
the layout Kodi's repository datadir expects). Run after bumping either
addon's version - CI does this automatically as part of the release
workflow (see .github/workflows/release.yml, "Update Kodi Repository
Manifest").

Usage: python3 generate_repo.py
"""
import hashlib
import os
import re
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ADDON_DIRS = ["metadata.tvshows.sportarr", "repository.sportarr"]
OUT_DIR = os.path.join(HERE, "repo")


def read_addon_xml(addon_dir):
    path = os.path.join(HERE, addon_dir, "addon.xml")
    with open(path, "r", encoding="utf-8") as f:
        content = f.read()
    # Strip the XML declaration - addons.xml wraps each <addon> block
    # directly inside one <addons> root with a single declaration.
    content = re.sub(r"<\?xml[^>]*\?>\s*", "", content, count=1)
    return content.strip()


def addon_version(addon_dir):
    # Strip the XML declaration first - it has its own version="1.0"
    # attribute that would otherwise match before the addon's real version.
    content = read_addon_xml(addon_dir)
    match = re.search(r'<addon\b[^>]*\bversion="([^"]+)"', content, re.DOTALL)
    if not match:
        raise ValueError(f"No version attribute found in {addon_dir}/addon.xml")
    return match.group(1)


def zip_addon(addon_dir, version):
    """Zips <addon_dir>/ into repo/<addon_dir>/<addon_dir>-<version>.zip,
    with the addon's own folder as the zip root - the layout Kodi expects
    when installing from a repository datadir."""
    addon_out_dir = os.path.join(OUT_DIR, addon_dir)
    os.makedirs(addon_out_dir, exist_ok=True)
    zip_path = os.path.join(addon_out_dir, f"{addon_dir}-{version}.zip")

    src_dir = os.path.join(HERE, addon_dir)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for root, _dirs, files in os.walk(src_dir):
            for name in files:
                file_path = os.path.join(root, name)
                arcname = os.path.join(addon_dir, os.path.relpath(file_path, src_dir))
                zf.write(file_path, arcname)

    return zip_path


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    blocks = [read_addon_xml(d) for d in ADDON_DIRS]
    addons_xml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<addons>\n'
    addons_xml += "\n".join(blocks)
    addons_xml += "\n</addons>\n"

    addons_xml_path = os.path.join(OUT_DIR, "addons.xml")
    with open(addons_xml_path, "w", encoding="utf-8") as f:
        f.write(addons_xml)

    md5 = hashlib.md5(addons_xml.encode("utf-8")).hexdigest()
    with open(os.path.join(OUT_DIR, "addons.xml.md5"), "w", encoding="utf-8") as f:
        f.write(md5)

    print(f"Wrote {addons_xml_path} (md5 {md5})")

    for addon_dir in ADDON_DIRS:
        version = addon_version(addon_dir)
        zip_path = zip_addon(addon_dir, version)
        print(f"Wrote {zip_path}")


if __name__ == "__main__":
    main()
