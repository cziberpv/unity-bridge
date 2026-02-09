# Changelog

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
