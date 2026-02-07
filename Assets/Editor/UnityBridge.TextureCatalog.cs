using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// UnityBridge texture catalog commands.
    /// Scans textures, computes hashes, maintains searchable catalog.
    ///
    /// Commands:
    /// - texture-scan: Scan folder and update catalog
    /// - texture-search: Search catalog by tags/description
    /// </summary>
    public static partial class UnityBridge
    {
        private const string CatalogPath = "Assets/LLM/texture-catalog.json";

        private static string HandleTextureScan(BridgeRequest request)
        {
            var folder = request.path ?? "Assets/Textures";

            var sb = new StringBuilder();
            sb.AppendLine("# Texture Scan\n");
            sb.AppendLine($"**Folder:** `{folder}`\n");

            // Load existing catalog
            var catalog = LoadCatalog();
            var existingCount = catalog.entries.Count;

            // Find all textures
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            sb.AppendLine($"**Found:** {guids.Length} textures\n");

            int added = 0;
            int updated = 0;
            int skipped = 0;

            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);

                // Skip non-image files
                var ext = Path.GetExtension(assetPath).ToLower();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".tga" && ext != ".psd")
                {
                    skipped++;
                    continue;
                }

                var hash = ComputeFileHash(assetPath);
                if (string.IsNullOrEmpty(hash))
                {
                    skipped++;
                    continue;
                }

                // Check if already in catalog
                var existing = catalog.entries.FirstOrDefault(e => e.hash == hash);
                if (existing != null)
                {
                    // Update path if different
                    if (!existing.paths.Contains(assetPath))
                    {
                        existing.paths.Add(assetPath);
                        updated++;
                    }
                    continue;
                }

                // New entry
                var entry = CreateCatalogEntry(assetPath, hash);
                catalog.entries.Add(entry);
                added++;
            }

            // Save catalog
            SaveCatalog(catalog);

            sb.AppendLine("## Results\n");
            sb.AppendLine($"- **Added:** {added} new textures");
            sb.AppendLine($"- **Updated:** {updated} (new paths for existing hashes)");
            sb.AppendLine($"- **Skipped:** {skipped}");
            sb.AppendLine($"- **Total in catalog:** {catalog.entries.Count}");
            sb.AppendLine();
            sb.AppendLine($"**Catalog saved to:** `{CatalogPath}`");

            // Show sample of new entries
            if (added > 0)
            {
                sb.AppendLine("\n## New Entries (sample)\n");
                var newEntries = catalog.entries.TakeLast(Math.Min(added, 10));
                foreach (var entry in newEntries)
                {
                    var shortPath = entry.paths.FirstOrDefault() ?? "";
                    if (shortPath.Length > 60)
                        shortPath = "..." + shortPath.Substring(shortPath.Length - 57);
                    sb.AppendLine($"- `{entry.hash.Substring(0, 8)}` {entry.width}x{entry.height} `{shortPath}`");
                }
            }

            // Show entries needing description
            var undescribed = catalog.entries.Count(e => string.IsNullOrEmpty(e.description));
            if (undescribed > 0)
            {
                sb.AppendLine($"\n**Need description:** {undescribed} textures");
                sb.AppendLine("Edit `texture-catalog.json` to add descriptions and tags.");
            }

            return sb.ToString();
        }

        private static string HandleTextureSearch(BridgeRequest request)
        {
            var query = request.query ?? request.value?.ToString() ?? "";
            if (string.IsNullOrEmpty(query))
                return "Error: query required. Example: {\"type\": \"texture-search\", \"query\": \"button blue\"}";

            var catalog = LoadCatalog();
            var keywords = query.ToLowerInvariant().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

            var sb = new StringBuilder();
            sb.AppendLine($"# Texture Search: \"{query}\"\n");

            var matches = catalog.entries.Where(e => MatchesSearch(e, keywords)).ToList();

            if (matches.Count == 0)
            {
                sb.AppendLine("No matches found.");
                sb.AppendLine("\nTry broader terms or check that textures have descriptions.");
                return sb.ToString();
            }

            sb.AppendLine($"**Found:** {matches.Count} matches\n");

            foreach (var entry in matches.Take(20))
            {
                var path = entry.paths.FirstOrDefault() ?? "unknown";
                var name = Path.GetFileNameWithoutExtension(path);
                sb.AppendLine($"### {name}");
                sb.AppendLine($"- **Path:** `{path}`");
                sb.AppendLine($"- **Size:** {entry.width}x{entry.height}");
                if (!string.IsNullOrEmpty(entry.spriteType))
                    sb.AppendLine($"- **Type:** {entry.spriteType}");
                if (entry.tags != null && entry.tags.Length > 0)
                    sb.AppendLine($"- **Tags:** {string.Join(", ", entry.tags)}");
                if (!string.IsNullOrEmpty(entry.description))
                    sb.AppendLine($"- **Description:** {entry.description}");
                sb.AppendLine();
            }

            if (matches.Count > 20)
                sb.AppendLine($"... and {matches.Count - 20} more matches.");

            return sb.ToString();
        }

        private static bool MatchesSearch(TextureCatalogEntry entry, string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                bool found = false;

                // Check tags
                if (entry.tags != null)
                {
                    foreach (var tag in entry.tags)
                    {
                        if (tag.ToLower().Contains(keyword))
                        {
                            found = true;
                            break;
                        }
                    }
                }

                // Check description
                if (!found && !string.IsNullOrEmpty(entry.description))
                {
                    if (entry.description.ToLower().Contains(keyword))
                        found = true;
                }

                // Check path (filename, folder)
                if (!found && entry.paths != null)
                {
                    foreach (var path in entry.paths)
                    {
                        if (path.ToLower().Contains(keyword))
                        {
                            found = true;
                            break;
                        }
                    }
                }

                // Check sprite type
                if (!found && !string.IsNullOrEmpty(entry.spriteType))
                {
                    if (entry.spriteType.ToLower().Contains(keyword))
                        found = true;
                }

                if (!found)
                    return false; // All keywords must match
            }

            return true;
        }

        private static TextureCatalogEntry CreateCatalogEntry(string assetPath, string hash)
        {
            var entry = new TextureCatalogEntry
            {
                hash = hash,
                paths = new List<string> { assetPath },
                tags = new string[0],
                description = ""
            };

            // Get texture info
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture != null)
            {
                entry.width = texture.width;
                entry.height = texture.height;
            }

            // Get sprite info from importer
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                entry.spriteType = importer.spriteImportMode.ToString();
                if (importer.spriteImportMode == SpriteImportMode.Single)
                {
                    var border = importer.spriteBorder;
                    if (border != Vector4.zero)
                    {
                        entry.spriteType = "Sliced";
                        entry.spriteBorder = $"{border.x},{border.y},{border.z},{border.w}";
                    }
                }
            }

            // Extract category from path
            entry.category = ExtractCategory(assetPath);

            return entry;
        }

        private static string ExtractCategory(string path)
        {
            // Try to extract meaningful category from path
            // Assets/Textures/Bundles/UI/2. Space Game GUI/PNG/Buttons/BTNs/file.png
            // → "Space Game GUI/Buttons"

            var parts = path.Split('/');
            var categories = new List<string>();

            bool inBundles = false;
            foreach (var part in parts)
            {
                if (part == "Bundles") { inBundles = true; continue; }
                if (!inBundles) continue;
                if (part == "UI" || part == "PNG") continue;
                if (part.EndsWith(".png") || part.EndsWith(".PNG")) continue;
                if (part.EndsWith(".jpg") || part.EndsWith(".tga")) continue;

                // Clean up numbered prefixes like "2. Space Game GUI"
                var clean = part;
                if (char.IsDigit(part[0]) && part.Contains(". "))
                    clean = part.Substring(part.IndexOf(". ") + 2);

                categories.Add(clean);
                if (categories.Count >= 2) break;
            }

            return string.Join("/", categories);
        }

        private static string ComputeFileHash(string assetPath)
        {
            try
            {
                var fullPath = Path.GetFullPath(assetPath);
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(fullPath))
                {
                    var hashBytes = md5.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
            }
            catch
            {
                return null;
            }
        }

        private static TextureCatalog LoadCatalog()
        {
            if (File.Exists(CatalogPath))
            {
                try
                {
                    var json = File.ReadAllText(CatalogPath);
                    return JsonUtility.FromJson<TextureCatalog>(json) ?? new TextureCatalog();
                }
                catch
                {
                    return new TextureCatalog();
                }
            }
            return new TextureCatalog();
        }

        private static void SaveCatalog(TextureCatalog catalog)
        {
            var json = JsonUtility.ToJson(catalog, true);
            File.WriteAllText(CatalogPath, json);
        }

        #region Texture Preview

        private static string HandleTexturePreview(BridgeRequest request)
        {
            // Parameters:
            // - query: filter (category, path substring, or "undescribed")
            // - value: cell size (default: auto based on texture size)
            // - depth: max count (default: 20)

            var filter = request.query ?? "";
            var maxCount = request.depth > 0 ? request.depth : 20;
            var cellSize = request.value?.ToObject<int>() ?? 0;

            var sb = new StringBuilder();
            sb.AppendLine("# Texture Preview\n");

            // Check we're in the right scene
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.name.Contains("Texture Preview"))
            {
                sb.AppendLine("**Error:** Please open 'Texture Preview Scene' first.");
                sb.AppendLine("This command creates temporary UI elements for screenshot.");
                return sb.ToString();
            }

            // Load catalog
            var catalog = LoadCatalog();
            if (catalog.entries.Count == 0)
            {
                sb.AppendLine("**Error:** Catalog is empty. Run `texture-scan` first.");
                return sb.ToString();
            }

            // Filter entries
            List<TextureCatalogEntry> entries;
            if (string.IsNullOrEmpty(filter))
            {
                entries = catalog.entries.Take(maxCount).ToList();
                sb.AppendLine($"**Showing:** first {entries.Count} textures");
            }
            else if (filter.ToLower() == "undescribed")
            {
                entries = catalog.entries
                    .Where(e => string.IsNullOrEmpty(e.description) && (e.tags == null || e.tags.Length == 0))
                    .Take(maxCount)
                    .ToList();
                sb.AppendLine($"**Showing:** {entries.Count} undescribed textures");
            }
            else
            {
                var keywords = filter.ToLower().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                entries = catalog.entries
                    .Where(e => MatchesSearch(e, keywords))
                    .Take(maxCount)
                    .ToList();
                sb.AppendLine($"**Filter:** \"{filter}\"");
                sb.AppendLine($"**Showing:** {entries.Count} matches");
            }

            if (entries.Count == 0)
            {
                sb.AppendLine("\nNo textures found matching filter.");
                return sb.ToString();
            }

            // Determine cell size
            if (cellSize == 0)
            {
                var avgSize = entries.Average(e => Math.Max(e.width, e.height));
                if (avgSize <= 64) cellSize = 80;
                else if (avgSize <= 128) cellSize = 140;
                else if (avgSize <= 256) cellSize = 180;
                else cellSize = 220;
            }
            sb.AppendLine($"**Cell size:** {cellSize}px");

            // Find or create preview container
            var canvas = GameObject.Find("Canvas");
            if (canvas == null)
            {
                sb.AppendLine("\n**Error:** Canvas not found in scene.");
                return sb.ToString();
            }

            // Clean up previous preview
            var existingContainer = canvas.transform.Find("Preview Container");
            if (existingContainer != null)
                UnityEngine.Object.DestroyImmediate(existingContainer.gameObject);

            // Create preview container
            var container = CreatePreviewContainer(canvas.transform, cellSize, entries.Count);
            sb.AppendLine($"**Created:** Preview Container with {entries.Count} items\n");

            // Add textures
            sb.AppendLine("## Textures\n");
            sb.AppendLine("| # | Hash | Size | Name |");
            sb.AppendLine("|---|------|------|------|");

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var path = entry.paths.FirstOrDefault();
                if (string.IsNullOrEmpty(path)) continue;

                var texture = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (texture == null)
                {
                    // Try loading as Texture2D and getting sprite
                    var tex2d = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex2d != null)
                    {
                        // Load all sprites from this texture
                        var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
                        if (sprites.Length > 0)
                            texture = sprites[0];
                    }
                }

                CreatePreviewItem(container.transform, i, entry, texture, cellSize);

                var shortHash = entry.hash.Substring(0, 8);
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length > 20) name = name.Substring(0, 17) + "...";
                sb.AppendLine($"| {i + 1} | `{shortHash}` | {entry.width}x{entry.height} | {name} |");
            }

            // Save scene so preview survives Play Mode
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            sb.AppendLine("\n---");
            sb.AppendLine("**Scene saved.** Ready for `screenshot`.");
            sb.AppendLine("**Next:** Use `screenshot` to capture, then `texture-tag` to add descriptions.");

            return sb.ToString();
        }

        private static GameObject CreatePreviewContainer(Transform parent, int cellSize, int itemCount)
        {
            var container = new GameObject("Preview Container");
            container.transform.SetParent(parent, false);

            var rect = container.AddComponent<RectTransform>();
            // Fill parent with minimal padding
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(5, 5);
            rect.offsetMax = new Vector2(-5, -5);

            // Grid layout
            var grid = container.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            grid.cellSize = new Vector2(cellSize, cellSize + 18); // Extra height for label
            grid.spacing = new Vector2(5, 5); // Tighter spacing
            grid.startCorner = UnityEngine.UI.GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = UnityEngine.UI.GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.Flexible;

            return container;
        }

        private static void CreatePreviewItem(Transform parent, int index, TextureCatalogEntry entry, Sprite sprite, int cellSize)
        {
            var item = new GameObject($"Item {index}");
            item.transform.SetParent(parent, false);

            var itemRect = item.AddComponent<RectTransform>();

            // Image container (for aspect ratio)
            var imageContainer = new GameObject("Image");
            imageContainer.transform.SetParent(item.transform, false);
            var imageRect = imageContainer.AddComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(0, 0.15f); // Leave space for label at bottom
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;

            var image = imageContainer.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.raycastTarget = false;

            // If no sprite, show placeholder color
            if (sprite == null)
            {
                image.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            }

            // Label at bottom
            var label = new GameObject("Label");
            label.transform.SetParent(item.transform, false);
            var labelRect = label.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = new Vector2(1, 0.14f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            // Background for label - fully opaque for readability
            var labelBg = label.AddComponent<UnityEngine.UI.Image>();
            labelBg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            labelBg.raycastTarget = false;

            // Label text with auto-size
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(label.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(2, 1);
            textRect.offsetMax = new Vector2(-2, -1);

            var text = textGo.AddComponent<TMPro.TextMeshProUGUI>();
            text.text = $"{index + 1}. {entry.hash.Substring(0, 6)}";
            text.enableAutoSizing = true;
            text.fontSizeMin = 6;
            text.fontSizeMax = 12;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private static string HandleTextureTag(BridgeRequest request)
        {
            // Parameters:
            // - path: hash (first 6-8 chars or full)
            // - value: description
            // - query: tags (comma-separated)

            var hashPrefix = request.path ?? "";
            var description = request.value?.ToString() ?? "";
            var tagsStr = request.query ?? "";

            if (string.IsNullOrEmpty(hashPrefix))
                return "Error: hash required. Example: {\"type\": \"texture-tag\", \"path\": \"dc9910a1\", \"value\": \"Main menu background\", \"query\": \"background,menu\"}";

            var catalog = LoadCatalog();
            var entry = catalog.entries.FirstOrDefault(e => e.hash.StartsWith(hashPrefix.ToLower()));

            if (entry == null)
                return $"Error: No texture found with hash starting with `{hashPrefix}`";

            var sb = new StringBuilder();
            sb.AppendLine("# Texture Tag\n");
            sb.AppendLine($"**Hash:** `{entry.hash}`");
            sb.AppendLine($"**Path:** `{entry.paths.FirstOrDefault()}`");
            sb.AppendLine();

            // Update description
            if (!string.IsNullOrEmpty(description))
            {
                entry.description = description;
                sb.AppendLine($"**Description:** {description}");
            }

            // Update tags
            if (!string.IsNullOrEmpty(tagsStr))
            {
                var newTags = tagsStr.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLower())
                    .Distinct()
                    .ToArray();
                entry.tags = newTags;
                sb.AppendLine($"**Tags:** {string.Join(", ", newTags)}");
            }

            SaveCatalog(catalog);
            sb.AppendLine("\n**Saved.**");

            return sb.ToString();
        }

        private static string HandleTextureTagBatch(BridgeRequest request)
        {
            // Parameters:
            // - value: JSON array of annotations
            //   [{\"path\": \"Assets/...\", \"tags\": [\"tag1\", \"tag2\"], \"description\": \"...\"}]

            var json = request.value?.ToString() ?? "";
            if (string.IsNullOrEmpty(json))
                return "Error: value required with JSON array. Example: {\"type\": \"texture-tag-batch\", \"value\": \"[{\\\"path\\\": \\\"Assets/...\\\", ...}]\"}";

            var sb = new StringBuilder();
            sb.AppendLine("# Texture Tag Batch\n");

            // Parse annotations
            BatchAnnotation[] annotations;
            try
            {
                // Wrap in object for JsonUtility
                var wrapped = "{\"items\":" + json + "}";
                var wrapper = JsonUtility.FromJson<BatchAnnotationWrapper>(wrapped);
                annotations = wrapper.items;
            }
            catch (Exception ex)
            {
                return $"Error parsing JSON: {ex.Message}\n\nExpected format: [{'{'}\"path\": \"...\", \"tags\": [...], \"description\": \"...\"{'}'}]";
            }

            if (annotations == null || annotations.Length == 0)
            {
                return "Error: No annotations in JSON array.";
            }

            sb.AppendLine($"**Input:** {annotations.Length} annotations\n");

            var catalog = LoadCatalog();
            int updated = 0;
            int notFound = 0;

            foreach (var ann in annotations)
            {
                // Find entry by path
                var entry = catalog.entries.FirstOrDefault(e =>
                    e.paths != null && e.paths.Any(p => p.Equals(ann.path, StringComparison.OrdinalIgnoreCase)));

                if (entry == null)
                {
                    sb.AppendLine($"- **Not found:** `{ann.path}`");
                    notFound++;
                    continue;
                }

                // Update
                if (ann.tags != null && ann.tags.Length > 0)
                    entry.tags = ann.tags;
                if (!string.IsNullOrEmpty(ann.description))
                    entry.description = ann.description;

                updated++;
            }

            SaveCatalog(catalog);

            sb.AppendLine($"\n## Results\n");
            sb.AppendLine($"- **Updated:** {updated}");
            if (notFound > 0)
                sb.AppendLine($"- **Not found:** {notFound}");
            sb.AppendLine($"\n**Catalog saved.**");

            return sb.ToString();
        }

        [Serializable]
        private class BatchAnnotationWrapper
        {
            public BatchAnnotation[] items;
        }

        [Serializable]
        private class BatchAnnotation
        {
            public string path;
            public string[] tags;
            public string description;
        }

        #endregion

        #region Catalog Data Classes

        [Serializable]
        private class TextureCatalog
        {
            public List<TextureCatalogEntry> entries = new List<TextureCatalogEntry>();
        }

        [Serializable]
        private class TextureCatalogEntry
        {
            public string hash;
            public List<string> paths;
            public int width;
            public int height;
            public string spriteType;
            public string spriteBorder;
            public string category;
            public string[] tags;
            public string description;
        }

        #endregion
    }
}
