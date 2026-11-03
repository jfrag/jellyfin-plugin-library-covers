#!/usr/bin/env python3
"""Ajoute (ou remplace) une version du plugin dans manifest.json, le dépôt lu par le catalogue Jellyfin.

Usage : update_manifest.py MANIFEST META VERSION SOURCE_URL CHECKSUM TIMESTAMP
"""
import json
import sys
from pathlib import Path


def main() -> None:
    manifest_path, meta_path, version, source_url, checksum, timestamp = sys.argv[1:7]
    meta = json.loads(Path(meta_path).read_text(encoding="utf-8"))

    path = Path(manifest_path)
    manifest = json.loads(path.read_text(encoding="utf-8")) if path.exists() else []
    previous = next((p for p in manifest if p.get("guid") == meta["guid"]), {})

    entry = {
        "version": version,
        "changelog": meta.get("changelog", ""),
        "targetAbi": meta["targetAbi"],
        "sourceUrl": source_url,
        "checksum": checksum,
        "timestamp": timestamp,
    }
    versions = [entry] + [v for v in previous.get("versions", []) if v.get("version") != version]

    plugin = {
        "guid": meta["guid"],
        "name": meta["name"],
        "description": meta["description"],
        "overview": meta["overview"],
        "owner": meta["owner"],
        "category": meta["category"],
        "versions": versions,
    }
    if previous.get("imageUrl"):
        plugin["imageUrl"] = previous["imageUrl"]

    manifest = [plugin if p.get("guid") == meta["guid"] else p for p in manifest]
    if not previous:
        manifest.append(plugin)

    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
