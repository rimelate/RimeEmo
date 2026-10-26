using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // What the single Shape Key list shows, relative to the clip being edited.
    internal enum ShapeState { All, Included, Changed, Unset }

    // Filters of the expression editor: part chips, clip state, search and hide rules.
    public sealed partial class RimeEmoWindow
    {
        [SerializeField] private ShapeState shapeState;
        private readonly int[] shapeStateCounts = new int[4];
        private bool rowsDefaultFace;
        private int hiddenShapeCount;
        // Rows edited under the current filter stay listed, so a row never vanishes in the middle of an edit.
        private readonly HashSet<EditorCurveBinding> keptShapes = new HashSet<EditorCurveBinding>();

        private static readonly string[] ShapeStateNames = { "すべて", "含まれる", "Defaultと違う", "含まれない" };
        private static readonly string[] ShapeStateTips =
        {
            "顔メッシュのすべてのShape Key",
            "このClipにカーブがあるShape Key",
            "このClipで値がDefault Faceと違う、または時間で変化するShape Key",
            "このClipにカーブがないShape Key。値を動かすと追加します",
        };

        private const string HideVrcPref = "RimeEmo.HideVrcShapes", HideSeparatorPref = "RimeEmo.HideSeparatorShapes";
        internal static bool HideVrcShapes { get => EditorPrefs.GetBool(HideVrcPref, false); set => EditorPrefs.SetBool(HideVrcPref, value); }
        internal static bool HideSeparatorShapes { get => EditorPrefs.GetBool(HideSeparatorPref, true); set => EditorPrefs.SetBool(HideSeparatorPref, value); }

        // Lip sync, blink and eye look keys that VRChat drives itself.
        internal static bool IsVrcShape(string name) => name.StartsWith("vrc.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("vrc_", StringComparison.OrdinalIgnoreCase);
        // Dummy keys that only label a section of the mesh, e.g. "-----EyeLook-----".
        private static readonly Regex Separator = new Regex(@"^[\s\-=_~*#.・─━]{3,}|[\s\-=_~*#.・─━]{3,}$|^[^\p{L}\p{N}]*$");
        internal static bool IsSeparatorShape(string name) => Separator.IsMatch(name);

        private bool MatchesPartAndSearch(ShapeEntry shape) => (category == 0 || (int)shape.part == category - 1) && InSelectedGroup(shape)
            && (string.IsNullOrEmpty(search) || shape.shape.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool InState(ShapeRow row, ShapeState state)
            => state == ShapeState.All || (state == ShapeState.Included ? row.included : state == ShapeState.Changed ? row.Changed : !row.included);

        private void EnsureRows()
        {
            bool editingDefault = EditingDefaultFace;
            if (!rowsDirty && rowsClip == selected && rowsDefaultFace == editingDefault) return;
            if (rowsClip != selected || rowsDefaultFace != editingDefault) keptShapes.Clear();
            rowsDirty = false; rowsClip = selected; rowsDefaultFace = editingDefault;
            shapeRows.Clear(); Array.Clear(shapeStateCounts, 0, shapeStateCounts.Length); hiddenShapeCount = 0;
            bool hideVrc = HideVrcShapes, hideSeparators = HideSeparatorShapes;
            foreach (var shape in RimeEmoFace.Shapes(project))
            {
                if (!MatchesPartAndSearch(shape)) continue;
                var binding = RimeEmoClips.Binding(shape);
                var curve = selected ? AnimationUtility.GetEditorCurve(selected, binding) : null;
                // Default Face holds every Shape Key at its value; hide rules still thin out separators and the like.
                var row = editingDefault
                    ? new ShapeRow { shape = shape, binding = binding, included = true, value = shape.defaultValue, constant = true }
                    : new ShapeRow { shape = shape, binding = binding, included = curve != null, value = curve == null ? shape.defaultValue : curve.Evaluate(0) };
                if (!editingDefault) row.constant = curve == null || RimeEmoClips.IsConstant(curve, row.value);
                // Hide rules only thin out candidates; whatever the clip contains is always listed.
                if ((!row.included || editingDefault) && !keptShapes.Contains(binding)
                    && ((hideVrc && IsVrcShape(shape.shape)) || (hideSeparators && IsSeparatorShape(shape.shape)))) { hiddenShapeCount++; continue; }
                for (int s = 0; s < shapeStateCounts.Length; s++) if (InState(row, (ShapeState)s)) shapeStateCounts[s]++;
                if (InState(row, shapeState) || keptShapes.Contains(binding)) shapeRows.Add(row);
            }
        }

        private void BindShapeFilters()
        {
            shapeGroupSignature = null; // A rebuilt layout (e.g. language switch) starts with an empty group row.
            var host = UI<VisualElement>("shape-state");
            host.Clear();
            for (int i = 0; i < ShapeStateNames.Length; i++)
            {
                var state = (ShapeState)i;
                var button = new Button(() => SelectShapeState(state)) { name = "shape-state-" + i, tooltip = RimeEmoLocalization.T(ShapeStateTips[i]) };
                button.AddToClassList("re-segment");
                host.Add(button);
            }
            UI<Button>("shape-filter-menu").clicked += ShowShapeFilterMenu;
        }

        // Any change of what is listed starts a fresh view: rows kept from earlier edits are released.
        private void ResetShapeView() { keptShapes.Clear(); rowsDirty = true; }

        private void SelectShapeState(ShapeState state)
        {
            shapeState = state;
            ResetShapeView();
            RefreshStaticToolkit();
        }

        private void ShowShapeFilterMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(RimeEmoLocalization.T("区切り用のShape Keyを候補から隠す")), HideSeparatorShapes, () => { HideSeparatorShapes = !HideSeparatorShapes; ResetShapeView(); RefreshStaticToolkit(); });
            menu.AddItem(new GUIContent(RimeEmoLocalization.T("VRChat用（vrc.）を候補から隠す")), HideVrcShapes, () => { HideVrcShapes = !HideVrcShapes; ResetShapeView(); RefreshStaticToolkit(); });
            string word = (search ?? "").Trim();
            if (word.Length > 0)
            {
                menu.AddSeparator("");
                // The search word becomes the new group's name pattern, so no rule has to be written by hand.
                menu.AddItem(new GUIContent(string.Format(RimeEmoLocalization.T("「{0}」を含むShape Keyでグループを作る"), RimeEmoLocalization.MenuText(word)) + "…"), false, () => CreateShapeGroup(null));
            }
            menu.AddSeparator("");
            menu.AddDisabledItem(new GUIContent(RimeEmoLocalization.T("このClipに含まれるShape Keyは常に表示します")));
            menu.DropDown(UI<Button>("shape-filter-menu").worldBound);
        }

        private void RefreshShapeFilters()
        {
            for (int i = 0; i < ShapeStateNames.Length; i++)
            {
                var button = UI<Button>("shape-state-" + i);
                if (button == null) continue;
                button.text = RimeEmoLocalization.T(ShapeStateNames[i]) + "  " + shapeStateCounts[i];
                button.EnableInClassList("is-active", (int)shapeState == i);
            }
            var hidden = UI<Label>("shape-hidden");
            hidden.text = hiddenShapeCount > 0 ? string.Format(RimeEmoLocalization.T("{0}件を候補から隠しています"), hiddenShapeCount) : "";
            hidden.style.display = hiddenShapeCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
