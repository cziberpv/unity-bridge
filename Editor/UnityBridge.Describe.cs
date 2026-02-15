using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Editor
{
    /// <summary>
    /// Semantic UI description system for AI agents.
    /// Widgets implement IDescribable to expose their state and actions.
    /// Bridge walks the hierarchy, builds semantic paths, renders text for the agent.
    /// </summary>
    public static partial class UnityBridge
    {
        #region Delta Cache

        /// <summary>
        /// Per-widget render cache keyed by semantic path.
        /// Survives between commands, reset on domain reload (static field).
        /// Used by HandleDescribeDelta to return only changed widgets.
        /// </summary>
        private static Dictionary<string, string> _describeCache = new();

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
        /// Full describe: renders all widgets, updates cache. Always returns complete output.
        /// </summary>
        private static string HandleDescribeFull(string root)
        {
            var describables = FindDescribables(root);

            if (describables.Count == 0)
            {
                _describeCache.Clear();
                if (!string.IsNullOrEmpty(root))
                    return $"No IDescribable found at `{root}` or its children.";
                return "No IDescribable found in scene.\n\nWidgets must implement `IDescribable` to appear here.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Screen");
            sb.AppendLine();

            var newCache = new Dictionary<string, string>();

            foreach (var (go, describable) in describables)
            {
                var basePath = "/" + GetFullPath(go);
                var fragment = describable.Describe();
                var widgetSb = new StringBuilder();
                RenderFragment(widgetSb, fragment, basePath, indent: 0);
                var rendered = widgetSb.ToString();

                newCache[basePath] = rendered;
                sb.Append(rendered);
            }

            _describeCache = newCache;
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
                if (hadWidgets)
                    return "All widgets gone.";
                return "No changes.";
            }

            var newCache = new Dictionary<string, string>();
            var delta = new StringBuilder();
            var hasChanges = false;

            // Render current widgets, compare with cache
            foreach (var (go, describable) in describables)
            {
                var basePath = "/" + GetFullPath(go);
                var fragment = describable.Describe();
                var widgetSb = new StringBuilder();
                RenderFragment(widgetSb, fragment, basePath, indent: 0);
                var rendered = widgetSb.ToString();

                newCache[basePath] = rendered;

                if (_describeCache.TryGetValue(basePath, out var cached))
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
                return "Error: path required. Example: {\"type\": \"interact\", \"path\": \"/Dock/Ship/Slot[1,0]/Unequip\"}";

            // Path format: /Root/.../WidgetObject/ActionName
            // We need to find the widget and the action within it

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

            // Return result + delta describe (interaction changes may ripple across the whole screen)
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(result))
            {
                sb.AppendLine($"**Result:** {result}");
                sb.AppendLine();
            }

            sb.Append(HandleDescribeDelta());

            return sb.ToString();
        }

        private static string HandleGameStep(BridgeRequest request)
        {
            if (!EditorApplication.isPlaying)
                return "Error: game-step requires Play Mode.";

            if (_gameStepPending)
                return "Error: game-step already in progress. Wait for it to complete.";

            var ms = request.ms > 0 ? request.ms : 500;
            var speed = request.speed > 0 ? request.speed : 1f;

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
                File.WriteAllText(ResponseFile, sb.ToString());
                Debug.LogWarning("[UnityBridge] game-step cancelled: Play Mode exited");
                return;
            }

            // Check elapsed real time
            var elapsedMs = (EditorApplication.timeSinceStartup - _gameStepStartTime) * 1000.0;
            if (elapsedMs < _gameStepDurationMs)
                return;

            // Duration elapsed — pause and respond
            Time.timeScale = 0f;
            _gameStepPending = false;

            Debug.Log($"[UnityBridge] game-step complete: {elapsedMs:F0}ms elapsed, paused");

            // Build response: describe delta (or full if cache empty)
            var response = new StringBuilder();
            response.AppendLine("<!-- Request: game-step -->");
            response.AppendLine($"<!-- Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");
            response.AppendLine();
            response.AppendLine($"**Stepped:** {_gameStepDurationMs}ms (timeScale={_gameStepTargetTimeScale})");
            response.AppendLine();
            response.Append(HandleDescribeDelta());

            File.WriteAllText(ResponseFile, response.ToString());
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

        #region Hierarchy Traversal

        /// <summary>
        /// Find all root IDescribable widgets in the scene.
        /// If path is specified, search only under that GameObject.
        /// Skips nested IDescribable (they are children of another IDescribable).
        /// </summary>
        private static List<(GameObject go, IDescribable describable)> FindDescribables(string rootPath)
        {
            var results = new List<(GameObject, IDescribable)>();

            IEnumerable<GameObject> roots;
            if (!string.IsNullOrEmpty(rootPath))
            {
                var rootGo = FindGameObjectByPath(rootPath);
                if (rootGo == null) return results;
                roots = new[] { rootGo };
            }
            else
            {
                roots = SceneManager.GetActiveScene().GetRootGameObjects();
            }

            foreach (var root in roots)
            {
                CollectRootDescribables(root, results, parentIsDescribable: false);
            }

            return results;
        }

        /// <summary>
        /// Recursively collects IDescribable that are NOT nested under another IDescribable.
        /// Nested ones are discovered by the parent's Describe() via Children.
        /// </summary>
        private static void CollectRootDescribables(GameObject go, List<(GameObject, IDescribable)> results, bool parentIsDescribable)
        {
            var describable = go.GetComponent<IDescribable>();
            if (describable != null)
            {
                if (!parentIsDescribable)
                    results.Add((go, describable));
                // Don't recurse into children — the widget owns its sub-tree
                return;
            }

            // No IDescribable on this GO — recurse into children
            foreach (Transform child in go.transform)
            {
                CollectRootDescribables(child.gameObject, results, parentIsDescribable);
            }
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
        /// Resolves an action path like "/Dock/Ship/Slot[1,0]/Unequip" to the widget + action.
        /// Strategy: walk from the scene root, find the deepest GameObject with IDescribable,
        /// then resolve the remaining path segments as action ID within the fragment tree.
        /// </summary>
        private static (IDescribable describable, GameObject go, string actionId, string error) ResolveAction(string actionPath)
        {
            if (string.IsNullOrEmpty(actionPath) || actionPath[0] != '/')
                return (null, null, null, $"Error: Action path must start with /. Got: `{actionPath}`");

            // Strip leading /
            var path = actionPath[1..];
            var parts = path.Split('/');

            if (parts.Length < 2)
                return (null, null, null, $"Error: Action path too short. Need at least /Widget/Action. Got: `{actionPath}`");

            // Try progressively longer prefixes to find the IDescribable GameObject
            GameObject bestGo = null;
            IDescribable bestDescribable = null;
            int bestIndex = -1;

            // Build path from parts, testing each prefix
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                var goPath = string.Join("/", parts, 0, i + 1);
                var go = FindGameObjectByPath(goPath);
                if (go != null)
                {
                    var describable = go.GetComponent<IDescribable>();
                    if (describable != null)
                    {
                        bestGo = go;
                        bestDescribable = describable;
                        bestIndex = i;
                        break;
                    }
                }
            }

            if (bestDescribable == null)
                return (null, null, null, $"Error: No IDescribable found in path `{actionPath}`");

            // Remaining segments form the action path within the fragment tree
            var remainingParts = parts.Skip(bestIndex + 1).ToArray();
            if (remainingParts.Length == 0)
                return (null, null, null, $"Error: No action specified in path `{actionPath}`. Path points to widget, not action.");

            // The last segment is the action ID, middle segments navigate children
            var actionId = remainingParts[^1];

            // If there are intermediate segments, we'd need to navigate the fragment tree
            // For now, we search the full fragment tree for the action
            // (child navigation through fragment.Children is handled by FindActionInFragment)

            return (bestDescribable, bestGo, actionId, null);
        }

        /// <summary>
        /// Searches the fragment tree (depth-first) for an action with the given ID.
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

    #region Contracts

    /// <summary>
    /// Implement on MonoBehaviour to make a widget visible to AI agents via "describe" command.
    /// The widget knows its own name and structure, but NOT its full path — the Bridge builds paths during traversal.
    /// See IDescribable.md for cookbook and best practices.
    /// </summary>
    public interface IDescribable
    {
        /// <summary>
        /// Return a snapshot of the widget's current state: name, children, available actions.
        /// Called every time the agent requests "describe". Must be stateless and cheap.
        /// </summary>
        ScreenFragment Describe();
    }

    /// <summary>
    /// A snapshot of one UI element: its identity, description, actions, and children.
    /// Immutable by convention — create new instances, don't mutate.
    /// </summary>
    public struct ScreenFragment
    {
        /// <summary>Display name of this element (e.g., "Sparrow MK-I", "Scanner-T1").</summary>
        public string Name;

        /// <summary>Optional semantic label (e.g., "Ship", "Slot[0,0]"). Renders as "Label: Name".</summary>
        public string Label;

        /// <summary>Optional description for context (e.g., "Damaged", "Tier 2").</summary>
        public string Description;

        /// <summary>Actions available on this element. Null or empty = no actions.</summary>
        public GameAction[] Actions;

        /// <summary>Nested elements. Null or empty = leaf node.</summary>
        public ScreenFragment[] Children;
    }

    /// <summary>
    /// An action the agent can invoke via "interact" command.
    /// </summary>
    public class GameAction
    {
        /// <summary>Semantic identifier used in action paths (e.g., "Unequip", "Launch", "Equip").</summary>
        public string Id;

        /// <summary>Optional human-readable hint (e.g., "Go to Hub"). Shown to agent if present.</summary>
        public string Hint;

        /// <summary>Whether the action can be invoked right now.</summary>
        public bool Enabled;

        /// <summary>Why disabled — shown to agent so it doesn't waste a turn. Null if enabled.</summary>
        public string DisabledReason;

        /// <summary>
        /// The callback. Returns an optional result message (null = silent success).
        /// Bridge calls this, then re-describes the widget to show updated state.
        /// </summary>
        public Func<string> Execute;

        /// <summary>Create an enabled action.</summary>
        public static GameAction Create(string id, Func<string> execute, string hint = null)
        {
            return new GameAction { Id = id, Enabled = true, Execute = execute, Hint = hint };
        }

        /// <summary>Create a disabled action (visible but not invocable).</summary>
        public static GameAction Disabled(string id, string reason)
        {
            return new GameAction { Id = id, Enabled = false, DisabledReason = reason, Execute = null };
        }
    }

    #endregion
}
