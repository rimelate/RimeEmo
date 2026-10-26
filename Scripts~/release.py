"""Build audited VPM archives and the VPM listing. Python 3.10+, standard library only."""
import argparse
import hashlib
import html
import json
import os
from pathlib import Path
import re
import urllib.parse
import urllib.request
import zipfile

PACKAGE = "io.github.rimelate.rimeemo"
REPOSITORY = "rimelate/RimeEmo"
GITHUB = "https://github.com/" + REPOSITORY
LISTING = "https://raw.githubusercontent.com/" + REPOSITORY + "/vpm/index.json"
# Only known source and UI files can enter a package. No animations, textures,
# avatar settings, generated controllers, local backups or arbitrary binaries.
CODE_EXTENSIONS = {".cs", ".asmdef"}
UI_FILES = {
    "README.md", "RimeEmoWindow.uxml", "RimeEmoGestureWindow.uxml",
    "RimeEmoClassificationWindow.uxml", "RimeEmoTheme.uss", "RimeEmoCustom.uss",
    "Icons/ATTRIBUTION.txt", "Icons/LICENSE.txt",
    "Icons/MaterialIcons-Regular.codepoints.txt", "Icons/MaterialIcons-Regular.ttf",
    "Icons/MaterialSymbolsRounded-Fill-Subset.ttf",
    "Icons/MaterialSymbolsRounded-Subset.codepoints.txt",
    "Icons/MaterialSymbolsRounded-Subset.ttf",
}
ROOT_FILES = {"package.json", "README.md", "CHANGELOG.md"}
VERSION = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?")

def version_parts(value):
    match = VERSION.fullmatch(value)
    if not match:
        raise ValueError("Use a SemVer version without build metadata: " + value)
    if match[4] and any(p.isdigit() and len(p) > 1 and p[0] == "0" for p in match[4].split(".")):
        raise ValueError("Invalid prerelease identifier")
    return tuple(int(match[i]) for i in (1, 2, 3)), match[4]

def manifest_for(root, tag):
    manifest = json.loads((root / "package.json").read_text(encoding="utf-8-sig"))
    version_parts(manifest["version"])
    if manifest["name"] != PACKAGE or tag != "v" + manifest["version"]:
        raise ValueError("Release tag must match package.json: v" + manifest["version"])
    expected = GITHUB + "/releases/download/" + tag + "/" + PACKAGE + "-" + manifest["version"] + ".zip"
    if manifest["url"] != expected:
        raise ValueError("Update package.json url to match the release tag")
    # Restrict migration to code folders; never remove Working or Generated.
    expected_folders = {r"Assets\rimerime\RimeEmo\Editor", r"Assets\rimerime\RimeEmo\Runtime"}
    if set(manifest.get("legacyFolders", {})) != expected_folders:
        raise ValueError("legacyFolders must only contain the Editor and Runtime code folders")
    return manifest

def package_files(root):
    selected = set(ROOT_FILES)
    for area in ("Editor", "Runtime"):
        for path in (root / area).rglob("*"):
            if path.is_symlink():
                raise ValueError("Symlinks are not allowed: " + str(path))
            if not path.is_file() or path.suffix == ".meta":
                continue
            rel = path.relative_to(root).as_posix()
            allowed = (path.parent == root / area and path.suffix in CODE_EXTENSIONS)
            if area == "Editor" and rel.startswith("Editor/UI/"):
                allowed = rel[len("Editor/UI/"):] in UI_FILES
            if not allowed:
                raise ValueError("Unreviewed file in distribution source: " + rel)
            selected.add(rel)
    for name in list(selected):
        source = root / name
        if not source.is_file() or source.is_symlink():
            raise ValueError("Missing or unsafe source: " + name)
        if (root / (name + ".meta")).is_file():
            selected.add(name + ".meta")
        parent = source.parent
        while parent != root:
            meta = parent.with_name(parent.name + ".meta")
            if not meta.is_file():
                raise ValueError("Missing folder metadata: " + str(meta))
            selected.add(meta.relative_to(root).as_posix())
            parent = parent.parent
    # All Unity sources need stable GUIDs, also in clones that have never opened Unity.
    for name in selected:
        if name.startswith(("Editor/", "Runtime/")) and not name.endswith(".meta") and name + ".meta" not in selected:
            raise ValueError("Missing file metadata: " + name)
    return sorted(selected)

def build(root, output, tag):
    manifest = manifest_for(root, tag)
    files = package_files(root)
    output.mkdir(parents=True, exist_ok=True)
    archive = output / (PACKAGE + "-" + manifest["version"] + ".zip")
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zipped:
        for name in files:
            info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            zipped.writestr(info, (root / name).read_bytes())
    write_json(output / "package.json", manifest)
    audit = {"archive": archive.name, "sha256": hashlib.sha256(archive.read_bytes()).hexdigest(), "files": files}
    write_json(output / "package-audit.json", audit)
    print("Built", archive.name, "with", len(files), "reviewed files")
    return archive

