using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    /// <summary>Edits the saved face shape classifications independently from the slider UI.</summary>
    public sealed class RimeEmoClassificationWindow : EditorWindow
    {
        [SerializeField] private RimeEmoProject project;
        private string search = "";
        private List<ShapeEntry> shapes = new List<ShapeEntry>();
        private ListView list;
        private Label countLabel;
        // 0: parts, 1: user groups.
        [SerializeField] private int tab;
        [SerializeField] private int groupIndex = -1;
        private const float PartRowHeight = 32;
        private VisualElement partsPage;
        private RimeEmoShapeGroupEditor groupEditor;
        // CreateGUI rebuilds the layout once the window is ready; a name focus requested before that waits for it.
        private bool guiCreated, focusNamePending;
        internal static event Action<RimeEmoProject> ClassificationChanged;

        public static void Open(RimeEmoProject project) => Open(project, 0, -1);

        // An open window showing the main window's previous settings follows it to the next ones, so its edits
        // never land on an avatar that is no longer being edited.
        internal static void FollowProject(RimeEmoProject previous, RimeEmoProject next)
        {
            if (previous == next || !HasOpenInstances<RimeEmoClassificationWindow>()) return;
            foreach (var window in Resources.FindObjectsOfTypeAll<RimeEmoClassificationWindow>())
                if (window && window.project == previous) { window.groupIndex = -1; window.groupEditor?.Select(-1); window.SetProject(next); }
        }

        public static void OpenGroups(RimeEmoProject project, int group, bool focusName = false)
        {
            Open(project, 1, group);
            // A new group opens with its name selected, ready to type.
            if (!focusName) return;
            var window = GetWindow<RimeEmoClassificationWindow>();
            if (window.guiCreated) window.groupEditor?.FocusName(); else window.focusNamePending = true;
        }

        private static void Open(RimeEmoProject project, int tab, int group)
        {
            var window = GetWindow<RimeEmoClassificationWindow>(RimeEmoLocalization.T("部位とグループ"));
            window.EnsureUi();
            window.tab = tab;
            if (group >= 0) { window.groupIndex = group; window.groupEditor?.Select(group); }
            window.SetProject(project);
            window.Show();
        }

        // Built in CreateGUI, not OnEnable: right after a domain reload OnEnable runs before the
        // style sheets can be loaded, and the window then lays out without the theme.
        public void CreateGUI()
        {
            BuildUi();
            guiCreated = true;
            if (focusNamePending) { focusNamePending = false; groupEditor?.FocusName(); }
        }

        private void EnsureUi() { if (list == null) BuildUi(); }

        private void OnEnable()
        {
            minSize = new Vector2(420, 300);
            Undo.undoRedoPerformed += Refresh;
            RimeEmoLocalization.Changed += BuildUi;
            RimeEmoShapeGroups.Changed += GroupsChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Refresh;
            RimeEmoLocalization.Changed -= BuildUi;
            RimeEmoShapeGroups.Changed -= GroupsChanged;
        }

        private void GroupsChanged(RimeEmoShapeGroupSet set) { if (project && project.shapeGroups == set) Refresh(); }

        private void SetProject(RimeEmoProject value)
        {
            project = value;
            EnsureUi();
            rootVisualElement.Q<ObjectField>()?.SetValueWithoutNotify(project);
            Refresh();
        }

        private void BuildUi()
        {
            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("re-app");
            RimeEmoLocalization.MarkLanguage(rootVisualElement);
            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this))).Replace('\\', '/') + "/UI/";
            foreach (string file in new[] { "RimeEmoTheme.uss", "RimeEmoCustom.uss" })
            { var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(folder + file); if (sheet) rootVisualElement.styleSheets.Add(sheet); }
            rootVisualElement.style.paddingLeft = 8;
            rootVisualElement.style.paddingRight = 8;
            rootVisualElement.style.paddingTop = 8;
            rootVisualElement.style.paddingBottom = 8;

            // Plain rows instead of a Toolbar: the theme buttons are taller than a toolbar strip.
            var header = new VisualElement(); header.AddToClassList("re-row"); header.style.flexShrink = 0;
            var projectField = new ObjectField("Project")
            {
                objectType = typeof(RimeEmoProject),
                allowSceneObjects = false,
                value = project
            };
            projectField.AddToClassList("re-compact-field"); projectField.AddToClassList("re-grow");
            projectField.RegisterValueChangedCallback(evt => SetProject(evt.newValue as RimeEmoProject));
            header.Add(projectField);

            var save = new Button(() =>
            {
                if (project) AssetDatabase.SaveAssets();
            }) { text = RimeEmoLocalization.T("保存") };
            save.AddToClassList("re-button"); save.AddToClassList("re-button--text");
            header.Add(save);
            rootVisualElement.Add(header);
            var tabRow = new VisualElement(); tabRow.AddToClassList("re-row"); tabRow.style.flexShrink = 0;
            var tabs = new VisualElement { name = "classification-tabs" }; tabs.AddToClassList("re-segmented");
            foreach (var (label, index) in new[] { (RimeEmoLocalization.T("部位"), 0), (RimeEmoLocalization.T("グループ"), 1) })
            {
                var button = new Button(() => { tab = index; Refresh(); }) { name = "classification-tab-" + index, text = label };
                button.AddToClassList("re-segment");
                tabs.Add(button);
            }
            tabRow.Add(tabs);
            countLabel = new Label();
            countLabel.AddToClassList("re-hint");
            countLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            countLabel.style.flexGrow = 1;
            tabRow.Add(countLabel);
            rootVisualElement.Add(tabRow);
            // Same card and search field as the groups tab.
            partsPage = new VisualElement { name = "parts-page", style = { flexGrow = 1 } };
            partsPage.AddToClassList("re-card");
            rootVisualElement.Add(partsPage);
            var searchRow = new VisualElement(); searchRow.AddToClassList("re-row"); searchRow.style.flexShrink = 0;
            var searchField = new TextField(RimeEmoLocalization.T("検索")) { name = "part-search" };
            searchField.SetValueWithoutNotify(search ?? ""); // The word survives a rebuild (language switch); the list is still filtered by it.
            searchField.AddToClassList("re-search"); searchField.AddToClassList("re-compact-field"); searchField.AddToClassList("re-grow");
            searchField.RegisterValueChangedCallback(e => { search = e.newValue; Refresh(); });
            searchRow.Add(searchField);
            partsPage.Add(searchRow);

            // Row height must hold the theme's 28px dropdown, or the dropdowns overlap the next row.
            list = new ListView(shapes, PartRowHeight, MakeRow, BindRow)
            {
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                style = { flexGrow = 1 }
            };
            partsPage.Add(list);
            // The groups page comes after the parts list, so the first ListView stays the parts list.
            groupEditor = new RimeEmoShapeGroupEditor();
            rootVisualElement.Add(groupEditor);
            Refresh();
        }

        private static List<string> PartNames() => new List<string>
            { RimeEmoLocalization.T("左目"), RimeEmoLocalization.T("右目"), RimeEmoLocalization.T("眉"), RimeEmoLocalization.T("口"), RimeEmoLocalization.T("その他") };

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = PartRowHeight;
            row.style.paddingLeft = 4;
            row.style.paddingRight = 4;

            var name = new Label { name = "shape-name" };
            name.style.flexGrow = 1;
            name.style.overflow = Overflow.Hidden;
            row.Add(name);

            var state = new Label { name = "part-state" };
            state.AddToClassList("re-hint");
            state.style.width = 44;
            row.Add(state);

            // Localized part names instead of the raw enum (LeftEye, RightEye, ...).
            var field = new DropdownField(PartNames(), 0) { name = "part-field" };
            field.style.width = 110;
            field.style.marginTop = 0; field.style.marginBottom = 0;
            field.RegisterValueChangedCallback(evt =>
            {
                var entry = field.userData as ShapeEntry;
                int index = field.index;
                if (entry == null || project == null || index < 0 || (FacePart)index == entry.part) return;
                Undo.RecordObject(project, "RimeEmo: Classify Face Shape");
                entry.part = (FacePart)index;
                entry.classificationOverridden = true;
                EditorUtility.SetDirty(project);
                ClassificationChanged?.Invoke(project);
                list.RefreshItems();
            });
            row.Add(field);

            var automatic = new Button { name = "part-auto", text = RimeEmoLocalization.T("自動に戻す"), tooltip = RimeEmoLocalization.T("手動で指定した部位を解除し、名前からの自動判定に戻します。") };
            automatic.AddToClassList("re-button");
            automatic.AddToClassList("re-button--quiet");
            automatic.clicked += () =>
            {
                var entry = automatic.userData as ShapeEntry;
                if (entry == null || project == null || !entry.classificationOverridden) return;
                Undo.RecordObject(project, "RimeEmo: Reset Face Shape Classification");
                entry.classificationOverridden = false;
                entry.part = RimeEmoClips.Classify(entry.shape);
                EditorUtility.SetDirty(project);
                ClassificationChanged?.Invoke(project);
                list.RefreshItems();
            };
            row.Add(automatic);
            return row;
        }

        private void BindRow(VisualElement element, int index)
        {
            if (index < 0 || index >= shapes.Count) return;
            var entry = shapes[index];
            element.Q<Label>("shape-name").text = entry.shape;
            element.Q<Label>("shape-name").tooltip = entry.shape;
            element.Q<Label>("part-state").text = RimeEmoLocalization.T(entry.classificationOverridden ? "手動" : "自動");
            var field = element.Q<DropdownField>("part-field");
            field.userData = entry;
            field.SetValueWithoutNotify(field.choices[Mathf.Clamp((int)entry.part, 0, field.choices.Count - 1)]);
            var automatic = element.Q<Button>("part-auto");
            automatic.userData = entry;
            automatic.SetEnabled(entry.classificationOverridden);
        }

        private void Refresh()
        {
            if (list == null) return;
            for (int i = 0; i < 2; i++) rootVisualElement.Q<Button>("classification-tab-" + i)?.EnableInClassList("is-active", tab == i);
            partsPage.style.display = tab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            groupEditor.style.display = tab == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            countLabel.style.display = tab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            groupEditor.Bind(project, groupEditor.Selected >= 0 ? groupEditor.Selected : groupIndex);
            groupIndex = groupEditor.Selected;
            shapes = project ? RimeEmoFace.Shapes(project).Where(s => string.IsNullOrEmpty(search) || s.shape.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList() : new List<ShapeEntry>();
            list.itemsSource = shapes;
            list.bindItem = BindRow;
            list.Rebuild();
            countLabel.text = project ? string.Format(RimeEmoLocalization.T("{0} 個のShape Key"), shapes.Count) : RimeEmoLocalization.T("Projectが未指定です");
        }
    }
}
