# Unity Bridge

**Let AI agents see and control Unity Editor through plain files.**

## Quick Start

**You need:** a Unity project (2021.3+) + an AI coding tool (Claude Code, Cursor, etc.)

**Install via Package Manager:**

In Unity: **Window → Package Manager → + → Add package from git URL:**
```
https://github.com/cziberpv/unity-bridge.git
```

That's it. Unity Bridge installs dependencies automatically and copies `unity-cmd.ps1` to your project root.

You'll see in the Console:
```
[UnityBridge] Initialized. Polling: Assets/LLM/Bridge/request.json
```

Now tell your AI to use `unity-cmd.ps1` — or just ask it to make a game.

## Features

- 🔍 **Read the scene** -- hierarchy, components, serialized properties, prefabs
- 🔧 **Modify anything** -- create, delete, rename, duplicate GameObjects; add/remove components; set any serialized property
- 🔬 **Lens system** -- filter inspect output by domain: `layout`, `physics`, `scripts`, `visual`, or `all`
- 📸 **Screenshots** -- capture Game View by entering Play Mode automatically
- 🖼️ **Texture catalog** *(experimental)* -- scan, search, tag, and preview project textures
- 🧪 **Scratch pad** -- run arbitrary one-off C# scripts inside the Editor
- 📦 **Batch commands** -- send a JSON array, get a combined response
- ⚡ **Compilation tracking** -- `refresh` triggers recompilation and returns errors or success
- 🎯 **Zero config** -- one git URL in Package Manager, done

## How It Works

Unity Bridge is a set of Editor scripts that give AI coding agents (Claude Code, Cursor, etc.) full access to Unity Editor via a file-based protocol. No sockets, no servers, no MCP. Install via Unity Package Manager, and your AI agent can read scenes, create GameObjects, set properties, take screenshots, and run arbitrary C# -- all through JSON commands.

```
AI Agent                        Unity Editor
   |                                |
   |-- write request.json --------->|
   |                                | (polling every 1s)
   |                                | Process command
   |<-------- response.md ---------|
   |                                |
   Read response                    |
```

```
┌─────────────────┐     ┌──────────────┐     ┌─────────────────────┐
│                  │     │              │     │    Unity Editor      │
│   Claude Code    │────>│  unity-cmd   │────>│                     │
│   (or any AI)    │     │   .ps1       │     │  Assets/LLM/Bridge/ │
│                  │<────│              │<────│   request.json      │
│                  │     │  (writes     │     │   response.md       │
│                  │     │   request,   │     │                     │
│                  │     │   polls      │     │  UnityBridge.cs     │
│                  │     │   response)  │     │  [InitializeOnLoad] │
│                  │     │              │     │  polls every 1s     │
└─────────────────┘     └──────────────┘     └─────────────────────┘
```

1. AI agent writes a JSON command to `Assets/LLM/Bridge/request.json`
2. `UnityBridge` (an `[InitializeOnLoad]` Editor script) polls the file every second
3. It dispatches the command, executes it inside Unity's Editor API
4. The result is written to `Assets/LLM/Bridge/response.md` as structured Markdown
5. AI agent reads the response

The protocol is **synchronous** from the agent's perspective: write request, wait for response. The included `unity-cmd.ps1` wrapper handles the polling loop so your agent just calls one shell command.

```powershell
# From your AI agent:
powershell -ExecutionPolicy Bypass -File unity-cmd.ps1 '{"type": "scene"}' -Timeout 60
```

## Lens System

When inspecting a complex GameObject, you don't want 200 lines of every component. Lenses filter the output to show only what matters for your current task.

**Without lens** -- minimal output with hints:
```
# Ship
**Path:** `Game World/Ship`

**Ship**
  [Lenses: layout, physics, scripts]
  Children:
    **Engine**
      [Lenses: physics, scripts]
    **Hull**
      [Lenses: visual, scripts]
```

**With `lens: "physics"`** -- only physics-relevant components:
```
# Ship
**Path:** `Game World/Ship`
**Lens:** physics

**Ship**
  Components:
    - **Transform**
      - Position: (0.0, 0.0, 0.0)
    - **Rigidbody2D**
      - Mass: 1.00
      - Gravity Scale: 0.00
  Children:
    **Engine**
      Components:
        - **BoxCollider2D**
          - Size: (2.0, 1.0)
```

| Lens | Shows |
|------|-------|
| *(none)* | Names only + hints about available lenses |
| `layout` | RectTransform, LayoutGroup, LayoutElement, ContentSizeFitter, Canvas, CanvasScaler |
| `physics` | Transform, Rigidbody2D, Collider2D, Joint2D |
| `scripts` | Custom MonoBehaviours (project scripts in Assembly-CSharp) |
| `visual` | SpriteRenderer, Image, RawImage, TMP_Text |
| `all` | Every component, no filtering |

The lens system dramatically reduces token usage. A typical UI Canvas might produce 500+ lines with `all` but only 40 lines with `layout`.

## Commands Reference

### Read Commands

