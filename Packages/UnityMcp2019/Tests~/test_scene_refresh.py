from __future__ import annotations

import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import Mock

from test_server import load_server


class SceneRefreshTests(unittest.TestCase):
    # Use a disposable project; mock only the Unity transport, not the file-edit behavior.
    def setUp(self):
        self.module = load_server()
        self.temp = tempfile.TemporaryDirectory()
        self.project = Path(self.temp.name)
        (self.project / "Assets").mkdir()
        self.server = self.module.McpServer(self.project, 1)
        self.server.bridge = Mock()
        self.server.bridge.call.return_value = {"Scheduled": True}
        self.cancelled = threading.Event()

    # Dispose worker resources and remove only the temporary test project.
    def tearDown(self):
        self.server._asset_executor.shutdown(wait=True)
        self.temp.cleanup()

    # Scene conflicts must stop the operation before any new source or directories are written.
    def test_conflict_prevents_script_creation(self):
        self.server.bridge.call.side_effect = self.module.BridgeError("scene_reload_required")
        with self.assertRaises(self.module.BridgeError):
            self.server._call_local_tool("create_script", {
                "path": "Assets/New.cs", "contents": "class New {}"
            }, self.cancelled)
        self.assertFalse((self.project / "Assets/New.cs").exists())

    # Creating a script imports just that asset after successfully checking scene state.
    def test_create_script_preflights_then_imports_one_path(self):
        self.server._call_local_tool("create_script", {
            "path": "Assets/New.cs", "contents": "class New {}"
        }, self.cancelled)
        calls = self.server.bridge.call.call_args_list
        self.assertEqual("prepare_script_edit", calls[0].args[0])
        self.assertEqual(("refresh_assets", {"AssetPaths": ["Assets/New.cs"]}), calls[1].args[:2])
        self.assertEqual("class New {}", (self.project / "Assets/New.cs").read_text())

    # An import failure after writing is not reported as a failed edit that should be replayed.
    def test_import_failure_reports_file_already_written(self):
        self.server.bridge.call.side_effect = [{}, self.module.BridgeError("scene_reload_required")]
        result = self.server._call_local_tool("create_script", {
            "path": "Assets/New.cs", "contents": "class New {}"
        }, self.cancelled)
        self.assertTrue(result["success"])
        self.assertTrue(result["data"]["refresh"]["FileWritten"])
        self.assertFalse(result["data"]["refresh"]["Scheduled"])
        self.assertEqual("class New {}", (self.project / "Assets/New.cs").read_text())

    # Conflicts preserve an existing script and its meta file when deleting through Unity fails.
    def test_delete_failure_keeps_script_and_meta(self):
        path = self.project / "Assets/Old.cs"
        path.write_text("class Old {}")
        Path(str(path) + ".meta").write_text("guid: retained")
        self.server.bridge.call.side_effect = self.module.BridgeError("scene_reload_required")
        with self.assertRaises(self.module.BridgeError):
            self.server._call_local_tool("delete_script", {"uri": "Assets/Old.cs"}, self.cancelled)
        self.assertTrue(path.exists())
        self.assertTrue(Path(str(path) + ".meta").exists())
        self.assertEqual("delete_script_asset", self.server.bridge.call.call_args.args[0])

    # Waiting for Unity must not overwrite source changes made concurrently by the user.
    def test_concurrent_source_change_during_preflight_is_preserved(self):
        path = self.project / "Assets/Old.cs"
        path.write_text("class Old {}")

        # Model an external editor changing the file while the Unity preflight is in flight.
        def concurrent_edit(*args):
            path.write_text("class UserWork {}")
            return {}

        self.server.bridge.call.side_effect = concurrent_edit
        with self.assertRaisesRegex(ValueError, "stale_file"):
            self.server._apply_text_edits({
                "uri": "Assets/Old.cs",
                "edits": [{"startLine": 1, "startCol": 7, "endLine": 1, "endCol": 10, "newText": "New"}]
            }, self.cancelled)
        self.assertEqual("class UserWork {}", path.read_text())

    # Readiness must expose dirty state and conflicts instead of advising endless retries.
    def test_state_exposes_conflict_and_dirty(self):
        result = self.server._normalize_resource_result("mcpforunity://editor/state", {
            "ReadyForTools": True, "ActiveSceneIsDirty": True,
            "SceneRefresh": {"ExternalSceneChanges": True, "Scenes": [{"Path": "Assets/A.unity", "IsDirty": True}]}
        })["data"]
        self.assertTrue(result["editor"]["active_scene"]["is_dirty"])
        self.assertFalse(result["advice"]["ready_for_tools"])
        self.assertEqual("resolve_scene_changes", result["advice"]["recommended_next_action"])
        self.assertEqual("Assets/A.unity", result["editor"]["scene_refresh"]["scenes"][0]["path"])

    # A clean changed scene tells the agent to refresh safely without asking to discard changes.
    def test_clean_external_scene_advises_safe_refresh(self):
        advice = self.server._normalize_resource_result("mcpforunity://editor/state", {
            "ReadyForTools": False,
            "SceneRefresh": {"ExternalSceneChanges": True, "CanAutoReload": True}
        })["data"]["advice"]
        self.assertEqual("refresh_unity", advice["recommended_next_action"])
        self.assertEqual(0, advice["recommended_retry_after_ms"])

    # Deferred refresh failures remain visible even when Unity itself is idle again.
    def test_deferred_failure_is_not_ready(self):
        result = self.server._normalize_resource_result("mcpforunity://editor/state", {
            "ReadyForTools": True, "SceneRefresh": {"LastError": "scene_reload_required"}
        })["data"]["advice"]
        self.assertFalse(result["ready_for_tools"])
        self.assertEqual("inspect_refresh_error", result["recommended_next_action"])


if __name__ == "__main__":
    unittest.main()
