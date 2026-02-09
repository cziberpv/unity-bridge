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

## Getting Started

```powershell
# See all available commands with parameters and examples
.\unity-cmd.ps1 '{"type": "help"}'

# Read the scene
.\unity-cmd.ps1 '{"type": "scene"}'

# Batch commands
.\unity-cmd.ps1 '[{"type": "create", "path": "Player"}, {"type": "add-component", "path": "Player", "component": "Rigidbody2D"}]'

# Compile scripts and check for errors
.\unity-cmd.ps1 '{"type": "refresh"}' -Timeout 120
```

The `help` command returns the full command reference with parameters, lens system, value types, and tips.

## Key Patterns

**Lens system** — filter inspect output by domain: `layout`, `physics`, `scripts`, `visual`, `all`.

**Batch commands** — send a JSON array to execute multiple operations in one call.

**Scratch pad** — edit `Assets/Editor/BridgeScratch.cs` for complex one-off automation, `refresh` + `scratch` to run.

**Property names** — use `m_` prefix for Unity internals (`m_LocalPosition`, `m_SizeDelta`). Short component names work (`Image`, not `UnityEngine.UI.Image`).

## Dependencies

- `com.unity.nuget.newtonsoft-json`
- `com.unity.textmeshpro`
