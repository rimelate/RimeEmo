using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // Material Symbols Rounded (Apache License 2.0), subset to the glyphs below.
    // Regular = FILL 0, Filled = FILL 1; both wght 400, GRAD 0, opsz 24. Labels never depend on the font.
    internal static class RimeEmoIcons
    {
        internal const string RegularFont = "Icons/MaterialSymbolsRounded-Subset.ttf";
        internal const string FilledFont = "Icons/MaterialSymbolsRounded-Fill-Subset.ttf";

        internal static readonly Dictionary<string, string> Symbols = new Dictionary<string, string>
        {
            { "face", "" }, { "animation", "" }, { "back_hand", "" }, { "settings", "" },
            { "save", "" }, { "help", "" }, { "add", "" }, { "edit", "" }, { "more_vert", "" },
            { "search", "" }, { "save_as", "" }, { "play_arrow", "" }, { "pause", "" }, { "stop", "" },
            { "skip_previous", "" }, { "skip_next", "" }, { "content_cut", "" }, { "content_copy", "" },
            { "delete", "" }, { "auto_fix_high", "" }, { "fit_screen", "" }, { "photo_camera", "" },
            { "refresh", "" }, { "person_add", "" }, { "tune", "" }, { "cleaning_services", "" },
            { "download", "" }, { "build", "" }, { "visibility", "" }, { "check_circle", "" },
            { "link_off", "" }, { "check", "" }, { "close", "" }, { "info", "" }, { "warning", "" }
        };

        // Element name → symbol.
        private static readonly Dictionary<string, string> Glyphs = new Dictionary<string, string>
        {
            { "static-mode", "face" }, { "dynamic-mode", "animation" }, { "gesture-settings", "back_hand" }, { "settings", "settings" },
            { "save", "save" }, { "show-guide", "help" }, { "rules-save", "save" }, { "gesture-save-draft", "save" },
            { "material-default", "add" }, { "material-import", "add" }, { "material-edit", "edit" }, { "material-more", "more_vert" },
            { "new-motion", "add" }, { "save-as", "save_as" }, { "add", "add" }, { "rule-add", "add" },
            { "play", "play_arrow" }, { "stop", "stop" }, { "previous", "skip_previous" }, { "next", "skip_next" },
            { "split-queue", "content_cut" }, { "duplicate-queue", "content_copy" }, { "delete-queue", "delete" }, { "auto-split", "auto_fix_high" },
            { "fit", "fit_screen" }, { "capture-frame", "photo_camera" }, { "reload-preview", "refresh" },
            { "setup", "person_add" }, { "part-settings", "tune" }, { "cleanup", "cleaning_services" },
            { "rules-import", "download" }, { "fx-build", "build" }, { "fx-inspect", "visibility" }, { "fx-apply", "check_circle" }, { "fx-remove", "link_off" }, { "apply", "check_circle" }, { "shape-filter-menu", "tune" }, { "shape-group-edit", "edit" }, { "shape-group-copy", "content_copy" }
        };
        private static readonly HashSet<string> IconOnly = new HashSet<string> { "stop", "previous", "next", "duplicate-queue", "delete-queue", "auto-split", "show-guide" };
        private static readonly HashSet<string> Rail = new HashSet<string> { "static-mode", "dynamic-mode", "gesture-settings", "settings" };

        private static Font regular, filled;

        private static bool Load(string uiFolder)
        {
            if (!regular) regular = AssetDatabase.LoadAssetAtPath<Font>(uiFolder + RegularFont);
            if (!filled) filled = AssetDatabase.LoadAssetAtPath<Font>(uiFolder + FilledFont);
            return regular;
        }

        private static void SetFont(Label label, bool fill)
        {
            var font = fill && filled ? filled : regular;
            if (!font) return;
            label.style.unityFont = font;
            label.style.unityFontDefinition = FontDefinition.FromFont(font);
        }

        // A standalone glyph, e.g. the check mark of a selected chip. Falls back to plain text without the font.
        internal static Label Glyph(string symbol, string fallback, bool fill = false)
        {
            var label = new Label(regular && Symbols.TryGetValue(symbol, out var glyph) ? glyph : fallback) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("re-action-icon");
            SetFont(label, fill);
            return label;
        }

        internal static void Apply(VisualElement root, string uiFolder)
        {
            if (!Load(uiFolder)) return; // All actions remain usable through their text labels.
            foreach (var pair in Glyphs)
            {
                var button = root.Q<Button>(pair.Key);
                if (button == null || button.Q<Label>("action-icon") != null || !Symbols.TryGetValue(pair.Value, out var glyph)) continue;
                var icon = new Label(glyph) { name = "action-icon", pickingMode = PickingMode.Ignore };
                icon.AddToClassList("re-action-icon");
                SetFont(icon, false);
                if (string.IsNullOrEmpty(button.tooltip)) button.tooltip = button.text;
                if (Rail.Contains(pair.Key))
                {
                    // Navigation rail: the label stays the button text, the icon sits in the indicator above it.
                    button.Add(icon);
                    continue;
                }
                button.Add(icon);
                button.AddToClassList("re-has-icon");
                string caption = button.text.Replace("＋ ", "").Replace("▶ ", "");
                button.text = "";
                if (IconOnly.Contains(pair.Key)) button.AddToClassList("re-icon-only");
                else
                {
                    var label = new Label(caption) { name = "action-caption", pickingMode = PickingMode.Ignore };
                    label.AddToClassList("re-action-caption");
                    button.Add(label);
                }
            }
        }

        // Active destinations use the filled icon, as Material 3 navigation does.
        internal static void SetActive(Button button, bool active)
        {
            if (button == null) return;
            button.EnableInClassList("is-active", active);
            var icon = button.Q<Label>("action-icon");
            if (icon != null) SetFont(icon, active);
        }

        internal static void SetPlayback(Button button, bool playing)
        {
            string caption = playing ? RimeEmoLocalization.T("一時停止") : RimeEmoLocalization.T("再生");
            var label = button.Q<Label>("action-caption");
            if (label != null) label.text = caption;
            else button.text = caption;
            button.tooltip = caption;
            var icon = button.Q<Label>("action-icon");
            if (icon != null) icon.text = playing ? Symbols["pause"] : Symbols["play_arrow"];
        }
    }
}
