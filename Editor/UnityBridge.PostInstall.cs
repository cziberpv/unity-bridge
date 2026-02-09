using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Copies required files from the UPM package to the user's project:
    /// - unity-cmd.ps1 → project root (updated on package version change)
    /// - BridgeScratch.cs → Assets/Editor/ (template, only if missing)
    /// - CLAUDE.md → Assets/Editor/ (only if missing)
    /// </summary>
    [InitializeOnLoad]
    public static class UnityBridgePostInstall
    {
        private const string VersionPrefKey = "UnityBridge.InstalledVersion";
        private const string ScratchPath = "Assets/Editor/BridgeScratch.cs";
        private const string ClaudeMdPath = "Assets/Editor/CLAUDE.md";
        private const string CmdScriptPath = "unity-cmd.ps1";

        static UnityBridgePostInstall()
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(UnityBridgePostInstall).Assembly);
            if (packageInfo == null) return;

            var packagePath = packageInfo.resolvedPath;
            var currentVersion = packageInfo.version;
            var installedVersion = EditorPrefs.GetString(VersionPrefKey, "");

            // Always ensure scratch template exists
            if (!File.Exists(ScratchPath))
            {
                Directory.CreateDirectory("Assets/Editor");
                File.WriteAllText(ScratchPath, ScratchTemplate);
                AssetDatabase.Refresh();
                Debug.Log("[UnityBridge] Created scratch pad: " + ScratchPath);
            }

            // Copy CLAUDE.md if missing
            if (!File.Exists(ClaudeMdPath))
            {
                Directory.CreateDirectory("Assets/Editor");
                var source = Path.Combine(packagePath, "Editor", "CLAUDE.md");
                if (File.Exists(source))
                {
                    File.Copy(source, ClaudeMdPath);
                    Debug.Log("[UnityBridge] Copied CLAUDE.md to " + ClaudeMdPath);
                }
            }

            // Copy/update unity-cmd.ps1 on install or version change
            if (installedVersion != currentVersion)
            {
                var source = Path.Combine(packagePath, "unity-cmd.ps1");
                if (File.Exists(source))
                {
                    File.Copy(source, CmdScriptPath, overwrite: true);
                    Debug.Log("[UnityBridge] Copied unity-cmd.ps1 to project root");
                }

                EditorPrefs.SetString(VersionPrefKey, currentVersion);
            }
        }

        private const string ScratchTemplate =
@"using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Scratch pad for Unity Bridge one-off scripts.
/// Edit Run() and execute via: {""type"": ""scratch""}
/// </summary>
public static class BridgeScratch
{
    public static string Run()
    {
        // Your one-off C# code here
        // Example:
        // var go = new GameObject(""Test"");
        // EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        // return ""Created test object"";

        return ""Scratch pad is empty. Edit this method to add your code."";
    }
}
";
    }
}
