using System;
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
        #region Read Command Handlers

        private static string HandleSceneRequest()
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();

            sb.AppendLine($"# Scene: {scene.name}");
            sb.AppendLine($"**Path:** `{scene.path}`");
            sb.AppendLine();

            foreach (var root in scene.GetRootGameObjects().OrderBy(go => go.transform.GetSiblingIndex()))
            {
                ExportGameObject(root, sb, 0, false);
            }

            return sb.ToString();
        }

        private static string HandlePrefabRequest(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "Error: prefab path required. Example: `prefab` with query `Assets/Prefabs/MyPrefab.prefab`";

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                return $"Error: Prefab not found at `{path}`";

            var sb = new StringBuilder();
            sb.AppendLine($"# Prefab: {prefab.name}");
            sb.AppendLine($"**Path:** `{path}`");
            sb.AppendLine();

            ExportGameObject(prefab, sb, 0, true);

            return sb.ToString();
        }

        private static string HandleFindRequest(string query)
        {
            if (string.IsNullOrEmpty(query))
                return "Error: component name required. Example: `find` with query `MultiNetworkAdapter`";

            var sb = new StringBuilder();
            sb.AppendLine($"# Find: {query}");
            sb.AppendLine();

            var type = FindType(query);
            if (type == null)
            {
                sb.AppendLine($"Component type `{query}` not found. Searching by name instead...");
                sb.AppendLine();

                // Fallback: search by GameObject name
                var byName = GetAllSceneGameObjects()
                    .Where(go => go.name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(20)
                    .ToList();

                if (byName.Count == 0)
                {
                    sb.AppendLine("No GameObjects found.");
                }
                else
                {
                    foreach (var go in byName)
                    {
                        var location = go.scene.IsValid() ? $"Scene: {go.scene.name}" : "Asset";
                        sb.AppendLine($"- **{GetFullPath(go)}** ({location})");
                    }
                }
            }
            else
            {
                var components = GetAllSceneGameObjects()
                    .SelectMany(go => go.GetComponents(type))
                    .Take(30)
                    .ToList();

                sb.AppendLine($"Found {components.Count} instances of `{type.Name}`:");
                sb.AppendLine();

                foreach (var comp in components.OfType<Component>())
                {
                    var go = comp.gameObject;
                    var active = go.activeInHierarchy ? "" : " *(inactive)*";
                    sb.AppendLine($"- **{GetFullPath(go)}**{active}");
                }
            }

            return sb.ToString();
        }

        private static string HandleInspectRequest(BridgeRequest request)
        {
            var path = request.path ?? request.query;
            if (string.IsNullOrEmpty(path))
                return "Error: path required. Example: {\"type\": \"inspect\", \"path\": \"Game World/Ship\"}";

            var go = FindGameObjectByPath(path);
            if (go == null)
                return $"Error: GameObject not found at path `{path}`";

            // Defaults: depth=1 (direct children), detail=full
            var depth = request.depth > 0 ? request.depth : 1;
            var detail = string.IsNullOrEmpty(request.detail) ? "full" : request.detail.ToLower();
            var lens = string.IsNullOrEmpty(request.lens) ? null : request.lens.ToLower();

            // Validate lens
            if (lens != null && !Lenses.ContainsKey(lens))
                return $"Error: Unknown lens `{lens}`. Available: {string.Join(", ", Lenses.Keys)}";

            var sb = new StringBuilder();
            InspectGameObject(go, sb, 0, depth, detail, lens);
            return sb.ToString();
        }

        private static void InspectGameObject(GameObject go, StringBuilder sb, int indent, int remainingDepth, string detail, string lensName)
        {
            var prefix = indent > 0 ? new string(' ', indent * 2) : "";
            var activeMarker = go.activeInHierarchy ? "" : " *(inactive)*";

            // Header
            if (indent == 0)
            {
                sb.AppendLine($"# {go.name}{activeMarker}");
                sb.AppendLine($"**Path:** `{GetFullPath(go)}`");
                if (!string.IsNullOrEmpty(lensName))
                    sb.AppendLine($"**Lens:** {lensName}");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"{prefix}**{go.name}**{activeMarker}");
            }

            // Default mode (no lens): show name + hints only
            if (string.IsNullOrEmpty(lensName))
            {
                var hints = GetLensHints(go).ToList();
                if (hints.Count > 0)
                    sb.AppendLine($"{prefix}  [Lenses: {string.Join(", ", hints)}]");

                // Recurse for children
                if (go.transform.childCount > 0 && remainingDepth > 0)
                {
                    foreach (Transform child in go.transform)
                        InspectGameObject(child.gameObject, sb, indent + 1, remainingDepth - 1, detail, null);
                }
                else if (go.transform.childCount > 0)
                {
                    sb.AppendLine($"{prefix}  Children: {go.transform.childCount}");
                }
                return;
            }

            // Lens-filtered components
            var lens = Lenses[lensName];
            var components = go.GetComponents<Component>()
                .Where(c => c != null && lens.Filter(c))
                .ToList();

            if (components.Count > 0)
            {
                if (detail == "minimal")
                {
                    var compNames = string.Join(", ", components.Select(c => c.GetType().Name));
                    sb.AppendLine($"{prefix}  [{compNames}]");
                }
                else if (detail == "components")
                {
                    var compNames = string.Join(", ", components.Select(c => c.GetType().Name));
                    sb.AppendLine($"{prefix}  Components: {compNames}");
                }
                else // full
                {
                    sb.AppendLine($"{prefix}  Components:");
                    foreach (var comp in components)
                    {
                        var compName = comp.GetType().Name;
                        var enabled = comp is Behaviour b && !b.enabled ? " *(disabled)*" : "";
                        sb.AppendLine($"{prefix}    - **{compName}**{enabled}");
                        ExportComponentValuesIndented(comp, sb, indent + 3);
                    }
                }
            }

            // Children
            if (go.transform.childCount > 0)
            {
                if (remainingDepth > 0)
                {
                    if (components.Count > 0 || indent == 0)
                        sb.AppendLine($"{prefix}  Children:");
                    foreach (Transform child in go.transform)
                        InspectGameObject(child.gameObject, sb, indent + 1, remainingDepth - 1, detail, lensName);
                }
                else
                {
                    sb.AppendLine($"{prefix}  Children: {go.transform.childCount}");
                }
            }

            if (indent == 0) sb.AppendLine();
        }

        private static string HandlePrefabsListRequest(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                folder = "Assets/Prefabs";

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            var sb = new StringBuilder();

            sb.AppendLine($"# Prefabs in `{folder}`");
            sb.AppendLine($"**Count:** {guids.Length}");
            sb.AppendLine();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    var components = prefab.GetComponents<Component>()
                        .Where(c => c != null && c is not Transform)
                        .Select(c => c.GetType().Name);

                    sb.AppendLine($"- **{prefab.name}**");
                    sb.AppendLine($"  - Path: `{path}`");
                    sb.AppendLine($"  - Components: {string.Join(", ", components)}");
                }
            }

            return sb.ToString();
        }

        private static string HandleSelectionRequest()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Current Selection");
            sb.AppendLine();

            if (Selection.activeGameObject != null)
            {
                sb.AppendLine("## Selected GameObject");
                ExportGameObject(Selection.activeGameObject, sb, 0, true);
            }
            else if (Selection.activeObject != null)
            {
                sb.AppendLine($"## Selected Asset");
                sb.AppendLine($"**Name:** {Selection.activeObject.name}");
                sb.AppendLine($"**Type:** {Selection.activeObject.GetType().Name}");
                sb.AppendLine($"**Path:** `{AssetDatabase.GetAssetPath(Selection.activeObject)}`");
            }
            else
            {
                sb.AppendLine("*Nothing selected*");
            }

            return sb.ToString();
        }

        private static string HandleLogsRequest(string countStr)
        {
            // Note: Unity doesn't expose console logs programmatically easily
            // This is a placeholder - would need reflection or custom log capture
            return "Log capture not implemented yet. Check Assets/Logs/ for file-based logs.";
        }

        #endregion

        #region Compilation Status

        private static string HandleErrorsRequest()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Compilation Status");
            sb.AppendLine();

            if (_isCompiling || EditorApplication.isCompiling)
            {
                var elapsed = (DateTime.Now - CompilationStartTime).TotalSeconds;
                sb.AppendLine("**Status:** Compiling...");
                sb.AppendLine($"**Elapsed:** {elapsed:F1}s");
                return sb.ToString();
            }

            if (_lastCompilationErrors.Count > 0)
            {
                sb.AppendLine($"**Status:** {_lastCompilationErrors.Count} error(s)");
                sb.AppendLine();

                foreach (var error in _lastCompilationErrors)
                {
                    var file = Path.GetFileName(error.file);
                    sb.AppendLine($"### {file}:{error.line}");
                    sb.AppendLine("```");
                    sb.AppendLine(error.message);
                    sb.AppendLine("```");
                    sb.AppendLine($"**Path:** `{error.file}`");
                    sb.AppendLine();
                }

                return sb.ToString();
            }

            sb.AppendLine("**Status:** No errors");
            sb.AppendLine();
            sb.AppendLine("All scripts compiled successfully.");
            return sb.ToString();
        }

        private static string HandleHelpRequest()
        {
            return @"# UnityBridge Commands

## Read Commands

| Type | Fields | Description |
|------|--------|-------------|
| `scene` | - | Export current scene hierarchy |
| `prefab` | `path` | Export prefab structure with values |
| `find` | `component` | Find all GameObjects with component |
| `inspect` | `path`, `depth`, `detail`, `lens` | Inspect GameObject with lens filter |
| `prefabs` | `path` | List all prefabs in folder |
| `selection` | - | Export currently selected object |
| `errors` | - | Show compilation errors (if any) |
| `help` | - | Show this help |

## Write Commands

| Type | Fields | Description |
|------|--------|-------------|
| `create` | `path` | Create empty GameObject at path |
| `add-component` | `path`, `component` | Add component to GameObject |
| `set` | `path`, `component`, `property`/`value` or `properties` | Set serialized properties |
| `save-scene` | - | Save current scene |
| `new-scene` | `path` | Create and open new scene |
| `open-scene` | `path` | Open existing scene |
| `refresh` | - | Trigger AssetDatabase.Refresh() |

## Examples

### Read operations:
```json
{""type"": ""find"", ""component"": ""MultiNetworkAdapter""}
{""type"": ""inspect"", ""path"": ""Game World/Ship""}
{""type"": ""inspect"", ""path"": ""Game World"", ""depth"": 2, ""detail"": ""components""}
{""type"": ""inspect"", ""path"": ""UI Canvas"", ""lens"": ""layout"", ""depth"": 2}
{""type"": ""inspect"", ""path"": ""Game World/Ship"", ""lens"": ""scripts""}
{""type"": ""prefab"", ""path"": ""Assets/Prefabs/Reactor.prefab""}
```

## Lens Parameter

| Lens | Includes |
|------|----------|
| (none) | Minimal output with hints about available lenses |
| `layout` | RectTransform, LayoutGroup, LayoutElement, ContentSizeFitter, Canvas, CanvasScaler |
| `physics` | Transform, Rigidbody2D, Collider2D, Joint2D |
| `scripts` | Custom MonoBehaviour (project scripts only) |
| `visual` | SpriteRenderer, Image, RawImage, TMP_Text |
| `all` | All components (no filtering) |

```json
```

### Create GameObject:
```json
{""type"": ""create"", ""path"": ""Game World/Station/NewDockingSlot""}
```

### Add component:
```json
{""type"": ""add-component"", ""path"": ""Game World/Ship"", ""component"": ""MultiNetworkAdapter""}
```

### Set single property:
```json
{""type"": ""set"", ""path"": ""Game World/Ship"", ""component"": ""MultiNetworkAdapter"", ""property"": ""weight"", ""value"": ""10""}
```

### Set multiple properties:
```json
{""type"": ""set"", ""path"": ""Game World/Ship"", ""component"": ""MultiNetworkAdapter"", ""properties"": [
  {""key"": ""weight"", ""value"": ""10""},
  {""key"": ""maxFlowRate"", ""value"": ""100""}
]}
```

### Batch request (multiple operations):
```json
[
  {""type"": ""create"", ""path"": ""Game World/Station/DockingSlot""},
  {""type"": ""add-component"", ""path"": ""Game World/Station/DockingSlot"", ""component"": ""DockingSlot""},
  {""type"": ""set"", ""path"": ""Game World/Station/DockingSlot"", ""component"": ""DockingSlot"", ""properties"": [
    {""key"": ""resourceType"", ""value"": ""Assets/Resources/ResourceTypes/Volume.asset""},
    {""key"": ""maxFlowRate"", ""value"": ""100""}
  ]}
]
```

## Object References

For ObjectReference properties, use asset paths directly:
```json
{""type"": ""set"", ""path"": ""../"", ""component"": ""MultiNetworkAdapter"", ""property"": ""resourceType"", ""value"": ""Assets/Resources/ResourceTypes/Energy.asset""}
```

Or use `@path` syntax for scene objects:
```json
{""value"": ""@Game World/Ship:Transform""}
```

## Compilation Check

Use `errors` after writing code to verify it compiles:
```json
{""type"": ""errors""}
```
";
        }

        #endregion
    }
}
