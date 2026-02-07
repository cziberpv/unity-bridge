using UnityEngine;
using UnityEditor;

namespace Editor
{
    public static partial class UnityBridge
    {
        private static string HandleScratch()
        {
            try
            {
                // Your one-off C# code here
                // Example:
                // var go = new GameObject("Test");
                // go.transform.position = Vector3.zero;
                // EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                // return "Created test object";

                return "Scratch pad is empty. Edit this method to add your code.";
            }
            catch (System.Exception ex)
            {
                return $"EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            }
        }
    }
}
