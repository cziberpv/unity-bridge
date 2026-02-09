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
    /// - ReadMe.ai → Assets/Editor/ (only if missing)
    /// </summary>
    [InitializeOnLoad]
    public static class UnityBridgePostInstall
    {
        private const string VersionPrefKey = "UnityBridge.InstalledVersion";
        private const string ScratchPath = "Assets/Editor/BridgeScratch.cs";
        private const string ReadMeAiPath = "Assets/Editor/ReadMe.ai";
        private const string CmdScriptPath = "unity-cmd.ps1";

        static UnityBridgePostInstall()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UnityBridgePostInstall).Assembly);
            if (packageInfo == null)
            {
                Debug.LogWarning("[UnityBridge PostInstall] Could not find package info. Files not exported.");
                return;
            }

            var packagePath = packageInfo.resolvedPath;
            var currentVersion = packageInfo.version;
            var installedVersion = EditorPrefs.GetString(VersionPrefKey, "");

            Debug.Log($"[UnityBridge PostInstall] Package: {packagePath}, version: {currentVersion}, installed: {installedVersion}");

            Directory.CreateDirectory("Assets/Editor");

            // Always ensure scratch template exists
            if (!File.Exists(ScratchPath))
            {
                File.WriteAllText(ScratchPath, ScratchTemplate);
                Debug.Log("[UnityBridge] Created scratch pad: " + ScratchPath);
            }

            // Copy ReadMe.ai if missing
            if (!File.Exists(ReadMeAiPath))
            {
                var source = Path.Combine(packagePath, "Editor", "ReadMe.ai");
                if (File.Exists(source))
                {
                    File.Copy(source, ReadMeAiPath);
                    Debug.Log("[UnityBridge] Copied ReadMe.ai to " + ReadMeAiPath);
                }
                else
                {
                    Debug.LogWarning($"[UnityBridge PostInstall] ReadMe.ai not found at: {source}");
                }
            }

            // Copy/update unity-cmd.ps1 on install or version change
            if (installedVersion != currentVersion || !File.Exists(CmdScriptPath))
            {
                var source = Path.Combine(packagePath, "unity-cmd.ps1");
                if (File.Exists(source))
                {
                    File.Copy(source, CmdScriptPath, overwrite: true);
                    Debug.Log("[UnityBridge] Copied unity-cmd.ps1 to project root (v" + currentVersion + ")");
                }
                else
                {
                    Debug.LogWarning($"[UnityBridge PostInstall] unity-cmd.ps1 not found at: {source}");
                }

                EditorPrefs.SetString(VersionPrefKey, currentVersion);
            }

            AssetDatabase.Refresh();
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
