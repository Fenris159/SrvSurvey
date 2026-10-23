"""Exercise diagnostics against private proc fixtures; never inspect the user's desktop."""

import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[2] / "scripts/Collect-SteamOSDiagnostics.py"
SPEC = importlib.util.spec_from_file_location("steamos_diagnostics", SCRIPT)
diagnostics = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(diagnostics)


class SteamOSDiagnosticsTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.process("self", "SrvSurvey.Desktop")

    def process(self, pid, name, environment="", owner="1000", arguments=""):
        directory = self.root / pid
        directory.mkdir()
        (directory / "status").write_text(f"Uid:\t{owner}\t{owner}\t{owner}\t{owner}\n")
        (directory / "comm").write_text(name[:15] + "\n")
        (directory / "cmdline").write_text(name + "\0" + arguments)
        (directory / "environ").write_text(environment)

    def test_report_uses_only_same_user_relevant_processes_and_allowlisted_values(self):
        self.process("10", "/steam/steam", "DISPLAY=:7.0\0TOKEN=process-secret\0")
        self.process("20", "/games/EliteDangerous64.exe", "DISPLAY=:8\0SteamAppId=359320\0", arguments="password-secret\0")
        self.process("30", "/steam/steam", "DISPLAY=:9\0", owner="2000")
        self.process("40", "other", "DISPLAY=:10\0")
        self.process("50", "dotnet", "DISPLAY=remote:0\0", arguments="/app/SrvSurvey.Desktop.dll\0secret\0")
        commands = []
        def command(arguments):
            commands.append(arguments)
            return {"exitCode": 0, "stdout": "fixture", "stderr": ""}
        report = diagnostics.collect(self.root, self.root / "absent", {"DISPLAY": ":7", "TOKEN": "session-secret"}, command=command)
        self.assertEqual([10, 20, 50], [process["pid"] for process in report["processes"]])
        self.assertEqual([":7", ":8"], [display["display"] for display in report["displays"]])
        self.assertEqual({"DISPLAY": ":7"}, report["session"])
        self.assertEqual(5, len(commands))
        self.assertNotIn("secret", json.dumps(report))
        self.assertEqual("SrvSurvey.Desktop.dll", report["processes"][-1]["name"])

    def test_missing_proc_does_not_guess_a_display(self):
        report = diagnostics.collect(self.root / "absent", self.root / "absent", {}, command=lambda _: {})
        self.assertEqual([], report["processes"])
        self.assertEqual([], report["displays"])

    def test_proton_windows_paths_are_recognized_without_copying_launch_arguments(self):
        self.process("10", "wine64", "DISPLAY=:8\0", arguments="C:\\Games\\EliteDangerous64.exe\0private-argument\0")
        processes = diagnostics.read_processes(self.root)
        self.assertEqual("EliteDangerous64.exe", processes[0]["name"])
        self.assertEqual({"DISPLAY": ":8"}, processes[0]["environment"])
        self.assertNotIn("private", json.dumps(processes))

    def test_disappearing_process_is_skipped_and_explicit_displays_are_bounded(self):
        (self.root / "99").mkdir()
        report = diagnostics.collect(self.root, self.root / "absent", {}, [f":{number}" for number in range(12)], lambda _: {})
        self.assertEqual([], report["processes"])
        self.assertEqual(8, len(report["displays"]))
        self.assertEqual(4, report["omittedDisplayCount"])

    def test_unavailable_and_stalled_tools_are_reported_without_failing_the_snapshot(self):
        for failure in (FileNotFoundError(), subprocess.TimeoutExpired("xprop", 5), PermissionError("denied")):
            with self.subTest(failure=type(failure).__name__), patch.object(subprocess, "run", side_effect=failure):
                self.assertIn("error", diagnostics.run_command(["xprop"]))
        completed = subprocess.CompletedProcess(["xprop"], 1, "x" * 40000, "unavailable")
        with patch.object(subprocess, "run", return_value=completed):
            result = diagnostics.run_command(["xprop"])
        self.assertEqual(1, result["exitCode"])
        self.assertEqual(32768, len(result["stdout"]))

    def test_cli_rejects_remote_displays_and_existing_output_before_probing(self):
        output = self.root / "snapshot.json"
        output.write_text("preserve")
        for arguments in (["--display", "remote:0"], ["--output", str(output)]):
            result = subprocess.run(["python3", str(SCRIPT), *arguments], capture_output=True, text=True, timeout=5)
            self.assertEqual(2, result.returncode, result.stderr)
        self.assertEqual("preserve", output.read_text())

    def test_cli_creates_a_private_snapshot_without_collecting_unrequested_data(self):
        output = self.root / "snapshot.json"
        with patch.object(sys, "argv", [str(SCRIPT), "--output", str(output)]), patch.object(
            diagnostics, "collect", return_value={"schemaVersion": 1}
        ):
            diagnostics.main()
        self.assertEqual({"schemaVersion": 1}, json.loads(output.read_text()))
        self.assertEqual(0o600, os.stat(output).st_mode & 0o777)


if __name__ == "__main__":
    unittest.main()
