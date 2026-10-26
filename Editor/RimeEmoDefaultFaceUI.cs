using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace RimeEmo.Editor
{
    // Default Face: the face with no gesture, i.e. the BlendShape values of the scene avatar's face mesh.
    // It is edited like an expression from the first row of the materials, and every change is written
    // straight to the scene avatar (undoable) and to the settings that generation and the preview use.
    public sealed partial class RimeEmoWindow
    {
        [SerializeField] private bool defaultFaceSelected;
        // The first row of the materials list; it has no clip of its own.
        private static readonly ClipEntry DefaultFaceEntry = new ClipEntry { gesture = "Default Face" };

        private bool EditingDefaultFace => defaultFaceSelected && mode == 0 && !selected && project;

        private void SelectDefaultFace()
        {
            selected = null; defaultFaceSelected = true;
            showDefault = false; time = 0; rowsDirty = true;
            RefreshStaticToolkit(); SyncToolkitTime();
        }

        private SkinnedMeshRenderer FaceRenderer(string path)
        {
            if (!avatar) return null;
            var target = string.IsNullOrEmpty(path) ? avatar.transform : avatar.transform.Find(path);
            var renderer = target ? target.GetComponent<SkinnedMeshRenderer>() : null;
            return renderer && renderer.sharedMesh ? renderer : null;
        }

        private float SceneValue(ShapeEntry shape, out bool found)
        {
            var renderer = FaceRenderer(shape.path);
            int index = renderer ? renderer.sharedMesh.GetBlendShapeIndex(shape.shape) : -1;
            found = index >= 0;
            return found ? renderer.GetBlendShapeWeight(index) : shape.defaultValue;
        }

        // Writes values to the scene avatar and to the settings as one undoable step.
        private void SetDefaultFaceValues(IEnumerable<KeyValuePair<ShapeEntry, float>> values)
        {
            var list = values.Where(p => p.Key != null && !float.IsNaN(p.Value) && !float.IsInfinity(p.Value)).ToList();
            if (!project || !avatar || list.Count == 0) return;
            var renderers = list.Select(p => FaceRenderer(p.Key.path)).Where(r => r).Distinct().ToList();
            Undo.RecordObjects(renderers.Cast<Object>().Concat(new Object[] { project }).ToArray(), "RimeEmo: Edit Default Face");
            foreach (var pair in list)
            {
                var renderer = FaceRenderer(pair.Key.path);
                int index = renderer ? renderer.sharedMesh.GetBlendShapeIndex(pair.Key.shape) : -1;
                if (index >= 0) renderer.SetBlendShapeWeight(index, pair.Value);
                pair.Key.defaultValue = pair.Value;
            }
            foreach (var renderer in renderers) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(project);
            EditorSceneManager.MarkSceneDirty(avatar.scene);
            rowsDirty = true; Changed();
        }

        private void SetDefaultFaceValue(EditorCurveBinding binding, float value)
        {
            var shape = RimeEmoFace.Shapes(project).FirstOrDefault(s => RimeEmoClips.Binding(s).Equals(binding));
            if (shape != null) SetDefaultFaceValues(new[] { new KeyValuePair<ShapeEntry, float>(shape, value) });
        }

        // Face Shape Keys whose scene value no longer matches Default Face, e.g. after another tool or an animation
        // preview changed the avatar.
        private int SceneDifferences()
        {
            if (!project || !avatar) return 0;
            int count = 0;
            foreach (var shape in RimeEmoFace.Shapes(project))
            {
                float value = SceneValue(shape, out bool found);
                if (found && Mathf.Abs(value - shape.defaultValue) > 0.001f) count++;
            }
            return count;
        }

        // "シーンの表情を読み込む": takes the face the scene avatar shows now as Default Face.
        private void CaptureSceneDefaultFace()
        {
            if (!project || !avatar) return;
            var changed = RimeEmoFace.Shapes(project)
                .Select(s => new KeyValuePair<ShapeEntry, float>(s, SceneValue(s, out bool found)))
                .Where(p => Mathf.Abs(p.Value - p.Key.defaultValue) > 0.001f).ToList();
            if (changed.Count == 0) { ShowNotification(new GUIContent(RimeEmoLocalization.T("シーンの表情とDefault Faceは同じです。"))); return; }
            SetDefaultFaceValues(changed);
            ShowNotification(new GUIContent(string.Format(RimeEmoLocalization.T("シーンの表情を読み込みました（{0}個のShape Key）。"), changed.Count)));
        }

        // "アニメーションをシーンに書き出す": some avatars set their resting face with an animation (e.g. the
        // default state of their FX). Writing that clip's face into the scene makes it Default Face.
        private void ShowDefaultFaceClipMenu()
        {
            if (!project) return;
            var menu = new GenericMenu();
            var materials = project.clips.Where(c => c != null && c.clip && RimeEmoClipClassification.Classify(project, c.clip) == RimeEmoClipKind.Static)
                .Select(c => c.clip).Distinct().OrderBy(c => c.name).ToList();
            foreach (var clip in materials)
            {
                var item = clip;
                menu.AddItem(new GUIContent(RimeEmoLocalization.MenuText(item.name)), false, () => Run(() => ApplyClipAsDefaultFace(item)));
            }
            if (materials.Count > 0) menu.AddSeparator("");
            menu.AddItem(new GUIContent(RimeEmoLocalization.T("ほかのアニメーションから選ぶ…")), false, () =>
            {
                var button = UI<Button>("default-face-from-clip");
                var rect = new Rect(position.position + button.worldBound.position, button.worldBound.size);
                RimeEmoClipPicker.Open(rect, project, clip => Run(() => ApplyClipAsDefaultFace(clip)));
            });
            menu.DropDown(UI<Button>("default-face-from-clip").worldBound);
        }

        private void ApplyClipAsDefaultFace(AnimationClip clip)
        {
            if (!project || !avatar || !clip) return;
            var values = new List<KeyValuePair<ShapeEntry, float>>();
            foreach (var shape in RimeEmoFace.Shapes(project))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(shape));
                if (curve != null && curve.length > 0) values.Add(new KeyValuePair<ShapeEntry, float>(shape, curve.Evaluate(0)));
            }
            if (values.Count == 0) { ShowNotification(new GUIContent(RimeEmoLocalization.T("このアニメーションには顔のShape Keyがありません。"))); return; }
            if (!EditorUtility.DisplayDialog("RimeEmo", string.Format(RimeEmoLocalization.T("「{0}」の顔（{1}個のShape Key）をシーンのアバターに書き込み、Default Faceにします。含まれないShape Keyは今のままです。Undoで戻せます。"), clip.name, values.Count),
                RimeEmoLocalization.T("書き出す"), RimeEmoLocalization.T("キャンセル"))) return;
            SetDefaultFaceValues(values);
            ShowNotification(new GUIContent(string.Format(RimeEmoLocalization.T("「{0}」をDefault Faceにしました。"), clip.name)));
        }

        private void BindDefaultFace()
        {
            Button("default-face-capture", CaptureSceneDefaultFace);
            Button("default-face-from-clip", ShowDefaultFaceClipMenu);
        }

        // The Default Face part of the expression editor: name, notice and its two actions.
        private void RefreshDefaultFace()
        {
            bool editing = EditingDefaultFace;
            UI<VisualElement>("default-face-actions").style.display = editing ? DisplayStyle.Flex : DisplayStyle.None;
            if (!editing) return;
            UI<TextField>("static-name").SetValueWithoutNotify("Default Face");
            UI<TextField>("static-name").SetEnabled(false);
            bool hasAvatar = avatar && RimeEmoFace.Shapes(project).Any(s => FaceRenderer(s.path));
            UI<Button>("default-face-capture").SetEnabled(hasAvatar);
            UI<Button>("default-face-from-clip").SetEnabled(hasAvatar);
            int differences = hasAvatar ? SceneDifferences() : 0;
            var notice = UI<Label>("static-notice");
            notice.text = !hasAvatar ? RimeEmoLocalization.T("Default Faceを編集するには、上のAvatar欄にシーンのアバターを指定してください。")
                : differences > 0 ? string.Format(RimeEmoLocalization.T("シーンのアバターの顔が、Default Faceと{0}個のShape Keyで違います。「シーンの表情を読み込む」で合わせられます。"), differences)
                : RimeEmoLocalization.T("Default Faceはシーンのアバターの顔メッシュの値です。変更はシーンに直接書き込みます（Undoで戻せます）。");
            notice.style.display = DisplayStyle.Flex;
        }
    }
}
