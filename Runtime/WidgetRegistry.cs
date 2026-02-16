using System.Collections.Generic;

namespace UnityBridge
{
    /// <summary>
    /// Central registry for IDescribable widgets. Assigns stable IDs that survive
    /// sibling changes (counter never decrements). Widgets register via DescribableWidget
    /// lifecycle (OnEnable/OnDisable) or manually.
    ///
    /// Static fields reset on domain reload — by design. Reload = fresh start,
    /// agent re-describes and gets current truth.
    /// </summary>
    public static class WidgetRegistry
    {
        private static readonly Dictionary<IDescribable, string> _widgets = new();
        private static readonly Dictionary<string, int> _nameCounters = new();

        /// <summary>
        /// Register a widget with a base name. Returns stable ID.
        /// First "Meteorite" → "Meteorite". Second → "Meteorite#2". Third → "Meteorite#3".
        /// If already registered, returns existing ID.
        /// </summary>
        public static string Register(IDescribable widget, string name)
        {
            if (_widgets.TryGetValue(widget, out var existingId))
                return existingId;

            _nameCounters.TryGetValue(name, out var count);
            var id = count == 0 ? name : $"{name}#{count + 1}";
            _nameCounters[name] = count + 1;
            _widgets[widget] = id;
            return id;
        }

        /// <summary>
        /// Unregister a widget. Counter is NOT decremented — keeps IDs stable.
        /// </summary>
        public static void Unregister(IDescribable widget)
        {
            _widgets.Remove(widget);
        }

        /// <summary>
        /// All currently registered widgets. Key = IDescribable, Value = stable ID.
        /// </summary>
        public static IReadOnlyDictionary<IDescribable, string> All => _widgets;

        /// <summary>
        /// Try to get stable ID for a widget. Returns false if not registered.
        /// </summary>
        public static bool TryGetId(IDescribable widget, out string id)
        {
            return _widgets.TryGetValue(widget, out id);
        }
    }
}
