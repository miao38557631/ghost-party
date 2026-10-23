# MCP for Unity 2019

An open-source Unity Package Manager package that connects MCP clients to
Unity `2019.4`. It provides a Unity Editor bridge plus a dependency-free
Python MCP server and tracks the public protocol surface of
[CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp).

[简体中文](README.zh-CN.md)

## What it implements

The default MCP catalog contains 48 tools plus 19 fixed resources and 6
resource templates. Implemented areas include scenes and GameObjects,
components and Prefabs, asset/material/shader/texture editing, C# source
editing, animation, cameras and screenshots, physics, graphics, profiling,
packages, builds, tests, optional content providers, and Unity API inspection.

- [Feature overview](FEATURES.md)
- [48-tool reference](Documentation~/tools-reference.md)
- [25-resource reference](Documentation~/resources-reference.md)
- [Unity 2019 support matrix](Documentation~/support-matrix.md)

## Requirements

- Unity `2019.4` or later. The release baseline is `2019.4.28f1`.
- Python `3.10` or later available on `PATH`.
- Git available on `PATH` when installing from a Git URL.
- An MCP-capable client such as Codex or Claude.

Optional packages such as ProBuilder, Visual Effect Graph, and model importers
are detected at runtime. Missing optional packages produce explicit capability
errors rather than silent success.

## Install

In Unity, open **Window > Package Manager**, choose **Add package from git
URL...**, and enter:

```text
https://github.com/ibelongedtoyou/unity-mcp-2019.git#v0.3.1
```

For local development, choose **Add package from disk...** and select this
repository's `package.json`.

## Start and connect

1. Open **Window > MCP for Unity 2019**.
2. Confirm the Python executable under **Connect > Advanced**.
3. Start the MCP stack. The default endpoint is
   `http://127.0.0.1:6500/mcp`.
4. Use the **Clients** tab to configure or copy settings for your MCP client.
5. Use **Diagnostics** to run the protocol self-test, bridge smoke test, and
   HTTP health check.

Codex command:

```powershell
codex mcp add unity-mcp-2019-http --url http://127.0.0.1:6500/mcp
```

Claude Code command:

```powershell
claude mcp add --scope local --transport http UnityMCP http://127.0.0.1:6500/mcp
```

## Architecture

```text
MCP client
  -> stdio or Streamable HTTP
Tools~/server.py
  -> authenticated loopback TCP bridge
Editor/Mcp2019Bridge.cs
  -> Unity 2019.4 Editor main-thread APIs
```

The Python server has no third-party Python dependencies. Runtime metadata,
logs, and asynchronous job state are written under the consuming project's
`Library/UnityMcp2019` directory and are not project assets.

## Security

This package gives a trusted MCP client powerful Editor capabilities,
including asset changes, build operations, client configuration changes, and
optional in-memory C# execution. Loopback binding prevents remote network
access, but any untrusted local process may still attempt to contact the MCP
HTTP endpoint. Review [SECURITY.md](SECURITY.md) before enabling it.

Provider-backed generation is opt-in and can transmit prompts or source assets
to the selected third-party provider. No provider credentials are bundled.

## Script edits and Reload Scene prompts

MCP script/shader edits check loaded scenes and Prefab Stage before writing.
They import the changed source asset instead of refreshing the entire project.
Before an explicit `refresh_unity`, externally changed **clean** scenes are
reloaded individually, preserving other loaded scenes. Unchanged dirty scenes
are retained. A scene changed both in memory and on disk blocks the operation
with `scene_reload_required`; resolve that conflict before retrying.

After edits, read `mcpforunity://editor/state`: `editor.active_scene.is_dirty`
and `editor.scene_refresh` expose loaded-scene changes, Prefab Stage state,
pending imports and deferred failures. A scheduled import is not a completed
import. If the result says `FileWritten=true`, retry the import after resolving
the reported issue, not the source edit.

This protects MCP-managed edits and refreshes. External tools can still trigger
Unity's native Auto Refresh before MCP runs its check. An already-open native
Reload Scene dialog, or Prefab Stage conflict, must be resolved in Unity; the
bridge does not click arbitrary dialogs or discard unsaved work. Code-only edits
do not require reloading an unchanged scene.

## Validate

From the package root:

```powershell
python "Tools~/validate_release.py"
python "Tools~/server.py" --project "<UNITY_PROJECT>" --self-test
python "Tools~/server.py" --project "<UNITY_PROJECT>" --bridge-smoke
```

See [Documentation~/index.md](Documentation~/index.md) for installation,
compatibility, configuration, and troubleshooting details.
Before publishing a tag, follow [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).

## License and attribution

This package is licensed under MIT. See [LICENSE.md](LICENSE.md) and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
