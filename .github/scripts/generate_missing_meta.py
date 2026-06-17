#!/usr/bin/env python3
"""Backfill Unity .meta files for assets/folders that are missing one.

Unity identifies every asset by the GUID stored in its sibling .meta file. A few
source files in this repository were committed without their .meta, which means
(a) the legacy .unitypackage build silently dropped them and (b) a Package
Manager (UPM) install would let Unity generate fresh, non-deterministic GUIDs on
every machine, breaking cross-asset references. This script creates the missing
.meta files with stable, collision-checked GUIDs and the canonical importer block
for each asset type, matching the format Unity itself writes.

Usage:
    python3 .github/scripts/generate_missing_meta.py <root> [<root> ...]

It only touches files that lack a .meta; existing .meta files are never modified.
Folders inside a `Resources/` segment are skipped (those are gitignored,
consumer-side config, never shipped).
"""
import os
import sys
import uuid

# Importer block (everything after the `guid:` line) per asset kind.
MONO = """MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
"""
TEXT = """TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"""
DEFAULT = """DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"""
FOLDER = """folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

TEXT_EXTS = {".md", ".txt", ".json", ".xml", ".csv", ".yml", ".yaml"}


def importer_for(path, is_dir):
    if is_dir:
        return FOLDER
    ext = os.path.splitext(path)[1].lower()
    if ext == ".cs":
        return MONO
    if ext in TEXT_EXTS:
        return TEXT
    return DEFAULT


def collect_existing_guids(roots):
    guids = set()
    for root in roots:
        for dirpath, _dirs, files in os.walk(root):
            for f in files:
                if f.endswith(".meta"):
                    try:
                        with open(os.path.join(dirpath, f), encoding="utf-8") as fh:
                            for line in fh:
                                if line.startswith("guid:"):
                                    guids.add(line.split(":", 1)[1].strip())
                                    break
                    except OSError:
                        pass
    return guids


def new_guid(used):
    while True:
        g = uuid.uuid4().hex  # 32 lowercase hex chars, Unity's GUID format
        if g not in used:
            used.add(g)
            return g


def write_meta(asset_path, is_dir, used):
    meta_path = asset_path + ".meta"
    if os.path.exists(meta_path):
        return None
    guid = new_guid(used)
    body = f"fileFormatVersion: 2\nguid: {guid}\n{importer_for(asset_path, is_dir)}"
    with open(meta_path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(body)
    return guid


def is_skipped(path):
    parts = path.replace("\\", "/").split("/")
    if "Resources" in parts:
        return True
    base = os.path.basename(path)
    return base.startswith(".")  # .DS_Store, .git, etc.


def main(argv):
    roots = argv[1:]
    if not roots:
        print(__doc__)
        return 1
    used = collect_existing_guids(roots)
    created = []
    for root in roots:
        for dirpath, dirs, files in os.walk(root):
            # prune skipped dirs in place
            dirs[:] = [d for d in dirs if not is_skipped(os.path.join(dirpath, d))]
            # folder metas (skip the top-level root itself; its meta is a sibling)
            if os.path.abspath(dirpath) != os.path.abspath(root):
                if not is_skipped(dirpath):
                    g = write_meta(dirpath, True, used)
                    if g:
                        created.append((dirpath, g))
            for f in sorted(files):
                if f.endswith(".meta") or f.startswith("."):
                    continue
                p = os.path.join(dirpath, f)
                if is_skipped(p):
                    continue
                g = write_meta(p, False, used)
                if g:
                    created.append((p, g))
    for path, guid in created:
        print(f"created {path}.meta  guid={guid}")
    print(f"\nTotal .meta created: {len(created)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
