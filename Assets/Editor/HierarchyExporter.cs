using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Editor
{
    public static class HierarchyExporter
    {
        private const string ExportFolder = "Assets/LLM/Exports";
        private const string OutputPath = ExportFolder + "/SceneHierarchy.md";

        // Типы полей которые стоит показывать
        private static readonly HashSet<Type> InterestingFieldTypes = new()
        {
            typeof(int), typeof(float), typeof(double), typeof(bool), typeof(string),
            typeof(Vector2), typeof(Vector3), typeof(Color),
            typeof(LayerMask), typeof(AnimationCurve)
        };

        [MenuItem("Tools/LLM/Export Hierarchy %#h")] // Ctrl+Shift+H
        public static void ExportHierarchy()
        {
            var selected = Selection.activeGameObject;
            var scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            string outputName;

            if (selected == null)
            {
                // Export full scene
                sb.AppendLine($"# Scene: {scene.name}");
                sb.AppendLine($"**Path:** `{scene.path}`");
                sb.AppendLine($"**Exported:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();

                var roots = scene.GetRootGameObjects()
                    .OrderBy(go => go.transform.GetSiblingIndex())
                    .ToArray();

                foreach (var root in roots)
                {
                    ExportGameObject(root, sb, 0);
                }

                outputName = scene.name;
            }
            else
            {
                // Export from selection
                sb.AppendLine($"# Hierarchy: {selected.name}");
                sb.AppendLine($"**Scene:** `{scene.name}`");

                // Build path from root to selection
                var pathParts = new List<string>();
                var current = selected.transform;
                while (current != null)
                {
                    pathParts.Insert(0, current.name);
                    current = current.parent;
                }
                sb.AppendLine($"**Path:** `{string.Join("/", pathParts)}`");
                sb.AppendLine($"**Exported:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();

                ExportGameObject(selected, sb, 0);

                outputName = selected.name;
            }

            var safeName = outputName.Replace(" ", "_").Replace("/", "_");
            var outputPath = $"{ExportFolder}/Hierarchy_{safeName}.md";
            Directory.CreateDirectory(ExportFolder);
            File.WriteAllText(outputPath, sb.ToString());
            AssetDatabase.Refresh();

            Debug.Log($"[HierarchyExporter] Exported to {outputPath}");
        }

        [MenuItem("Tools/LLM/Export Selected Prefab %#p")] // Ctrl+Shift+P
        public static void ExportSelectedPrefab()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[HierarchyExporter] No GameObject selected");
                return;
            }

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selected);
            if (string.IsNullOrEmpty(prefabPath))
            {
                // Может быть это сам prefab asset
                prefabPath = AssetDatabase.GetAssetPath(selected);
            }

            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(prefabPath))
            {
                sb.AppendLine($"# Prefab: {selected.name}");
                sb.AppendLine($"**Path:** `{prefabPath}`");
            }
            else
            {
                sb.AppendLine($"# GameObject: {selected.name}");
                sb.AppendLine("**Source:** Scene instance (not a prefab)");
            }

            sb.AppendLine($"**Exported:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            ExportGameObject(selected, sb, 0, includeValues: true);

            var safeName = selected.name.Replace(" ", "_").Replace("/", "_");
            var outputPath = $"{ExportFolder}/Prefab_{safeName}.md";
            Directory.CreateDirectory(ExportFolder);
            File.WriteAllText(outputPath, sb.ToString());
            AssetDatabase.Refresh();

            Debug.Log($"[HierarchyExporter] Exported to {outputPath}");
        }

        private static void ExportGameObject(GameObject go, StringBuilder sb, int indent, bool includeValues = false)
        {
            var prefix = new string(' ', indent * 2);
            var activeMarker = go.activeSelf ? "" : " *(inactive)*";

            sb.AppendLine($"{prefix}- **{go.name}**{activeMarker}");

            var components = go.GetComponents<Component>()
                .Where(c => c != null && c is not Transform)
                .ToArray();

            foreach (var comp in components)
            {
                var compName = comp.GetType().Name;
                var enabledMarker = "";

                if (comp is Behaviour behaviour && !behaviour.enabled)
                    enabledMarker = " *(disabled)*";

                sb.AppendLine($"{prefix}  - `{compName}`{enabledMarker}");

                if (includeValues)
                {
                    ExportComponentValues(comp, sb, indent + 2);
                }
            }

            foreach (Transform child in go.transform)
            {
                ExportGameObject(child.gameObject, sb, indent + 1, includeValues);
            }
        }

        private static void ExportComponentValues(Component comp, StringBuilder sb, int indent)
        {
            var prefix = new string(' ', indent * 2);
            var type = comp.GetType();

            // SerializedObject даёт доступ к тем же полям что видны в Inspector
            var so = new SerializedObject(comp);
            var prop = so.GetIterator();

            var values = new List<string>();

            if (prop.NextVisible(true))
            {
                do
                {
                    // Пропускаем служебные поля
                    if (prop.name == "m_Script" || prop.name == "m_ObjectHideFlags")
                        continue;

                    var value = GetPropertyValueString(prop);
                    if (value != null)
                    {
                        values.Add($"{prop.displayName}: {value}");
                    }
                } while (prop.NextVisible(false));
            }

            foreach (var val in values.Take(10)) // Лимит на количество полей
            {
                sb.AppendLine($"{prefix}- {val}");
            }

            if (values.Count > 10)
            {
                sb.AppendLine($"{prefix}- *...and {values.Count - 10} more fields*");
            }
        }

        private static string GetPropertyValueString(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => prop.intValue.ToString(),
                SerializedPropertyType.Boolean => prop.boolValue.ToString(),
                SerializedPropertyType.Float => prop.floatValue.ToString("F2"),
                SerializedPropertyType.String => string.IsNullOrEmpty(prop.stringValue) ? null : $"\"{prop.stringValue}\"",
                SerializedPropertyType.Enum => prop.enumDisplayNames.ElementAtOrDefault(prop.enumValueIndex),
                SerializedPropertyType.Vector2 => prop.vector2Value.ToString(),
                SerializedPropertyType.Vector3 => prop.vector3Value.ToString(),
                SerializedPropertyType.Color => ColorToHex(prop.colorValue),
                SerializedPropertyType.ObjectReference => prop.objectReferenceValue != null
                    ? $"→ {prop.objectReferenceValue.name}"
                    : null,
                SerializedPropertyType.LayerMask => LayerMaskToString(prop.intValue),
                _ => null // Пропускаем сложные типы
            };
        }

        private static string ColorToHex(Color c)
        {
            return $"#{ColorUtility.ToHtmlStringRGBA(c)}";
        }

        private static string LayerMaskToString(int mask)
        {
            if (mask == 0) return "Nothing";
            if (mask == -1) return "Everything";

            var layers = new List<string>();
            for (var i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    var name = LayerMask.LayerToName(i);
                    if (!string.IsNullOrEmpty(name))
                        layers.Add(name);
                }
            }
            return string.Join(", ", layers);
        }
    }
}
