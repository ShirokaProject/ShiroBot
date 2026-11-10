import os
from pathlib import Path
import tempfile
import unittest
import zipfile
from release_zip import create_archive


class ReleaseZipTests(unittest.TestCase):
    def test_rebuild_is_identical_despite_paths_modes_times_and_input_order(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            digests = []
            for number in range(2):
                root = base / f"checkout-{number}"; root.mkdir()
                entries = [("bin/ShiroBot", b"executable contents\n" * 200), ("assets/中文.txt", "中文😀\n".encode()), ("index.html", b"<html>fixture</html>")]
                for name, data in entries[::1 if number == 0 else -1]:
                    path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)
                    os.utime(path, (1600000000 + number * 12345, 1700000000 + number * 23456))
                    path.chmod(0o777 if number == 0 else 0o600)
                output = base / f"release-{number}.zip"
                digests.append(create_archive(root, output, executable=["bin/ShiroBot"]))
                with zipfile.ZipFile(output) as archive:
                    self.assertEqual(set(archive.namelist()), {name for name, _ in entries})
                    self.assertIsNone(archive.testzip())
                    for name, data in entries:
                        self.assertEqual(archive.read(name), data)
                        info = archive.getinfo(name)
                        self.assertEqual(info.date_time, (1980, 1, 1, 0, 0, 0))
                        self.assertEqual(info.extra, b"")
                        self.assertIn(info.compress_type, [0, 8])
                        self.assertEqual((info.external_attr >> 16) & 0o777, 0o755 if name == "bin/ShiroBot" else 0o644)
                self.assertFalse(output.with_name(output.name + ".sha256").exists())
            self.assertEqual(digests[0], digests[1])

    def test_single_file_selection_omits_other_publish_outputs(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory); root = base / "publish"; root.mkdir()
            (root / "ShiroBot.exe").write_bytes(b"host fixture" * 100)
            (root / "ShiroBot.pdb").write_bytes(b"symbols")
            output = base / "windows.zip"
            create_archive(root, output, selected=["ShiroBot.exe"], executable=["ShiroBot.exe"])
            with zipfile.ZipFile(output) as archive:
                self.assertEqual(archive.namelist(), ["ShiroBot.exe"])

    def test_rejects_escaping_symlink_empty_or_missing_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory); root = base / "input"; root.mkdir()
            (base / "outside").write_text("keep")
            for selected in [None, ["../outside"], ["missing"]]:
                with self.assertRaises((ValueError, FileNotFoundError)):
                    create_archive(root, base / "bad.zip", selected)
            (root / "link").symlink_to(base / "outside")
            with self.assertRaises(ValueError):
                create_archive(root, base / "bad.zip")
            with self.assertRaises(ValueError):
                create_archive(root, root / "bad.zip")


if __name__ == "__main__":
    unittest.main()
