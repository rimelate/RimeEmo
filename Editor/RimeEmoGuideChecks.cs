using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal static class RimeEmoGuideChecks
    {
        internal static void Run()
        {
            bool seen = EditorPrefs.GetBool(RimeEmoWindow.GuidePreference, false);
            bool hadKey = EditorPrefs.HasKey(RimeEmoWindow.GuidePreference);
            var avatar = new GameObject("Guide check avatar");
            var child = new GameObject("Nested animator"); child.transform.SetParent(avatar.transform);
            RimeEmoWindow window = null;
            try
            {
                avatar.AddComponent<Animator>(); child.AddComponent<Animator>();
                Check(RimeEmoHierarchy.ResolveAvatarRoot(child) == avatar, "nested Animator was treated as a separate avatar");
                Check(RimeEmoHierarchy.ResolveAvatarRoot(avatar) == avatar, "avatar launcher not detected");
                EditorPrefs.DeleteKey(RimeEmoWindow.GuidePreference);
                window = ScriptableObject.CreateInstance<RimeEmoWindow>();
                window.Show(); window.CreateGUI();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(RimeEmoWindow);
                type.GetMethod("StartInitialGuide", flags).Invoke(window, null);
                type.GetMethod("RenderGuide", flags).Invoke(window, null);
                Check(window.rootVisualElement.Q<VisualElement>("guide-card") != null, "first-launch guide missing");
                var language = window.rootVisualElement.Q<DropdownField>("language");
                Check(!window.rootVisualElement.Q<VisualElement>("settings-page").Contains(language), "language is hidden in settings");
                Check(window.rootVisualElement.Q<VisualElement>("utility-toolbar").Contains(language), "language not in persistent toolbar");
                var root = window.rootVisualElement;
                root.panel.visualTree.style.width = 1000; root.panel.visualTree.style.height = 760;
                root.style.width = 1000; root.style.height = 760;
                var validate = root.panel.GetType().GetMethod("ValidateLayout", flags | BindingFlags.Public);
                var expected = new[] { "language", "avatar", "static-mode", "dynamic-mode", "dynamic-mode", "save", "gesture-settings" };
                for (int step = 0; step < expected.Length; step++)
                {
                    type.GetField("guideStep", flags).SetValue(window, step);
                    type.GetMethod("RenderGuide", flags).Invoke(window, null);
                    validate.Invoke(root.panel, null);
                    var target = root.Q<VisualElement>(expected[step]);
                    Check(target.ClassListContains("re-guide-target"), "wrong guide target at step " + step);
                    Check(target.resolvedStyle.borderTopWidth >= 2 && target.resolvedStyle.borderTopColor.a > 0.9f,
                        "guide border was overridden at step " + step);
                    Check(target.resolvedStyle.opacity >= 0.99f, "disabled action dimmed guide highlight at step " + step);
                    Check(root.Q<Label>("guide-title").text.StartsWith((step + 1) + " / 7"), "setup step was not removed");
                }
                // A guide already open on capture must follow mode changes without pressing Next again.
                var project = ScriptableObject.CreateInstance<RimeEmoProject>();
                try
                {
                    type.GetField("project", flags).SetValue(window, project);
                    type.GetField("guideStep", flags).SetValue(window, 4);
                    type.GetMethod("SwitchMode", flags).Invoke(window, new object[] { 1 });
                    type.GetMethod("RefreshToolkit", flags).Invoke(window, null);
                    validate.Invoke(root.panel, null);
                    var capture = root.Q<Button>("capture-frame");
                    Check(capture.ClassListContains("re-guide-target") && capture.resolvedStyle.borderTopColor.a > 0.9f,
                        "capture highlight did not follow the displayed timeline");
                    Check(!root.Q<Button>("dynamic-mode").ClassListContains("re-guide-target"), "previous guide target remained highlighted");
                    type.GetMethod("SwitchMode", flags).Invoke(window, new object[] { 0 });
                    type.GetMethod("RefreshToolkit", flags).Invoke(window, null);
                    Check(root.Q<Button>("dynamic-mode").ClassListContains("re-guide-target"), "hidden capture action remained the guide target");
                }
                finally { type.GetField("project", flags).SetValue(window, null); UnityEngine.Object.DestroyImmediate(project); }
                type.GetField("guideStep", flags).SetValue(window, 4);
                window.CreateGUI();
                Check((int)type.GetField("guideStep", flags).GetValue(window) == 4, "UI rebuild restarted the guide");
                type.GetMethod("CloseGuide", flags).Invoke(window, null);
                type.GetMethod("StartInitialGuide", flags).Invoke(window, null);
                type.GetMethod("RenderGuide", flags).Invoke(window, null);
                Check(window.rootVisualElement.Q<VisualElement>("guide-card") == null, "first-launch guide repeated");
                Check(avatar.transform.childCount == 1, "launcher detection modified the avatar");
            }
            finally
            {
                if (window) { window.DiscardChanges(); window.Close(); }
                UnityEngine.Object.DestroyImmediate(avatar);
                if (hadKey) EditorPrefs.SetBool(RimeEmoWindow.GuidePreference, seen);
                else EditorPrefs.DeleteKey(RimeEmoWindow.GuidePreference);
            }
        }
        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Guide: " + message);
        }
    }
}