def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

def request(url, binary=False):
    # Credentials are only sent to the GitHub API, never to release redirects.
    if not url.startswith("https://api.github.com/repos/" + REPOSITORY + "/"):
        raise ValueError("Unexpected API URL")
    headers = {"Accept": "application/octet-stream" if binary else "application/vnd.github+json",
               "User-Agent": "RimeEmo-release"}
    token = os.environ.get("GH_TOKEN")
    if token:
        headers["Authorization"] = "Bearer " + token
    class Redirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, req, fp, code, msg, headers, newurl):
            if not newurl.startswith("https://"):
                raise ValueError("Non-HTTPS release redirect")
            redirected = super().redirect_request(req, fp, code, msg, headers, newurl)
            redirected.remove_header("Authorization")
            return redirected
    opener = urllib.request.build_opener(Redirect())
    with opener.open(urllib.request.Request(url, headers=headers), timeout=60) as response:
        data = response.read()
    return data if binary else json.loads(data)

def collect_listing(releases, download):
    versions = {}
    stable = []
    for release in releases:
        if release["draft"]:
            continue
        assets = {asset["name"]: asset for asset in release.get("assets", [])}
        if "package.json" not in assets:
            continue
        manifest = json.loads(download(assets["package.json"]["url"]))
        if manifest.get("name") != PACKAGE:
            raise ValueError("Unexpected package in release " + release["tag_name"])
        version = manifest["version"]
        core, prerelease = version_parts(version)
        if release["tag_name"] != "v" + version:
            raise ValueError("Tag/manifest mismatch")
        if bool(release["prerelease"]) != bool(prerelease):
            raise ValueError("GitHub prerelease flag must agree with the SemVer version")
        filename = PACKAGE + "-" + version + ".zip"
        expected = GITHUB + "/releases/download/v" + version + "/" + filename
        if filename not in assets or assets[filename]["browser_download_url"] != expected or manifest.get("url") != expected:
            raise ValueError("Package archive missing or URL mismatch for " + version)
        manifest["zipSHA256"] = hashlib.sha256(download(assets[filename]["url"])).hexdigest()
        versions[version] = manifest
        if not prerelease:
            stable.append((core, version))
    listing = {"name": "RimeEmo", "id": PACKAGE + ".repository", "url": LISTING,
               "author": "rimelate", "packages": {PACKAGE: {"versions": versions}}}
    latest = {"name": PACKAGE, "version": "", "releaseUrl": ""}
    if stable:
        version = max(stable)[1]
        latest.update(version=version, releaseUrl=GITHUB + "/releases/tag/v" + version)
    return listing, latest

def listing(output):
    releases = []
    page = 1
    while True:
        batch = request("https://api.github.com/repos/" + REPOSITORY + "/releases?per_page=100&page=" + str(page))
        releases.extend(batch)
        if len(batch) < 100:
            break
        page += 1
    index, latest = collect_listing(releases, lambda url: request(url, binary=True))
    output.mkdir(parents=True, exist_ok=True)
    write_json(output / "index.json", index)
    write_json(output / "latest.json", latest)
    # Optional GitHub Pages landing page; raw VPM URLs work without Pages.
    link = "vcc://vpm/addRepo?url=" + urllib.parse.quote(LISTING, safe="")
    page_html = f"""<!doctype html><html lang="ja"><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><title>RimeEmo — VPM</title>
<style>body{{font:18px system-ui;max-width:760px;margin:80px auto;padding:24px;background:#16151d;color:#eee}}a{{color:#c6b5ff}}code{{overflow-wrap:anywhere}}.button{{display:inline-block;padding:16px 28px;border-radius:28px;background:#c6b5ff;color:#211638;text-decoration:none}}</style>
<h1>RimeEmo</h1><p>VRChat向け 表情・動き編集ツール</p>
<p><a class="button" href="{html.escape(link)}">VCCに追加</a></p>
<p>ALCOMでは下記URLをリポジトリとして追加してください。</p><p><code>{LISTING}</code></p>
<p>最新版: {html.escape(latest["version"] or "未公開")}</p>
<p><a href="{GITHUB}#readme">使い方</a> · <a href="{GITHUB}/releases">更新内容</a></p></html>"""
    (output / "index.html").write_text(page_html, encoding="utf-8")
    (output / ".nojekyll").write_text("", encoding="utf-8")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["build", "listing"])
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--tag")
    args = parser.parse_args()
    if args.command == "build":
        if not args.tag:
            parser.error("build requires --tag")
        build(args.root.resolve(), args.output, args.tag)
    else:
        listing(args.output)
