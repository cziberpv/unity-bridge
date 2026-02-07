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

namespace Editor
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
    public static partial class UnityBridge
    {
        private const string BridgeFolder = "Assets/LLM/Bridge";
        private const string RequestFile = "Assets/LLM/Bridge/request.json";
        private const string ResponseFile = "Assets/LLM/Bridge/response.md";

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

        static UnityBridge()
        {
            Initialize();
            SetupCompilationTracking();
            InitializeScreenshot();
            EditorApplication.quitting += Cleanup;
        }

        // Partial method for screenshot initialization (implemented in UnityBridge.Screenshot.cs)
        static partial void InitializeScreenshot();

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
                    var request = JsonConvert.DeserializeObject<BridgeRequest>(json);
                    if (string.IsNullOrEmpty(request.type))
                        return;

                    Debug.Log($"[UnityBridge] Processing request: {request.type} {request.path ?? request.query}");

                    var response = HandleRequest(request);
                    // Some handlers write response directly (e.g., errors during compilation)
                    if (response != null)
                        WriteResponse(request, response);
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

            // Parse JSON array manually (JsonUtility doesn't support arrays at root)
            var requests = ParseBatchRequests(json);
            if (requests == null || requests.Count == 0)
            {
                WriteError("Failed to parse batch request. Ensure it's a valid JSON array.");
                return;
            }

            var results = new List<(string type, bool success, string message)>();
            foreach (var request in requests)
            {
                try
                {
                    var response = HandleRequest(request);
                    var isError = response.StartsWith("Error:");
                    results.Add((request.type, !isError, response));
                }
                catch (Exception ex)
                {
                    results.Add((request.type, false, ex.Message));
                }
            }

            WriteBatchResponse(results);
        }

        private static List<BridgeRequest> ParseBatchRequests(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<List<BridgeRequest>>(json)
                    ?.Where(r => !string.IsNullOrEmpty(r?.type))
                    .ToList() ?? new List<BridgeRequest>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityBridge] Failed to parse batch request: {ex.Message}");
                return new List<BridgeRequest>();
            }
        }

        private static void WriteBatchResponse(List<(string type, bool success, string message)> results)
        {
            var sb = new StringBuilder();
            var succeeded = 0;
            foreach (var r in results)
                if (r.success) succeeded++;

            sb.AppendLine($"# Batch: {succeeded}/{results.Count} succeeded");
            sb.AppendLine();

            for (int i = 0; i < results.Count; i++)
            {
                var (type, success, message) = results[i];
                var icon = success ? "+" : "x";
                var firstLine = message.Split('\n')[0];
                sb.AppendLine($"{i + 1}. [{icon}] {type}: {firstLine}");
            }

            sb.AppendLine();
            sb.AppendLine($"<!-- Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");

            File.WriteAllText(ResponseFile, sb.ToString());
            AssetDatabase.Refresh();
        }

        private static string HandleRequest(BridgeRequest request)
        {
            return request.type.ToLower() switch
            {
                // Read commands
                "scene" => HandleSceneRequest(),
                "prefab" => HandlePrefabRequest(request.path ?? request.query),
                "find" => HandleFindRequest(request.component ?? request.query),
                "inspect" => HandleInspectRequest(request),
                "prefabs" => HandlePrefabsListRequest(request.path ?? request.query),
                "selection" => HandleSelectionRequest(),
                "logs" => HandleLogsRequest(request.query),
                "errors" => HandleErrorsRequest(),
                "help" => HandleHelpRequest(),
                // Scratch pad for one-time scripts
                "scratch" => HandleScratch(),
                // Screenshot
                "screenshot" => HandleScreenshotRequest(request),
                // Texture catalog
                "texture-scan" => HandleTextureScan(request),
                "texture-search" => HandleTextureSearch(request),
                "texture-preview" => HandleTexturePreview(request),
                "texture-tag" => HandleTextureTag(request),
                "texture-tag-batch" => HandleTextureTagBatch(request),
                // Write commands
                "create" => HandleCreateRequest(request),
                "delete" => HandleDeleteRequest(request),
                "rename" => HandleRenameRequest(request),
                "duplicate" => HandleDuplicateRequest(request),
                "add-component" => HandleAddComponentRequest(request),
                "delete-component" => HandleDeleteComponentRequest(request),
                "set" => HandleSetRequest(request),
                "save-scene" => HandleSaveSceneRequest(),
                "new-scene" => HandleNewSceneRequest(request),
                "open-scene" => HandleOpenSceneRequest(request),
                "refresh" => HandleRefreshRequest(),
                _ => $"Unknown request type: {request.type}\n\nUse `help` to see available commands."
            };
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

            File.WriteAllText(ResponseFile, sb.ToString());
            Debug.Log($"[UnityBridge] Compilation {(success ? "succeeded" : "failed")} in {duration:F1}s");
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
