using UnityEditor;
using UnityEngine;

namespace DroneStar.EditorTools
{
    /// <summary>
    /// One-click setup for the bundled MCP for Unity bridge (com.coplaydev.unity-mcp), so an MCP client such
    /// as Claude Code can drive this editor. The keys below are the package's own EditorPrefs.
    /// </summary>
    public static class McpSetup
    {
        const string AutoStartKey = "MCPForUnity.AutoStartOnLoad";
        const string UseHttpKey = "MCPForUnity.UseHttpTransport";
        const string ScopeKey = "MCPForUnity.HttpTransportScope";
        public const string DefaultEndpoint = "http://127.0.0.1:8080/mcp";

        [MenuItem("Drone Star/MCP/Connect Automatically On Load", priority = 40)]
        public static void EnableAutoConnect()
        {
            EditorPrefs.SetBool(UseHttpKey, true);
            EditorPrefs.SetString(ScopeKey, "local");
            EditorPrefs.SetBool(AutoStartKey, true);
            Debug.Log("[DroneStar] MCP for Unity will start its local HTTP server and connect when the editor loads. " +
                      "Register it in Claude Code with: claude mcp add --scope local --transport http UnityMCP " + DefaultEndpoint);
        }

        [MenuItem("Drone Star/MCP/Connect Automatically On Load", true)]
        static bool ValidateEnableAutoConnect()
        {
            Menu.SetChecked("Drone Star/MCP/Connect Automatically On Load", EditorPrefs.GetBool(AutoStartKey, false));
            return true;
        }

        [MenuItem("Drone Star/MCP/Disable Auto-Connect", priority = 41)]
        public static void DisableAutoConnect()
        {
            EditorPrefs.SetBool(AutoStartKey, false);
            Debug.Log("[DroneStar] MCP for Unity auto-connect disabled.");
        }
    }
}
