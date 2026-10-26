using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // "Add material": a searchable list of this avatar's animation clips only. Unity's object picker lists every clip
    // in the project, so with several avatars (e.g. MAYO and Manuka) the other avatar's expressions showed up too.
    internal sealed class RimeEmoClipPicker : EditorWindow
    {
        private readonly List<AnimationClip> all = new List<AnimationClip>();
        private readonly List<AnimationClip> shown = new List<AnimationClip>();
        private RimeEmoProject project;
        private Action<AnimationClip> pick;
        private ListView list;
        private Label count;

        internal static void Open(Rect screenRect, RimeEmoProject project, Action<AnimationClip> pick)
        {
            var candidates = Candidates(project);
            var window = CreateInstance<RimeEmoClipPicker>();
            window.project = project; window.pick = pick; window.all.AddRange(candidates);
            window.Build();
            window.ShowAsDropDown(screenRect, new Vector2(Mathf.Max(360, screenRect.width), 440));
        }

        // Animation clips anywhere in the project that belong to this avatar (by face Shape Key names) and are not
        // already materials. RimeEmo's own working copies and generated output are left out.
        internal static List<AnimationClip> Candidates(RimeEmoProject project)
        {
            var result = new List<AnimationClip>();
            if (!project) return result;
            var known = new HashSet<AnimationClip>(project.clips.Concat(project.animations).Where(c => c != null).SelectMany(c => new[] { c.clip, c.source }).Where(c => c));
            string toolRoot = ToolRoot();
            // Face expressions are .anim files. Clips inside model files (FBX) were the slow part of the first scan
            // (15–20 s) and are body animation in practice, so they are skipped.
            var paths = AssetDatabase.FindAssets("t:AnimationClip").Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();

            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    string path = paths[i];
                    if (i % 50 == 0) EditorUtility.DisplayProgressBar("RimeEmo", RimeEmoLocalization.T("このアバターのアニメーションを探しています…"), (float)i / paths.Length);
                    if (path.StartsWith(RimeEmoAssets.DataRoot + "/", StringComparison.Ordinal) || (toolRoot != null && path.StartsWith(toolRoot, StringComparison.Ordinal))) continue;
                    if (!MayMoveFace(path)) continue;
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip && !known.Contains(clip) && RimeEmoClipClassification.CanImport(project, clip)) result.Add(clip);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            return result.Distinct().OrderBy(c => c.name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // A cheap look at the text of an .anim before loading it: a clip that never names a BlendShape cannot be a
        // face expression. (The mesh path is checked after loading: YAML may quote it.) Binary-serialized files cannot
        // be read this way and are always loaded.
        private static bool MayMoveFace(string path)
        {
            try
            {
                string text = File.ReadAllText(path);
                if (!text.StartsWith("%YAML", StringComparison.Ordinal)) return true;
                return text.Contains("blendShape.");
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        // The tool's own folder (older versions kept Working and Generated inside it).
        private static string ToolRoot()
        {
            string script = AssetDatabase.FindAssets("RimeEmoWindow t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.EndsWith("/RimeEmoWindow.cs", StringComparison.Ordinal));
            if (string.IsNullOrEmpty(script)) return null;
            string editor = Path.GetDirectoryName(script);
            string root = Path.GetDirectoryName(editor);
            return string.IsNullOrEmpty(root) ? null : root.Replace('\\', '/') + "/";
        }

        private void Build()
        {
            var root = rootVisualElement;
            root.AddToClassList("re-app");
            RimeEmoLocalization.MarkLanguage(root);
            string folder = ToolRoot() + "Editor/UI/";
            foreach (string file in new[] { "RimeEmoTheme.uss", "RimeEmoCustom.uss" })
            { var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(folder + file); if (sheet) root.styleSheets.Add(sheet); }
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 8;
            var title = new Label(RimeEmoLocalization.T("このアバターのアニメーションから追加")); title.AddToClassList("re-title");
            root.Add(title);
            var search = new TextField(RimeEmoLocalization.T("検索")) { name = "clip-picker-search" };
            search.AddToClassList("re-search"); search.AddToClassList("re-compact-field");
            search.RegisterValueChangedCallback(e => Filter(e.newValue));
            root.Add(search);
            count = new Label(); count.AddToClassList("re-hint");
            root.Add(count);
            list = new ListView(shown, 26, () =>
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 6 } };
                var kind = new Label { name = "kind" }; kind.AddToClassList("re-material-type"); row.Add(kind);
                var name = new Label { name = "name", style = { flexGrow = 1, overflow = Overflow.Hidden } }; row.Add(name);
                return row;
            }, (row, i) =>
            {
                var clip = shown[i];
                bool animated = RimeEmoImportedClips.IsAnimated(clip, project);
                var kind = row.Q<Label>("kind"); kind.text = RimeEmoLocalization.T(animated ? "動的" : "静的"); kind.EnableInClassList("is-dynamic", animated);
                row.Q<Label>("name").text = clip.name;
                row.tooltip = AssetDatabase.GetAssetPath(clip);
            }) { name = "clip-picker-list", selectionType = SelectionType.Single, style = { flexGrow = 1 } };
            // One click picks, as in Unity's own picker.
            list.selectionChanged += items => { var clip = items.OfType<AnimationClip>().FirstOrDefault(); if (clip) { var action = pick; Close(); action?.Invoke(clip); } };
            root.Add(list);
            Filter("");
            search.schedule.Execute(() => search.Focus());
        }

        private void Filter(string text)
        {
            shown.Clear();
            shown.AddRange(all.Where(c => c && (string.IsNullOrEmpty(text) || c.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)));
            list.RefreshItems();
            count.text = all.Count == 0
                ? RimeEmoLocalization.T("追加できるアニメーションはありません。このアバターのShape Keyを動かすClipだけを表示します。")
                : string.Format(RimeEmoLocalization.T("{0}件（このアバターのShape Keyに一致するClipだけを表示）"), shown.Count);
        }
    }
}
