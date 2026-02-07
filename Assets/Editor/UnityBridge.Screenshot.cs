using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// UnityBridge screenshot command.
    /// Enters Play Mode, waits for delay, captures screenshot, exits Play Mode.
    ///
    /// Usage: {"type": "screenshot", "delay": 2}
    /// - delay: seconds to wait after Play Mode starts (default: 1)
    ///
    /// State survives domain reload via EditorPrefs.
    /// </summary>
    public static partial class UnityBridge
    {
        private const string ScreenshotFolder = "Assets/LLM/Bridge/Screenshots";

        // EditorPrefs keys for state persistence across domain reload
        private const string PrefKeyScreenshotPending = "UnityBridge.Screenshot.Pending";
        private const string PrefKeyScreenshotDelay = "UnityBridge.Screenshot.Delay";
        private const string PrefKeyScreenshotStartTime = "UnityBridge.Screenshot.StartTime";
        private const string PrefKeyScreenshotPath = "UnityBridge.Screenshot.Path";

        private static bool _screenshotSubscribed = false;

        /// <summary>
        /// Call this from UnityBridge static constructor to setup screenshot monitoring.
        /// </summary>
        static partial void InitializeScreenshot()
        {
            // Check if we have a pending screenshot (after domain reload)
            if (EditorPrefs.GetBool(PrefKeyScreenshotPending, false))
            {
                SubscribeToScreenshotUpdate();
            }
        }

        private static string HandleScreenshotRequest(BridgeRequest request)
        {
            // Parse delay (default 1 second)
            float delay = request.delay > 0 ? request.delay : 1f;

            // Generate output path
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"screenshot_{timestamp}.png";
            var fullPath = Path.Combine(ScreenshotFolder, filename);

            // Ensure folder exists
            Directory.CreateDirectory(ScreenshotFolder);

            // Save state to EditorPrefs (survives domain reload)
            EditorPrefs.SetBool(PrefKeyScreenshotPending, true);
            EditorPrefs.SetFloat(PrefKeyScreenshotDelay, delay);
            EditorPrefs.SetString(PrefKeyScreenshotStartTime, DateTime.Now.ToString("O"));
            EditorPrefs.SetString(PrefKeyScreenshotPath, fullPath);

            // Subscribe to update before entering Play Mode
            SubscribeToScreenshotUpdate();

            // Enter Play Mode
            Debug.Log($"[UnityBridge] Screenshot: entering Play Mode, delay={delay}s, output={fullPath}");
            EditorApplication.isPlaying = true;

            // Response will be written by ScreenshotUpdate after capture
            return null; // Don't write response yet
        }

        private static void SubscribeToScreenshotUpdate()
        {
            if (_screenshotSubscribed) return;
            _screenshotSubscribed = true;
            EditorApplication.update += ScreenshotUpdate;
        }

        private static void UnsubscribeFromScreenshotUpdate()
        {
            _screenshotSubscribed = false;
            EditorApplication.update -= ScreenshotUpdate;
        }

        private static void ScreenshotUpdate()
        {
            // Check if we have pending screenshot
            if (!EditorPrefs.GetBool(PrefKeyScreenshotPending, false))
            {
                UnsubscribeFromScreenshotUpdate();
                return;
            }

            // Wait until we're actually in Play Mode and playing
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
            {
                return;
            }

            // Check if enough time has passed
            var startTimeStr = EditorPrefs.GetString(PrefKeyScreenshotStartTime, "");
            if (string.IsNullOrEmpty(startTimeStr))
            {
                ClearScreenshotState();
                return;
            }

            var startTime = DateTime.Parse(startTimeStr);
            var delay = EditorPrefs.GetFloat(PrefKeyScreenshotDelay, 1f);
            var elapsed = (DateTime.Now - startTime).TotalSeconds;

            if (elapsed < delay)
            {
                return; // Still waiting
            }

            // Time to capture!
            var path = EditorPrefs.GetString(PrefKeyScreenshotPath, "");
            if (string.IsNullOrEmpty(path))
            {
                ClearScreenshotState();
                return;
            }

            Debug.Log($"[UnityBridge] Capturing screenshot to: {path}");

            try
            {
                // TODO: Add JPEG support for smaller file sizes
                // Currently PNG only, JPEG requires post-capture conversion
                ScreenCapture.CaptureScreenshot(path);

                // Schedule exit from Play Mode and response writing
                // Need to wait a frame for screenshot to be written
                EditorApplication.delayCall += () =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        WriteScreenshotResponse(path, true, null);
                        EditorApplication.isPlaying = false;
                    };
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityBridge] Screenshot failed: {ex.Message}");
                WriteScreenshotResponse(path, false, ex.Message);
                EditorApplication.isPlaying = false;
            }

            ClearScreenshotState();
        }

        private static void ClearScreenshotState()
        {
            EditorPrefs.DeleteKey(PrefKeyScreenshotPending);
            EditorPrefs.DeleteKey(PrefKeyScreenshotDelay);
            EditorPrefs.DeleteKey(PrefKeyScreenshotStartTime);
            EditorPrefs.DeleteKey(PrefKeyScreenshotPath);
            UnsubscribeFromScreenshotUpdate();
        }

        private static void WriteScreenshotResponse(string path, bool success, string error)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!-- Request: screenshot -->");
            sb.AppendLine($"<!-- Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");
            sb.AppendLine();
            sb.AppendLine("# Screenshot");
            sb.AppendLine();

            if (success)
            {
                sb.AppendLine("**Status:** Success");
                sb.AppendLine();
                sb.AppendLine($"**Path:** `{path}`");
                sb.AppendLine();
                sb.AppendLine("Screenshot captured from Game View.");
            }
            else
            {
                sb.AppendLine("**Status:** Failed");
                sb.AppendLine();
                sb.AppendLine($"**Error:** {error}");
            }

            File.WriteAllText(ResponseFile, sb.ToString());
        }
    }
}
