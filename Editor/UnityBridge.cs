using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityBridge.Editor
{
    /// <summary>
    /// File-based bridge for AI communication with Unity Editor.
    /// Monitors Assets/LLM/Bridge/request.json and writes responses to response.md
    ///
    /// Split into partial classes:
    /// - UnityBridge.cs (Core) - initialization, request routing
    /// - UnityBridge.Read.cs - read command handlers
    /// - UnityBridge.Write.cs - write command handlers
    /// - UnityBridge.Helpers.cs - utility methods
    /// </summary>
    [InitializeOnLoad]
    public static partial class Bridge
    {
        private const string BridgeFolder = "Assets/LLM/Bridge";
        private const string RequestFile = "Assets/LLM/Bridge/request.json";
        private const string ResponseFile = "Assets/LLM/Bridge/response.md";
        private static readonly Encoding Utf8Bom = new UTF8Encoding(true);

        private static DateTime _lastRequestCheck = DateTime.MinValue;
        private static DateTime _lastRequestModified = DateTime.MinValue;
        private const double PollIntervalSeconds = 1.0;
        private static List<CompilerMessage> _lastCompilationErrors = new();
        private static bool _isCompiling = false;

        // EditorPrefs keys - survive domain reload
        private const string PrefKeyCompilationStart = "UnityBridge.CompilationStartTime";
        private const string PrefKeyRefreshPending = "UnityBridge.RefreshPending";

        private static DateTime CompilationStartTime
        {
            get
            {
                var str = EditorPrefs.GetString(PrefKeyCompilationStart, "");
                return string.IsNullOrEmpty(str) ? DateTime.MinValue : DateTime.Parse(str);
            }
            set => EditorPrefs.SetString(PrefKeyCompilationStart, value.ToString("O"));
        }

        private static bool RefreshPending
        {
            get => EditorPrefs.GetBool(PrefKeyRefreshPending, false);
            set => EditorPrefs.SetBool(PrefKeyRefreshPending, value);
        }

        static Bridge()
        {
            Initialize();
            SetupCompilationTracking();
            InitializeScreenshot();
            InitializePlay();
            EditorApplication.quitting += Cleanup;
        }

        // Partial method for screenshot initialization (implemented in UnityBridge.Screenshot.cs)
        static partial void InitializeScreenshot();

        // Partial method for play command initialization (implemented in UnityBridge.Describe.cs)
        static partial void InitializePlay();

        private static void SetupCompilationTracking()
        {
            // Check if we just finished a compilation (domain reload happened)
            if (RefreshPending && CompilationStartTime != DateTime.MinValue && !EditorApplication.isCompiling)
            {
                // Domain reload completed = successful compilation
                var duration = (float)(DateTime.Now - CompilationStartTime).TotalSeconds;
                CompilationStartTime = DateTime.MinValue;
                RefreshPending = false;

                WriteCompilationResult(true, duration, null);
            }

            // Subscribe to compilation events
            CompilationPipeline.compilationStarted += _ =>
            {
                _isCompiling = true;
                CompilationStartTime = DateTime.Now;
                _lastCompilationErrors.Clear();
            };

            CompilationPipeline.compilationFinished += _ =>
            {
                _isCompiling = false;

                // If there were errors, write them immediately (no domain reload will happen)
                if (_lastCompilationErrors.Count > 0 && RefreshPending)
                {
                    var duration = (float)(DateTime.Now - CompilationStartTime).TotalSeconds;
                    WriteCompilationResult(false, duration, _lastCompilationErrors);

                    CompilationStartTime = DateTime.MinValue;
                    RefreshPending = false;
                }
                // Success case: domain reload will happen, handled above
            };

            CompilationPipeline.assemblyCompilationFinished += (_, messages) =>
            {
                var errors = messages.Where(m => m.type == CompilerMessageType.Error).ToList();
                if (errors.Count > 0)
                    _lastCompilationErrors.AddRange(errors);
            };
        }

        private static void Initialize()
        {
            Directory.CreateDirectory(BridgeFolder);

            // Create empty request file if not exists
            if (!File.Exists(RequestFile))
            {
                File.WriteAllText(RequestFile, "{}");
            }

            // Ensure game continues running when Editor loses focus.
            // Critical for game-step timing — without this, Play Mode stalls
            // when the user switches to another window.
            Application.runInBackground = true;

            // Poll for changes (works even when Unity not focused)
            EditorApplication.update += PollForRequest;

            Debug.Log("[UnityBridge] Initialized. Polling: " + RequestFile);
        }

        private static void Cleanup()
        {
            EditorApplication.update -= PollForRequest;
        }

        private static void PollForRequest()
        {
            // Async operations that need per-frame checks (before poll interval gate)
            GameStepUpdate();

            // Check every PollIntervalSeconds
            var now = DateTime.Now;
            if ((now - _lastRequestCheck).TotalSeconds < PollIntervalSeconds)
                return;
            _lastRequestCheck = now;

            // Check if file was modified
            if (!File.Exists(RequestFile)) return;
            var modified = File.GetLastWriteTime(RequestFile);
            if (modified <= _lastRequestModified) return;
            _lastRequestModified = modified;

            ProcessRequest();
        }

        private static void ProcessRequest()
        {
            try
            {
                var json = File.ReadAllText(RequestFile).Trim();
                if (string.IsNullOrWhiteSpace(json) || json == "{}")
                    return;

                // Detect batch request (JSON array)
                if (json.StartsWith("["))
                {
                    ProcessBatchRequest(json);
                }
                else
                {
                    var jObj = JObject.Parse(json);
                    var request = jObj.ToObject<BridgeRequest>();
                    if (string.IsNullOrEmpty(request.type))
                        return;

                    Debug.Log($"[UnityBridge] Processing request: {request.type} {request.path ?? request.query}");

                    var warning = ValidateFields(jObj, request.type);
                    var response = HandleRequest(request);
                    // Some handlers write response directly (e.g., errors during compilation)
                    if (response != null)
                        WriteResponse(request, warning != null ? warning + response : response);
                }

                // Clear request file
                File.WriteAllText(RequestFile, "{}");
            }
            catch (Exception ex)
            {
                WriteError($"Error processing request: {ex.Message}");
                Debug.LogError($"[UnityBridge] {ex}");
            }
        }

        private static void ProcessBatchRequest(string json)
        {
            Debug.Log("[UnityBridge] Processing batch request");

            // Parse JSON array for both deserialization and field validation
            JArray jArray;
            List<BridgeRequest> requests;
            try
            {
                jArray = JArray.Parse(json);
                requests = jArray
                    .OfType<JObject>()
                    .Select(o => o.ToObject<BridgeRequest>())
                    .Where(r => !string.IsNullOrEmpty(r?.type))
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityBridge] Failed to parse batch request: {ex.Message}");
                requests = new List<BridgeRequest>();
                jArray = null;
            }

            if (requests.Count == 0)
            {
                WriteError("Failed to parse batch request. Ensure it's a valid JSON array.");
                return;
            }

            // Collect field warnings per batch item
            var warnings = new List<string>();
            var jObjects = jArray?.OfType<JObject>()
                .Where(o => !string.IsNullOrEmpty(o["type"]?.ToString()))
                .ToList();

            var results = new List<(string type, bool success, string message)>();
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (jObjects != null && i < jObjects.Count)
                {
                    var w = ValidateFields(jObjects[i], request.type);
                    if (w != null) warnings.Add($"[{i + 1}] {request.type}: {w.Trim()}");
                }

                try
                {
                    var response = HandleRequest(request);
                    if (response == null)
                    {
                        // Async command (game-step, play, screenshot) — result will be written separately
                        results.Add((request.type, true, "Command started asynchronously, result will follow."));
                    }
                    else
                    {
                        var isError = response.StartsWith("Error:");
                        results.Add((request.type, !isError, response));
                    }
                }
                catch (Exception ex)
                {
                    results.Add((request.type, false, ex.Message));
                }
            }

            WriteBatchResponse(results, warnings);
        }

        private static void WriteBatchResponse(List<(string type, bool success, string message)> results,
            List<string> warnings = null)
        {
            var sb = new StringBuilder();
            var succeeded = 0;
            foreach (var r in results)
                if (r.success) succeeded++;

            sb.AppendLine($"# Batch: {succeeded}/{results.Count} succeeded");
            sb.AppendLine();

            if (warnings != null && warnings.Count > 0)
            {
                foreach (var w in warnings)
                    sb.AppendLine(w);
                sb.AppendLine();
            }

            for (int i = 0; i < results.Count; i++)
            {
                var (type, success, message) = results[i];
                var icon = success ? "+" : "x";
                var firstLine = message.Split('\n')[0];
                sb.AppendLine($"{i + 1}. [{icon}] {type}: {firstLine}");
            }

            sb.AppendLine();
            sb.AppendLine($"<!-- Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");

            File.WriteAllText(ResponseFile, sb.ToString(), Utf8Bom);
            AssetDatabase.Refresh();
        }

        private static string HandleRequest(BridgeRequest request)
        {
            if (CommandMap.TryGetValue(request.type.ToLower(), out var cmd))
                return cmd.Handler(request);

            return $"Unknown request type: {request.type}\n\nUse `help` to see available commands.";
        }

        #region Compilation Result

        private static void WriteCompilationResult(bool success, float duration, List<CompilerMessage> errors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!-- Request: refresh -->");
            sb.AppendLine($"<!-- Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");
            sb.AppendLine();
            sb.AppendLine("# Compilation Result");
            sb.AppendLine();
            sb.AppendLine($"**Duration:** {duration:F1}s");
            sb.AppendLine();

            if (success)
            {
                sb.AppendLine("**Status:** Success");
                sb.AppendLine();
                sb.AppendLine("All scripts compiled successfully.");
            }
            else
            {
                sb.AppendLine($"**Status:** {errors?.Count ?? 0} error(s)");
                sb.AppendLine();

                if (errors != null)
                {
                    foreach (var error in errors)
                    {
                        var file = Path.GetFileName(error.file);
                        sb.AppendLine($"### {file}:{error.line}");
                        sb.AppendLine("```");
                        sb.AppendLine(error.message);
                        sb.AppendLine("```");
                        sb.AppendLine($"**Path:** `{error.file}`");
                        sb.AppendLine();
                    }
                }
            }

            File.WriteAllText(ResponseFile, sb.ToString(), Utf8Bom);
            Debug.Log($"[UnityBridge] Compilation {(success ? "succeeded" : "failed")} in {duration:F1}s");
        }

        #endregion

        #region Command Registry

        private class CommandInfo
        {
            public readonly string Type;
            public readonly string Fields;
            public readonly string Description;
            public readonly string Category;
            public readonly Func<BridgeRequest, string> Handler;

            public CommandInfo(string type, string fields, string description, string category,
                Func<BridgeRequest, string> handler)
            {
                Type = type;
                Fields = fields;
                Description = description;
                Category = category;
                Handler = handler;
            }
        }

        private static readonly CommandInfo[] Commands =
        {
            // Read
            new("scene", "-", "Export current scene hierarchy", "Read",
                _ => HandleSceneRequest()),
            new("inspect", "path, depth, detail, lens", "Inspect GameObject with lens filter", "Read",
                HandleInspectRequest),
            new("find", "component", "Find GameObjects by component type or name", "Read",
                req => HandleFindRequest(req.component ?? req.query)),
            new("prefab", "path", "Export prefab structure with values", "Read",
                req => HandlePrefabRequest(req.path ?? req.query)),
            new("prefabs", "path", "List all prefabs in folder", "Read",
                req => HandlePrefabsListRequest(req.path ?? req.query)),
            new("selection", "-", "Export currently selected object", "Read",
                _ => HandleSelectionRequest()),
            new("errors", "-", "Show compilation errors (if any)", "Read",
                _ => HandleErrorsRequest()),
            new("status", "-", "Play Mode, compilation state, version", "Read",
                _ => HandleStatusRequest()),
            new("help", "-", "Show all commands with parameters", "Read",
                _ => HandleHelpRequest()),

            // Write
            new("create", "path, components", "Create GameObject. Optional components array", "Write",
                HandleCreateRequest),
            new("delete", "path", "Delete a GameObject", "Write",
                HandleDeleteRequest),
            new("rename", "path, value", "Rename a GameObject", "Write",
                HandleRenameRequest),
            new("duplicate", "path, value", "Duplicate a GameObject", "Write",
                HandleDuplicateRequest),
            new("add-component", "path, component", "Add component to GameObject", "Write",
                HandleAddComponentRequest),
            new("delete-component", "path, component", "Remove a component", "Write",
                HandleDeleteComponentRequest),
            new("set", "path, component, property/value or properties", "Set properties (component: \"GameObject\" for active)", "Write",
                HandleSetRequest),
            new("save-scene", "-", "Save current scene", "Write",
                _ => HandleSaveSceneRequest()),
            new("new-scene", "path, force", "Create and open new scene", "Write",
                HandleNewSceneRequest),
            new("open-scene", "path, force", "Open existing scene", "Write",
                HandleOpenSceneRequest),
            new("refresh", "-", "Recompile scripts, return errors or success", "Write",
                _ => HandleRefreshRequest()),

            // Advanced
            new("screenshot", "delay", "Capture Game View via Play Mode (default delay: 1s)", "Advanced",
                HandleScreenshotRequest),
            new("scratch", "-", "Run one-off C# from UnityBridge.Scratch.cs", "Advanced",
                _ => HandleScratch()),

            // AI Play
            new("describe", "path", "Describe IDescribable widgets in scene (semantic UI for agents)", "AI Play",
                HandleDescribe),
            new("interact", "path", "Invoke action by semantic path (e.g. /Dock/Ship/Slot[1,0]/Unequip)", "AI Play",
                HandleInteract),
            new("game-step", "ms (required), speed", "Let game run for ms milliseconds at timeScale=speed (default 1), then pause and return describe delta", "AI Play",
                HandleGameStep),
            new("play", "speed (required)", "Enter Play Mode at timeScale=speed", "AI Play",
                HandlePlay),
            new("stop", "-", "Exit Play Mode", "AI Play",
                HandleStop),
            new("time-scale", "value", "Get/set Time.timeScale (0.5 = half speed, 2 = double)", "AI Play",
                HandleTimeScale),

            // Texture (experimental)
            new("texture-scan", "path", "Scan folder, build texture catalog", "Texture (Experimental)",
                HandleTextureScan),
            new("texture-search", "query", "Search catalog by tags/description", "Texture (Experimental)",
                HandleTextureSearch),
            new("texture-preview", "query, depth, value", "Generate visual preview grid", "Texture (Experimental)",
                HandleTexturePreview),
            new("texture-tag", "path, value, query", "Tag texture with description and keywords", "Texture (Experimental)",
                HandleTextureTag),
            new("texture-tag-batch", "value", "Batch-tag multiple textures", "Texture (Experimental)",
                HandleTextureTagBatch),
        };

        private static Dictionary<string, CommandInfo> _commandMap;

        private static Dictionary<string, CommandInfo> CommandMap =>
            _commandMap ??= Commands.ToDictionary(c => c.Type);

        #endregion

        #region Field Validation

        // Known BridgeRequest field names — for detecting typos in agent requests
        private static readonly HashSet<string> KnownFields = new()
        {
            "type", "path", "component", "property", "value", "properties",
            "prefab", "parent", "components", "depth", "detail", "lens", "contrast",
            "delay", "ms", "speed", "force", "query"
        };

        /// <summary>
        /// Check raw JSON keys against known BridgeRequest fields.
        /// Returns a warning string if unknown fields found, null otherwise.
        /// </summary>
        private static string ValidateFields(JObject jObj, string commandType)
        {
            var unknown = new List<string>();
            foreach (var prop in jObj.Properties())
            {
                if (!KnownFields.Contains(prop.Name))
                    unknown.Add(prop.Name);
            }

            if (unknown.Count == 0) return null;

            var fieldList = string.Join("`, `", unknown);
            var hint = "";
            if (CommandMap.TryGetValue(commandType?.ToLower() ?? "", out var cmd) && cmd.Fields != "-")
                hint = $" Known fields for `{commandType}`: `{cmd.Fields}`.";

            return $"> **Warning:** Unknown field(s): `{fieldList}`.{hint}\n\n";
        }

        #endregion

        #region Data Classes

        private class BridgeRequest
        {
            public string type;

            // Structured fields
            public string path;           // GameObject path
            public string component;      // Component type name
            public string property;       // Single property (for simple set)
            public JToken value;          // Single value - supports native JSON types
            public PropertyKV[] properties;  // Property array for batch set

            // Prefab operations
            public string prefab;
            public string parent;

            // Create with components
            public string[] components;       // Component types to add on create

            // Inspect options
            public int depth;             // Children depth: 0=none, 1=direct, 2+=recursive (default 1)
            public string detail;         // "minimal", "components", "full" (default "full")
            public string lens;           // "layout", "physics", "scripts", "visual", "all" (default: hints only)
            public bool contrast;         // Only show non-default values (future, not implemented)

            // Screenshot options
            public float delay;           // Seconds to wait before capture (default 1)

            // AI Play options
            public int ms;                // game-step: milliseconds to run (default 500)
            public float speed = -1f;     // game-step/play: timeScale (-1 = not provided)

            // Scene operations
            public bool force;            // For new-scene/open-scene: discard unsaved changes without asking

            // Legacy (deprecated, use structured fields instead)
            public string query;
        }

        private class PropertyKV
        {
            public string key;
            public JToken value;          // Supports native JSON types: number, bool, array, string
        }

        #endregion
    }
}
