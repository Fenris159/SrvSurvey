"""Failure-path checks for the composition runner, without launching a compositor."""

import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import MagicMock, patch


SCRIPT = Path(__file__).resolve().parents[1] / "SrvSurvey.GamescopeSmoke/run.py"
SPEC = importlib.util.spec_from_file_location("gamescope_composition_runner", SCRIPT)
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class GamescopeCompositionRunnerTests(unittest.TestCase):
    def test_existing_artifacts_are_rejected_before_build_or_launch(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(
            runner, "run", side_effect=AssertionError("Artifacts were accepted and a build was attempted")
        ) as run:
            with patch.object(sys, "argv", [str(SCRIPT), "--protocol", "protocol.xml", "--output", directory]):
                with self.assertRaises(FileExistsError):
                    runner.main()
            run.assert_not_called()

    def test_zero_compositor_exit_cannot_replace_child_success_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "fresh"
            process = MagicMock()
            process.wait.return_value = 0
            process.poll.return_value = 0
            arguments = [str(SCRIPT), "--protocol", "protocol.xml", "--output", str(output)]
            with patch.object(sys, "argv", arguments), patch.object(runner, "run"), patch.object(runner.shutil, "copy2"), patch.object(
                subprocess, "Popen", return_value=process
            ), patch.object(subprocess, "check_output", return_value="1280x800") as pixels:
                with self.assertRaisesRegex(RuntimeError, "child.*success"):
                    runner.main()
                pixels.assert_not_called()

    def test_failed_avalonia_process_cannot_publish_success(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            game, probe = MagicMock(), MagicMock()
            game.poll.return_value = 0
            probe.poll.return_value = 2
            probe.wait.return_value = 2
            with patch.dict("os.environ", {"DISPLAY": ":primary", "STEAM_GAME_DISPLAY_0": ":game"}), patch.object(
                runner, "run"
            ), patch.object(runner.time, "sleep"), patch.object(subprocess, "Popen", side_effect=[game, probe]):
                with self.assertRaises(RuntimeError):
                    runner.child(output, False)
            self.assertFalse((output / "child-success").exists())


if __name__ == "__main__":
    unittest.main()
