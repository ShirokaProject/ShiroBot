"""Regression checks for preventing partially validated NuGet uploads."""
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import verify_nuget_release as release


class ReleaseGateTests(unittest.TestCase):
    def test_template_version_conflict_blocks_sdk_upload_too(self):
        requests = []
        def response(url, **kwargs):
            requests.append(url)
            versions = ["0.9.11"] if "templates" in url else ["0.9.8"]
            return io.BytesIO(json.dumps({"versions": versions}).encode())
        with tempfile.TemporaryDirectory() as root:
            directory = Path(root)
            (directory / release.CHECKSUMS).write_text('{"sdk": "verified"}')
            with patch.object(release, "PACKAGE_DIRECTORY", directory), \
                 patch.object(release, "validate_packages", return_value={"sdk": "verified"}), \
                 patch.object(release, "run_dotnet") as upload, \
                 patch.object(release.urllib.request, "urlopen", side_effect=response), \
                 patch.dict(os.environ, SDK_VERSION="0.9.9", TEMPLATE_VERSION="0.9.11", TEMPLATE_ONLY="false", NUGET_API_KEY="do-not-log"), \
                 patch.object(sys, "argv", ["verify", "--push"]):
                with self.assertRaisesRegex(RuntimeError, "No packages uploaded"):
                    release.main()
                upload.assert_not_called()
                self.assertEqual(len(requests), 2)

    def test_changed_artifact_blocks_network_and_upload(self):
        with tempfile.TemporaryDirectory() as root:
            directory = Path(root)
            (directory / release.CHECKSUMS).write_text('{"sdk": "old"}')
            with patch.object(release, "PACKAGE_DIRECTORY", directory), \
                 patch.object(release, "validate_packages", return_value={"sdk": "changed"}), \
                 patch.object(release, "ensure_unpublished") as preflight, \
                 patch.object(release, "run_dotnet") as upload, \
                 patch.dict(os.environ, SDK_VERSION="0.9.9", TEMPLATE_VERSION="0.9.11", TEMPLATE_ONLY="false"), \
                 patch.object(sys, "argv", ["verify", "--push"]):
                with self.assertRaisesRegex(RuntimeError, "artifacts"):
                    release.main()
                preflight.assert_not_called()
                upload.assert_not_called()

    def test_failed_consumer_build_cannot_keep_previous_success(self):
        with tempfile.TemporaryDirectory() as root:
            directory = Path(root)
            (directory / release.CHECKSUMS).write_text('{"sdk": "previous-success"}')
            with patch.object(release, "PACKAGE_DIRECTORY", directory), \
                 patch.object(release, "validate_packages", return_value={"sdk": "verified"}), \
                 patch.object(release, "verify_consumers", side_effect=RuntimeError("consumer build failed")), \
                 patch.dict(os.environ, SDK_VERSION="0.9.9", TEMPLATE_VERSION="0.9.11", TEMPLATE_ONLY="false"), \
                 patch.object(sys, "argv", ["verify"]):
                with self.assertRaisesRegex(RuntimeError, "consumer build failed"):
                    release.main()
                self.assertFalse((directory / release.CHECKSUMS).exists())


if __name__ == "__main__":
    unittest.main()
