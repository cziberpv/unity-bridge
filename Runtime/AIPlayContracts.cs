using System;
using System.Collections.Generic;

namespace UnityBridge
{
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

        /// <summary>
        /// Returns accumulated events since last call. Events are cleared after reading.
        /// Return null or empty list if no events.
        /// </summary>
        List<string> GetEvents() => null;
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
}
