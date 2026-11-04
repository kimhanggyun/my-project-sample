#!/usr/bin/env python3
"""src/(Unity 로컬 패키지)의 파일·폴더마다 .meta를 만든다. GUID는 경로에서 정해지므로 다시 돌려도 같다.
이미 있는 .meta는 건드리지 않는다 (Unity가 만든 것 포함). 사용: python3 tools/unity_meta.py"""
import hashlib, os, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src")
SKIP_DIRS = {"bin", "obj"}

def guid(rel):
    return hashlib.md5(("gamecore/" + rel.replace(os.sep, "/")).encode()).hexdigest()

def body(rel, is_dir):
    g = guid(rel)
    if is_dir:
        return f"fileFormatVersion: 2\nguid: {g}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    ext = os.path.splitext(rel)[1]
    if ext == ".cs":
        return (f"fileFormatVersion: 2\nguid: {g}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n"
                "  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    if ext == ".asmdef":
        return f"fileFormatVersion: 2\nguid: {g}\nAssemblyDefinitionImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    if os.path.basename(rel) == "package.json":
        return f"fileFormatVersion: 2\nguid: {g}\nPackageManifestImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    return f"fileFormatVersion: 2\nguid: {g}\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"

made = 0
for dirpath, dirnames, filenames in os.walk(ROOT):
    dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS and not d.startswith("."))
    rel_dir = os.path.relpath(dirpath, ROOT)
    items = [(d, True) for d in dirnames] + [(f, False) for f in sorted(filenames) if not f.endswith(".meta") and not f.startswith(".")]
    for name, is_dir in items:
        rel = os.path.normpath(os.path.join(rel_dir, name))
        meta = os.path.join(ROOT, rel + ".meta")
        if os.path.exists(meta):
            continue
        with open(meta, "w", newline="\n") as f:
            f.write(body(rel, is_dir))
        made += 1
print(f"made {made} .meta files")
