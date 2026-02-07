using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    public static partial class UnityBridge
    {
        #region Write Command Handlers

        private static string HandleCreateRequest(BridgeRequest request)
        {
            // Use structured field, fallback to legacy query
            var path = request.path ?? request.query;

            if (string.IsNullOrEmpty(path))
                return "Error: path required. Example: {\"type\": \"create\", \"path\": \"Game World/NewObject\"}";

            // Parse parent path and name
            var lastSlash = path.LastIndexOf('/');
            var parentPath = lastSlash > 0 ? path[..lastSlash] : null;
            var name = lastSlash > 0 ? path[(lastSlash + 1)..] : path;

            // Find parent
            Transform parent = null;
            if (parentPath != null)
            {
                var parentGo = FindGameObjectByPath(parentPath);
                if (parentGo == null)
                    return $"Error: Parent not found: `{parentPath}`";
                parent = parentGo.transform;
            }

            // Create
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);

            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

            // Add components if specified
            var componentResults = new StringBuilder();
            if (request.components != null && request.components.Length > 0)
            {
                foreach (var compName in request.components)
                {
                    var compType = FindType(compName);
                    if (compType == null)
                    {
                        componentResults.AppendLine($"  - {compName}: Error - type not found");
                        continue;
                    }

                    var comp = go.AddComponent(compType);

                    // Auto-assign placeholder sprite for SpriteRenderer (if available)
                    if (comp is SpriteRenderer sr && sr.sprite == null)
                    {
                        var placeholderPath = "Assets/Sprites/WhiteSquare.png";
                        if (File.Exists(placeholderPath))
                        {
                            var placeholder = AssetDatabase.LoadAssetAtPath<Sprite>(placeholderPath);
                            if (placeholder != null)
                                sr.sprite = placeholder;
                        }
                    }

                    componentResults.AppendLine($"  - {compType.Name}");
                }
            }

            EditorUtility.SetDirty(go);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

            var result = $"Created: `{GetFullPath(go)}`";
            if (componentResults.Length > 0)
                result += $"\nComponents:\n{componentResults}";

            return result;
        }

        private static string HandleAddComponentRequest(BridgeRequest request)
        {
            // Prefer structured fields, fallback to legacy query parsing
            string path;
            string typeName;

            if (!string.IsNullOrEmpty(request.path) && !string.IsNullOrEmpty(request.component))
            {
                // Structured fields
                path = request.path;
                typeName = request.component;
            }
            else if (!string.IsNullOrEmpty(request.query))
            {
                // Legacy: parse "Path/To/Object TypeName" using LAST space as delimiter
                var lastSpace = request.query.LastIndexOf(' ');
                if (lastSpace <= 0)
                    return "Error: Both path and component required.\nStructured: {\"type\": \"add-component\", \"path\": \"...\", \"component\": \"...\"}";

                path = request.query[..lastSpace];
                typeName = request.query[(lastSpace + 1)..];
            }
            else
            {
                return "Error: path and component required.\nExample: {\"type\": \"add-component\", \"path\": \"Game World/Ship\", \"component\": \"MultiNetworkAdapter\"}";
            }

            var type = FindType(typeName);
            if (type == null)
                return $"Error: Component type not found: `{typeName}`";

            // Check if this is a prefab asset path
            if (path.StartsWith("Assets/") && path.EndsWith(".prefab"))
            {
                return AddComponentToPrefab(path, type);
            }

            // Scene GameObject
            var go = FindGameObjectByPath(path);
            if (go == null)
                return $"Error: GameObject not found: `{path}`";

            // Check if already has component
            if (go.GetComponent(type) != null)
                return $"Warning: `{go.name}` already has `{type.Name}` component";

            var component = Undo.AddComponent(go, type);

            // Auto-assign placeholder sprite for SpriteRenderer (if available)
            var spriteInfo = "";
            if (component is SpriteRenderer sr && sr.sprite == null)
            {
                var placeholderPath = "Assets/Sprites/WhiteSquare.png";
                if (File.Exists(placeholderPath))
                {
                    var placeholder = AssetDatabase.LoadAssetAtPath<Sprite>(placeholderPath);
                    if (placeholder != null)
                    {
                        sr.sprite = placeholder;
                        spriteInfo = " (placeholder sprite assigned)";
                    }
                }
            }

            EditorUtility.SetDirty(go);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

            return $"Added `{type.Name}` to `{GetFullPath(go)}`{spriteInfo}";
        }

        private static string AddComponentToPrefab(string assetPath, Type componentType)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                return $"Error: Prefab not found at `{assetPath}`";

            // Check if already has component
            if (prefab.GetComponent(componentType) != null)
                return $"Warning: `{prefab.name}` already has `{componentType.Name}` component";

            // Open prefab for editing
            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

            try
            {
                prefabRoot.AddComponent(componentType);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                return $"Added `{componentType.Name}` to prefab `{prefab.name}`";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static string HandleSaveSceneRequest()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.isDirty)
                return $"Scene `{scene.name}` has no unsaved changes.";

            var saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            if (saved)
                return $"Saved scene: `{scene.name}`\nPath: `{scene.path}`";
            else
                return $"Failed to save scene: `{scene.name}`";
        }

        private static string HandleRefreshRequest()
        {
            // Mark that we're waiting for compilation result
            RefreshPending = true;

            // Trigger refresh
            AssetDatabase.Refresh();

            // If compiling, don't write response now - WriteCompilationResult will do it later
            if (EditorApplication.isCompiling || _isCompiling)
            {
                return null; // Signal: don't write response yet
            }

            // No compilation needed
            RefreshPending = false;
            return "# Refresh\n\n**Status:** No changes detected\n\nNo scripts needed recompilation.";
        }

        private static string HandleNewSceneRequest(BridgeRequest request)
        {
            var path = request.path ?? request.query;

            if (string.IsNullOrEmpty(path))
                return "Error: path required. Example: {\"type\": \"new-scene\", \"path\": \"Assets/Scenes/Prototypes/MyScene.unity\"}";

            // Ensure path ends with .unity
            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                path += ".unity";

            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Check if scene already exists
            if (File.Exists(path))
                return $"Error: Scene already exists at `{path}`. Use `open-scene` to open it.";

            // Handle dirty scene
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (currentScene.isDirty)
            {
                if (request.force)
                {
                    // Force: discard changes without asking
                }
                else
                {
                    var saved = UnityEditor.SceneManagement.EditorSceneManager.SaveModifiedScenesIfUserWantsTo(new[] { currentScene });
                    if (!saved)
                        return "Error: User cancelled save dialog. Scene not created. Use \"force\": true to discard changes.";
                }
            }

            // Create new scene
            var newScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // Save immediately to the specified path
            var saveResult = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(newScene, path);
            if (!saveResult)
                return $"Error: Failed to save scene to `{path}`";

            AssetDatabase.Refresh();

            return $"Created and opened new scene: `{path}`";
        }

        private static string HandleOpenSceneRequest(BridgeRequest request)
        {
            var path = request.path ?? request.query;

            if (string.IsNullOrEmpty(path))
                return "Error: path required. Example: {\"type\": \"open-scene\", \"path\": \"Assets/Scenes/MyScene.unity\"}";

            // Ensure path ends with .unity
            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                path += ".unity";

            // Check if scene exists
            if (!File.Exists(path))
                return $"Error: Scene not found at `{path}`. Use `new-scene` to create it.";

            // Handle dirty scene
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (currentScene.isDirty)
            {
                if (request.force)
                {
                    // Force: discard changes without asking
                }
                else
                {
                    var saved = UnityEditor.SceneManagement.EditorSceneManager.SaveModifiedScenesIfUserWantsTo(new[] { currentScene });
                    if (!saved)
                        return "Error: User cancelled save dialog. Scene not opened. Use \"force\": true to discard changes.";
                }
            }

            // Open the scene
            var openedScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Single);

            if (!openedScene.IsValid())
                return $"Error: Failed to open scene at `{path}`";

            return $"Opened scene: `{path}`\nRoot objects: {openedScene.rootCount}";
        }

        private static string HandleSetRequest(BridgeRequest request)
        {
            // Prefer structured fields
            if (!string.IsNullOrEmpty(request.path) && !string.IsNullOrEmpty(request.component))
            {
                return HandleSetRequestStructured(request);
            }

            // Legacy query-based set is no longer supported
            return "Error: path, component, and properties required.\nExample: {\"type\": \"set\", \"path\": \"Game World/Ship\", \"component\": \"MultiNetworkAdapter\", \"property\": \"weight\", \"value\": \"10\"}";
        }

        private static string HandleSetRequestStructured(BridgeRequest request)
        {
            // Check if this is a prefab asset path
            if (request.path.StartsWith("Assets/") && request.path.EndsWith(".prefab"))
            {
                return SetPrefabProperties(request);
            }

            var go = FindGameObjectByPath(request.path);
            if (go == null)
                return $"Error: GameObject not found: `{request.path}`";

            var compType = FindType(request.component);
            if (compType == null)
                return $"Error: Component type not found: `{request.component}`";

            var component = go.GetComponent(compType);
            if (component == null)
                return $"Error: `{go.name}` does not have `{request.component}` component";

            var so = new SerializedObject(component);
            var sb = new StringBuilder();
            sb.AppendLine($"Set `{GetFullPath(go)}` {request.component}:");

            // Handle properties array (batch set)
            if (request.properties != null && request.properties.Length > 0)
            {
                foreach (var kvp in request.properties)
                {
                    var prop = so.FindProperty(kvp.key) ?? so.FindProperty("_" + kvp.key);
                    if (prop == null)
                    {
                        sb.AppendLine($"  - {kvp.key}: Error - property not found");
                        continue;
                    }

                    var result = SetPropertyValue(prop, kvp.value);
                    if (result != null)
                    {
                        sb.AppendLine($"  - {kvp.key}: Error - {result}");
                    }
                    else
                    {
                        sb.AppendLine($"  - {kvp.key} = {kvp.value}");
                    }
                }
            }
            // Handle single property/value
            else if (!string.IsNullOrEmpty(request.property) && request.value != null)
            {
                var prop = so.FindProperty(request.property) ?? so.FindProperty("_" + request.property);
                if (prop == null)
                    return $"Error: Property `{request.property}` not found on `{request.component}`";

                var result = SetPropertyValue(prop, request.value);
                if (result != null)
                    return result;

                sb.AppendLine($"  - {request.property} = {request.value}");
            }
            else
            {
                return "Error: Either 'properties' array or 'property'/'value' pair required.";
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

            return sb.ToString();
        }

        private static string SetPrefabProperties(BridgeRequest request)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(request.path);
            if (prefab == null)
                return $"Error: Prefab not found at `{request.path}`";

            var compType = FindType(request.component);
            if (compType == null)
                return $"Error: Component type not found: `{request.component}`";

            // Open prefab for editing
            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

            try
            {
                var component = prefabRoot.GetComponent(compType);
                if (component == null)
                    return $"Error: `{prefab.name}` does not have `{request.component}` component";

                var so = new SerializedObject(component);
                var sb = new StringBuilder();
                sb.AppendLine($"Set prefab `{prefab.name}` {request.component}:");

                // Handle properties array (batch set)
                if (request.properties != null && request.properties.Length > 0)
                {
                    foreach (var kvp in request.properties)
                    {
                        var prop = so.FindProperty(kvp.key) ?? so.FindProperty("_" + kvp.key);
                        if (prop == null)
                        {
                            sb.AppendLine($"  - {kvp.key}: Error - property not found");
                            continue;
                        }

                        var result = SetPropertyValue(prop, kvp.value);
                        if (result != null)
                        {
                            sb.AppendLine($"  - {kvp.key}: Error - {result}");
                        }
                        else
                        {
                            sb.AppendLine($"  - {kvp.key} = {kvp.value}");
                        }
                    }
                }
                // Handle single property/value
                else if (!string.IsNullOrEmpty(request.property) && request.value != null)
                {
                    var prop = so.FindProperty(request.property) ?? so.FindProperty("_" + request.property);
                    if (prop == null)
                        return $"Error: Property `{request.property}` not found on `{request.component}`";

                    var result = SetPropertyValue(prop, request.value);
                    if (result != null)
                        return result;

                    sb.AppendLine($"  - {request.property} = {request.value}");
                }
                else
                {
                    return "Error: Either 'properties' array or 'property'/'value' pair required.";
                }

                so.ApplyModifiedProperties();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);

                return sb.ToString();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static string SetPropertyValue(SerializedProperty prop, JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                {
                    prop.objectReferenceValue = null;
                    return null;
                }
                return "Error: null value not supported for this property type";
            }

            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        prop.intValue = value.Value<int>();
                        break;

                    case SerializedPropertyType.Float:
                        prop.floatValue = value.Value<float>();
                        break;

                    case SerializedPropertyType.Boolean:
                        prop.boolValue = value.Value<bool>();
                        break;

                    case SerializedPropertyType.String:
                        prop.stringValue = value.Value<string>();
                        break;

                    case SerializedPropertyType.Enum:
                        if (value.Type == JTokenType.Integer)
                        {
                            prop.enumValueIndex = value.Value<int>();
                        }
                        else
                        {
                            var enumName = value.Value<string>();
                            var enumIndex = Array.IndexOf(prop.enumNames, enumName);
                            if (enumIndex >= 0)
                                prop.enumValueIndex = enumIndex;
                            else
                                return $"Error: Invalid enum value `{enumName}`. Valid: {string.Join(", ", prop.enumNames)}";
                        }
                        break;

                    case SerializedPropertyType.Vector2:
                        if (value.Type == JTokenType.Array)
                        {
                            var arr = (JArray)value;
                            if (arr.Count >= 2)
                                prop.vector2Value = new Vector2(arr[0].Value<float>(), arr[1].Value<float>());
                            else
                                return "Error: Vector2 array must have 2 elements: [x, y]";
                        }
                        else
                            return "Error: Vector2 requires array format: [x, y]";
                        break;

                    case SerializedPropertyType.Vector3:
                        if (value.Type == JTokenType.Array)
                        {
                            var arr = (JArray)value;
                            if (arr.Count >= 3)
                                prop.vector3Value = new Vector3(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>());
                            else
                                return "Error: Vector3 array must have 3 elements: [x, y, z]";
                        }
                        else
                            return "Error: Vector3 requires array format: [x, y, z]";
                        break;

                    case SerializedPropertyType.Color:
                        if (value.Type == JTokenType.Array)
                        {
                            var arr = (JArray)value;
                            if (arr.Count >= 3)
                            {
                                var a = arr.Count > 3 ? arr[3].Value<float>() : 1f;
                                prop.colorValue = new Color(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>(), a);
                            }
                            else
                                return "Error: Color array must have 3-4 elements: [r, g, b] or [r, g, b, a]";
                        }
                        else if (value.Type == JTokenType.String)
                        {
                            var colorStr = value.Value<string>();
                            if (colorStr.StartsWith("#") && ColorUtility.TryParseHtmlString(colorStr, out var color))
                                prop.colorValue = color;
                            else
                                return $"Error: Invalid color string `{colorStr}`. Use #RRGGBB or [r,g,b,a] array";
                        }
                        else
                            return "Error: Color requires #RRGGBB string or [r,g,b,a] array";
                        break;

                    case SerializedPropertyType.ObjectReference:
                        var refStr = value.Value<string>();
                        if (string.IsNullOrEmpty(refStr) || refStr.ToLower() == "null")
                        {
                            prop.objectReferenceValue = null;
                        }
                        else if (refStr.StartsWith("@"))
                        {
                            var refPath = refStr[1..];
                            var colonIndex = refPath.IndexOf(':');
                            string compTypeName = null;
                            if (colonIndex > 0)
                            {
                                compTypeName = refPath[(colonIndex + 1)..];
                                refPath = refPath[..colonIndex];
                            }

                            var refGo = FindGameObjectByPath(refPath);
                            if (refGo != null)
                            {
                                if (compTypeName != null)
                                {
                                    var refCompType = FindType(compTypeName);
                                    if (refCompType == null)
                                        return $"Error: Component type not found: `{compTypeName}`";
                                    var refComp = refGo.GetComponent(refCompType);
                                    if (refComp == null)
                                        return $"Error: `{refPath}` does not have `{compTypeName}` component";
                                    prop.objectReferenceValue = refComp;
                                }
                                else
                                {
                                    prop.objectReferenceValue = refGo;
                                }
                            }
                            else
                            {
                                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(refPath);
                                if (asset == null)
                                    return $"Error: Object not found at `{refPath}`";
                                prop.objectReferenceValue = asset;
                            }
                        }
                        else if (refStr.StartsWith("Assets/"))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(refStr);
                            if (asset == null)
                                return $"Error: Asset not found at `{refStr}`";
                            prop.objectReferenceValue = asset;
                        }
                        else
                        {
                            return "Error: ObjectReference requires @Path/To/Object or Assets/path.ext";
                        }
                        break;

                    default:
                        // Check if it's an array
                        if (prop.isArray && value.Type == JTokenType.Array)
                        {
                            return SetArrayPropertyValue(prop, (JArray)value);
                        }
                        return $"Error: Property type `{prop.propertyType}` not supported yet";
                }

                return null; // Success
            }
            catch (Exception ex)
            {
                return $"Error: Failed to set property: {ex.Message}";
            }
        }

        private static string SetArrayPropertyValue(SerializedProperty arrayProp, JArray values)
        {
            // Resize array to match
            arrayProp.arraySize = values.Count;

            for (int i = 0; i < values.Count; i++)
            {
                var element = arrayProp.GetArrayElementAtIndex(i);
                var result = SetPropertyValue(element, values[i]);
                if (result != null)
                    return $"Error setting array element [{i}]: {result}";
            }

            return null; // Success
        }

        private static string HandleDeleteComponentRequest(BridgeRequest request)
        {
            var path = request.path ?? request.query;
            var typeName = request.component;

            if (string.IsNullOrEmpty(path))
                return "Error: path required. Example: {\"type\": \"delete-component\", \"path\": \"Player\", \"component\": \"Rigidbody2D\"}";

            if (string.IsNullOrEmpty(typeName))
                return "Error: component required. Example: {\"type\": \"delete-component\", \"path\": \"Player\", \"component\": \"Rigidbody2D\"}";

            var type = FindType(typeName);
            if (type == null)
                return $"Error: Component type not found: `{typeName}`";

            // Prevent deleting Transform
            if (type == typeof(Transform) || type == typeof(RectTransform))
                return $"Error: Cannot delete `{typeName}` — it's a required component";

            // Check if this is a prefab asset path
            if (path.StartsWith("Assets/") && path.EndsWith(".prefab"))
            {
                return DeleteComponentFromPrefab(path, type);
            }

            // Scene GameObject
            var go = FindGameObjectByPath(path);
            if (go == null)
                return $"Error: GameObject not found: `{path}`";

            var component = go.GetComponent(type);
            if (component == null)
                return $"Error: `{go.name}` does not have `{typeName}` component";

            Undo.DestroyObjectImmediate(component);
            EditorUtility.SetDirty(go);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

            return $"Deleted `{type.Name}` from `{GetFullPath(go)}`";
        }

        private static string DeleteComponentFromPrefab(string assetPath, Type componentType)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                return $"Error: Prefab not found at `{assetPath}`";

            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

            try
            {
                var component = prefabRoot.GetComponent(componentType);
                if (component == null)
                    return $"Error: `{prefab.name}` does not have `{componentType.Name}` component";

                UnityEngine.Object.DestroyImmediate(component);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                return $"Deleted `{componentType.Name}` from prefab `{prefab.name}`";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static string HandleRenameRequest(BridgeRequest request)
        {
            var path = request.path ?? request.query;
            var newName = request.value?.Value<string>();

            if (string.IsNullOrEmpty(path))
                return "Error: path required. Example: {\"type\": \"rename\", \"path\": \"OldName\", \"value\": \"NewName\"}";

            if (string.IsNullOrEmpty(newName))
                return "Error: value (new name) required. Example: {\"type\": \"rename\", \"path\": \"OldName\", \"value\": \"NewName\"}";

            var go = FindGameObjectByPath(path);
            if (go == null)
                return $"Error: GameObject not found: `{path}`";

            var oldName = go.name;
            Undo.RecordObject(go, $"Rename {oldName}");
            go.name = newName;
            EditorUtility.SetDirty(go);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

            return $"Renamed `{oldName}` → `{newName}`\nNew path: `{GetFullPath(go)}`";
        }

        private static string HandleDuplicateRequest(BridgeRequest request)
        {
            var path = request.path ?? request.query;
            var newName = request.value?.Value<string>();

            if (string.IsNullOrEmpty(path))
                return "Error: path required. Example: {\"type\": \"duplicate\", \"path\": \"Player\"}";

            var go = FindGameObjectByPath(path);
            if (go == null)
                return $"Error: GameObject not found: `{path}`";

            var duplicate = UnityEngine.Object.Instantiate(go, go.transform.parent);
            duplicate.name = newName ?? go.name; // Unity auto-adds (1) if same name

            Undo.RegisterCreatedObjectUndo(duplicate, $"Duplicate {go.name}");
            EditorUtility.SetDirty(duplicate);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(duplicate.scene);

            return $"Duplicated `{GetFullPath(go)}` → `{GetFullPath(duplicate)}`";
        }

        private static string HandleDeleteRequest(BridgeRequest request)
        {
            var path = request.path ?? request.query;

            if (string.IsNullOrEmpty(path))
                return "Error: path required. Example: {\"type\": \"delete\", \"path\": \"Player\"}";

            var go = FindGameObjectByPath(path);
            if (go == null)
                return $"Error: GameObject not found: `{path}`";

            var fullPath = GetFullPath(go);
            Undo.DestroyObjectImmediate(go);

            return $"Deleted `{fullPath}`";
        }

        #endregion
    }
}
