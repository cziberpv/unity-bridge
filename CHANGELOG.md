# Changelog

## [1.3.2] - 2026-07-11

### Fixed
- `unity-cmd.ps1` failed on macOS/Linux — hardcoded backslashes in `Join-Path` made pwsh write the request to a
  file literally named `Assets\LLM\Bridge\request.json`, so every command timed out ([#4](https://github.com/cziberpv/unity-bridge/issues/4))
- A malformed item in a batch no longer discards the whole batch — each item is deserialized in isolation and
  reports its own `Parse error`, while valid items still execute ([#3](https://github.com/cziberpv/unity-bridge/issues/3))

### Added
- `set` now accepts `properties` as a JSON object (`{"key": value, ...}`) in addition to the KV-array form
  (`[{"key": ..., "value": ...}]`). Both shapes work; the object form is what agents write naturally.

## [1.0.0] - 2026-02-09

### Added
- UPM package support — install via git URL in Package Manager
- Auto-dependency resolution (Newtonsoft JSON, TextMeshPro)
- Post-install: auto-copies `unity-cmd.ps1` and scratch template to project
- `BridgeScratch.cs` — editable scratch pad in `Assets/Editor/`
- `INSTALL.md` — machine-readable install instructions for AI agents

### Changed
- Repository restructured to UPM layout (`Editor/` at root)
- Scratch pad moved from package to user-editable `Assets/Editor/BridgeScratch.cs`
- Assembly renamed to `com.cziberpv.unity-bridge.Editor`
