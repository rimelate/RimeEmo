using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        private bool staticAdjusting;
        private AnimationClip adjustingClip;
        private RimeEmoProject adjustingProject;
        private bool adjustingEditable;
        private bool adjustingDefaultFace;
        private bool pendingShape;
        private EditorCurveBinding pendingShapeBinding;
        private float pendingShapeValue, pendingShapeOriginal;

        private void EndStaticGesture(bool commit = true)
        {
            staticAdjusting = false;
            bool write = pendingShape && commit && (adjustingClip || adjustingDefaultFace) && pendingShapeValue != pendingShapeOriginal;
            pendingShape = false;
            if (write && adjustingDefaultFace) SetDefaultFaceValue(pendingShapeBinding, pendingShapeValue);
            else if (write) RimeEmoClips.Set(adjustingClip, pendingShapeBinding, pendingShapeValue);
            preview?.ClearLiveShape();
            RequestToolkitPreview();
        }
        private void BindStaticToolkit()
        {
            var name = UI<TextField>("static-name"); name.isDelayed = true;
            name.RegisterValueChangedCallback(e => Run(() =>
            {
                // Only the field's own edits rename the clip. A label's ChangeEvent<string> also reaches this callback (retargeted
                // to the field, so e.target cannot tell them apart); its new value is the label text, not the field's value.
                if (e.newValue != name.value || !CanEditStatic() || string.IsNullOrWhiteSpace(e.newValue)) return;
                string error = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(selected), RimeEmoAssets.SafeName(e.newValue));
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
                toolkitDirty = true;
            }));
            // Value check: a label's ChangeEvent<string> reaches here too and must not become the search word.
            UI<TextField>("shape-search").RegisterValueChangedCallback(e => { if (e.newValue != ((TextField)e.currentTarget).value) return; search = e.newValue; ResetShapeView(); RefreshStaticToolkit(); });
            // The word survives a rebuild (language switch, reload); show it, or the list stays filtered by an invisible word.
            UI<TextField>("shape-search").SetValueWithoutNotify(search ?? "");
            var tabs = UI<VisualElement>("shape-part-tabs");
            tabs.Clear();
            for (int i = 0; i < Parts.Length; i++)
            {
                int index = i;
                // Material 3 filter chip: check mark + label, selected state from RefreshShapePartTabs.
                var tab = new Button(() => SelectShapePart(index)) { name = "shape-part-tab-" + i, focusable = true };
                tab.AddToClassList("re-chip"); tab.AddToClassList("re-shape-part-tab");
                var check = RimeEmoIcons.Glyph("check", "✓"); check.AddToClassList("re-chip-check"); check.RemoveFromClassList("re-action-icon"); tab.Add(check);
                var caption = new Label(Parts[i]) { pickingMode = PickingMode.Ignore }; caption.AddToClassList("re-chip-label"); tab.Add(caption);
                tabs.Add(tab);
                tab.RegisterCallback<KeyDownEvent>(e =>
                {
                    int next = index;
                    if (e.keyCode == KeyCode.LeftArrow) next = (index + Parts.Length - 1) % Parts.Length;
                    else if (e.keyCode == KeyCode.RightArrow) next = index + 1;
                    else if (e.keyCode == KeyCode.Home) next = 0;
                    else if (e.keyCode == KeyCode.End) next = Parts.Length - 1;
                    else return;
                    e.StopPropagation();
                    // Right from the last part continues into the user groups.
                    if (next >= Parts.Length)
                    {
                        if (RimeEmoShapeGroups.Groups(project).Count == 0) next = 0;
                        else { UI<Button>("shape-group-tab-0").Focus(); SelectShapeGroup(0); return; }
                    }
                    UI<Button>("shape-part-tab-" + next).Focus();
                    SelectShapePart(next);
                });
            }
            RefreshShapePartTabs();
            var timeSlider = UI<Slider>("static-time");
            timeSlider.RegisterValueChangedCallback(e => { time = e.newValue; RequestToolkitPreview(staticAdjusting); });
            BindStaticGesture(timeSlider);
            BindShapeFilters();
            BindShapeList();
            BindDefaultFace();
        }
        private void SelectShapePart(int index)
        {
            category = Mathf.Clamp(index, 0, Parts.Length - 1);
            shapeGroup = -1;
            ResetShapeView();
            RefreshStaticToolkit();
        }
        private void RefreshShapePartTabs()
        {
            for (int i = 0; i < Parts.Length; i++)
            {
                var tab = UI<Button>("shape-part-tab-" + i);
                if (tab != null) tab.EnableInClassList("is-active", i == category && shapeGroup < 0);
            }
            RefreshShapeGroupTabs();
        }
        private bool CanEditStatic() => EditingDefaultFace
            ? avatar && RimeEmoFace.Shapes(project).Any(s => FaceRenderer(s.path))
            : project && selected && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(project)) && RimeEmoAssets.Owns(project, selected);
        private void BindStaticGesture(VisualElement control)
        {
            control.RegisterCallback<PointerDownEvent>(_ =>
            {
                EndStaticGesture();
                staticAdjusting = true;
                adjustingClip = selected; adjustingProject = project; adjustingEditable = CanEditStatic(); adjustingDefaultFace = EditingDefaultFace;
            }, TrickleDown.TrickleDown);
            control.RegisterCallback<PointerUpEvent>(_ => EndStaticGesture());
            control.RegisterCallback<PointerCaptureOutEvent>(_ => EndStaticGesture());
            control.RegisterCallback<DetachFromPanelEvent>(_ => EndStaticGesture());
        }
        // One list for every Shape Key. A row the clip does not contain shows the Default Face value;
        // moving its slider adds it, × removes it again.
        private void BindShapeList()
        {
            // Must match .re-shape-row height in RimeEmoTheme.uss, or rows overlap.
            var list = UI<ListView>("shape-list"); list.fixedItemHeight = 32; list.selectionType = SelectionType.None;
            list.itemsSource = shapeRows;
            list.makeItem = () =>
            {
                var row = new VisualElement(); row.AddToClassList("re-shape-row");
                row.AddManipulator(new ContextualMenuManipulator(e => BuildShapeRowMenu(e.menu, row.userData as ShapeRow)));
                var label = new Label { name = "shape-label" }; label.AddToClassList("re-shape-label"); row.Add(label);
                // The value field is a sibling, not the slider's own input field: that one lives inside the
                // slider's short box and is clipped by it.
                var slider = new Slider(0, 100) { name = "shape-value" }; slider.AddToClassList("re-shape-slider"); row.Add(slider); BindStaticGesture(slider);
                var number = new FloatField { name = "shape-number", isDelayed = true, formatString = "0.###" }; number.AddToClassList("re-shape-number"); row.Add(number);
                number.RegisterValueChangedCallback(e =>
                {
                    if (!(row.userData is ShapeRow data) || !data.constant || float.IsNaN(e.newValue) || float.IsInfinity(e.newValue)) return;
                    // Typed values may leave the 0–100 track; widen it so the slider does not clamp them.
                    slider.lowValue = Mathf.Min(slider.lowValue, e.newValue); slider.highValue = Mathf.Max(slider.highValue, e.newValue);
                    slider.value = e.newValue;
                });
                slider.RegisterValueChangedCallback(e =>
                {
                    if (!(row.userData is ShapeRow data) || !data.constant || float.IsNaN(e.newValue) || float.IsInfinity(e.newValue)) return;
                    number.SetValueWithoutNotify(e.newValue);
                    bool editable = staticAdjusting && adjustingClip == selected && adjustingProject == project
                        ? adjustingEditable : CanEditStatic();
                    if (!editable || data.value == e.newValue) return;
                    keptShapes.Add(data.binding);
                    if (staticAdjusting)
                    {
                        if (!pendingShape) { pendingShapeOriginal = data.included ? data.value : float.NaN; pendingShapeBinding = data.binding; }
                        pendingShape = true; pendingShapeValue = e.newValue;
                        preview?.SetLiveShape(EditingDefaultFace ? defaults : selected, data.binding, e.newValue);
                    }
                    else if (EditingDefaultFace) SetDefaultFaceValue(data.binding, e.newValue);
                    else RimeEmoClips.Set(selected, data.binding, e.newValue);
                    data.value = e.newValue;
                    row.EnableInClassList("is-default", data.included && Mathf.Abs(data.value - data.shape.defaultValue) < 0.0001f);
                    RequestToolkitPreview(staticAdjusting);
                });
                var flatten = new Button(() =>
                {
                    if (!(row.userData is ShapeRow data) || !CanEditStatic()) return;
                    keptShapes.Add(data.binding);
                    RimeEmoClips.Set(selected, data.binding, data.value); rowsDirty = true; ClipEdited();
                }) { name = "shape-flatten", text = RimeEmoLocalization.T("0秒の値で固定") };
                flatten.AddToClassList("re-button"); flatten.AddToClassList("re-grow"); row.Add(flatten);
                var change = new Button(() =>
                {
                    if (!(row.userData is ShapeRow data) || !CanEditStatic()) return;
                    keptShapes.Add(data.binding);
                    if (data.included) RimeEmoClips.Remove(selected, data.binding); else RimeEmoClips.Set(selected, data.binding, data.value);
                    rowsDirty = true; ClipEdited();
                }) { name = "shape-change" };
                change.AddToClassList("re-button"); change.AddToClassList("re-button--icon"); row.Add(change);
                return row;
            };
            list.bindItem = (row, index) =>
            {
                var data = shapeRows[index]; row.userData = null;
                row.Q<Label>("shape-label").text = data.shape.shape;
                row.tooltip = data.shape.shape + "\nDefault: " + data.shape.defaultValue + "\n" + data.shape.path
                    + (data.included ? "" : "\n" + RimeEmoLocalization.T("このClipには含まれていません。値を動かすと追加します。"));
                row.EnableInClassList("is-unset", !data.included);
                row.EnableInClassList("is-default", data.included && data.constant && Mathf.Abs(data.value - data.shape.defaultValue) < 0.0001f);
                var slider = row.Q<Slider>("shape-value"); slider.lowValue = Mathf.Min(0, data.value); slider.highValue = Mathf.Max(100, data.value); slider.SetValueWithoutNotify(data.value);
                var number = row.Q<FloatField>("shape-number"); number.SetValueWithoutNotify(data.value);
                row.userData = data;
                slider.style.display = data.constant ? DisplayStyle.Flex : DisplayStyle.None;
                number.style.display = data.constant ? DisplayStyle.Flex : DisplayStyle.None;
                row.Q<Button>("shape-flatten").style.display = data.constant ? DisplayStyle.None : DisplayStyle.Flex;
                var change = row.Q<Button>("shape-change");
                change.text = data.included ? "×" : "+";
                // Default Face has every Shape Key; there is nothing to add or remove.
                change.style.display = EditingDefaultFace ? DisplayStyle.None : DisplayStyle.Flex;
                change.tooltip = RimeEmoLocalization.T(data.included ? "このClipから外す（Default Faceの値に戻ります）" : "Default Faceの値でこのClipに追加");
            };
        }
        // ListView's own "List is empty" is English only and does not say what to do next.
        private void RefreshShapeEmpty()
        {
            bool none = project && shapeRows.Count == 0;
            var group = SelectedShapeGroup;
            bool emptyGroup = none && group != null && !RimeEmoFace.Shapes(project).Any(s => RimeEmoShapeGroups.Contains(group, s.shape));
            var label = UI<Label>("shape-empty");
            label.text = !none ? ""
                : emptyGroup ? string.Format(RimeEmoLocalization.T("「{0}」にはまだShape Keyが入っていません。右クリックの「編集…」で名前の条件を追加するか、「すべて」に戻して行を右クリックし「グループに追加」を選びます。"), group.name)
                : RimeEmoLocalization.T("条件に合うShape Keyはありません。検索語や絞り込みを変えてください。");
            label.style.display = none ? DisplayStyle.Flex : DisplayStyle.None;
            UI<ListView>("shape-list").style.display = none ? DisplayStyle.None : DisplayStyle.Flex;
        }
        private void RefreshStaticToolkit()
        {
            if (toolkitRoot == null) return;
            bool editable = CanEditStatic();
            UI<VisualElement>("static-controls").SetEnabled(project);
            UI<TextField>("static-name").SetValueWithoutNotify(selected ? selected.name : "");
            UI<TextField>("static-name").SetEnabled(editable);
            UI<ListView>("shape-list").SetEnabled(editable);
            var notice = UI<Label>("static-notice");
            notice.text = !selected ? RimeEmoLocalization.T("素材一覧からClipを選択してください。")
                : editable ? ""
                : RimeEmoImportedClips.IsAnimated(selected, project) ? RimeEmoLocalization.T("これは動きです。「動きを編集」かダブルクリックで動きの編集を開きます。")
                : RimeEmoLocalization.T("このClipは直接編集できません。「表情を編集」を押すとコピーを作って編集します。");
            // An empty notice would still leave a blank line above the filters.
            notice.style.display = string.IsNullOrEmpty(notice.text) ? DisplayStyle.None : DisplayStyle.Flex;
            RefreshShapePartTabs();
            if (project) EnsureRows();
            else { shapeRows.Clear(); Array.Clear(shapeStateCounts, 0, shapeStateCounts.Length); hiddenShapeCount = 0; rowsDirty = false; rowsClip = selected; }
            RefreshShapeFilters();
            UI<ListView>("shape-list").RefreshItems();
            RefreshShapeEmpty();
            UI<Slider>("static-time").style.display = RimeEmoImportedClips.IsAnimated(selected, project) ? DisplayStyle.Flex : DisplayStyle.None;
            var slider = UI<Slider>("static-time"); slider.highValue = selected ? Mathf.Max(1f / 60, selected.length) : 1; time = Mathf.Clamp(time, 0, slider.highValue); slider.SetValueWithoutNotify(time);
            RefreshDefaultFace();
            RequestToolkitPreview();
        }
    }
}
