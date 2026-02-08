using System;
using System.Collections.Generic;
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
    /// Safety: guaranteed exit from Play Mode via timeout.
    /// Runtime errors during Play Mode are captured and included in response.
    /// State survives domain reload via EditorPrefs.
    /// </summary>
    public static partial class UnityBridge
    {
        private const string ScreenshotFolder = "Assets/LLM/Bridge/Screenshots";
        private const float ScreenshotSafetyMargin = 30f;

        // EditorPrefs keys for state persistence across domain reload
        private const string PrefKeyScreenshotPending = "UnityBridge.Screenshot.Pending";
        private const string PrefKeyScreenshotDelay = "UnityBridge.Screenshot.Delay";
        private const string PrefKeyScreenshotStartTime = "UnityBridge.Screenshot.StartTime";
        private const string PrefKeyScreenshotPath = "UnityBridge.Screenshot.Path";
        private const string PrefKeyScreenshotCaptured = "UnityBridge.Screenshot.Captured";

        private static bool _screenshotSubscribed;
        private static bool _screenshotErrorsSubscribed;
        private static readonly List<string> _screenshotRuntimeErrors = new();

        /// <summary>
        /// Call this from UnityBridge static constructor to setup screenshot monitoring.
        /// </summary>
        static partial void InitializeScreenshot()
        {
            if (!EditorPrefs.GetBool(PrefKeyScreenshotPending, false)) return;

            // Stale state detection: if not in Play Mode and started long ago, clean up
            if (!EditorApplication.isPlaying)
            {
                var startTimeStr = EditorPrefs.GetString(PrefKeyScreenshotStartTime, "");
                if (!string.IsNullOrEmpty(startTimeStr))
                {
                    var elapsed = (DateTime.Now - DateTime.Parse(startTimeStr)).TotalSeconds;
                    if (elapsed > 120)
                    {
                        Debug.LogWarning("[UnityBridge] Cleaning up stale screenshot state");
                        ClearScreenshotState();
                        return;
                    }
                }
            }

            SubscribeToScreenshotUpdate();
        }

        private static string HandleScreenshotRequest(BridgeRequest request)
        {
            float delay = request.delay > 0 ? request.delay : 1f;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"screenshot_{timestamp}.png";
            var fullPath = Path.Combine(ScreenshotFolder, filename);

            Directory.CreateDirectory(ScreenshotFolder);

            // Save state to EditorPrefs (survives domain reload)
            EditorPrefs.SetBool(PrefKeyScreenshotPending, true);
            EditorPrefs.SetBool(PrefKeyScreenshotCaptured, false);
            EditorPrefs.SetFloat(PrefKeyScreenshotDelay, delay);
            EditorPrefs.SetString(PrefKeyScreenshotStartTime, DateTime.Now.ToString("O"));
            EditorPrefs.SetString(PrefKeyScreenshotPath, fullPath);

            _screenshotRuntimeErrors.Clear();
            SubscribeToRuntimeErrors();
            SubscribeToScreenshotUpdate();

            Debug.Log($"[UnityBridge] Screenshot: entering Play Mode, delay={delay}s, output={fullPath}");
            EditorApplication.isPlaying = true;

            return null; // Response written by ScreenshotUpdate after capture
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

        private static void SubscribeToRuntimeErrors()
        {
            if (_screenshotErrorsSubscribed) return;
            _screenshotErrorsSubscribed = true;
            Application.logMessageReceived += OnScreenshotRuntimeLog;
        }

        private static void UnsubscribeFromRuntimeErrors()
        {
            _screenshotErrorsSubscribed = false;
            Application.logMessageReceived -= OnScreenshotRuntimeLog;
        }

        private static void OnScreenshotRuntimeLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
            {
                _screenshotRuntimeErrors.Add($"[{type}] {message}");
            }
        }

        private static void ScreenshotUpdate()
        {
            if (!EditorPrefs.GetBool(PrefKeyScreenshotPending, false))
            {
                UnsubscribeFromScreenshotUpdate();
                return;
            }

            var startTimeStr = EditorPrefs.GetString(PrefKeyScreenshotStartTime, "");
            if (string.IsNullOrEmpty(startTimeStr))
            {
                ClearScreenshotState();
                return;
            }

            var startTime = DateTime.Parse(startTimeStr);
            var delay = EditorPrefs.GetFloat(PrefKeyScreenshotDelay, 1f);
            var elapsed = (DateTime.Now - startTime).TotalSeconds;

            // Safety timeout: force exit Play Mode regardless of state
            if (elapsed > delay + ScreenshotSafetyMargin)
            {
                Debug.LogError($"[UnityBridge] Screenshot safety timeout after {elapsed:F0}s — forcing exit from Play Mode");
                var path = EditorPrefs.GetString(PrefKeyScreenshotPath, "");
                WriteScreenshotResponse(path, false,
                    $"Safety timeout: Play Mode exceeded {delay + ScreenshotSafetyMargin:F0}s limit",
                    _screenshotRuntimeErrors);
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;
                ClearScreenshotState();
                return;
            }

            // Wait until actually in Play Mode
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                return;

            // Wait for delay
            if (elapsed < delay)
                return;

            // Already captured — waiting for delayCall to finish
            if (EditorPrefs.GetBool(PrefKeyScreenshotCaptured, false))
                return;

            // Capture
            var capturePath = EditorPrefs.GetString(PrefKeyScreenshotPath, "");
            if (string.IsNullOrEmpty(capturePath))
            {
                ClearScreenshotState();
                return;
            }

            Debug.Log($"[UnityBridge] Capturing screenshot to: {capturePath}");
            EditorPrefs.SetBool(PrefKeyScreenshotCaptured, true);

            try
            {
                ScreenCapture.CaptureScreenshot(capturePath);

                // Wait frames for screenshot file to be written, then exit Play Mode
                EditorApplication.delayCall += () =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        WriteScreenshotResponse(capturePath, true, null, _screenshotRuntimeErrors);
                        EditorApplication.isPlaying = false;
                        ClearScreenshotState();
                    };
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityBridge] Screenshot failed: {ex.Message}");
                WriteScreenshotResponse(capturePath, false, ex.Message, _screenshotRuntimeErrors);
                EditorApplication.isPlaying = false;
                ClearScreenshotState();
            }
        }

        private static void ClearScreenshotState()
        {
            EditorPrefs.DeleteKey(PrefKeyScreenshotPending);
            EditorPrefs.DeleteKey(PrefKeyScreenshotDelay);
            EditorPrefs.DeleteKey(PrefKeyScreenshotStartTime);
            EditorPrefs.DeleteKey(PrefKeyScreenshotPath);
            EditorPrefs.DeleteKey(PrefKeyScreenshotCaptured);
            UnsubscribeFromScreenshotUpdate();
            UnsubscribeFromRuntimeErrors();
            _screenshotRuntimeErrors.Clear();
        }

        private static void WriteScreenshotResponse(string path, bool success, string error, List<string> runtimeErrors)
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

            if (runtimeErrors != null && runtimeErrors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"## Runtime Errors ({runtimeErrors.Count})");
                sb.AppendLine();
                foreach (var err in runtimeErrors)
                {
                    sb.AppendLine($"- {err}");
                }
            }

            File.WriteAllText(ResponseFile, sb.ToString());
        }
    }
}
