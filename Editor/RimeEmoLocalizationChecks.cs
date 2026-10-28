using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal static class RimeEmoLocalizationChecks
    {
        internal static void Run()
        {
            // Menus open a submenu at "/" and drop "&"; a clip named "R&R" showed as "RR".
            Check(RimeEmoLocalization.MenuText("R&R/Smile") == "R＆R／Smile", "menu text did not keep / and &");
            bool original = RimeEmoLocalization.English;
            bool hadPreference = EditorPrefs.HasKey("RimeEmo.Language");
            RimeEmoWindow window = null;
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var draft = RimeEmoDrafts.Create(project);
            try
            {
                RimeEmoLocalization.English = false;
                window = ScriptableObject.CreateInstance<RimeEmoWindow>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(RimeEmoWindow);
                type.GetField("project", flags).SetValue(window, project);
                type.GetField("motion", flags).SetValue(window, draft);
                type.GetField("mode", flags).SetValue(window, 1);
                type.GetField("time", flags).SetValue(window, 0.75f);
                draft.name = "保存"; // User data that happens to match a translation key must stay untouched.
                EditorUtility.SetDirty(draft);
                type.GetMethod("Changed", flags).Invoke(window, null);
                window.position = new Rect(0, 0, 1000, 760);
                window.Show(); window.CreateGUI();
                // Switch through the selector, as a user does: the rebuild then runs inside the selector's ChangeEvent,
                // so events raised while translating are queued and reach the new fields' callbacks. Setting
                // RimeEmoLocalization.English directly dispatched them before any callback existed and missed the bug.
                window.rootVisualElement.Q<DropdownField>("language").value = "English";
                Check(RimeEmoLocalization.English, "language selector did not switch to English");
                Check((string)type.GetField("search", flags).GetValue(window) == "" && (string)type.GetField("materialSearch", flags).GetValue(window) == "",
                    "language switch typed a label into a search field");
                Check(!project.faceMeshSelected && string.IsNullOrEmpty(project.faceMeshPath), "language switch changed the face mesh");
                Check(draft.name == "保存", "language switch renamed the motion");
                Check(window.rootVisualElement.Q<Button>("dynamic-mode").text == "Motion", "mode label was not translated");
                Check(window.rootVisualElement.Q<TextField>("motion-name").value == "保存", "language switch renamed user data");
                Check(window.rootVisualElement.Q<DropdownField>("language").value == "English", "language selector is stale");
                Check((RimeEmoMotion)type.GetField("motion", flags).GetValue(window) == draft && window.hasUnsavedChanges, "language switch discarded the draft");
                Check(Mathf.Approximately((float)type.GetField("time", flags).GetValue(window), 0.75f), "language switch moved the cursor");
                Check(RimeEmoLocalization.T("変更を保存しますか？") == "Save changes?", "dialog translation missing");
                var root = window.rootVisualElement;
                root.panel.visualTree.style.width = 1000; root.panel.visualTree.style.height = 760;
                root.style.width = 1000; root.style.height = 760;
                root.panel.GetType().GetMethod("ValidateLayout", flags | BindingFlags.Public).Invoke(root.panel, null);
                foreach (var id in new[] { "save", "capture-frame", "dynamic-mode", "new-motion", "save-as", "split-queue", "duplicate-queue", "delete-queue", "add", "gesture-settings" })
                {
                    var button = root.Q<Button>(id);
                    Check(button.worldBound.width > 0 && button.worldBound.xMin >= root.worldBound.xMin && button.worldBound.xMax <= root.worldBound.xMax + 1
                        && button.worldBound.yMax <= root.worldBound.yMax + 1, "English action is clipped: " + id);
                }
                Check(root.Q<VisualElement>("preview-host").worldBound.height >= 80, "preview collapsed beneath its actions");
                Check(root.Q<ListView>("material-list").worldBound.height >= 55, "material list collapsed beneath import controls");
                // The Shape Key state filters show their whole label (a swatch inside a button once covered it).
                for (int i = 0; i < 4; i++)
                {
                    var filter = root.Q<Button>("shape-state-" + i);
                    if (filter == null || filter.worldBound.width <= 0) continue;
                    var size = filter.MeasureTextSize(filter.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                    Check(filter.childCount == 0 && size.x <= filter.contentRect.width + 1, "Shape Key filter label is cut off: " + filter.text);
                }
                RimeEmoLocalization.English = false;
                Check(root.Q<Button>("dynamic-mode").text == "動き", "switch back to Japanese failed");
                Check(RimeEmoLocalization.T("変更を保存しますか？") == "変更を保存しますか？", "Japanese dialog changed");

                // Labels, buttons and field text draw with RimeEmo's own font, also after Play mode renews it.
                if (RimeEmoFonts.Font)
                {
                    Check(RimeEmoFonts.Font.HasCharacters("シーンの表情を読み込む違う", out _, false, true), "the RimeEmo font cannot draw Japanese");
                    for (int pass = 0; pass < 2; pass++)
                    {
                        if (pass == 1) RimeEmoFonts.Renew();
                        root.panel.GetType().GetMethod("ValidateLayout", flags | BindingFlags.Public).Invoke(root.panel, null);
                        var texts = new TextElement[] { root.Q<Button>("dynamic-mode"), root.Q<Label>(), root.Q<TextField>("motion-name").Q<TextElement>() };
                        foreach (var text in texts)
                            Check(text != null && text.resolvedStyle.unityFontDefinition.fontAsset == RimeEmoFonts.Font, "text does not use the RimeEmo font: " + text?.name);
                    }
                }
            }
            finally
            {
                if (window) { window.DiscardChanges(); window.Close(); }
                if (draft) UnityEngine.Object.DestroyImmediate(draft);
                UnityEngine.Object.DestroyImmediate(project);
                RimeEmoLocalization.English = original;
                if (!hadPreference) EditorPrefs.DeleteKey("RimeEmo.Language");
            }
        }
        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Localization: " + message);
        }
    }
}
