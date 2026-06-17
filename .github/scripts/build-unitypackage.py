#!/usr/bin/env python3
"""Build a .unitypackage from this UPM (repo-root) package, no Unity license needed.

The repository is laid out as a Unity Package Manager package at the repo root
(installable via a git URL). A .unitypackage, by contrast, bakes a fixed
`Assets/...` import path into every asset. This script bridges the two from a
single branch: it reads the package source here and emits a .unitypackage whose
assets import into the historical `Assets/Plugins/ProjectMakers/horizOn/...`
location, so existing .unitypackage users keep the same layout.

A .unitypackage is a gzip-compressed tar where each asset is a directory named by
its Unity GUID containing:
    <guid>/pathname    the project-relative import path (Assets/...)
    <guid>/asset.meta  the asset's .meta file (carries the same GUID)
    <guid>/asset       the raw asset bytes (omitted for folder assets)

We reuse the committed .meta GUIDs, so GUIDs are stable across releases and match
the UPM install (no broken references when a project switches install methods).

Usage:
    python3 .github/scripts/build-unitypackage.py [--output FILE] [--root DIR]

If --output is omitted, the name defaults to horizOn-SDK-v<version>.unitypackage
using the version from package.json.
"""
import argparse
import io
import json
import os
import re
import sys
import tarfile

# (source path relative to repo root) -> (import path baked into the .unitypackage)
HORIZON = "Assets/Plugins/ProjectMakers/horizOn"
DIR_MAPPINGS = [
    ("CloudSDK", f"{HORIZON}/CloudSDK"),
    ("Samples~/Examples", f"{HORIZON}/CloudSDK/Examples"),
    ("Samples~/ExampleUI", f"{HORIZON}/CloudSDK/ExampleUI"),
    ("Documentation~", f"{HORIZON}/Documentation"),
]
FILE_MAPPINGS = [
    ("README.md", f"{HORIZON}/README.md"),
    ("QUICKSTART.md", f"{HORIZON}/QUICKSTART.md"),
    ("LICENSE.md", f"{HORIZON}/LICENSE.md"),
]
# Never shipped: the SDK's own consumer-side config Resources, which the editor
# importer creates locally and which is gitignored. Paths are relative to the repo
# root. Sample Resources folders (e.g. Samples~/ExampleUI/Resources, holding the
# demo UXML and PanelSettings) are intentionally NOT skipped.
SKIP_DIRS = {"CloudSDK/Resources"}


def read_guid(meta_path):
    with open(meta_path, encoding="utf-8") as fh:
        for line in fh:
            if line.startswith("guid:"):
                return line.split(":", 1)[1].strip()
    raise ValueError(f"no guid in {meta_path}")


class Package:
    def __init__(self, root):
        self.root = root
        self.entries = []  # (guid, import_path, src_abs, is_dir)
        self.seen_guids = {}

    def add(self, src_abs, import_path, is_dir):
        meta = src_abs + ".meta"
        if not os.path.exists(meta):
            raise SystemExit(
                f"FATAL: missing .meta for {src_abs}; every shipped asset must have one"
            )
        guid = read_guid(meta)
        if guid in self.seen_guids:
            raise SystemExit(
                f"FATAL: duplicate GUID {guid}\n  {self.seen_guids[guid]}\n  {import_path}"
            )
        self.seen_guids[guid] = import_path
        self.entries.append((guid, import_path, src_abs, is_dir))

    def add_dir_tree(self, src_rel, dst_root):
        src_abs = os.path.join(self.root, src_rel)
        if not os.path.isdir(src_abs):
            print(f"WARNING: missing dir {src_rel}", file=sys.stderr)
            return
        if os.path.exists(src_abs + ".meta"):
            self.add(src_abs, dst_root, is_dir=True)  # the mapped root folder itself
        else:
            # tilde folders (e.g. Documentation~) carry no folder .meta; their
            # contents are still mapped and the folder is created on import.
            print(f"note: {src_rel} has no folder .meta; mapping its contents only", file=sys.stderr)
        for dirpath, dirs, files in os.walk(src_abs):
            kept = []
            for d in dirs:
                rel = os.path.relpath(os.path.join(dirpath, d), self.root).replace(os.sep, "/")
                if rel not in SKIP_DIRS:
                    kept.append(d)
            dirs[:] = sorted(kept)
            for d in dirs:
                sp = os.path.join(dirpath, d)
                rel = os.path.relpath(sp, src_abs).replace(os.sep, "/")
                self.add(sp, f"{dst_root}/{rel}", is_dir=True)
            for f in sorted(files):
                if f.endswith(".meta"):
                    continue
                sp = os.path.join(dirpath, f)
                rel = os.path.relpath(sp, src_abs).replace(os.sep, "/")
                self.add(sp, f"{dst_root}/{rel}", is_dir=False)

    def add_file(self, src_rel, dst):
        src_abs = os.path.join(self.root, src_rel)
        if not os.path.isfile(src_abs):
            print(f"WARNING: missing file {src_rel}", file=sys.stderr)
            return
        self.add(src_abs, dst, is_dir=False)


def _tarinfo(name, size):
    ti = tarfile.TarInfo(name)
    ti.size = size
    ti.mtime = 0  # deterministic output
    ti.mode = 0o644
    return ti


def write_unitypackage(pkg, out_path):
    files = 0
    with tarfile.open(out_path, "w:gz") as tar:
        for guid, import_path, src_abs, is_dir in pkg.entries:
            with open(src_abs + ".meta", "rb") as fh:
                meta_bytes = fh.read()
            tar.addfile(_tarinfo(f"{guid}/asset.meta", len(meta_bytes)), io.BytesIO(meta_bytes))
            path_bytes = (import_path + "\n").encode("utf-8")
            tar.addfile(_tarinfo(f"{guid}/pathname", len(path_bytes)), io.BytesIO(path_bytes))
            if not is_dir:  # folder assets have no `asset` blob
                with open(src_abs, "rb") as fh:
                    data = fh.read()
                tar.addfile(_tarinfo(f"{guid}/asset", len(data)), io.BytesIO(data))
                files += 1
    return files


def default_output(root):
    with open(os.path.join(root, "package.json"), encoding="utf-8") as fh:
        version = json.load(fh)["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise SystemExit(f"package.json version is not SemVer: {version!r}")
    return f"horizOn-SDK-v{version}.unitypackage"


def find_repo_root(start):
    """Walk up from `start` until the directory that holds package.json."""
    d = start
    while d != os.path.dirname(d):
        if os.path.exists(os.path.join(d, "package.json")):
            return d
        d = os.path.dirname(d)
    return start


def main(argv):
    here = os.path.dirname(os.path.abspath(__file__))
    default_root = find_repo_root(here)
    ap = argparse.ArgumentParser(description="Build a .unitypackage from the UPM package source.")
    ap.add_argument("--root", default=default_root, help="repo root (default: parent of tools/)")
    ap.add_argument("--output", default=None, help="output .unitypackage path")
    args = ap.parse_args(argv[1:])

    out = args.output or default_output(args.root)
    pkg = Package(args.root)
    for src_rel, dst in DIR_MAPPINGS:
        pkg.add_dir_tree(src_rel, dst)
    for src_rel, dst in FILE_MAPPINGS:
        pkg.add_file(src_rel, dst)

    files = write_unitypackage(pkg, out)
    folders = len(pkg.entries) - files
    print(f"Wrote {out}")
    print(f"  {len(pkg.entries)} assets ({files} files, {folders} folders)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
