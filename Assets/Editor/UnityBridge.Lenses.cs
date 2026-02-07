using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Editor
{
    public static partial class UnityBridge
    {
        #region Lens System

        private class LensDefinition
        {
            public string Name { get; }
            public string Description { get; }
            public Func<Component, bool> Filter { get; }

            public LensDefinition(string name, string description, Func<Component, bool> filter)
            {
                Name = name;
                Description = description;
                Filter = filter;
            }
        }

        private static readonly Dictionary<string, LensDefinition> Lenses = new()
        {
            ["layout"] = new LensDefinition(
                "layout",
                "RectTransform, LayoutGroup, LayoutElement, ContentSizeFitter, Canvas, CanvasScaler",
                c => c is RectTransform
                     || c is LayoutGroup
                     || c is LayoutElement
                     || c is ContentSizeFitter
                     || c is Canvas
                     || c is CanvasScaler
            ),

            ["physics"] = new LensDefinition(
                "physics",
                "Transform, Rigidbody2D, Collider2D, Joint2D",
                c => (c is Transform && c is not RectTransform)
                     || c is Rigidbody2D
                     || c is Collider2D
                     || c is Joint2D
            ),

            ["scripts"] = new LensDefinition(
                "scripts",
                "Custom MonoBehaviour (project scripts)",
                IsCustomScript
            ),

            ["visual"] = new LensDefinition(
                "visual",
                "SpriteRenderer, Image, RawImage, TMP_Text",
                c => c is SpriteRenderer
                     || c is Image
                     || c is RawImage
                     || c is TMP_Text
            ),

            ["all"] = new LensDefinition(
                "all",
                "All components (no filtering)",
                _ => true
            )
        };

        /// <summary>
        /// Detects custom scripts vs Unity built-in components.
        /// Custom = assembly is project code, not Unity/System/third-party.
        /// </summary>
        private static bool IsCustomScript(Component c)
        {
            if (c == null) return false;

            var type = c.GetType();
            var assemblyName = type.Assembly.GetName().Name;

            // Project assemblies
            if (assemblyName == "Assembly-CSharp" || assemblyName == "Assembly-CSharp-Editor")
                return true;

            // Exclude known system/Unity assemblies
            var excludePrefixes = new[] { "Unity", "System", "mscorlib", "netstandard",
                                          "Newtonsoft", "nunit", "TMPro", "R3" };
            return !excludePrefixes.Any(p => assemblyName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns which lenses would show components for this GameObject.
        /// </summary>
        private static IEnumerable<string> GetLensHints(GameObject go)
        {
            var components = go.GetComponents<Component>().Where(c => c != null).ToList();
            var hints = new List<string>();

            foreach (var (name, lens) in Lenses)
            {
                if (name == "all") continue;
                if (components.Any(c => lens.Filter(c)))
                    hints.Add(name);
            }

            return hints;
        }

        #endregion
    }
}
