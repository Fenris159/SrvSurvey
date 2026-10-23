"""Reject unrelated setup failures as evidence that the real driver's fork failed."""

import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import MagicMock, patch


SCRIPT = Path(__file__).resolve().parents[1] / "SrvSurvey.GamescopeSmoke/run-process-failures.py"
SPEC = importlib.util.spec_from_file_location("gamescope_process_failures", SCRIPT)
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class GamescopeProcessFailureTests(unittest.TestCase):
    def test_x11_setup_failure_cannot_replace_the_injected_fork(self):
        result = subprocess.CompletedProcess([], 2, "SAFE_STOP 10001\nSAFE_STOP 10002\n", "XOpenDisplay failed")
        with self.assertRaisesRegex(RuntimeError, "injection was not reached"):
            runner.verify_failure(result, 4, 2)
        result.stdout += "INJECTED_FORK 2\n"
        with self.assertRaises(RuntimeError):
            runner.verify_failure(result, 4, 2)
        result.stdout += "INJECTED_FORK 4\n"
        runner.verify_failure(result, 4, 2)
        with self.assertRaisesRegex(RuntimeError, "unexpected children"):
            runner.verify_failure(result, 4, 1)

    def test_eof_and_exited_servers_are_not_ready_displays(self):
        for number, status in [("", None), ("invalid\n", None), ("7\n", 1), ("٧\n", None)]:
            with self.subTest(number=number, status=status), tempfile.TemporaryFile(mode="w+") as stdout:
                stdout.write(number)
                stdout.seek(0)
                server = MagicMock(stdout=stdout)
                server.poll.return_value = status
                with self.assertRaises(RuntimeError):
                    runner.read_display(server)

    def test_ready_server_requires_acknowledgement(self):
        server = MagicMock()
        with patch.object(runner.select, "select", return_value=([], [], [])):
            with self.assertRaisesRegex(RuntimeError, "acknowledge"):
                runner.read_display(server)
        with tempfile.TemporaryFile(mode="w+") as stdout:
            stdout.write("7\n")
            stdout.seek(0)
            server.stdout = stdout
            server.poll.return_value = None
            self.assertEqual(":7", runner.read_display(server))


if __name__ == "__main__":
    unittest.main()