| Command | Parameters | Description |
|---------|-----------|-------------|
| `scene` | -- | Full scene hierarchy with all root objects |
| `inspect` | `path`, `depth`, `detail`, `lens` | Inspect a GameObject. `depth`: children levels (default 1). `detail`: `minimal` / `components` / `full`. `lens`: see table above |
| `find` | `component` | Find all GameObjects with a component type. Falls back to name search if type not found |
| `prefab` | `path` | Export prefab structure with serialized values |
| `prefabs` | `path` | List all prefabs in a folder |
| `selection` | -- | Export currently selected object in Unity |
| `errors` | -- | Show compilation status and errors |
| `help` | -- | List all available commands |

### Write Commands

| Command | Parameters | Description |
|---------|-----------|-------------|
| `create` | `path`, `components` | Create GameObject at hierarchy path. Optional `components` array to add components in one call |
| `delete` | `path` | Delete a GameObject |
| `rename` | `path`, `value` | Rename a GameObject |
| `duplicate` | `path`, `value` | Duplicate a GameObject. Optional `value` for new name |
| `add-component` | `path`, `component` | Add a component. Works on scene objects and prefab assets |
| `delete-component` | `path`, `component` | Remove a component (Transform cannot be removed) |
| `set` | `path`, `component`, `property`/`value` or `properties` | Set serialized properties. Supports single or batch |
| `save-scene` | -- | Save current scene |
| `new-scene` | `path`, `force` | Create and open a new scene. `force: true` skips save dialog |
| `open-scene` | `path`, `force` | Open an existing scene |
| `refresh` | -- | Trigger AssetDatabase.Refresh(). Returns compilation result (success or errors with file:line) |

### Screenshot

| Command | Parameters | Description |
|---------|-----------|-------------|
| `screenshot` | `delay` | Enter Play Mode, wait `delay` seconds (default 1), capture Game View, exit Play Mode. Output: `Assets/LLM/Bridge/Screenshots/screenshot_{timestamp}.png` |

### Texture Catalog (Experimental)

> **Early stage.** The goal is to let AI agents build a local texture library -- scan, describe, tag, and auto-texture scenes. Usable today, but expect rough edges.

| Command | Parameters | Description |
|---------|-----------|-------------|
| `texture-scan` | `path` | Scan a folder, compute hashes, build searchable catalog |
| `texture-search` | `query` | Search catalog by tags, description, filename, or path |
| `texture-preview` | `query`, `depth`, `value` | Generate visual preview grid in a special scene |
| `texture-tag` | `path` (hash), `value` (description), `query` (tags) | Tag a texture with description and keywords |
| `texture-tag-batch` | `value` (JSON array) | Batch-tag multiple textures |

### Scratch Pad

| Command | Parameters | Description |
|---------|-----------|-------------|
| `scratch` | -- | Execute `BridgeScratch.Run()` from `Assets/Editor/BridgeScratch.cs` |

Edit the scratch file with any C# code, call `refresh` to compile, then `scratch` to run. One file for ad-hoc automation -- no dead code accumulation.

### Supported Value Types

The `set` command uses native JSON types through Newtonsoft.Json:

| Unity Type | JSON Format | Example |
|-----------|-------------|---------|
| int | number | `10` |
| float | number | `0.5` |
| bool | boolean | `true` |
| string | string | `"hello"` |
| Vector2 | array | `[0.5, 0.5]` |
| Vector3 | array | `[1, 2, 3]` |
| Color | array or hex string | `[1, 0, 0, 1]` or `"#FF0000"` |
| Enum | string or int | `"Volume"` or `2` |
| ObjectReference | asset path or scene ref | `"Assets/Sprites/Player.png"` or `"@Game World/Ship:Transform"` |
| Arrays | JSON array | `[1, 2, 3]` |

### Batch Commands

Send a JSON array to execute multiple commands in sequence:

```json
[
  {"type": "create", "path": "Player", "components": ["Rigidbody2D", "BoxCollider2D", "SpriteRenderer"]},
  {"type": "set", "path": "Player", "component": "Rigidbody2D", "properties": [
    {"key": "gravityScale", "value": 0},
    {"key": "linearDamping", "value": 2}
  ]},
  {"type": "set", "path": "Player", "component": "SpriteRenderer", "property": "m_Color", "value": [0, 1, 0, 1]}
]
```

Response:
```
# Batch: 3/3 succeeded

1. [+] create: Created: `Player`
2. [+] set: Set `Player` Rigidbody2D:
3. [+] set: Set `Player` SpriteRenderer:
```

## Usage with Claude Code

Add this to your project's `CLAUDE.md`:

