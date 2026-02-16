using System.Collections.Generic;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Base class for widgets that need stable IDs and automatic lifecycle management.
    /// Registers with WidgetRegistry on enable, unregisters on disable.
    ///
    /// For simple widgets (no duplicates, no lifecycle needs), implementing IDescribable
    /// directly is still fine — Bridge discovers both.
    /// </summary>
    public abstract class DescribableWidget : MonoBehaviour, IDescribable
    {
        /// <summary>
        /// Stable ID assigned by WidgetRegistry. Available after OnEnable.
        /// Example: "Meteorite", "Meteorite#2".
        /// </summary>
        public string WidgetId { get; private set; }

        protected virtual void OnEnable()
        {
            WidgetId = WidgetRegistry.Register(this, GetWidgetName());
        }

        protected virtual void OnDisable()
        {
            WidgetRegistry.Unregister(this);
        }

        /// <summary>
        /// The base name for this widget. Used by WidgetRegistry to assign stable IDs.
        /// Return a semantic name: "Meteorite", "SmallHarvester", "Slot@0,-1".
        /// </summary>
        protected abstract string GetWidgetName();

        public abstract ScreenFragment Describe();

        public virtual List<string> GetEvents() => null;
    }
}
