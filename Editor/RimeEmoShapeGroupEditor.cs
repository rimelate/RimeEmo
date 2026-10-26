using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // Group page of the parts-and-groups window: group list on the left, the selected group's
    // name patterns and a live checklist of the face shapes on the right.
    internal sealed class RimeEmoShapeGroupEditor : VisualElement
    {
        private RimeEmoProject project;
        private int selected = -1;
        private string search = "";
        private bool membersOnly;

        private readonly TextField baseField, authorField;
        private readonly ListView groupList, shapeList;
        private readonly VisualElement detail, patternHost;
        private readonly TextField nameField;
        private readonly Label emptyLabel, summary, missing;
        private readonly Button deleteButton, pruneButton;
        private readonly List<ShapeGroup> groups = new List<ShapeGroup>();
        private readonly List<string> shapes = new List<string>();
        private List<string> shapeOrder = new List<string>();
        private string shapeOrderKey;

        internal int Selected => selected;
        private RimeEmoShapeGroupSet Set => project ? project.shapeGroups : null;
        private ShapeGroup Group => selected >= 0 && selected < groups.Count ? groups[selected] : null;

        internal RimeEmoShapeGroupEditor()
        {
            name = "group-editor";
            style.flexGrow = 1;

            // Set-level info: one row, aligned labels.
            var info = Row(false);
            baseField = Field("対象の素体", "group-base");
            baseField.tooltip = RimeEmoLocalization.T("別のアバターでこのグループを提案するときや、ファイルを配布するときに表示します");
            authorField = Field("作者", "group-author");
            baseField.RegisterValueChangedCallback(_ => SaveInfo()); authorField.RegisterValueChangedCallback(_ => SaveInfo());
            info.Add(baseField); info.Add(authorField);
            Add(info);

            var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, minHeight = 0 } };
            Add(body);

            // Left card: the groups, with their add action in the header as on the materials shelf.
            var left = new VisualElement { style = { width = 220, flexShrink = 0 } };
            left.AddToClassList("re-card");
            var leftHeader = Row(false);
            var title = new Label(RimeEmoLocalization.T("グループ")); title.AddToClassList("re-title"); title.style.flexGrow = 1;
            var add = new Button(AddGroup) { name = "group-add", text = RimeEmoLocalization.T("＋ グループ") };
            add.AddToClassList("re-button"); add.AddToClassList("re-button--tonal");
            leftHeader.Add(title); leftHeader.Add(add);
            left.Add(leftHeader);
            groupList = new ListView(groups, 26, () => new Label { style = { unityTextAlign = TextAnchor.MiddleLeft, paddingLeft = 8 } },
                (element, i) => ((Label)element).text = i < groups.Count ? groups[i].name : "")
            { name = "group-list", selectionType = SelectionType.Single, style = { flexGrow = 1 } };
            groupList.selectionChanged += _ => { if (groupList.selectedIndex >= 0) { selected = groupList.selectedIndex; RefreshDetail(); } };
            left.Add(groupList);
            body.Add(left);

            // Right card: the selected group.
            var right = new VisualElement { style = { flexGrow = 1, minWidth = 0 } };
            right.AddToClassList("re-card");
            emptyLabel = new Label(RimeEmoLocalization.T("グループはまだありません。「＋ グループ」で作成します。")) { name = "group-empty" };
            emptyLabel.AddToClassList("re-hint");
            right.Add(emptyLabel);
            detail = new VisualElement { name = "group-detail", style = { flexGrow = 1, minHeight = 0 } };
            var nameRow = Row(false);
            nameField = Field("名前", "group-name");
            nameField.RegisterValueChangedCallback(e =>
            {
                if (Group == null || string.IsNullOrWhiteSpace(e.newValue)) { nameField.SetValueWithoutNotify(Group?.name ?? ""); return; }
                RimeEmoShapeGroups.Rename(Set, Group, e.newValue);
            });
            deleteButton = new Button(DeleteGroup) { name = "group-delete", text = RimeEmoLocalization.T("グループを削除") };
            deleteButton.AddToClassList("re-button"); deleteButton.AddToClassList("re-button--text");
            nameRow.Add(nameField); nameRow.Add(deleteButton);
            detail.Add(nameRow);

            var patternTitle = new Label(RimeEmoLocalization.T("名前の条件")); patternTitle.AddToClassList("re-subtitle"); detail.Add(patternTitle);
            var patternHint = new Label(RimeEmoLocalization.T("＊は任意の文字列です。例: Eyelid_*（〜で始まる）、*Tare*（〜を含む）、*_R（〜で終わる）。大文字と小文字は区別しません。"));
            patternHint.AddToClassList("re-hint"); detail.Add(patternHint);
            patternHost = new VisualElement { name = "group-patterns" };
            detail.Add(patternHost);
            var addPattern = new Button(() => { if (Group != null) RimeEmoShapeGroups.AddPattern(Set, Group, ""); FocusLastPattern(); }) { name = "group-pattern-add", text = RimeEmoLocalization.T("＋ 条件を追加") };
            addPattern.AddToClassList("re-button"); addPattern.AddToClassList("re-button--text");
            addPattern.style.alignSelf = Align.FlexStart;
            detail.Add(addPattern);

            var membersTitle = new Label(RimeEmoLocalization.T("Shape Key")); membersTitle.AddToClassList("re-subtitle"); detail.Add(membersTitle);
            var filter = Row(false);
            var searchField = Field("検索", "group-search");
            searchField.isDelayed = false;
            searchField.AddToClassList("re-search");
            searchField.RegisterValueChangedCallback(e => { search = e.newValue; RefreshShapes(); });
            filter.Add(searchField);
            // Check box first, text after: reads as a filter option rather than a form field.
            var members = new Toggle { name = "group-members-only", text = RimeEmoLocalization.T("グループ内だけ") };
            members.AddToClassList("re-group-option");
            members.RegisterValueChangedCallback(e => { membersOnly = e.newValue; RefreshShapes(); });
            filter.Add(members);
            summary = new Label { name = "group-summary" }; summary.AddToClassList("re-hint");
            filter.Add(summary);
            detail.Add(filter);

            shapeList = new ListView(shapes, 22, MakeShapeRow, BindShapeRow) { name = "group-shapes", selectionType = SelectionType.None, style = { flexGrow = 1, minHeight = 80 } };
            detail.Add(shapeList);
            var footer = Row(false);
            missing = new Label { name = "group-missing" }; missing.AddToClassList("re-hint"); missing.style.flexGrow = 1;
            pruneButton = new Button(PruneMissing) { name = "group-prune", text = RimeEmoLocalization.T("このメッシュにない名前を削除"), tooltip = RimeEmoLocalization.T("同じグループを使う別のアバターでは必要な場合があります") };
            pruneButton.AddToClassList("re-button"); pruneButton.AddToClassList("re-button--text");
            footer.Add(missing); footer.Add(pruneButton);
            detail.Add(footer);
            right.Add(detail);
            body.Add(right);
        }

        private static VisualElement Row(bool wrap)
        {
            var row = new VisualElement(); row.AddToClassList("re-row");
            row.style.flexShrink = 0; // Fixed rows never give up height to the lists.
            if (wrap) row.AddToClassList("re-wrap");
            return row;
        }

        // Text fields share one label width so their inputs line up.
        private static TextField Field(string label, string id)
        {
            var field = new TextField(RimeEmoLocalization.T(label)) { name = id, isDelayed = true };
            field.AddToClassList("re-compact-field"); field.AddToClassList("re-grow");
            return field;
        }

        internal void Select(int index) => selected = index;

        internal void FocusName() => schedule.Execute(() => { nameField.Focus(); nameField.SelectAll(); });

        internal void Bind(RimeEmoProject value, int index)
        {
            if (value != project) shapeOrderKey = null;
            project = value;
            selected = index;
            Refresh();
        }

        internal void Refresh()
        {
            groups.Clear();
            if (Set) groups.AddRange(Set.groups);
            if (selected >= groups.Count) selected = groups.Count - 1;
            if (selected < 0 && groups.Count > 0) selected = 0;
            baseField.SetEnabled(Set); authorField.SetEnabled(Set);
            baseField.SetValueWithoutNotify(Set ? Set.baseModel ?? "" : "");
            authorField.SetValueWithoutNotify(Set ? Set.author ?? "" : "");
            groupList.RefreshItems();
            if (selected >= 0) groupList.SetSelectionWithoutNotify(new[] { selected }); else groupList.ClearSelection();
            deleteButton.SetEnabled(Group != null);
            RefreshDetail();
        }

        private void RefreshDetail()
        {
            var group = Group;
            emptyLabel.style.display = group == null ? DisplayStyle.Flex : DisplayStyle.None;
            detail.style.display = group == null ? DisplayStyle.None : DisplayStyle.Flex;
            deleteButton.SetEnabled(group != null);
            if (group == null) return;
            nameField.SetValueWithoutNotify(group.name);
            patternHost.Clear();
            for (int i = 0; i < group.patterns.Count; i++)
            {
                int index = i;
                var row = Row(false);
                var field = new TextField { value = group.patterns[i], isDelayed = true, name = "group-pattern-" + i };
                field.AddToClassList("re-grow");
                field.RegisterValueChangedCallback(e => RimeEmoShapeGroups.SetPattern(Set, group, index, e.newValue));
                var remove = new Button(() => RimeEmoShapeGroups.RemovePattern(Set, group, index)) { text = "×", tooltip = RimeEmoLocalization.T("この条件を削除") };
                remove.AddToClassList("re-button"); remove.AddToClassList("re-button--icon");
                row.Add(field); row.Add(remove);
                patternHost.Add(row);
            }
            if (group.patterns.Count == 0)
            {
                var none = new Label(RimeEmoLocalization.T("条件はありません。下の一覧のチェックだけで決まります。")); none.AddToClassList("re-hint");
                patternHost.Add(none);
            }
            RefreshShapes();
        }

        private void FocusLastPattern()
        {
            schedule.Execute(() => patternHost.Query<TextField>().ToList().LastOrDefault()?.Focus());
        }

        private void RefreshShapes()
        {
            var group = Group;
            shapes.Clear();
            if (group == null || !project) { shapeList.RefreshItems(); return; }
            var meshNames = RimeEmoFace.Shapes(project).Select(s => s.shape).ToList();
            int members = meshNames.Count(n => RimeEmoShapeGroups.Contains(group, n));
            // Members first. Re-sorted only when the group, its conditions or the search change: a ticked row stays
            // where it was clicked instead of jumping to the top. "Members only" always shows the current members.
            string key = selected + "|" + search + "|" + meshNames.Count + "|" + string.Join("\n", group.patterns);
            if (key != shapeOrderKey)
            {
                shapeOrderKey = key;
                shapeOrder = meshNames.Where(n => string.IsNullOrEmpty(search) || n.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(n => RimeEmoShapeGroups.Contains(group, n)).ToList();
            }
            shapes.AddRange(membersOnly ? shapeOrder.Where(n => RimeEmoShapeGroups.Contains(group, n)) : shapeOrder);
            shapeList.RefreshItems();
            summary.text = string.Format(RimeEmoLocalization.T("{0}件が該当（全{1}件中）"), members, meshNames.Count);
            var lost = RimeEmoShapeGroups.MissingNames(group, new HashSet<string>(meshNames));
            missing.text = lost.Count > 0 ? string.Format(RimeEmoLocalization.T("このメッシュにない名前: {0}件"), lost.Count) : "";
            missing.tooltip = string.Join("\n", lost.Take(30));
            pruneButton.style.display = lost.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement MakeShapeRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 4 } };
            var toggle = new Toggle { name = "group-member" };
            toggle.RegisterValueChangedCallback(e =>
            {
                if (!(row.userData is string shape) || Group == null) return;
                if (e.newValue) RimeEmoShapeGroups.Add(Set, Group, shape); else RimeEmoShapeGroups.Remove(Set, Group, shape);
            });
            row.Add(toggle);
            var label = new Label { name = "group-shape-name", style = { flexGrow = 1, overflow = Overflow.Hidden } };
            row.Add(label);
            var reason = new Label { name = "group-reason", style = { width = 72 } }; reason.AddToClassList("re-hint");
            row.Add(reason);
            return row;
        }

        private void BindShapeRow(VisualElement row, int index)
        {
            var group = Group;
            if (group == null || index >= shapes.Count) return;
            string shape = shapes[index];
            row.userData = null;
            bool member = RimeEmoShapeGroups.Contains(group, shape);
            row.Q<Toggle>("group-member").SetValueWithoutNotify(member);
            row.Q<Label>("group-shape-name").text = shape;
            row.Q<Label>("group-reason").text = RimeEmoLocalization.T(group.removed.Contains(shape) ? "手動で除外" : group.added.Contains(shape) ? "手動で追加" : member ? "条件" : "");
            row.userData = shape;
        }

        private void AddGroup()
        {
            if (!project) return;
            try
            {
                selected = RimeEmoShapeGroups.Create(project, RimeEmoLocalization.T("新しいグループ"), null, null);
                Refresh();
                schedule.Execute(() => { nameField.Focus(); nameField.SelectAll(); });
            }
            catch (Exception ex) { Debug.LogException(ex); EditorUtility.DisplayDialog("RimeEmo", ex.Message, "OK"); }
        }

        private void DeleteGroup()
        {
            var group = Group;
            if (group == null || !EditorUtility.DisplayDialog("RimeEmo", string.Format(RimeEmoLocalization.T("グループ「{0}」を削除しますか？Shape Key自体は変更されません。"), group.name),
                RimeEmoLocalization.T("削除"), RimeEmoLocalization.T("キャンセル"))) return;
            RimeEmoShapeGroups.Delete(Set, selected);
        }

        private void PruneMissing()
        {
            var group = Group;
            if (group == null || !project) return;
            RimeEmoShapeGroups.PruneMissing(Set, group, new HashSet<string>(RimeEmoFace.Shapes(project).Select(s => s.shape)));
        }

        private void SaveInfo()
        {
            if (Set) RimeEmoShapeGroups.SetInfo(Set, baseField.value, authorField.value);
        }
    }
}
