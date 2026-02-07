using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Editor
{
    public static partial class UnityBridge
    {
        #region Helpers

        private static IEnumerable<GameObject> GetAllSceneGameObjects()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var go in GetAllChildren(root))
                {
                    yield return go;
                }
            }
        }

        private static IEnumerable<GameObject> GetAllChildren(GameObject parent)
        {
            yield return parent;
            foreach (Transform child in parent.transform)
            {
                foreach (var go in GetAllChildren(child.gameObject))
                {
                    yield return go;
                }
            }
        }

        private static void ExportGameObject(GameObject go, StringBuilder sb, int indent, bool includeValues)
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
                    ExportComponentValuesIndented(comp, sb, indent + 2);
                }
            }

            foreach (Transform child in go.transform)
            {
                ExportGameObject(child.gameObject, sb, indent + 1, includeValues);
            }
        }

        private static void ExportComponentValues(Component comp, StringBuilder sb)
        {
            var so = new SerializedObject(comp);
            var prop = so.GetIterator();

            if (prop.NextVisible(true))
            {
                do
                {
                    if (prop.name == "m_Script" || prop.name == "m_ObjectHideFlags")
                        continue;

                    var value = GetPropertyValueString(prop);
                    if (value != null)
                    {
                        sb.AppendLine($"- **{prop.displayName}:** {value}");
                    }
                } while (prop.NextVisible(false));
            }
        }

        private static void ExportComponentValuesIndented(Component comp, StringBuilder sb, int indent)
        {
            var prefix = new string(' ', indent * 2);
            var so = new SerializedObject(comp);
            var prop = so.GetIterator();

            var count = 0;
            if (prop.NextVisible(true))
            {
                do
                {
                    if (prop.name == "m_Script" || prop.name == "m_ObjectHideFlags")
                        continue;

                    var value = GetPropertyValueString(prop);
                    if (value != null && count < 8)
                    {
                        sb.AppendLine($"{prefix}- {prop.displayName}: {value}");
                        count++;
                    }
                } while (prop.NextVisible(false));
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
                SerializedPropertyType.Color => $"#{ColorUtility.ToHtmlStringRGBA(prop.colorValue)}",
                SerializedPropertyType.ObjectReference => prop.objectReferenceValue != null
                    ? $"-> {prop.objectReferenceValue.name}"
                    : null,
                _ => null
            };
        }

        private static string GetFullPath(GameObject go)
        {
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private static GameObject FindGameObjectByPath(string path)
        {
            var parts = path.Split('/');
            GameObject current = null;

            // Find root
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == parts[0])
                {
                    current = root;
                    break;
                }
            }

            if (current == null) return null;

            // Navigate path
            for (var i = 1; i < parts.Length; i++)
            {
                var found = false;
                foreach (Transform child in current.transform)
                {
                    if (child.name == parts[i])
                    {
                        current = child.gameObject;
                        found = true;
                        break;
                    }
                }
                if (!found) return null;
            }

            return current;
        }

        private static Type FindType(string name)
        {
            // Search in all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetTypes()
                        .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                                            && typeof(Component).IsAssignableFrom(t));
                    if (type != null) return type;
                }
                catch
                {
                    // Some assemblies may throw on GetTypes()
                }
            }
            return null;
        }

        private static bool IsAsset(GameObject go)
        {
            return !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(go));
        }

        private static void WriteResponse(BridgeRequest request, string content)
        {
            var sb = new StringBuilder();
            // Build request info string
            var requestInfo = request.type;
            if (!string.IsNullOrEmpty(request.path))
                requestInfo += $" path=\"{request.path}\"";
            if (!string.IsNullOrEmpty(request.component))
                requestInfo += $" component=\"{request.component}\"";
            if (!string.IsNullOrEmpty(request.query))
                requestInfo += $" query=\"{request.query}\"";

            sb.AppendLine($"<!-- Request: {requestInfo} -->");
            sb.AppendLine($"<!-- Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");
            sb.AppendLine();
            sb.Append(content);

            File.WriteAllText(ResponseFile, sb.ToString());
            AssetDatabase.Refresh();
        }

        private static void WriteError(string message)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Error");
            sb.AppendLine();
            sb.AppendLine(message);
            sb.AppendLine();
            sb.AppendLine("Use `help` command for available options.");

            File.WriteAllText(ResponseFile, sb.ToString());
        }

        #endregion
    }
}
