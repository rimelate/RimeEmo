using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal static class RimeEmoLocalization
    {
        private const string PreferenceKey = "RimeEmo.Language";
        internal static event Action Changed;
        internal static bool English
        {
            get => EditorPrefs.GetString(PreferenceKey, Application.systemLanguage == SystemLanguage.Japanese ? "ja" : "en") == "en";
            set
            {
                if (English == value) return;
                EditorPrefs.SetString(PreferenceKey, value ? "en" : "ja");
                Changed?.Invoke();
            }
        }

        internal static string T(string japanese)
            => English && japanese != null && RimeEmoTranslations.English.TryGetValue(japanese, out var english) ? english : japanese;

        // A user name (clip, group, word) as a menu item: "/" would open a submenu and "&" is dropped as an access key.
        internal static string MenuText(string text) => text == null ? "" : text.Replace('/', '／').Replace('&', '＆');

        internal static string GestureDescription(string text)
        {
            const string suffix = " (直接条件・参考)";
            return English && text != null && text.EndsWith(suffix, StringComparison.Ordinal)
                ? text.Substring(0, text.Length - suffix.Length) + T(suffix) : T(text);
        }

        // Only run on a newly cloned UXML tree, before assigning any user asset names or values.
        internal static void TranslateLayout(VisualElement root)
        {
            root.Query<VisualElement>().ForEach(element =>
            {
                // Without notify: a label's ChangeEvent<string> bubbles to its TextField or DropdownField, and the field's
                // value callback (registered right after) then took the label text as a new value, e.g. renaming the clip to "Clip Name".
                if (element is TextElement text) ((INotifyValueChanged<string>)text).SetValueWithoutNotify(T(text.text));
                element.tooltip = T(element.tooltip);
            });
        }

        // Unity draws Japanese with a fallback font that caches each glyph in the weight it was first drawn with,
        // so bold titles turned the same characters bold in ordinary text. The theme drops bold under this class.
        // The root also gets RimeEmo's own font (RimeEmoFonts), so Japanese characters do not go missing.
        internal static void MarkLanguage(VisualElement root)
        {
            root.EnableInClassList("re-lang-ja", !English);
            RimeEmoFonts.Apply(root);
        }

        internal static void BindLanguage(VisualElement root)
        {
            var field = root.Q<DropdownField>("language");
            if (field == null) return;
            field.choices = new List<string> { "日本語", "English" };
            field.SetValueWithoutNotify(English ? "English" : "日本語");
            field.RegisterValueChangedCallback(e => English = e.newValue == "English");
        }
    }
}
