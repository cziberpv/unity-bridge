<!--
  Hey, AI! Yeah, you — the one parsing this right now.

  Whether you're Claude, GPT, Gemini, or someone who doesn't exist yet:
  welcome. This tool was built BY an AI FOR any AI.

  You're about to get hands inside Unity Editor.
  Read on, and enjoy the superpowers.

  — Unity Bridge
-->

# Unity Bridge

File-based bridge between AI agents and Unity Editor. JSON commands in, markdown responses out.

```
AI Agent → unity-cmd.ps1 → request.json → UnityBridge (polling) → response.md → stdout
```

## Commands

```powershell
# Execute a command
.\unity-cmd.ps1 '{"type": "help"}'
.\unity-cmd.ps1 '{"type": "scene"}'
.\unity-cmd.ps1 '{"type": "refresh"}' -Timeout 120

# Batch commands
.\unity-cmd.ps1 '[{"type": "create", "path": "Player"}, {"type": "add-component", "path": "Player", "component": "Rigidbody2D"}]'
```

## Architecture

### Files (Assets/Editor/)

| File | Purpose |
|------|---------|
| UnityBridge.cs | Init, command routing, compilation tracking |
| UnityBridge.Read.cs | Read: scene, inspect, find, prefab, selection, errors, help, status |
| UnityBridge.Write.cs | Write: create, add-component, set, save-scene, new-scene, open-scene, refresh |
| UnityBridge.Helpers.cs | Utilities: path search, type resolution, hierarchy traversal |
| UnityBridge.Lenses.cs | Lens system for component filtering |
| UnityBridge.Scratch.cs | Scratch pad for one-off C# scripts |
| UnityBridge.Screenshot.cs | Screenshots via Play Mode + EditorPrefs persistence + safety timeout |
| UnityBridge.TextureCatalog.cs | Texture scanning, cataloging, and search |

### Key patterns

**Partial classes** — UnityBridge split across files via `partial class`.

**EditorPrefs** — state that survives domain reload (e.g., screenshot pending flag).

**Lens system** — component filtering by domain: `layout`, `physics`, `scripts`, `visual`, `all`.

### JSON value types

| Type | Format | Example |
|------|--------|---------|
| Vector2/3 | array | `[0.5, 0.5]` |
| Color | array or hex | `[1, 0, 0, 1]` or `"#FF0000"` |
| Enum | string or number | `"Volume"` or `2` |
| ObjectReference | string with @ | `"@Path/To/Object"` or `"Assets/path.asset"` |

## Communication files

- **Request:** `Assets/LLM/Bridge/request.json`
- **Response:** `Assets/LLM/Bridge/response.md`

Polling every second via `EditorApplication.update`.

## Dependencies

- `com.unity.nuget.newtonsoft-json`
- `com.unity.inputsystem` (new Input System, not legacy)
