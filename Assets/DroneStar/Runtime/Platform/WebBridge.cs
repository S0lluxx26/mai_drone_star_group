using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace DroneStar.App
{
    /// <summary>
    /// File exchange with the outside world. In the browser: real downloads and a file picker (via
    /// DroneStarBridge.jslib). On desktop and in the editor: files under persistentDataPath.
    /// </summary>
    public sealed class WebBridge : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void DroneStar_DownloadText(string fileName, string text, string mime);
        [DllImport("__Internal")] static extern void DroneStar_OpenTextFile(string objectName, string method, string accept);
#endif

        public const int MaxImportBytes = 2 * 1024 * 1024;

        Action<string> pendingImport;
        Action<string> pendingImportError;

        public static bool IsWeb => Application.platform == RuntimePlatform.WebGLPlayer;

        public static string ExportFolder => Path.Combine(Application.persistentDataPath, "Exports");
        public static string ImportFolder => Path.Combine(Application.persistentDataPath, "Import");

        /// <summary>Offers text as a download. Returns a human-readable description of where it went.</summary>
        public string SaveText(string fileName, string text, string mime)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            DroneStar_DownloadText(fileName, text, mime);
            return "Downloaded " + fileName;
#else
            Directory.CreateDirectory(ExportFolder);
            string path = Path.Combine(ExportFolder, fileName);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return "Saved to " + path;
#endif
        }

        /// <summary>
        /// Web: opens the browser file picker and calls back with the file text. Desktop: not available
        /// (the Open dialog lists files placed in <see cref="ImportFolder"/> instead).
        /// </summary>
        public bool PickTextFile(Action<string> onText, Action<string> onError)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            pendingImport = onText;
            pendingImportError = onError;
            DroneStar_OpenTextFile(gameObject.name, nameof(OnFilePicked), ".json,.dronestar.json,application/json");
            return true;
#else
            return false;
#endif
        }

        /// <summary>Called from JavaScript with the file text, or with a "!reason" marker on failure.</summary>
        public void OnFilePicked(string payload)
        {
            Action<string> ok = pendingImport, fail = pendingImportError;
            pendingImport = null;
            pendingImportError = null;
            if (payload == null)
            {
                fail?.Invoke("The file could not be read.");
                return;
            }
            if (payload.StartsWith("!", StringComparison.Ordinal))
            {
                fail?.Invoke(payload == "!too-large" ? "That file is larger than 2 MB." : "The file could not be read.");
                return;
            }
            ok?.Invoke(payload);
        }
    }
}
