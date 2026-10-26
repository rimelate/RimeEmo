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
        private readonly List<ClipEntry> materialItems = new List<ClipEntry>();
        private string materialSearch = "";
        private AnimationClip materialDrag;
        private AnimationClip motionMaterial;
        private AnimationClip MaterialSelection
        {
            get => mode == 0 ? selected : motionMaterial;
            set { if (mode == 0) { selected = value; defaultFaceSelected = false; } else motionMaterial = value; }
        }
        private Vector2 materialDragStart;
        private AnimationClip hoveredMaterial;
        private VisualElement hoveredMaterialRow;
        // The row under the pointer as UI Toolkit reports it (PointerEnter/Leave), the same hit test that draws the
        // row's hover color. Picking a stored pointer position by hand drifted by the window's tab height (about one row)
        // and previewed the row below the one under the cursor.
        private VisualElement pointerMaterialRow;

        // Scrolling moves other rows under a still pointer; clear until UI Toolkit reports the new row on the next move.
        private void ForgetMaterialUnderPointer()
        {
            pointerMaterialRow = null;
            PreviewMaterial(null);
        }

        private void PreviewMaterial(VisualElement row)
        {
            if (playing && row != null) return;
            var clip = (row?.userData as ClipEntry)?.clip;
            if (hoveredMaterialRow == row && hoveredMaterial == clip) return;
            if (hoveredMaterial && !clip && mode == 1)
            {
                showDefault = false;
                preview?.InvalidateMotion();
            }
            hoveredMaterialRow?.RemoveFromClassList("is-previewing");
            hoveredMaterialRow = row; hoveredMaterial = clip;
            hoveredMaterialRow?.AddToClassList("is-previewing");
            if (retainedTimeline != null) SyncToolkitTime();
            RequestToolkitPreview();
        }

        private void BindMaterials()
        {
            var list = UI<ListView>("material-list");
            // Must match .re-material-row height in RimeEmoTheme.uss.
            list.fixedItemHeight = 26; list.selectionType = SelectionType.Single;
            // Delete key = "Delete Expression…" in the Other menu (it still asks first).
            list.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode != KeyCode.Delete && e.keyCode != KeyCode.Backspace) || mode != 0 || EditingDefaultFace || !CanEditStatic()) return;
                Run(DeleteSelectedExpression); e.StopPropagation();
            });
            list.itemsSource = materialItems;
            list.makeItem = () =>
            {
                var row = new VisualElement(); row.AddToClassList("re-material-row");
                // The kind badge leads the row so it forms one column; after the name it moved with each gesture text.
                var kind = new Label { name = "clip-kind" }; kind.AddToClassList("re-material-type"); row.Add(kind);
                var name = new Label { name = "clip-name" }; name.AddToClassList("re-material-name"); row.Add(name);
                var gesture = new Label { name = "clip-gesture" }; gesture.AddToClassList("re-material-gesture"); row.Add(gesture);
                row.RegisterCallback<PointerEnterEvent>(_ => { pointerMaterialRow = row; PreviewMaterial(row); });
                row.RegisterCallback<PointerLeaveEvent>(_ => { if (pointerMaterialRow == row) pointerMaterialRow = null; if (hoveredMaterialRow == row) PreviewMaterial(null); });
                row.RegisterCallback<DetachFromPanelEvent>(_ => { if (hoveredMaterialRow == row) PreviewMaterial(null); });
                row.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0 || !(row.userData is ClipEntry entry) || !entry.clip) return;
                    MaterialSelection = materialDrag = entry.clip; materialDragStart = e.position;
                    UpdateMaterialButtons();
                    if (e.clickCount == 2) { materialDrag = null; Run(OpenSelectedMaterial); }
                });
                return row;
            };
            list.bindItem = (row, i) =>
            {
                var item = materialItems[i]; row.userData = item;
                if (item == DefaultFaceEntry)
                {
                    // Always the first row: the face with no gesture, edited on the scene avatar.
                    row.Q<Label>("clip-name").text = "Default Face";
                    var baseKind = row.Q<Label>("clip-kind"); baseKind.text = RimeEmoLocalization.T("基準"); baseKind.EnableInClassList("is-dynamic", false);
                    row.Q<Label>("clip-gesture").text = RimeEmoLocalization.T("シーンのアバターの顔");
                    row.tooltip = RimeEmoLocalization.T("割り当てのない手の形の顔です。シーンのアバターの顔メッシュの値を編集します。");
                    return;
                }
                row.Q<Label>("clip-name").text = item.clip.name;
                bool animated = RimeEmoImportedClips.IsAnimated(item.clip, project);
                var kind = row.Q<Label>("clip-kind");
                kind.text = RimeEmoLocalization.T(animated ? "動的" : "静的");
                kind.EnableInClassList("is-dynamic", animated);
                var assigned = AssignedGestures(item.clip);
                row.Q<Label>("clip-gesture").text = assigned.Length > 0
                    ? string.Join(" / ", assigned.Take(2)) + (assigned.Length > 2 ? " …" : "")
                    : !string.IsNullOrEmpty(item.gesture) && item.gesture != "Unknown" && item.gesture != "未割り当て"
                        ? RimeEmoLocalization.T("元FX: ") + RimeEmoLocalization.GestureDescription(item.gesture)
                        : RimeEmoLocalization.T("未割り当て");
                row.tooltip = AssetDatabase.GetAssetPath(item.clip) + (assigned.Length > 0 ? "\n" + RimeEmoLocalization.T("Gesture: ") + string.Join(", ", assigned) : "");
                // A row that now shows another material while the pointer stays on it previews the new one.
                if (row == pointerMaterialRow) PreviewMaterial(row);
            };
            list.unbindItem = (row, _) => { if (hoveredMaterialRow == row) PreviewMaterial(null); row.userData = null; };
            list.RegisterCallback<PointerLeaveEvent>(_ => ForgetMaterialUnderPointer());
            list.Q<ScrollView>().verticalScroller.valueChanged += _ => ForgetMaterialUnderPointer();
            list.selectionChanged += values =>
            {
                var item = values.OfType<ClipEntry>().FirstOrDefault();
                if (item == DefaultFaceEntry) { SelectDefaultFace(); UpdateMaterialButtons(); return; }
                if (item != null) MaterialSelection = item.clip;
                if (mode == 0) { showDefault = false; time = 0; }
                UpdateMaterialButtons();
            };
            list.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!materialDrag || (e.pressedButtons & 1) == 0 || ((Vector2)e.position - materialDragStart).sqrMagnitude < 16) return;
                PreviewMaterial(null);
                DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences = new UnityEngine.Object[] { materialDrag };
                string name = materialDrag.name; materialDrag = null; DragAndDrop.StartDrag(name); e.StopPropagation();
            });
            list.RegisterCallback<PointerUpEvent>(_ => materialDrag = null);
            UI<TextField>("material-search").RegisterValueChangedCallback(e => { if (e.newValue != ((TextField)e.currentTarget).value) return; materialSearch = e.newValue; RefreshMaterials(); });
            UI<TextField>("material-search").SetValueWithoutNotify(materialSearch ?? ""); // Kept across rebuilds, like the Shape Key search.
            // Only this avatar's clips are offered; Unity's object picker would list every avatar's clips.
            Button("material-import", () =>
            {
                if (!project) return;
                var button = UI<Button>("material-import");
                var rect = new Rect(position.position + button.worldBound.position, button.worldBound.size);
                RimeEmoClipPicker.Open(rect, project, clip => Run(() => { ImportFromShelf(clip); RefreshMaterials(); ScrollToSelectedMaterial(); }));
            });
            Button("material-default", () =>
            {
                MaterialSelection = RimeEmoAssets.NewDefault(project); showDefault = false; PreviewMaterial(null); rowsDirty = true; materialSearch = ""; UI<TextField>("material-search").SetValueWithoutNotify(""); RefreshMaterials();
                ScrollToSelectedMaterial();
                // A new expression contains no Shape Keys yet, so "Included" and "Differs from Default" would show an empty list.
                if (shapeState == ShapeState.Included || shapeState == ShapeState.Changed) shapeState = ShapeState.All;
                // The name comes first: the field opens selected, so typing replaces "新しい表情" and Enter renames the clip.
                var name = UI<TextField>("static-name");
                name.schedule.Execute(() => { name.Focus(); name.SelectAll(); }).StartingIn(50);
            });
            Button("material-edit", OpenSelectedMaterial);
            Button("material-more", OpenMaterialActions);
            var shelf = UI<VisualElement>("material-shelf");
            shelf.RegisterCallback<DragUpdatedEvent>(e =>
            {
                if (!project || !DragAndDrop.objectReferences.OfType<AnimationClip>().Any()) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy; shelf.AddToClassList("is-drop-target"); e.StopPropagation();
            });
            shelf.RegisterCallback<DragLeaveEvent>(_ => shelf.RemoveFromClassList("is-drop-target"));
            shelf.RegisterCallback<DragExitedEvent>(_ => shelf.RemoveFromClassList("is-drop-target"));
            shelf.RegisterCallback<DragPerformEvent>(e =>
            {
                shelf.RemoveFromClassList("is-drop-target");
                if (!project) return;
                var clips = DragAndDrop.objectReferences.OfType<AnimationClip>().Distinct().ToArray();
                if (clips.Length == 0) return;
                DragAndDrop.AcceptDrag();
                Run(() => { foreach (var clip in clips) ImportFromShelf(clip); RefreshMaterials(); ScrollToSelectedMaterial(); });
                e.StopPropagation();
            });
        }
        // Added clips sort to the end of the shelf; bring the newly selected row into view.
        private void ScrollToSelectedMaterial()
        {
            int index = materialItems.FindIndex(c => c.clip && c.clip == MaterialSelection);
            if (index >= 0) UI<ListView>("material-list").ScrollToItem(index);
        }

        private void RefreshMaterials()
        {
            materialItems.Clear();
            if (project)
            {
                var saved = project.motions.Where(m => m && m.animationClip).Select(m => new ClipEntry { clip = m.animationClip, gesture = "未割り当て" });
                materialItems.AddRange(project.clips.Concat(project.animations).Concat(saved)
                    .Where(c => c != null && c.clip && RimeEmoClipClassification.Classify(project, c.clip) != RimeEmoClipKind.Excluded)
                    .GroupBy(c => c.clip).Select(g => g.First())
                    .Where(c => string.IsNullOrEmpty(materialSearch) || c.clip.name.IndexOf(materialSearch, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            var list = UI<ListView>("material-list"); list.RefreshItems();
            if (project && mode == 0 && (string.IsNullOrEmpty(materialSearch) || "Default Face".IndexOf(materialSearch, StringComparison.OrdinalIgnoreCase) >= 0))
                materialItems.Insert(0, DefaultFaceEntry);
            list.RefreshItems();
            int selectedIndex = EditingDefaultFace ? materialItems.IndexOf(DefaultFaceEntry) : materialItems.FindIndex(c => c != DefaultFaceEntry && c.clip == MaterialSelection);
            list.SetSelectionWithoutNotify(new[] { selectedIndex }.Where(i => i >= 0));
            UpdateMaterialButtons();
        }
        private void UpdateMaterialButtons()
        {
            var kind = RimeEmoClipClassification.Classify(project, MaterialSelection);
            bool hasClip = kind != RimeEmoClipKind.Excluded && !EditingDefaultFace;
            UI<Button>("material-more").SetEnabled(hasClip && kind == RimeEmoClipKind.Static);
            UI<Button>("material-more").style.display = mode == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            UI<Button>("material-edit").SetEnabled(hasClip);
            UI<Button>("material-edit").style.display = DisplayStyle.Flex;
            SetButtonCaption(UI<Button>("material-edit"), RimeEmoLocalization.T(kind == RimeEmoClipKind.Dynamic ? "動きを編集" : "表情を編集"));
            UI<Button>("material-default").style.display = mode == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            UI<Label>("material-heading").text = RimeEmoLocalization.T("素材");
            UI<Label>("material-hint").text = RimeEmoLocalization.T(mode == 0
                ? "表情を選ぶと下で編集できます。動きはダブルクリックか「動きを編集」で開きます。"
                : "素材をタイムラインの部位へドラッグして配置できます。動きを丸ごと開くにはダブルクリックします。");
        }

        // Shows the RimeEmo assignment, not the source FX hint, so the shelf reflects what will be built.
        private string[] AssignedGestures(AnimationClip clip)
        {
            if (!project || !clip) return Array.Empty<string>();
            string Hand(int index) => index < 0 ? "ANY" : RimeEmoGestures.Names[Mathf.Clamp(index, 0, RimeEmoGestures.Names.Length - 1)];
            bool Targets(AnimationClip ruleClip, RimeEmoMotion ruleMotion) => ruleClip == clip || (ruleMotion && ruleMotion.animationClip == clip);
            return project.gestureRules.Where(r => r != null && Targets(r.clip, r.motion)).Select(r => "L " + Hand(r.left) + " · R " + Hand(r.right))
                .Concat(project.gestureChoices.Where(c => c != null && !c.useDefault && Targets(c.clip, c.motion)).Select(c => "L " + Hand(c.left) + " · R " + Hand(c.right)))
                .Distinct().ToArray();
        }
    }
}

