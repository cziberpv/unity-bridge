# Unity Bridge — Install Instructions for AI Agents

This file is meant to be read by AI agents (Claude Code, Cursor, etc.) to install Unity Bridge automatically.

## Prerequisites

- Unity must be open with the target project loaded
- The AI agent needs filesystem access to the Unity project folder

## Step 1: Add package to manifest

Open `Packages/manifest.json` in the user's Unity project and add this line to the `"dependencies"` object:

```json
"com.cziberpv.unity-bridge": "https://github.com/cziberpv/unity-bridge.git"
```

Save the file. Unity will automatically download the package and all its dependencies (including Newtonsoft JSON).

## Step 2: Wait for import

Unity needs time to download and compile the package. Wait until `unity-cmd.ps1` appears in the project root (the package auto-copies it there). Poll every 3-5 seconds, timeout 60s.

If `unity-cmd.ps1` does not appear, check Unity Console for errors.

## Step 3: Verify

Send a test command (always use `-ExecutionPolicy Bypass` on Windows):

```powershell
powershell -ExecutionPolicy Bypass -File unity-cmd.ps1 '{"type": "help"}'
```

Or check Unity Console for: `[UnityBridge] Initialized. Polling: Assets/LLM/Bridge/request.json`

Unity Bridge is ready. Read `Assets/Editor/CLAUDE.md` for usage instructions.
