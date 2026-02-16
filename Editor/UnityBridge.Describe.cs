using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityBridge;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Semantic UI description system for AI agents.
    /// Widgets implement IDescribable to expose their state and actions.
    /// Bridge uses flat discovery (all active IDescribable in scene), builds semantic paths, renders text for the agent.
    /// </summary>
    public static partial class UnityBridge
    {
        #region Play Mode State

        // EditorPrefs keys for play command (survive domain reload)
        private const string PrefKeyPlayPending = "UnityBridge.PlayPending";
        private const string PrefKeyPlaySpeed = "UnityBridge.PlaySpeed";

        /// <summary>
        /// Called from Initialize (via partial method) to check if a play command
        /// is pending after domain reload, and to subscribe to playModeStateChanged.
        /// </summary>
        static partial void InitializePlay()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            if (!EditorPrefs.GetBool(PrefKeyPlayPending, false)) return;

            // Play command was pending — apply timeScale and write response
            var speed = EditorPrefs.GetFloat(PrefKeyPlaySpeed, 1f);

            // Clear prefs before writing response
            EditorPrefs.DeleteKey(PrefKeyPlayPending);
            EditorPrefs.DeleteKey(PrefKeyPlaySpeed);

            Time.timeScale = speed;
            Debug.Log($"[UnityBridge] Play Mode started, timeScale={speed}");

            // Write response: confirmation + full describe
            var sb = new StringBuilder();
            sb.AppendLine("<!-- Request: play -->");
            sb.AppendLine($"<!-- Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");
            sb.AppendLine();
            sb.AppendLine($"**Play Mode started** at timeScale={speed}");
            if (speed == 0f)
                sb.AppendLine("\n> **Paused.** Use `game-step` to advance or `time-scale` to set speed.");
            sb.AppendLine();
            sb.Append(HandleDescribeFull(null));

            File.WriteAllText(ResponseFile, sb.ToString(), Utf8Bom);
        }

        private static string HandlePlay(BridgeRequest request)
        {
            // speed is required — no default. -1 means not provided.
            if (request.speed < 0f)
                return "Error: `speed` is required. Example: `{\"type\": \"play\", \"speed\": 1}`\nUse speed=0 to start paused.";

            var speed = request.speed;

            if (EditorApplication.isPlaying)
            {
                // Already in Play Mode — just set timeScale
                Time.timeScale = speed;
                var pause = speed == 0f ? "\n> **Paused.** Use `game-step` to advance or `time-scale` to set speed.\n" : "";
                return $"**timeScale={speed}**\n{pause}\n" + HandleDescribeFull(null);
            }

            // Guard: dirty scene triggers modal dialog
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.isDirty)
                return "Error: Scene has unsaved changes. Use `save-scene` first — entering Play Mode with a dirty scene triggers a modal dialog that freezes the Bridge.";

            // Store in EditorPrefs and enter Play Mode (domain reload will follow)
            EditorPrefs.SetBool(PrefKeyPlayPending, true);
            EditorPrefs.SetFloat(PrefKeyPlaySpeed, speed);
            EditorApplication.isPlaying = true;

            Debug.Log($"[UnityBridge] play: entering Play Mode at speed={speed}");
            return null; // Response written by OnPlayModeStateChanged after domain reload
        }

        private static string HandleStop(BridgeRequest request)
        {
            if (!EditorApplication.isPlaying)
                return "Not in Play Mode.";

            EditorApplication.isPlaying = false;
            return "**Exiting Play Mode...**";
        }

        #endregion

        #region Delta Cache

        /// <summary>
        /// Per-widget render cache keyed by short semantic path.
        /// Survives between commands, reset on domain reload (static field).
        /// Used by HandleDescribeDelta to return only changed widgets.
        /// </summary>
        private static Dictionary<string, string> _describeCache = new();

        /// <summary>
        /// Maps short semantic path → (GameObject, IDescribable).
        /// Rebuilt on every describe/describe-delta. Used by interact to resolve actions.
        /// Key format: "Name" (if unique) or "Name#gameObjectName" (if ambiguous).
        /// </summary>
        private static Dictionary<string, (GameObject go, IDescribable describable)> _widgetMapping = new();

        #endregion

        #region Game Step State

        /// <summary>
        /// True while game-step is running (timeScale != 0, waiting for duration to elapse).
        /// Resets to false on domain reload — acceptable, response is lost but Bridge won't hang.
        /// </summary>
        private static bool _gameStepPending = false;

        /// <summary>EditorApplication.timeSinceStartup when game-step started (real time, unaffected by timeScale).</summary>
        private static double _gameStepStartTime;

        /// <summary>How long to let the game run, in milliseconds.</summary>
        private static int _gameStepDurationMs;

        /// <summary>What timeScale to use during the step (default 1).</summary>
        private static float _gameStepTargetTimeScale = 1f;

        #endregion

        #region Describe Command Handlers

        private static string HandleDescribe(BridgeRequest request)
        {
            return HandleDescribeFull(request.path);
        }

        /// <summary>
        /// Builds _widgetMapping from a list of describables.
        /// Registered widgets (via WidgetRegistry) get stable IDs assigned by the registry.
        /// Unregistered widgets (plain IDescribable) use fragment.Name, disambiguated with #gameObjectName if needed.
        /// </summary>
        private static Dictionary<string, (GameObject go, IDescribable describable)> BuildWidgetMapping(
            List<(GameObject go, IDescribable describable)> describables)
        {
            var mapping = new Dictionary<string, (GameObject go, IDescribable describable)>();

            // Separate registered (stable ID) from unregistered (need disambiguation)
            var unregistered = new List<(GameObject go, IDescribable describable, string name)>();

            foreach (var (go, describable) in describables)
            {
                if (WidgetRegistry.TryGetId(describable, out var stableId))
                {
                    mapping[stableId] = (go, describable);
                }
                else
                {
                    var fragment = describable.Describe();
                    var name = !string.IsNullOrEmpty(fragment.Name) ? fragment.Name : go.name;
                    unregistered.Add((go, describable, name));
                }
            }

            // Unregistered: old disambiguation logic
            if (unregistered.Count > 0)
            {
                var nameCounts = new Dictionary<string, int>();
                foreach (var (_, _, name) in unregistered)
                {
                    nameCounts.TryGetValue(name, out var count);
                    nameCounts[name] = count + 1;
                }

                foreach (var (go, describable, name) in unregistered)
                {
                    var key = nameCounts[name] > 1 ? $"{name}#{go.name}" : name;
                    mapping[key] = (go, describable);
                }
            }

            return mapping;
        }

        /// <summary>
        /// Full describe: renders all widgets, updates cache and mapping. Always returns complete output.
        /// </summary>
        private static string HandleDescribeFull(string root)
        {
            var describables = FindDescribables(root);

            if (describables.Count == 0)
            {
                _describeCache.Clear();
                _widgetMapping.Clear();
                if (!string.IsNullOrEmpty(root))
                    return $"No IDescribable found at `{root}` or its children.";
                return "No IDescribable found in scene.\n\nWidgets must implement `IDescribable` to appear here.";
            }

            var mapping = BuildWidgetMapping(describables);
            _widgetMapping = mapping;

            var sb = new StringBuilder();
            sb.AppendLine("# Screen");
            sb.AppendLine();

            var newCache = new Dictionary<string, string>();

            foreach (var (shortPath, (go, describable)) in mapping)
            {
                var fragment = describable.Describe();
                var widgetSb = new StringBuilder();
                RenderFragment(widgetSb, fragment, shortPath, indent: 0);
                var rendered = widgetSb.ToString();

                newCache[shortPath] = rendered;
                sb.Append(rendered);
            }

            _describeCache = newCache;

            // Collect and append events (describe clears events as side effect)
            var events = CollectEvents();
            if (!string.IsNullOrEmpty(events))
                sb.Append(events);

            return sb.ToString();
        }

        /// <summary>
        /// Delta describe: renders all widgets, compares with cache, returns only changes.
        /// Markers: ~ changed, + new, - gone. Falls back to full if cache is empty.
        /// </summary>
        private static string HandleDescribeDelta()
        {
            // Empty cache = first call after domain reload or session start — return full
            if (_describeCache.Count == 0)
                return HandleDescribeFull(null);

            var describables = FindDescribables(null);

            if (describables.Count == 0)
            {
                var hadWidgets = _describeCache.Count > 0;
                _describeCache.Clear();
                _widgetMapping.Clear();
                if (hadWidgets)
                    return "All widgets gone.";
                return "No changes.";
            }

            // Rebuild mapping (widgets may have appeared/disappeared)
            var mapping = BuildWidgetMapping(describables);
            _widgetMapping = mapping;

            var newCache = new Dictionary<string, string>();
            var delta = new StringBuilder();
            var hasChanges = false;

            // Render current widgets, compare with cache
            foreach (var (shortPath, (go, describable)) in mapping)
            {
                var fragment = describable.Describe();
                var widgetSb = new StringBuilder();
                RenderFragment(widgetSb, fragment, shortPath, indent: 0);
                var rendered = widgetSb.ToString();

                newCache[shortPath] = rendered;

                if (_describeCache.TryGetValue(shortPath, out var cached))
                {
                    // Existed before — check if changed
                    if (rendered != cached)
                    {
                        delta.Append($"~ {rendered}");
                        hasChanges = true;
                    }
                }
                else
                {
                    // New widget
                    delta.Append($"+ {rendered}");
                    hasChanges = true;
                }
            }

            // Detect gone widgets (in old cache but not in new)
            foreach (var oldPath in _describeCache.Keys)
            {
                if (!newCache.ContainsKey(oldPath))
                {
                    delta.AppendLine($"- {oldPath} gone");
                    hasChanges = true;
                }
            }

            _describeCache = newCache;

            return hasChanges ? delta.ToString() : "No changes.";
        }

        private static string HandleInteract(BridgeRequest request)
        {
            var actionPath = request.path;
            if (string.IsNullOrEmpty(actionPath))
                return "Error: path required. Example: {\"type\": \"interact\", \"path\": \"WidgetName/Action\"}";

            var (describable, go, actionId, error) = ResolveAction(actionPath);
            if (error != null)
                return error;

            // Find the action in the fragment tree
            var fragment = describable.Describe();
            var action = FindActionInFragment(fragment, actionId);

            if (action == null)
                return $"Error: Action `{actionId}` not found on `{GetFullPath(go)}`";

            if (!action.Enabled)
                return $"Error: Action `{actionId}` is disabled — {action.DisabledReason ?? "no reason given"}";

            // Execute
            string result;
            try
            {
                result = action.Execute();
            }
            catch (Exception ex)
            {
                return $"Error executing `{actionId}`: {ex.Message}";
            }

            // Return result + events + delta describe (interaction changes may ripple across the whole screen)
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(result))
            {
                sb.AppendLine($"**Result:** {result}");
                sb.AppendLine();
            }

            var events = CollectEvents();
            if (!string.IsNullOrEmpty(events))
                sb.Append(events);

            sb.Append(HandleDescribeDelta());

            return sb.ToString();
        }

        private static string HandleGameStep(BridgeRequest request)
        {
            if (!EditorApplication.isPlaying)
                return "Error: game-step requires Play Mode.";

            if (_gameStepPending)
                return "Error: game-step already in progress. Wait for it to complete.";

            if (request.ms <= 0)
                return "Error: game-step requires `ms` — how many milliseconds to run. Example: {\"type\": \"game-step\", \"ms\": 150}";
            var ms = request.ms;
            var speed = request.speed >= 0 ? request.speed : 1f;

            // Set timeScale to let the game run
            _gameStepTargetTimeScale = speed;
            Time.timeScale = speed;

            // Record start in real time (unaffected by timeScale)
            _gameStepStartTime = EditorApplication.timeSinceStartup;
            _gameStepDurationMs = ms;
            _gameStepPending = true;

            Debug.Log($"[UnityBridge] game-step: running {ms}ms at timeScale={speed}");

            return null; // Response written by GameStepUpdate when duration elapses
        }

        /// <summary>
        /// Called from main update loop. Checks if game-step duration has elapsed,
        /// then pauses (timeScale=0) and writes the describe response.
        /// </summary>
        private static void GameStepUpdate()
        {
            if (!_gameStepPending)
                return;

            // Edge case: game exited Play Mode while step was running
            if (!EditorApplication.isPlaying)
            {
                _gameStepPending = false;
                var sb = new StringBuilder();
                sb.AppendLine("<!-- Request: game-step -->");
                sb.AppendLine($"<!-- Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");
                sb.AppendLine();
                sb.AppendLine("Error: Play Mode exited during game-step.");
                File.WriteAllText(ResponseFile, sb.ToString(), Utf8Bom);
                Debug.LogWarning("[UnityBridge] game-step cancelled: Play Mode exited");
                return;
            }

            // Check elapsed real time
            var elapsedMs = (EditorApplication.timeSinceStartup - _gameStepStartTime) * 1000.0;
            if (elapsedMs < _gameStepDurationMs)
                return;

            // Duration elapsed — pause and respond.
            // try/finally ensures _gameStepPending is cleared and timeScale is reset
            // even if describe/events/file-write throws, so the bridge never gets stuck.
            try
            {
                Time.timeScale = 0f;

                Debug.Log($"[UnityBridge] game-step complete: {elapsedMs:F0}ms elapsed, paused");

                // Build response: events + describe delta (or full if cache empty)
                var response = new StringBuilder();
                response.AppendLine("<!-- Request: game-step -->");
                response.AppendLine($"<!-- Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");
                response.AppendLine();
                response.AppendLine($"**Stepped:** {_gameStepDurationMs}ms (timeScale={_gameStepTargetTimeScale})");
                response.AppendLine();

                var events = CollectEvents();
                if (!string.IsNullOrEmpty(events))
                    response.Append(events);

                response.Append(HandleDescribeDelta());

                File.WriteAllText(ResponseFile, response.ToString(), Utf8Bom);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityBridge] game-step response failed: {ex}");
                try
                {
                    var error = new StringBuilder();
                    error.AppendLine("<!-- Request: game-step -->");
                    error.AppendLine($"<!-- Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");
                    error.AppendLine();
                    error.AppendLine($"Error during game-step response: {ex.Message}");
                    File.WriteAllText(ResponseFile, error.ToString(), Utf8Bom);
                }
                catch
                {
                    // Last resort: if even error response write fails, just log
                    Debug.LogError("[UnityBridge] Failed to write game-step error response");
                }
            }
            finally
            {
                _gameStepPending = false;
                Time.timeScale = 0f;
            }
        }

        private static string HandleTimeScale(BridgeRequest request)
        {
            if (!EditorApplication.isPlaying)
                return "Error: time-scale requires Play Mode.";

            // No value — read current
            if (request.value == null || request.value.Type == JTokenType.Null)
                return $"**Time.timeScale:** {Time.timeScale}";

            var scale = request.value.Value<float>();
            if (scale < 0f)
                return "Error: timeScale cannot be negative.";

            Time.timeScale = scale;
            return $"**Time.timeScale:** {Time.timeScale}";
        }

        #endregion

        #region Event Collection

        /// <summary>
        /// Collects events from all IDescribable widgets. Events are cleared on read (GetEvents contract).
        /// Returns formatted markdown string, or empty string if no events.
        /// </summary>
        private static string CollectEvents()
        {
            var describables = FindDescribables(null);
            if (describables.Count == 0) return "";

            var sb = new StringBuilder();

            foreach (var (go, describable) in describables)
            {
                var events = describable.GetEvents();
                if (events == null || events.Count == 0) continue;

                var widgetName = go.name;
                foreach (var evt in events)
                {
                    sb.AppendLine($"- **{widgetName}**: {evt}");
                }
            }

            if (sb.Length == 0) return "";

            var result = new StringBuilder();
            result.AppendLine("## Events");
            result.Append(sb);
            result.AppendLine();
            return result.ToString();
        }

        #endregion

        #region Discovery

        /// <summary>
        /// Flat discovery: find ALL active IDescribable widgets in the scene.
        /// Every IDescribable is equal — no hierarchy filtering. Internal widget trees
        /// (Children in ScreenFragment) are each widget's own responsibility.
        /// If rootPath is specified, filters to widgets whose scene path starts with rootPath.
        /// </summary>
        private static List<(GameObject go, IDescribable describable)> FindDescribables(string rootPath)
        {
            var results = new List<(GameObject, IDescribable)>();

            // Find all MonoBehaviours implementing IDescribable in the scene
            var allBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            foreach (var mb in allBehaviours)
            {
                if (mb is not IDescribable describable) continue;
                if (!mb.gameObject.activeInHierarchy) continue;

                // If rootPath specified, filter by scene path prefix (boundary-aware)
                if (!string.IsNullOrEmpty(rootPath))
                {
                    var fullPath = GetFullPath(mb.gameObject);
                    if (!fullPath.StartsWith(rootPath, StringComparison.Ordinal))
                        continue;
                    // Ensure match is at path boundary: exact match or next char is '/'
                    if (fullPath.Length > rootPath.Length && fullPath[rootPath.Length] != '/')
                        continue;
                }

                results.Add((mb.gameObject, describable));
            }

            return results;
        }

        #endregion

        #region Rendering

        private static void RenderFragment(StringBuilder sb, ScreenFragment fragment, string currentPath, int indent)
        {
            var prefix = indent > 0 ? new string(' ', indent * 2) : "";

            // Build display line: "Label: Name" or just "Name"
            var displayName = !string.IsNullOrEmpty(fragment.Label)
                ? $"{fragment.Label}: {fragment.Name}"
                : fragment.Name;

            sb.AppendLine($"{prefix}{displayName}");

            // Description (if present)
            if (!string.IsNullOrEmpty(fragment.Description))
                sb.AppendLine($"{prefix}  {fragment.Description}");

            // Actions
            if (fragment.Actions != null)
            {
                foreach (var action in fragment.Actions)
                {
                    var actionPath = $"{currentPath}/{action.Id}";
                    var actionLine = $"{prefix}  [interact] {actionPath}";

                    if (!action.Enabled)
                        actionLine += string.IsNullOrEmpty(action.DisabledReason)
                            ? " — disabled"
                            : $" — disabled: \"{action.DisabledReason}\"";
                    else if (!string.IsNullOrEmpty(action.Hint))
                        actionLine += $" — \"{action.Hint}\"";

                    sb.AppendLine(actionLine);
                }
            }

            // Children
            if (fragment.Children != null)
            {
                foreach (var child in fragment.Children)
                {
                    // Child path: append child name (or label+name for disambiguation)
                    var childSegment = child.Name;
                    var childPath = $"{currentPath}/{childSegment}";
                    RenderFragment(sb, child, childPath, indent + 1);
                }
            }
        }

        #endregion

        #region Action Resolution

        /// <summary>
        /// Resolves a semantic action path like "Meteorite/Capture" or "Dock/Reactor@0,0/Remove"
        /// to the widget + action by navigating through the fragment tree.
        ///
        /// Format: WidgetKey/[ChildName/]*/ActionId
        /// - First segment = top-level widget key (from _widgetMapping)
        /// - Middle segments (if any) = child names (navigate through ScreenFragment.Children)
        /// - Last segment = action ID
        ///
        /// Examples:
        /// - "Dock/Remove" → widget=Dock, action=Remove (top-level action)
        /// - "Dock/Reactor@0,0/Remove" → widget=Dock, child=Reactor@0,0, action=Remove
        /// - "Dock#GO_1/Child/Action" → widget=Dock#GO_1, child=Child, action=Action
        /// </summary>
        private static (IDescribable describable, GameObject go, string actionId, string error) ResolveAction(string actionPath)
        {
            if (string.IsNullOrEmpty(actionPath))
                return (null, null, null, "Error: Action path required. Example: `{\"type\": \"interact\", \"path\": \"WidgetName/Action\"}`");

            // Strip leading / if present (backward compat, but paths should no longer start with /)
            if (actionPath[0] == '/')
                actionPath = actionPath[1..];

            var segments = actionPath.Split('/');
            if (segments.Length < 2)
                return (null, null, null, $"Error: Action path must contain at least Widget/Action. Got: `{actionPath}`");

            // First segment = top-level widget key
            var widgetKey = segments[0];
            if (!_widgetMapping.TryGetValue(widgetKey, out var entry))
            {
                var available = _widgetMapping.Count > 0
                    ? "Available widgets: " + string.Join(", ", _widgetMapping.Keys)
                    : "No widgets available (run `describe` first)";
                return (null, null, null, $"Error: Widget `{widgetKey}` not found.\n{available}");
            }

            var (go, describable) = entry;
            var fragment = describable.Describe();

            // Last segment = action ID
            var actionId = segments[^1];

            // Middle segments (if any) = navigate through children
            // Example: "Dock/Reactor@0,0/Remove" → segments = ["Dock", "Reactor@0,0", "Remove"]
            // Need to navigate through "Reactor@0,0" to find the fragment containing "Remove"
            for (int i = 1; i < segments.Length - 1; i++)
            {
                var childName = segments[i];
                var child = FindChildByName(fragment, childName);

                if (child == null)
                {
                    // Build helpful error with available children
                    var availableChildren = fragment.Children != null && fragment.Children.Length > 0
                        ? "Available children: " + string.Join(", ", Array.ConvertAll(fragment.Children, c => c.Name))
                        : "No children available";
                    var pathSoFar = string.Join("/", segments[..(i + 1)]);
                    return (null, null, null, $"Error: Child `{childName}` not found in path `{pathSoFar}`.\n{availableChildren}");
                }

                fragment = child.Value;
            }

            // Now fragment is the one that should contain the action
            return (describable, go, actionId, null);
        }

        /// <summary>
        /// Searches fragment.Children for a child with the given name.
        /// Returns null if not found or fragment has no children.
        /// </summary>
        private static ScreenFragment? FindChildByName(ScreenFragment fragment, string name)
        {
            if (fragment.Children == null) return null;

            foreach (var child in fragment.Children)
            {
                if (child.Name == name)
                    return child;
            }

            return null;
        }

        /// <summary>
        /// Searches the fragment tree (depth-first) for an action with the given ID.
        /// NOTE: Now only used to find the action in the RESOLVED fragment (not the whole tree),
        /// because ResolveAction navigates to the correct fragment first.
        /// </summary>
        private static GameAction FindActionInFragment(ScreenFragment fragment, string actionId)
        {
            // Check this fragment's actions
            if (fragment.Actions != null)
            {
                foreach (var action in fragment.Actions)
                {
                    if (action.Id == actionId)
                        return action;
                }
            }

            // Recurse into children
            if (fragment.Children != null)
            {
                foreach (var child in fragment.Children)
                {
                    var found = FindActionInFragment(child, actionId);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        #endregion
    }

}
