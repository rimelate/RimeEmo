using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    public static class RimeEmoPathUtility
    {
        public static string GetTransformPath(Transform root, Transform target)
        {
            if (root == null || target == null || root == target)
            {
                return string.Empty;
            }

            var names = new System.Collections.Generic.Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        public static string SafeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Unnamed";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                builder.Append(System.Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return builder.ToString();
        }

        public static string EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }

            return path;
        }

        public static string EnsureAvatarGeneratedFolders(string avatarName, out string controllers, out string clips, out string profiles, out string logs)
        {
            if (!AssetDatabase.IsValidFolder("Assets/RimeEmoGenerated"))
            {
                AssetDatabase.CreateFolder("Assets", "RimeEmoGenerated");
            }

            string root = EnsureFolder("Assets/RimeEmoGenerated", SafeFileName(avatarName));
            controllers = EnsureFolder(root, "Controllers");
            clips = EnsureFolder(root, "Clips");
            profiles = EnsureFolder(root, "Profiles");
            logs = EnsureFolder(root, "Logs");
            return root;
        }

        public static bool IsPackageAsset(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return path.StartsWith("Packages/");
        }
    }
}
