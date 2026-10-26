using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // User group chips after the part chips. One chip (part or group) is selected at a time.
    public sealed partial class RimeEmoWindow
    {
        [SerializeField] private int shapeGroup = -1;
        private string shapeGroupSignature;

        private ShapeGroup SelectedShapeGroup
        {
            get
            {
                var groups = RimeEmoShapeGroups.Groups(project);
                if (shapeGroup >= groups.Count) shapeGroup = -1; // Deleted, undone or another set linked.
                return shapeGroup >= 0 ? groups[shapeGroup] : null;
            }
        }

        private bool InSelectedGroup(ShapeEntry shape)
        {
            var group = SelectedShapeGroup;
            return group == null || RimeEmoShapeGroups.Contains(group, shape.shape);
        }

        private void SelectShapeGroup(int index)
        {
            shapeGroup = index; category = 0;
            ResetShapeView();
            RefreshStaticToolkit();
        }

        private void ShapeGroupsEdited()
        {
            shapeGroupSignature = null;
            ResetShapeView();
            RefreshStaticToolkit();
        }

        // Edits from the parts-and-groups window, or from another avatar sharing the same set.
        private void GroupSetEdited(RimeEmoShapeGroupSet set)
        {
            if (!project || project.shapeGroups != set) return;
            ShapeGroupsEdited();
            toolkitDirty = true;
        }

        // Rebuilt only when the groups change, so focus and hover survive ordinary refreshes.
        private void RefreshShapeGroupTabs()
        {
            var host = UI<VisualElement>("shape-group-tabs");
            if (host == null) return;
            var groups = RimeEmoShapeGroups.Groups(project);
            string signature = (project ? project.GetInstanceID() : 0) + "|" + string.Join("\n", groups.Select(g => g.name));
            if (signature != shapeGroupSignature)
            {
                shapeGroupSignature = signature;
                host.Clear();
                for (int i = 0; i < groups.Count; i++)
                {
                    int index = i;
                    var chip = Chip("shape-group-tab-" + i, groups[i].name, () => SelectShapeGroup(index));
                    chip.tooltip = groups[i].name + "\n" + RimeEmoLocalization.T("右クリックで名前の変更・並べ替え・削除");
                    chip.AddManipulator(new ContextualMenuManipulator(e => BuildShapeGroupMenu(e.menu, index)));
                    chip.RegisterCallback<KeyDownEvent>(e =>
                    {
                        int next = e.keyCode == KeyCode.LeftArrow ? index - 1 : e.keyCode == KeyCode.RightArrow ? index + 1 : int.MinValue;
                        if (next == int.MinValue) return;
                        e.StopPropagation();
                        if (next < 0) { UI<Button>("shape-part-tab-" + (Parts.Length - 1)).Focus(); SelectShapePart(Parts.Length - 1); return; }
                        if (next >= RimeEmoShapeGroups.Groups(project).Count) return;
                        UI<Button>("shape-group-tab-" + next).Focus(); SelectShapeGroup(next);
                    });
                    host.Add(chip);
                }
                var add = new Button(() => CreateShapeGroup(null)) { name = "shape-group-add", text = "＋", tooltip = RimeEmoLocalization.T("グループを追加して設定を開く") };
                add.AddToClassList("re-chip"); add.AddToClassList("re-chip--add");
                host.Add(add);
            }
            for (int i = 0; i < groups.Count; i++) UI<Button>("shape-group-tab-" + i)?.EnableInClassList("is-active", i == shapeGroup);
        }

        private static Button Chip(string name, string text, Action clicked)
        {
            var chip = new Button(clicked) { name = name, focusable = true };
            chip.AddToClassList("re-chip");
            var check = RimeEmoIcons.Glyph("check", "✓"); check.AddToClassList("re-chip-check"); check.RemoveFromClassList("re-action-icon"); chip.Add(check);
            var caption = new Label(text) { pickingMode = PickingMode.Ignore }; caption.AddToClassList("re-chip-label"); chip.Add(caption);
            return chip;
        }

        private void BuildShapeGroupMenu(DropdownMenu menu, int index)
        {
            var set = project ? project.shapeGroups : null;
            if (!set || index >= set.groups.Count) return;
            var group = set.groups[index];
            menu.AppendAction(RimeEmoLocalization.T("編集") + "…", _ => RimeEmoClassificationWindow.OpenGroups(project, index));
            menu.AppendAction(RimeEmoLocalization.T("名前を変更"), _ => ShowShapeGroupNameEditor(UI<Button>("shape-group-tab-" + index), group.name, null,
                (name, use) => Run(() => { RimeEmoShapeGroups.Rename(set, group, name); ShapeGroupsEdited(); })));
            menu.AppendAction(RimeEmoLocalization.T("左へ移動"), _ => MoveShapeGroup(set, index, -1), index > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction(RimeEmoLocalization.T("右へ移動"), _ => MoveShapeGroup(set, index, 1), index < set.groups.Count - 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendSeparator();
            menu.AppendAction(RimeEmoLocalization.T("削除"), _ => Run(() =>
            {
                if (!EditorUtility.DisplayDialog("RimeEmo", string.Format(RimeEmoLocalization.T("グループ「{0}」を削除しますか？Shape Key自体は変更されません。"), group.name),
                    RimeEmoLocalization.T("削除"), RimeEmoLocalization.T("キャンセル"))) return;
                RimeEmoShapeGroups.Delete(set, index);
                if (shapeGroup == index) shapeGroup = -1; else if (shapeGroup > index) shapeGroup--;
                ShapeGroupsEdited();
            }));
        }

        private void MoveShapeGroup(RimeEmoShapeGroupSet set, int index, int offset) => Run(() =>
        {
            RimeEmoShapeGroups.Move(set, index, offset);
            // The selection follows the moved chip or the chip it swapped with.
            if (shapeGroup == index) shapeGroup = index + offset; else if (shapeGroup == index + offset) shapeGroup = index;
            ShapeGroupsEdited();
        });

        // Right-click on a Shape Key row.
        private void BuildShapeRowMenu(DropdownMenu menu, ShapeRow data)
        {
            if (!project || data == null) return;
            var set = project.shapeGroups;
            string shape = data.shape.shape;
            string addTo = RimeEmoLocalization.T("グループに追加") + "/";
            if (set)
                foreach (var group in set.groups)
                {
                    var target = group;
                    bool member = RimeEmoShapeGroups.Contains(target, shape);
                    menu.AppendAction(addTo + RimeEmoLocalization.MenuText(target.name),
                        _ => Run(() => { RimeEmoShapeGroups.Add(set, target, shape); ShapeGroupsEdited(); }),
                        member ? DropdownMenuAction.Status.Checked | DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                }
            menu.AppendAction(addTo + RimeEmoLocalization.T("新しいグループ") + "…", _ => CreateShapeGroup(shape));
            var selectedGroup = SelectedShapeGroup;
            if (set && selectedGroup != null && RimeEmoShapeGroups.Contains(selectedGroup, shape))
                menu.AppendAction(string.Format(RimeEmoLocalization.T("「{0}」から外す"), RimeEmoLocalization.MenuText(selectedGroup.name)),
                    _ => Run(() => { RimeEmoShapeGroups.Remove(set, selectedGroup, shape); ShapeGroupsEdited(); }));
        }

        // From a row the new group starts with that Shape Key; otherwise it can take the search word as a pattern.
        // Creates the group at once and opens it in the parts-and-groups window, where its name and patterns are set.
        // From a row it starts with that Shape Key; otherwise a search word becomes its first pattern.
        private void CreateShapeGroup(string shape) => Run(() =>
        {
            if (!project) return;
            string word = shape == null ? (search ?? "").Trim() : "";
            string name = shape ?? (word.Length > 0 ? word : RimeEmoLocalization.T("新しいグループ"));
            string pattern = word.Length > 0 ? (word.Contains("*") ? word : "*" + word + "*") : null;
            int index = RimeEmoShapeGroups.Create(project, name, pattern, shape);
            shapeGroupSignature = null;
            SelectShapeGroup(index);
            RimeEmoClassificationWindow.OpenGroups(project, index, true);
        });

        // ---------- Settings page: which group file these avatar settings use ----------

        private void BindShapeGroupSettings()
        {
            BindObject<RimeEmoShapeGroupSet>("shape-group-set", () => project ? project.shapeGroups : null, value =>
            {
                try { if (project && value != project.shapeGroups) LinkShapeGroups(value); }
                finally { UI<UnityEditor.UIElements.ObjectField>("shape-group-set").SetValueWithoutNotify(project ? project.shapeGroups : null); }
            });
            Button("shape-group-edit", () => RimeEmoClassificationWindow.OpenGroups(project, shapeGroup));
            Button("shape-group-copy", () => { if (project && project.shapeGroups) LinkShapeGroups(RimeEmoShapeGroups.Duplicate(project, project.shapeGroups)); });
        }

        private void LinkShapeGroups(RimeEmoShapeGroupSet set)
        {
            Undo.RecordObject(project, "RimeEmo: Link Shape Groups");
            project.shapeGroups = set;
            EditorUtility.SetDirty(project);
            RimeEmoShapeGroups.InvalidateUsers();
            shapeGroup = -1;
            ShapeGroupsEdited();
            toolkitDirty = true;
        }

        private void RefreshShapeGroupSettings()
        {
            var set = project ? project.shapeGroups : null;
            var field = UI<UnityEditor.UIElements.ObjectField>("shape-group-set");
            field.SetValueWithoutNotify(set); field.SetEnabled(project);
            UI<Button>("shape-group-edit").SetEnabled(project);
            int others = set ? RimeEmoShapeGroups.Users(set).Count(p => p != project) : 0;
            string root = project && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(project)) ? RimeEmoAssets.Root(project) : "";
            bool own = set && System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(set)).Replace('\\', '/') == root;
            UI<Button>("shape-group-copy").SetEnabled(set && (others > 0 || !own));
            string text = "";
            if (project && !set) text = RimeEmoLocalization.T("グループはまだありません。表情の編集画面で、部位チップの右の＋から作れます。別のアバターのグループを使うには、上の欄にShapeGroupsファイルを指定します。");
            else if (set)
            {
                text = string.Format(RimeEmoLocalization.T("{0}個のグループ"), set.groups.Count);
                if (!string.IsNullOrEmpty(set.baseModel)) text += " · " + RimeEmoLocalization.T("対象の素体: ") + set.baseModel;
                if (!string.IsNullOrEmpty(set.author)) text += " · " + RimeEmoLocalization.T("作者: ") + set.author;
                if (others > 0) text += "\n" + string.Format(RimeEmoLocalization.T("他の{0}個の設定でも使用中です。変更はすべてに反映されます。"), others);
            }
            UI<Label>("shape-group-usage").text = text;
        }

        // After a new setup: offer groups made for another avatar whose Shape Key names fit this one.
        private void SuggestShapeGroups()
        {
            if (!project || project.shapeGroups || Application.isBatchMode) return;
            var match = RimeEmoShapeGroups.FindMatch(project, out float score);
            if (!match) return;
            string info = string.Format(RimeEmoLocalization.T("{0}個のグループ"), match.groups.Count)
                + (string.IsNullOrEmpty(match.baseModel) ? "" : " · " + RimeEmoLocalization.T("対象の素体: ") + match.baseModel)
                + (string.IsNullOrEmpty(match.author) ? "" : " · " + RimeEmoLocalization.T("作者: ") + match.author);
            int choice = EditorUtility.DisplayDialogComplex("RimeEmo",
                string.Format(RimeEmoLocalization.T("Shape Key名が共通するグループが見つかりました（一致率 {0:0}%）。\n{1}\n{2}\n\nこのアバターでも使いますか？\n複製: このアバター専用のコピーを作ります。\n共有: 同じファイルを使い、変更が両方に反映されます。"),
                    score * 100, AssetDatabase.GetAssetPath(match), info),
                RimeEmoLocalization.T("複製して使う"), RimeEmoLocalization.T("使わない"), RimeEmoLocalization.T("共有して使う"));
            if (choice == 0) LinkShapeGroups(RimeEmoShapeGroups.Duplicate(project, match));
            else if (choice == 2) LinkShapeGroups(match);
        }

        // Inline name field in place of a chip: Enter or OK commits, Esc or × cancels.
        private void ShowShapeGroupNameEditor(VisualElement anchor, string initial, string option, Action<string, bool> done)
        {
            if (anchor == null || anchor.parent == null) return;
            var host = anchor.parent;
            host.Q<VisualElement>("shape-group-editor")?.RemoveFromHierarchy();
            foreach (var hidden in host.Children().Where(c => c.style.display == DisplayStyle.None).ToList()) hidden.style.display = DisplayStyle.Flex;
            var editor = new VisualElement { name = "shape-group-editor" }; editor.AddToClassList("re-group-editor");
            var field = new TextField { value = initial, name = "shape-group-name" }; field.AddToClassList("re-group-name");
            editor.Add(field);
            Toggle toggle = null;
            if (option != null) { toggle = new Toggle(option) { value = true }; toggle.AddToClassList("re-group-option"); editor.Add(toggle); }
            void Close() { editor.RemoveFromHierarchy(); anchor.style.display = DisplayStyle.Flex; }
            void Commit()
            {
                string name = field.value == null ? "" : field.value.Trim();
                if (name.Length == 0) return;
                bool use = toggle != null && toggle.value;
                Close(); done(name, use);
            }
            var ok = new Button(Commit) { text = "OK" }; ok.AddToClassList("re-button"); ok.AddToClassList("re-button--tonal"); editor.Add(ok);
            var cancel = new Button(Close) { text = "×", tooltip = RimeEmoLocalization.T("キャンセル") }; cancel.AddToClassList("re-button"); cancel.AddToClassList("re-button--icon"); editor.Add(cancel);
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Commit(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            host.Insert(host.IndexOf(anchor), editor);
            anchor.style.display = DisplayStyle.None;
            field.schedule.Execute(() => { field.Focus(); field.SelectAll(); });
        }
    }
}
