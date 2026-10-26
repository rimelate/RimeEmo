using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal static class RimeEmoUpdateChecks
    {
        internal static void Run()
        {
            Check(RimeEmoUpdates.IsNewerStable("0.10.0", "0.9.9"), "numeric version order");
            Check(!RimeEmoUpdates.IsNewerStable("0.9.9", "0.10.0"), "no downgrade");
            Check(!RimeEmoUpdates.IsNewerStable("1.0.0+build2", "1.0.0+build1"), "build metadata");
            Check(RimeEmoUpdates.IsNewerStable("1.0.0", "1.0.0-beta.1"), "prerelease to stable");
            Check(!RimeEmoUpdates.IsNewerStable("1.1.0-beta.1", "1.0.0"), "prerelease notifications");
            foreach (string invalid in new[] { "", null, "v1.0.0", "01.0.0", "1.0", "1.0.0-01", "1.0.0\n" })
                Check(!RimeEmoUpdates.IsNewerStable(invalid, "0.1.0"), "invalid version: " + invalid);
            string release = JsonUtility.ToJson(new RimeEmoUpdates.Release {
                name = RimeEmoUpdates.PackageName, version = "0.2.0",
                releaseUrl = RimeEmoUpdates.RepositoryUrl + "/releases/tag/v0.2.0"
            });
            Check(RimeEmoUpdates.TryReadRelease(release, out _), "valid release rejected");
            Check(!RimeEmoUpdates.TryReadRelease(release.Replace("https://github.com/", "https://example.com/"), out _), "untrusted URL");
            Check(!RimeEmoUpdates.TryReadRelease(release.Replace(RimeEmoUpdates.PackageName, "other.package"), out _), "wrong package");
            Check(!RimeEmoUpdates.TryReadRelease("{}", out _) && !RimeEmoUpdates.TryReadRelease("<html>error</html>", out _), "invalid payload");
            Check(!string.IsNullOrEmpty(RimeEmoUpdates.CurrentVersion), "installed manifest missing");

            var window = ScriptableObject.CreateInstance<RimeEmoWindow>();
            try
            {
                window.CreateGUI();
                Check(window.rootVisualElement.Q<Button>("updates-check") != null, "manual check missing");
                Check(window.rootVisualElement.Q<Toggle>("updates-automatic") != null, "automatic check toggle missing");
                Check(window.rootVisualElement.Q<Button>("updates-vcc") != null, "VCC link missing");
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Update checks: " + message);
        }
    }
}
