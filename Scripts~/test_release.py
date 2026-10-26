import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

import release

ROOT = Path(__file__).resolve().parents[1]

class ReleaseTests(unittest.TestCase):
    def test_package_excludes_private_data_and_preserves_metadata(self):
        files = release.package_files(ROOT)
        self.assertTrue(any(p.endswith(".cs") for p in files))
        for name in files:
            self.assertNotIn(name.split("/")[0], ["Working", "Generated", "Backup~", ".git"])
            self.assertNotIn(Path(name).suffix, [".anim", ".controller", ".asset", ".prefab", ".fbx", ".png"])
        self.assertIn("Editor/UI/Icons/LICENSE.txt", files)
        self.assertIn("Editor/UI/Icons/ATTRIBUTION.txt", files)

    def test_build_is_reproducible_and_zip_has_manifest_at_root(self):
        version = json.loads((ROOT / "package.json").read_text())["version"]
        with tempfile.TemporaryDirectory() as folder:
            out = Path(folder)
            first = release.build(ROOT, out / "a", "v" + version)
            second = release.build(ROOT, out / "b", "v" + version)
            self.assertEqual(first.read_bytes(), second.read_bytes())
            with zipfile.ZipFile(first) as archive:
                self.assertIn("package.json", archive.namelist())
                self.assertEqual(set(archive.namelist()), set(release.package_files(ROOT)))

    def test_tag_mismatch_is_rejected(self):
        with self.assertRaises(ValueError):
            release.manifest_for(ROOT, "v999.0.0")

    def test_versions(self):
        for invalid in ["1.0", "01.0.0", "1.0.0-01", "1.0.0\n", "1.0.0+build"]:
            with self.assertRaises(ValueError):
                release.version_parts(invalid)

    def fixtures(self):
        downloads = {}
        releases = []
        for version, draft in [("0.9.0", False), ("0.10.0", False), ("1.0.0-beta.1", False), ("2.0.0", True)]:
            filename = "RimeEmo_v" + version + ".zip"
            url = release.GITHUB + "/releases/download/v" + version + "/" + filename
            manifest_url = "manifest/" + version
            zip_url = "zip/" + version
            downloads[manifest_url] = json.dumps({"name": release.PACKAGE, "version": version, "url": url}).encode()
            downloads[zip_url] = ("zip-" + version).encode()
            releases.append({"tag_name": "v" + version, "draft": draft, "prerelease": "-" in version,
                             "assets": [{"name": "package.json", "url": manifest_url},
                                        {"name": filename, "url": zip_url, "browser_download_url": url}]})
        return releases, downloads

    def test_listing_uses_highest_stable_and_excludes_drafts(self):
        releases, downloads = self.fixtures()
        index, latest = release.collect_listing(releases, downloads.__getitem__)
        versions = index["packages"][release.PACKAGE]["versions"]
        self.assertEqual(latest["version"], "0.10.0")
        self.assertEqual(set(versions), {"0.9.0", "0.10.0", "1.0.0-beta.1"})
        self.assertEqual(versions["0.10.0"]["zipSHA256"], hashlib.sha256(b"zip-0.10.0").hexdigest())
        # Unpublishing the latest release rolls the listing back to the remaining stable.
        _, remaining = release.collect_listing([releases[0]], downloads.__getitem__)
        self.assertEqual(remaining["version"], "0.9.0")

    def test_partial_release_does_not_publish_broken_listing(self):
        releases, downloads = self.fixtures()
        releases[0]["assets"] = releases[0]["assets"][:1]
        with self.assertRaises(ValueError):
            release.collect_listing(releases, downloads.__getitem__)

    def test_prerelease_flag_must_match(self):
        releases, downloads = self.fixtures()
        releases[1]["prerelease"] = True
        with self.assertRaises(ValueError):
            release.collect_listing(releases, downloads.__getitem__)

if __name__ == "__main__":
    unittest.main()
