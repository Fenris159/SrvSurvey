"""Exercise release dependency setup without downloading or installing host packages."""

import hashlib
import os
from pathlib import Path
import re
import subprocess
import tempfile
import textwrap
import unittest


ROOT = Path(__file__).resolve().parents[2]


class ReleaseSourceTests(unittest.TestCase):
    def test_every_checkout_uses_the_workflow_commit(self):
        workflow = (ROOT / ".github/workflows/build-srvsurvey-xp.yml").read_text()
        refs = re.findall(r"^\s+ref:\s*(.+)$", workflow, re.MULTILINE)
        self.assertEqual(["${{ github.sha }}"] * 4, refs)

    def test_conflicting_manual_source_is_rejected_before_checkout(self):
        workflow = (ROOT / ".github/workflows/build-srvsurvey-xp.yml").read_text()
        guard = re.search(
            r"      - name: Validate manual release source\n[\s\S]*?        run: \|\n((?:          .*\n|\n)+)",
            workflow,
        )
        self.assertIsNotNone(guard)
        self.assertLess(guard.start(), workflow.index("      - name: Check out source"))
        body = textwrap.dedent(guard.group(1))
        sha = "1234567890abcdef1234567890abcdef12345678"
        for event, requested, ref, expected in [
            ("workflow_dispatch", "", "SrvSurvey-Avalonia", 0),
            ("workflow_dispatch", "SrvSurvey-Avalonia", "SrvSurvey-Avalonia", 0),
            ("workflow_dispatch", "release-tag", "release-tag", 0),
            ("workflow_dispatch", sha, "release-tag", 0),
            ("workflow_dispatch", "unreviewed-branch", "SrvSurvey-Avalonia", 2),
            ("workflow_dispatch", "f" * 40, "SrvSurvey-Avalonia", 2),
            ("pull_request", "", "246/merge", 0),
        ]:
            with self.subTest(event=event, requested=requested, ref=ref):
                result = subprocess.run(
                    ["bash", "-eu", "-o", "pipefail", "-c", body],
                    env={
                        **os.environ,
                        "GITHUB_EVENT_NAME": event,
                        "REQUESTED_SOURCE_REF": requested,
                        "WORKFLOW_REF_NAME": ref,
                        "WORKFLOW_SHA": sha,
                    },
                    capture_output=True,
                    text=True,
                    check=False,
                    timeout=5,
                )
                self.assertEqual(expected, result.returncode, result.stderr)


class ReleaseDependencyTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.work = Path(self.directory.name)
        self.bin = self.work / "bin"
        self.bin.mkdir()
        self.cache = self.work / "cache with spaces"
        self.calls = self.work / "calls"
        self.env = {
            **os.environ,
            "PATH": f"{self.bin}:{os.environ['PATH']}",
            "TEST_CALLS": str(self.calls),
        }
        self.command("sudo", '[[ $1 != -n ]] || shift\nexec "$@"')
        self.command("dpkg-query", 'printf "%s" "${TEST_PACKAGE_STATUS:-installed}"')
        self.command(
            "apt-get",
            'printf "%s\\n" "$*" >> "$TEST_CALLS"\n'
            'if [[ $* == *"--download-only"* && ${TEST_DOWNLOAD_FAILURE:-0} == 1 ]]; then exit 1; fi',
        )

    def command(self, name, body):
        path = self.bin / name
        path.write_text(f"#!/usr/bin/env bash\nset -euo pipefail\n{body}\n")
        path.chmod(0o755)

    def run_script(self, name, *arguments):
        return subprocess.run(
            ["bash", str(ROOT / "scripts" / name), *map(str, arguments)],
            env=self.env,
            capture_output=True,
            text=True,
            check=False,
            timeout=10,
        )

    def test_installed_dependencies_do_not_update_or_download(self):
        result = self.run_script("Install-LinuxReleaseDependencies.sh", "build", self.cache)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse(self.calls.exists(), "An already provisioned runner must not contact APT.")

    def test_only_missing_packages_are_installed_without_recommendations(self):
        self.command(
            "dpkg-query",
            'if [[ ${*: -1} != desktop-file-utils ]]; then printf installed; else exit 1; fi',
        )
        result = self.run_script("Install-LinuxReleaseDependencies.sh", "build", self.cache)
        self.assertEqual(0, result.returncode, result.stderr)
        calls = self.calls.read_text().splitlines()
        self.assertEqual(3, len(calls))
        self.assertIn("update", calls[0])
        self.assertIn("--download-only", calls[1])
        self.assertIn("--no-download", calls[2])
        for call in calls[1:]:
            self.assertIn("--no-install-recommends", call)
            self.assertTrue(call.endswith("install desktop-file-utils"), call)
            self.assertIn(f"Dir::Cache::archives={self.cache}", call)
            self.assertIn("APT::Keep-Downloaded-Packages=true", call)

    def test_download_failure_retries_another_mirror_and_never_installs(self):
        self.env.update(TEST_PACKAGE_STATUS="not-installed", TEST_DOWNLOAD_FAILURE="1")
        result = self.run_script("Install-LinuxReleaseDependencies.sh", "runtime", self.cache)
        self.assertNotEqual(0, result.returncode)
        calls = self.calls.read_text().splitlines()
        self.assertEqual(2, sum("--download-only" in call for call in calls))
        self.assertFalse(any("--no-download" in call for call in calls))
        self.assertIn("archive.ubuntu.com", result.stdout)
        self.assertIn("azure.archive.ubuntu.com", result.stdout)

    def test_runtime_installs_missing_graphics_libraries(self):
        self.command(
            "dpkg-query",
            'if [[ ${*: -1} != libfontconfig1 ]]; then printf installed; else exit 1; fi',
        )
        result = self.run_script("Install-LinuxReleaseDependencies.sh", "runtime", self.cache)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(self.calls.exists(), "A minimal runtime runner needs fontconfig for Skia.")
        self.assertTrue(self.calls.read_text().splitlines()[-1].endswith("install libfontconfig1"))

    def test_install_failure_is_reported_without_retrying_dpkg(self):
        self.env["TEST_PACKAGE_STATUS"] = "not-installed"
        self.command(
            "apt-get",
            'printf "%s\\n" "$*" >> "$TEST_CALLS"\n'
            'if [[ $* == *"--no-download"* ]]; then exit 42; fi',
        )
        result = self.run_script("Install-LinuxReleaseDependencies.sh", "runtime", self.cache)
        self.assertEqual(42, result.returncode)
        self.assertEqual(3, len(self.calls.read_text().splitlines()))

    def test_unknown_profile_fails_before_apt(self):
        result = self.run_script("Install-LinuxReleaseDependencies.sh", "typo", self.cache)
        self.assertEqual(2, result.returncode)
        self.assertFalse(self.calls.exists())

    def configure_tool_download(self):
        # Validate the real verification/recovery path with a deterministic digest.
        # The production manifest remains unchanged and is checked in live validation.
        payload = self.work / "verified tool"
        payload.write_bytes(b"known tool contents")
        self.env["TEST_PAYLOAD"] = str(payload)
        self.command(
            "curl",
            'printf "download\\n" >> "$TEST_CALLS"\n'
            'while [[ $# -gt 0 ]]; do\n'
            '  if [[ $1 == --output ]]; then cp "$TEST_PAYLOAD" "$2"; break; fi\n'
            '  shift\n'
            'done',
        )
        digest = hashlib.sha256(payload.read_bytes()).hexdigest()
        script = (ROOT / "scripts" / "Get-AppImageTools.sh").read_text()
        script = re.sub(r"\b[0-9a-f]{64}\b", digest, script)
        fixture = self.work / "get-tools.sh"
        fixture.write_text(script)
        return fixture

    def run_tool_fixture(self, fixture):
        return subprocess.run(
            ["bash", str(fixture), str(self.cache)],
            env=self.env,
            capture_output=True,
            text=True,
            check=False,
            timeout=10,
        )

    def test_verified_tools_are_reused_and_corrupted_cache_is_repaired(self):
        fixture = self.configure_tool_download()
        cold = self.run_tool_fixture(fixture)
        self.assertEqual(0, cold.returncode, cold.stderr)
        self.assertEqual(3, len(self.calls.read_text().splitlines()))
        warm = self.run_tool_fixture(fixture)
        self.assertEqual(0, warm.returncode, warm.stderr)
        self.assertEqual(3, len(self.calls.read_text().splitlines()), "Warm cache downloaded tools again.")
        (self.cache / "linuxdeploy-x86_64.AppImage").write_bytes(b"corrupted")
        repaired = self.run_tool_fixture(fixture)
        self.assertEqual(0, repaired.returncode, repaired.stderr)
        self.assertEqual(4, len(self.calls.read_text().splitlines()))
        self.assertEqual(b"known tool contents", (self.cache / "linuxdeploy-x86_64.AppImage").read_bytes())
        self.assertTrue(os.access(self.cache / "appimagetool-x86_64.AppImage", os.X_OK))

    def test_unverified_download_is_not_cached(self):
        fixture = self.configure_tool_download()
        Path(self.env["TEST_PAYLOAD"]).write_bytes(b"wrong contents")
        result = self.run_tool_fixture(fixture)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual([], list(self.cache.iterdir()))

    def test_interrupted_download_is_not_cached(self):
        fixture = self.configure_tool_download()
        self.command("curl", 'while [[ $1 != --output ]]; do shift; done\nprintf partial > "$2"\nexit 28')
        result = self.run_tool_fixture(fixture)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual([], list(self.cache.iterdir()))


if __name__ == "__main__":
    unittest.main()