```markdown
## Unity Editor Operations

Use `unity-cmd.ps1` to interact with Unity Editor:

### Basic commands
powershell -ExecutionPolicy Bypass -File unity-cmd.ps1 '{"type": "scene"}'
powershell -ExecutionPolicy Bypass -File unity-cmd.ps1 '{"type": "inspect", "path": "Player", "lens": "scripts"}'
powershell -ExecutionPolicy Bypass -File unity-cmd.ps1 '{"type": "refresh"}' -Timeout 120

### Workflow
1. `scene` to understand current state
2. `create` / `add-component` / `set` to build
3. `inspect` with lenses to verify
4. `save-scene` to persist
5. Write C# scripts, then `refresh` to compile and check errors

### Tips
- Use batch commands (JSON array) to group independent operations
- Use `scratch` for complex multi-step setup instead of many JSON commands
- Always `save-scene` before `new-scene` or `open-scene`
- For serialized Unity properties, use `m_` prefix: `m_LocalPosition`, `m_SizeDelta`
- Component names are short: `Image`, not `UnityEngine.UI.Image`
```

## Examples

### Create a complete 2D player

```json
[
  {"type": "create", "path": "Player", "components": ["Rigidbody2D", "BoxCollider2D", "SpriteRenderer"]},
  {"type": "set", "path": "Player", "component": "Transform", "properties": [
    {"key": "m_LocalPosition", "value": [0, 0, 0]},
    {"key": "m_LocalScale", "value": [0.5, 0.5, 1]}
  ]},
  {"type": "set", "path": "Player", "component": "Rigidbody2D", "property": "gravityScale", "value": 0}
]
```

### Inspect UI layout

```powershell
.\unity-cmd.ps1 '{"type": "inspect", "path": "UI Canvas", "lens": "layout", "depth": 3}'
```

### Write a script, compile, attach

```powershell
# 1. Write the script file (your AI writes to disk normally)
# 2. Compile
.\unity-cmd.ps1 '{"type": "refresh"}' -Timeout 120
# 3. If compilation succeeded, attach to GameObject
.\unity-cmd.ps1 '{"type": "add-component", "path": "Player", "component": "PlayerController"}'
```

### Complex setup via scratch

Instead of 50+ JSON commands to create a grid of objects:

```csharp
// Edit Assets/Editor/BridgeScratch.cs → Run()
public static string Run()
{
    for (int x = 0; x < 8; x++)
    for (int y = 0; y < 4; y++)
    {
        var brick = new GameObject($"Brick_{x}_{y}");
        brick.transform.position = new Vector3(x * 1.5f, y * 0.5f + 5f, 0);
        brick.AddComponent<BoxCollider2D>();
        var sr = brick.AddComponent<SpriteRenderer>();
        sr.color = Color.HSVToRGB((float)y / 4, 0.8f, 1f);
    }
    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    return "Created 32 bricks";
}
```

Then: `refresh` to compile, `scratch` to execute. Two commands instead of 96.

## Built With Unity Bridge

Games created entirely by AI agents using Unity Bridge, without human interaction with the Editor:

- **Lane Defense** -- tower defense with enemy waves, upgrades, and UI
- **Snake** -- classic snake with score tracking
- **Pac-Man** -- maze with ghosts, pellets, and power-ups
- **Breakout** -- with colored brick rows, physics materials, and restart flow
- **Pong** -- paddle controls, ball physics, scoring system
- Several prototype games (Asteroid Dodge, ClickerPro, Bouncing Ball)

## Unity Bridge vs MCP

| | Unity Bridge | MCP-based tools |
|---|---|---|
| **Protocol** | File I/O (request.json / response.md) | WebSocket / HTTP |
| **Dependencies** | Newtonsoft.Json (ships with Unity) | MCP server, SDK, runtime |
| **Setup** | One git URL in Package Manager | Install server, configure ports, manage process |
| **Works when Unity not focused** | Yes (file polling) | Depends on implementation |
| **Domain reload safe** | Yes (EditorPrefs persistence) | Must handle reconnection |
| **Agent integration** | Any agent that can write files and run shell commands | Requires MCP client support |
| **Batch operations** | Native (JSON array) | Typically per-request |
| **Debugging** | Read request.json / response.md in any text editor | Inspect WebSocket traffic |

Unity Bridge's design philosophy: **the simplest protocol that works**. Files are universal. Every AI agent, every OS, every shell can write a file and read another. No protocol negotiation, no handshakes, no connection management.

## Architecture

```
UnityBridge (partial class, [InitializeOnLoad])
├── UnityBridge.cs           Core: init, polling, request routing, compilation tracking
├── UnityBridge.Read.cs      scene, inspect, find, prefab, prefabs, selection, errors, help
├── UnityBridge.Write.cs     create, delete, rename, duplicate, add/delete-component, set, scenes, refresh
├── UnityBridge.Lenses.cs    Lens definitions and filtering logic
├── UnityBridge.Helpers.cs   Path resolution, type lookup, hierarchy traversal, response writing
├── UnityBridge.Screenshot.cs    Play Mode screenshot capture with EditorPrefs persistence
├── UnityBridge.TextureCatalog.cs    Texture scanning, hashing, search, preview, tagging
├── UnityBridge.PostInstall.cs  Auto-copies unity-cmd.ps1 and scratch template to project
├── UnityBridge.Scratch.cs   One-off script execution pad
└── HierarchyExporter.cs     Scene hierarchy JSON export
```

Everything is a single `static partial class`. No MonoBehaviours, no ScriptableObjects, no runtime code. Pure Editor scripts.

## License

MIT
