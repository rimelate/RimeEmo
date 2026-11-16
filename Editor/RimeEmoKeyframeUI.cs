using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        private RimeEmoKeyframeTimeline keyframeTimeline;
        private List<ShapeEntry> keyframeRows = new List<ShapeEntry>();
        private List<RimeEmoKeyframes.CopiedKey> keyframeClipboard = new List<RimeEmoKeyframes.CopiedKey>();
        private string keyframeSearch = "";
        private int keyframePart;
        private bool keyframeOnlyUsed = true, keyframeAutoKey, keyframeShowCurve;
        private ShapeEntry keyframePinnedShape, keyframePendingShape;
        private float keyframePendingValue, keyframePendingTime;
        private Interpolation keyframeInterpolation = Interpolation.EaseInOut;
        private bool keyframeRefreshing, keyframeValueGesture;

        private static bool FiniteKeyValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private ShapeEntry ActiveKeyframeShape => keyframeTimeline != null && keyframeTimeline.ActiveShape >= 0 && keyframeTimeline.ActiveShape < keyframeRows.Count
            ? keyframeRows[keyframeTimeline.ActiveShape] : null;
        private float KeyframeValue(ShapeEntry shape) => shape == null ? 0
            : keyframePendingShape == shape && Mathf.Approximately(keyframePendingTime, time) ? keyframePendingValue
            : AnimationUtility.GetEditorCurve(importedDraft, RimeEmoClips.Binding(shape))?.Evaluate(time) ?? shape.defaultValue;
        private void ResetKeyframeValue() { keyframeValueGesture = false; keyframePendingShape = null; preview?.ClearLiveShape(); }

        private Button KeyButton(string id, string caption, Action action)
        {
            var button = new Button(() => Run(action)) { name = id, text = RimeEmoLocalization.T(caption) };
            button.AddToClassList("re-button"); button.AddToClassList("re-button--tonal"); return button;
        }
        private void BuildKeyframeEditor(VisualElement host)
        {
            host.Clear(); host.AddToClassList("kf-editor");
            var filters = new VisualElement(); filters.AddToClassList("re-row"); host.Add(filters);
            var searchField = new TextField { name = "kf-search", tooltip = RimeEmoLocalization.T("Shape Key検索"), value = keyframeSearch };
            searchField.style.flexGrow = 1;
            searchField.RegisterValueChangedCallback(e => { keyframeSearch = e.newValue; RefreshKeyframeEditor(); }); filters.Add(searchField);
            var part = new DropdownField(Parts.ToList(), keyframePart) { name = "kf-part" }; part.style.width = 110;
            part.RegisterValueChangedCallback(e => { if (e.newValue != part.value) return; keyframePart = part.index; RefreshKeyframeEditor(); }); filters.Add(part);
            var used = new Toggle(RimeEmoLocalization.T("キーありのみ")) { name = "kf-used", value = keyframeOnlyUsed };
            used.RegisterValueChangedCallback(e => { keyframeOnlyUsed = e.newValue; RefreshKeyframeEditor(); }); filters.Add(used);
            var addRow = new VisualElement(); addRow.AddToClassList("re-row"); host.Add(addRow);
            var addShape = new DropdownField { name = "kf-add-shape", label = RimeEmoLocalization.T("Shape Key追加") }; addShape.style.flexGrow = 1;
            addShape.RegisterValueChangedCallback(e =>
            {
                if (e.newValue != addShape.value) return;
                var shape = RimeEmoFace.Shapes(project).FirstOrDefault(s => s.shape == e.newValue); if (shape == null) return;
                keyframePinnedShape = shape; keyframeSearch = ""; keyframePart = 0;
                RefreshKeyframeEditor(); keyframeTimeline.ActiveShape = keyframeRows.IndexOf(shape); ResetKeyframeValue(); RefreshKeyframeInspector();
            }); addRow.Add(addShape);
            addRow.Add(KeyButton("kf-copy", "コピー", () => CopyKeyframeSelection()));
            addRow.Add(KeyButton("kf-paste", "貼り付け", PasteKeyframeSelection));
            addRow.Add(KeyButton("kf-duplicate", "複製", DuplicateKeyframeSelection));
            addRow.Add(KeyButton("kf-delete", "削除", DeleteKeyframeSelection));
            var body = new VisualElement(); body.AddToClassList("kf-body"); host.Add(body);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "kf-tracks" }; scroll.style.flexGrow = 1; scroll.style.flexBasis = 0; body.Add(scroll);
            keyframeTimeline = new RimeEmoKeyframeTimeline(); scroll.Add(keyframeTimeline);
            var inspector = new ScrollView { name = "kf-inspector" }; inspector.AddToClassList("re-inspector"); body.Add(inspector);
            keyframeTimeline.SelectionChanged += () => { if (!keyframeRefreshing) { ResetKeyframeValue(); RefreshKeyframeInspector(); } };
            keyframeTimeline.SeekRequested += at => { time = at; playing = false; showDefault = false; ResetKeyframeValue(); SyncToolkitTime(); RequestToolkitPreview(); };
            keyframeTimeline.MoveRequested += delta => Run(() => MoveKeyframeSelection(delta));
            keyframeTimeline.AddRequested += (row, at) => Run(() =>
            {
                keyframeTimeline.ActiveShape = row; time = at; ResetKeyframeValue(); AddCurrentKeyframe(); SyncToolkitTime();
            });
            keyframeTimeline.DeleteRequested += () => Run(DeleteKeyframeSelection);
            keyframeTimeline.CopyRequested += () => CopyKeyframeSelection();
            keyframeTimeline.PasteRequested += () => Run(PasteKeyframeSelection);
            keyframeTimeline.DuplicateRequested += () => Run(DuplicateKeyframeSelection);
            keyframeTimeline.PlayToggleRequested += () => Run(ToggleToolkitPlayback);
            var hint = new Label(RimeEmoLocalization.T("時刻を選び、値を調整してキーを追加。Ctrl+クリックで複数選択、ドラッグで移動。")); hint.AddToClassList("re-hint"); host.Add(hint);
        }

        private void RefreshKeyframeEditor()
        {
            if (!ImportedEditing || toolkitRoot == null || keyframeRefreshing) return;
            keyframeRefreshing = true;
            try
            {
                var host = UI<VisualElement>("imported-curves");
                if (keyframeTimeline == null || !host.Contains(keyframeTimeline)) BuildKeyframeEditor(host);
                var active = ActiveKeyframeShape;
                var shapes = RimeEmoFace.Shapes(project).ToList();
                var rows = shapes.Where(s => (keyframePart == 0 || (int)s.part == keyframePart - 1)
                    && (string.IsNullOrEmpty(keyframeSearch) || s.shape.IndexOf(keyframeSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    && (!keyframeOnlyUsed || s == keyframePinnedShape || AnimationUtility.GetEditorCurve(importedDraft, RimeEmoClips.Binding(s)) != null)).ToList();
                if (!keyframeRows.SequenceEqual(rows)) keyframeTimeline.SelectedKeys.Clear();
                keyframeRows = rows; keyframeTimeline.Shapes = rows; keyframeTimeline.Clip = importedDraft; keyframeTimeline.FrameSnap = frameSnap;
                keyframeTimeline.ActiveShape = active != null && rows.Contains(active) ? rows.IndexOf(active) : rows.Count > 0 ? 0 : -1;
                UI<TextField>("kf-search").SetValueWithoutNotify(keyframeSearch);
                UI<DropdownField>("kf-part").SetValueWithoutNotify(Parts[keyframePart]);
                var add = UI<DropdownField>("kf-add-shape"); add.choices = shapes.Select(s => s.shape).ToList();
                add.SetValueWithoutNotify(keyframePinnedShape != null ? keyframePinnedShape.shape : "");
                keyframeTimeline.Refresh(); keyframeTimeline.SetTime(time);
                RefreshKeyframeInspector();
            }
            finally { keyframeRefreshing = false; }
        }

        private void RefreshKeyframeInspector()
        {
            var host = toolkitRoot?.Q<ScrollView>("kf-inspector"); if (host == null || !ImportedEditing) return;
            host.Clear(); var shape = ActiveKeyframeShape;
            if (shape == null) { host.Add(new Label(RimeEmoLocalization.T("Shape Keyを追加または選択してください。"))); return; }
            host.Add(new Label(shape.shape) { tooltip = shape.shape });
            var auto = new Toggle(RimeEmoLocalization.T("自動キー登録")) { name = "kf-auto-key", value = keyframeAutoKey };
            auto.RegisterValueChangedCallback(e => keyframeAutoKey = e.newValue); host.Add(auto);
            host.Add(new Label(RimeEmoLocalization.T("現在時刻の値")));
            var slider = new Slider(0, 100) { name = "kf-value-slider", value = KeyframeValue(shape) };
            var value = new FloatField { name = "kf-value", value = KeyframeValue(shape), isDelayed = true };
            // Keep the controls and preview stable while dragging; auto-key commits one Undo step on release.
            slider.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) keyframeValueGesture = true; }, TrickleDown.TrickleDown);
            slider.RegisterCallback<PointerUpEvent>(_ => EndKeyframeValueGesture());
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => EndKeyframeValueGesture());
            slider.RegisterCallback<PointerCancelEvent>(_ => { ResetKeyframeValue(); SyncKeyframeTime(); });
            slider.RegisterValueChangedCallback(e => { value.SetValueWithoutNotify(e.newValue); SetPendingKeyframeValue(shape, e.newValue); });
            value.RegisterValueChangedCallback(e =>
            {
                if (!FiniteKeyValue(e.newValue)) { value.SetValueWithoutNotify(KeyframeValue(shape)); return; }
                slider.SetValueWithoutNotify(e.newValue); SetPendingKeyframeValue(shape, e.newValue);
            }); host.Add(slider); host.Add(value);
            host.Add(KeyButton("kf-add-key", "◇ キーを追加 / 更新", AddCurrentKeyframe));
            var selection = keyframeTimeline.SelectedKeys.ToArray();
            host.Add(new Label(string.Format(RimeEmoLocalization.T("選択キー: {0}"), selection.Length)));
            if (selection.Length == 1)
            {
                var id = selection[0]; var curve = AnimationUtility.GetEditorCurve(importedDraft, RimeEmoClips.Binding(keyframeRows[id.x]));
                if (curve != null && id.y < curve.length)
                {
                    var key = curve[id.y];
                    var at = new FloatField(RimeEmoLocalization.T("キーの時刻（秒）")) { name = "kf-key-time", value = key.time, isDelayed = true };
                    at.RegisterValueChangedCallback(e => { if (FiniteKeyValue(e.newValue)) MoveKeyframeSelection(SnapTime(e.newValue) - key.time); }); host.Add(at);
                    var keyValue = new FloatField(RimeEmoLocalization.T("キーの値")) { name = "kf-key-value", value = key.value, isDelayed = true };
                    keyValue.RegisterValueChangedCallback(e =>
                    {
                        if (!FiniteKeyValue(e.newValue)) { keyValue.SetValueWithoutNotify(key.value); return; }
                        EditImported(() => { var keys = curve.keys; keys[id.y].value = e.newValue; curve.keys = keys; AnimationUtility.SetEditorCurve(importedDraft, RimeEmoClips.Binding(keyframeRows[id.x]), curve); });
                    }); host.Add(keyValue);
                }
            }
            int selectedMode = selection.Length == 0 ? (int)keyframeInterpolation : SelectedKeyframeInterpolation(selection);
            var interpolation = new DropdownField(RimeEmoLocalization.T("補間"), new List<string> { RimeEmoLocalization.T("一定の速さ"), RimeEmoLocalization.T("なめらか"), RimeEmoLocalization.T("最後に切替") }, selectedMode) { name = "kf-interpolation" };
            if (selectedMode < 0) interpolation.SetValueWithoutNotify(RimeEmoLocalization.T("カスタム / 混在"));
            interpolation.RegisterValueChangedCallback(e =>
            {
                if (e.newValue != interpolation.value || interpolation.index < 0) return;
                keyframeInterpolation = (Interpolation)interpolation.index;
                if (keyframeTimeline.SelectedKeys.Count > 0) EditImported(() => RimeEmoKeyframes.SetInterpolation(importedDraft, keyframeRows, keyframeTimeline.SelectedKeys, keyframeInterpolation));
            }); host.Add(interpolation);
            var curveToggle = new Toggle(RimeEmoLocalization.T("カーブを表示")) { name = "kf-show-curve", value = keyframeShowCurve };
            curveToggle.RegisterValueChangedCallback(e => { keyframeShowCurve = e.newValue; RefreshKeyframeInspector(); }); host.Add(curveToggle);
            if (keyframeShowCurve) host.Add(new IMGUIContainer(() => DrawKeyframeCurve(shape)) { name = "kf-curve", style = { height = 135 } });
            var materials = project.clips.Where(c => c.clip && RimeEmoClipClassification.Classify(project, c.clip) == RimeEmoClipKind.Static).Select(c => c.clip).Distinct().ToArray();
            var material = new DropdownField(RimeEmoLocalization.T("表情"), new[] { "Default Face" }.Concat(materials.Select((c, i) => (i + 1) + ". " + c.name)).ToList(), 0) { name = "kf-pose" };
            host.Add(material); host.Add(KeyButton("kf-apply-pose", "表情をこの時刻に登録", () => ApplyKeyframePose(material.index > 0 ? materials[material.index - 1] : null)));
        }

        private void SetPendingKeyframeValue(ShapeEntry shape, float value)
        {
            if (!ImportedEditing || shape == null || !FiniteKeyValue(value)) return;
            keyframePendingShape = shape; keyframePendingValue = value; keyframePendingTime = time;
            playing = false; showDefault = false; PreviewMaterial(null);
            if (keyframeAutoKey && !keyframeValueGesture) AddCurrentKeyframe();
            else { preview?.SetLiveShape(importedDraft, RimeEmoClips.Binding(shape), value); RequestToolkitPreview(); }
        }
        private void EndKeyframeValueGesture()
        {
            if (!keyframeValueGesture) return;
            keyframeValueGesture = false;
            if (keyframeAutoKey && keyframePendingShape != null) AddCurrentKeyframe();
        }
        private int SelectedKeyframeInterpolation(IEnumerable<Vector2Int> selection)
        {
            int result = -2;
            foreach (var id in selection)
            {
                var curve = AnimationUtility.GetEditorCurve(importedDraft, RimeEmoClips.Binding(keyframeRows[id.x]));
                var left = AnimationUtility.GetKeyLeftTangentMode(curve, id.y);
                var right = AnimationUtility.GetKeyRightTangentMode(curve, id.y);
                int current = left != right || curve[id.y].weightedMode != WeightedMode.None ? -1
                    : left == AnimationUtility.TangentMode.Linear ? (int)Interpolation.Linear
                    : left == AnimationUtility.TangentMode.Constant ? (int)Interpolation.Step
                    : left == AnimationUtility.TangentMode.ClampedAuto ? (int)Interpolation.EaseInOut : -1;
                if (result != -2 && result != current) return -1;
                result = current;
            }
            return result;
        }
        private void AddCurrentKeyframe()
        {
            var shape = ActiveKeyframeShape; if (!ImportedEditing || shape == null) return;
            float value = KeyframeValue(shape); int index = -1;
            EditImported(() => index = RimeEmoKeyframes.SetKey(importedDraft, shape, SnapTime(time), value, keyframeInterpolation));
            ResetKeyframeValue(); keyframeTimeline.SelectedKeys.Clear();
            if (index >= 0) keyframeTimeline.SelectedKeys.Add(new Vector2Int(keyframeRows.IndexOf(shape), index));
            RefreshKeyframeEditor();
        }
        private void MoveKeyframeSelection(float delta)
        {
            if (!ImportedEditing || keyframeTimeline == null) return;
            HashSet<Vector2Int> moved = null; bool success = false;
            EditImported(() => success = RimeEmoKeyframes.Move(importedDraft, keyframeRows, keyframeTimeline.SelectedKeys, delta, out moved));
            if (success) { keyframeTimeline.SelectedKeys.Clear(); keyframeTimeline.SelectedKeys.UnionWith(moved); }
            ResetKeyframeValue(); RefreshKeyframeEditor();
        }
        private void DeleteKeyframeSelection()
        {
            if (!ImportedEditing || keyframeTimeline == null) return;
            EditImported(() => RimeEmoKeyframes.Delete(importedDraft, keyframeRows, keyframeTimeline.SelectedKeys));
            keyframeTimeline.SelectedKeys.Clear(); ResetKeyframeValue(); RefreshKeyframeEditor();
        }
        private void CopyKeyframeSelection()
        {
            if (ImportedEditing && keyframeTimeline != null) keyframeClipboard = RimeEmoKeyframes.Copy(importedDraft, keyframeRows, keyframeTimeline.SelectedKeys);
        }
        private void PasteKeyframeSelection() => PasteKeyframes(keyframeClipboard, SnapTime(time));
        private void DuplicateKeyframeSelection()
        {
            if (!ImportedEditing || keyframeTimeline == null) return;
            var keys = RimeEmoKeyframes.Copy(importedDraft, keyframeRows, keyframeTimeline.SelectedKeys);
            if (keys.Count > 0) PasteKeyframes(keys, keys.Max(k => k.key.time) + 1f / 60);
        }
        private void PasteKeyframes(List<RimeEmoKeyframes.CopiedKey> keys, float at)
        {
            if (!ImportedEditing || keys.Count == 0) return;
            var shapes = RimeEmoFace.Shapes(project).ToList();
            HashSet<Vector2Int> pasted = null;
            EditImported(() => pasted = RimeEmoKeyframes.Paste(importedDraft, shapes, keys, at));
            keyframeTimeline.SelectedKeys.Clear();
            ResetKeyframeValue(); RefreshKeyframeEditor();
            foreach (var id in pasted)
            {
                int row = keyframeRows.IndexOf(shapes[id.x]);
                if (row >= 0) keyframeTimeline.SelectedKeys.Add(new Vector2Int(row, id.y));
            }
            keyframeTimeline.Refresh(); RefreshKeyframeInspector();
        }
        private void ApplyKeyframePose(AnimationClip pose)
        {
            if (!ImportedEditing) return;
            EditImported(() =>
            {
                foreach (var shape in RimeEmoFace.Shapes(project))
                {
                    var curve = pose ? AnimationUtility.GetEditorCurve(pose, RimeEmoClips.Binding(shape)) : null;
                    if (pose && curve == null) continue;
                    RimeEmoKeyframes.SetKey(importedDraft, shape, SnapTime(time), curve != null ? curve.Evaluate(0) : shape.defaultValue, keyframeInterpolation);
                }
            }); ResetKeyframeValue(); keyframeTimeline.SelectedKeys.Clear(); RefreshKeyframeEditor();
        }
        private void DrawKeyframeCurve(ShapeEntry shape)
        {
            if (!ImportedEditing) return;
            var binding = RimeEmoClips.Binding(shape); var curve = AnimationUtility.GetEditorCurve(importedDraft, binding) ?? new AnimationCurve();
            EditorGUI.BeginChangeCheck(); var edited = EditorGUILayout.CurveField(curve, GUILayout.Height(125));
            if (EditorGUI.EndChangeCheck() && edited.keys.All(k => FiniteKeyValue(k.time) && k.time >= 0 && k.time <= 600 && FiniteKeyValue(k.value)))
            {
                EditImported(() => AnimationUtility.SetEditorCurve(importedDraft, binding, edited.length > 0 ? edited : null));
                keyframeTimeline.SelectedKeys.Clear(); ResetKeyframeValue();
            }
        }
        private void SyncKeyframeTime()
        {
            if (!ImportedEditing || keyframeTimeline == null) return;
            if (keyframePendingShape != null && !Mathf.Approximately(time, keyframePendingTime)) ResetKeyframeValue();
            keyframeTimeline.SetTime(time);
            var shape = ActiveKeyframeShape; if (shape == null) return;
            UI<FloatField>("kf-value")?.SetValueWithoutNotify(KeyframeValue(shape));
            UI<Slider>("kf-value-slider")?.SetValueWithoutNotify(KeyframeValue(shape));
        }
    }
}
