using System;
using System.Collections.Generic;
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
        #region Describe Command Handlers

        private static string HandleDescribe(BridgeRequest request)
        {
            var root = request.path;

            // Collect all IDescribable in scene
            var describables = FindDescribables(root);

            if (describables.Count == 0)
            {
                if (!string.IsNullOrEmpty(root))
                    return $"No IDescribable found at `{root}` or its children.";
                return "No IDescribable found in scene.\n\nWidgets must implement `IDescribable` to appear here.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Screen");
            sb.AppendLine();

            foreach (var (go, describable) in describables)
            {
                var basePath = "/" + GetFullPath(go);
                var fragment = describable.Describe();
                RenderFragment(sb, fragment, basePath, indent: 0);
            }

            return sb.ToString();
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

            // Return result + full describe (vdoh-vydoh: interaction changes may ripple across the whole screen)
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(result))
            {
                sb.AppendLine($"**Result:** {result}");
                sb.AppendLine();
            }

            sb.Append(HandleDescribe(new BridgeRequest { type = "describe" }));

            return sb.ToString();
        }

        private static string HandleGameStep(BridgeRequest request)
        {
            if (!EditorApplication.isPlaying)
                return "Error: game-step requires Play Mode.";

            // Auto-pause if not paused (step without pause makes no sense)
            if (!EditorApplication.isPaused)
                EditorApplication.isPaused = true;

            var frames = request.frames > 0 ? request.frames : 1;

            for (int i = 0; i < frames; i++)
                EditorApplication.Step();

            var sb = new StringBuilder();
            sb.AppendLine($"**Stepped:** {frames} frame(s)");
            sb.AppendLine();
            sb.Append(HandleDescribe(new BridgeRequest { type = "describe" }));

            return sb.ToString();
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
                        actionLine += $" — disabled: \"{action.DisabledReason ?? "unavailable"}\"";
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
