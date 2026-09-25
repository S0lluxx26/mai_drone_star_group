using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DroneStar.Core;
using UnityEngine;

namespace DroneStar.App
{
    /// <summary>
    /// Saved shows on this device, as <c>.dronestar.json</c> files under persistentDataPath/Shows. In the web
    /// build that folder lives in the browser's IndexedDB (the loader sets autoSyncPersistentDataPath, so
    /// every write is persisted immediately) — no PlayerPrefs size cap applies.
    /// </summary>
    public sealed class ShowLibrary
    {
        readonly string folder;

        public ShowLibrary()
        {
            folder = Path.Combine(Application.persistentDataPath, "Shows");
        }

        public string Location => Application.platform == RuntimePlatform.WebGLPlayer ? "this browser" : folder;

        public List<string> List()
        {
            var names = new List<string>();
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.GetFiles(folder, "*" + ShowSerializer.FileExtension))
                {
                    string name = Path.GetFileName(file);
                    names.Add(name.Substring(0, name.Length - ShowSerializer.FileExtension.Length));
                }
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>Saves under a file-safe version of the title and returns the name used.</summary>
        public string Save(string title, string json)
        {
            string name = SafeName(title);
            Directory.CreateDirectory(folder);
            string path = PathFor(name);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            return name;
        }

        public string Load(string name)
        {
            string path = PathFor(SafeName(name));
            if (!File.Exists(path)) throw new FileNotFoundException("No saved show named " + name);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public void Delete(string name)
        {
            string path = PathFor(SafeName(name));
            if (File.Exists(path)) File.Delete(path);
        }

        string PathFor(string safeName) => Path.Combine(folder, safeName + ShowSerializer.FileExtension);

        /// <summary>Keeps letters (any script), digits, spaces, dashes and underscores; max 48 chars.</summary>
        public static string SafeName(string title)
        {
            var sb = new StringBuilder();
            foreach (char c in title ?? "")
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') sb.Append(c);
                if (sb.Length >= 48) break;
            }
            string s = sb.ToString().Trim();
            return s.Length == 0 ? "Untitled Show" : s;
        }
    }
}
