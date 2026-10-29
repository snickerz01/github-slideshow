#!/usr/bin/env python3
"""Pack unity/Assets into a .unitypackage.

A .unitypackage is a gzipped tar where every top-level entry is a directory
named after the asset GUID and contains:

    pathname    the project-relative path, e.g. Assets/CarnivalWorld/Scripts/Foo.cs
    asset       the raw file bytes (empty for folders)
    asset.meta  the .meta YAML, which carries the same GUID

GUIDs are derived from the asset path, so rebuilds are stable: Unity does not
treat a rebuilt package as a different set of assets.

Usage:  python3 script/build-unitypackage.py
Output: dist/CarnivalWorld-<version>.unitypackage
"""

import gzip
import hashlib
import io
import pathlib
import sys
import tarfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "unity" / "Assets"
OUT_DIR = ROOT / "dist"
VERSION = "0.20.0"
OUT = OUT_DIR / ("CarnivalWorld-" + VERSION + ".unitypackage")

FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

MONO_META = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

DEFAULT_META = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def guid_for(rel_path):
    return hashlib.md5(("carnival-world:" + rel_path).encode("utf-8")).hexdigest()


def meta_for(rel_path, is_dir):
    g = guid_for(rel_path)

    if is_dir:
        return FOLDER_META.format(guid=g)
    if rel_path.endswith(".cs"):
        return MONO_META.format(guid=g)
    return DEFAULT_META.format(guid=g)


def collect():
    """Every folder and file under unity/Assets, parents included."""
    entries = []

    if not SRC.is_dir():
        sys.exit("missing source tree: " + str(SRC))

    for path in sorted(SRC.rglob("*")):
        rel = path.relative_to(ROOT).as_posix()

        if path.is_dir():
            entries.append((rel, b"", True))
        else:
            entries.append((rel, path.read_bytes(), False))

    return entries


def write_tar(entries):
    buffer = io.BytesIO()

    with tarfile.open(fileobj=buffer, mode="w") as tar:
        for rel, payload, is_dir in entries:
            folder = guid_for(rel)

            def add(name, data):
                info = tarfile.TarInfo(name=folder + "/" + name)
                info.size = len(data)
                info.mtime = 0
                info.mode = 0o644
                info.uid = 0
                info.gid = 0
                tar.addfile(info, io.BytesIO(data))

            add("pathname", rel.encode("utf-8"))
            add("asset", payload)
            add("asset.meta", meta_for(rel, is_dir).encode("utf-8"))

    return buffer.getvalue()


def verify(blob, expected_paths):
    """Reopen the archive and prove it is a well-formed package."""
    seen = {}
    problems = []

    with tarfile.open(fileobj=io.BytesIO(blob), mode="r:") as tar:
        for member in tar.getmembers():
            if "/" not in member.name:
                problems.append("stray top-level file: " + member.name)
                continue

            folder, name = member.name.split("/", 1)
            seen.setdefault(folder, set()).add(name)

        handles = {name for m in tar.getmembers() for name in [m.name]}

    for folder, names in seen.items():
        for required in ("pathname", "asset", "asset.meta"):
            if required not in names:
                problems.append(folder + " missing " + required)

    # pathnames must round-trip
    with tarfile.open(fileobj=io.BytesIO(blob), mode="r:") as tar:
        for member in tar.getmembers():
            if not member.name.endswith("/pathname"):
                continue
            folder = member.name.split("/", 1)[0]
            value = tar.extractfile(member).read().decode("utf-8")
            if value not in expected_paths:
                problems.append("unexpected pathname: " + value)
            if folder != guid_for(value):
                problems.append("guid mismatch for " + value)

    return problems


def main():
    entries = collect()
    blob = write_tar(entries)

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    with open(OUT, "wb") as handle:
        with gzip.GzipFile(fileobj=handle, mode="wb", mtime=0) as gz:
            gz.write(blob)

    problems = verify(blob, {rel for rel, _, _ in entries})
    size_kb = OUT.stat().st_size / 1024.0

    print("packed {0} assets into {1} ({2:.1f} KB)".format(len(entries), OUT.relative_to(ROOT), size_kb))

    for rel, payload, is_dir in entries:
        print("  {0:>7}  {1}".format("<dir>" if is_dir else str(len(payload)) + " B", rel))

    if problems:
        print("\nINVALID PACKAGE:")
        for problem in problems:
            print("  - " + problem)
        sys.exit(1)

    print("\nstructure verified: every entry has pathname, asset and asset.meta; GUIDs match pathnames.")

    # A package is a release artifact, not a history: keep exactly the build that
    # just verified. Pruning only after verification means a failed build never
    # takes the last good archive with it, and dist does not grow a copy per run
    # for someone to hand a superseded version out of.
    for stale in sorted(OUT_DIR.glob("CarnivalWorld-*.unitypackage")):
        if stale == OUT:
            continue

        stale.unlink()
        print("removed superseded " + stale.relative_to(ROOT).as_posix())


if __name__ == "__main__":
    main()
