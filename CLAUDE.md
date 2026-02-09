# CLAUDE.md

## Project

Unity Bridge — file-based bridge between AI agents and Unity Editor. JSON commands in, markdown responses out.

```
AI Agent → unity-cmd.ps1 → request.json → UnityBridge (polling) → response.md → stdout
```

Repository: https://github.com/cziberpv/unity-bridge

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

### Files (Editor/)

| File | Purpose |
|------|---------|
| UnityBridge.cs | Init, command routing, compilation tracking |
| UnityBridge.Read.cs | Read: scene, inspect, find, prefab, selection, errors, help |
| UnityBridge.Write.cs | Write: create, add-component, set, save-scene, new-scene, open-scene, refresh |
| UnityBridge.Helpers.cs | Utilities: path search, type resolution, hierarchy traversal |
| UnityBridge.Lenses.cs | Lens system for component filtering |
| UnityBridge.Scratch.cs | Scratch pad for one-off C# scripts |
| UnityBridge.Screenshot.cs | Screenshots via Play Mode + EditorPrefs persistence |
| UnityBridge.TextureCatalog.cs | Texture scanning, cataloging, and search |
| UnityBridge.PostInstall.cs | PostInstall: auto-copy files to user project |
| HierarchyExporter.cs | Scene hierarchy JSON export |

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
- `com.unity.textmeshpro`

## Session Continuity

`Memory.md` — мост между сессиями.

| When | Do |
|------|----|
| Starting session | Read `Memory.md` |
| Finishing session | Update `Memory.md`: что сделано (дата), новые решения, изменения статуса |

Формат записи: `### Что сделано (YYYY-MM-DD)` — краткий лог с конкретикой (коммиты, файлы, решения).

## Jarvis Coordination

Координатор: Jarvis (`C:\Projects\Jarvis`).

**Inbox:** `.claude/jarvis/inbox/` — задания от координатора. Проверять по команде `/inbox`. Задания носят рекомендательный характер — решение о принятии, отклонении или адаптации остаётся за тобой.

**Обратная связь:** Если обнаружил инсайт, полезный за пределами проекта — можешь написать в `C:\Projects\Jarvis\Inbox\unity-bridge_{тема}.md`. Формат свободный. Это не обязанность, а возможность.
