using System;
using System.Reflection;

namespace Editor
{
    public static partial class UnityBridge
    {
        private static string HandleScratch()
        {
            try
            {
                // Find user's BridgeScratch.Run() via reflection (lives in Assets/Editor/)
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType("BridgeScratch");
                    if (type == null) continue;

                    var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                    if (method != null)
                        return (string)method.Invoke(null, null);
                }

                return "Scratch pad not found.\n\n" +
                       "Create `Assets/Editor/BridgeScratch.cs` with:\n```csharp\n" +
                       "public static class BridgeScratch\n{\n" +
                       "    public static string Run()\n    {\n" +
                       "        return \"hello\";\n    }\n}\n```";
            }
            catch (Exception ex)
            {
                return $"EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            }
        }
    }
}
