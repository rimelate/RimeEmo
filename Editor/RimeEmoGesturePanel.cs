using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace RimeEmo.Editor
{
    // Gesture assignment and FX generation UI. Hosted by the main window's Gesture page and by RimeEmoGestureWindow.
    internal sealed class RimeEmoGesturePanel
    {
        // Plain text with VRChat's gesture numbers: the editor font does not reliably render emoji.
        private static readonly List<string> Hands = new List<string> { "ANY", "0 Neutral", "1 Fist", "2 HandOpen", "3 FingerPoint", "4 Victory", "5 RockNRoll", "6 HandGun", "7 ThumbsUp" };

        private readonly Func<RimeEmoProject> getProject;
        private readonly Action<RimeEmoProject> setProject;
        private readonly Func<GameObject> getAvatar;
        private readonly Action<GameObject> setAvatar;
        private readonly bool embedded;
        private VisualElement root;
        private bool dirty;
        private string status = "";

        internal AnimationClip InitialClip;
        internal RimeEmoMotion InitialMotion;
        // Raised after assignments change so the host can refresh labels that show them.
        internal event Action Edited;

        private RimeEmoProject Project => getProject();
        private GameObject Avatar => getAvatar();

        internal RimeEmoGesturePanel(Func<RimeEmoProject> getProject, Action<RimeEmoProject> setProject, Func<GameObject> getAvatar, Action<GameObject> setAvatar, bool embedded)
        {
            this.getProject = getProject; this.setProject = setProject; this.getAvatar = getAvatar; this.setAvatar = setAvatar; this.embedded = embedded;
        }

        private T UI<T>(string name) where T : VisualElement => root.Q<T>(name);

        internal void MarkDirty() => dirty = true;
        internal void Tick() { if (dirty && root != null && root.panel != null) Refresh(); }
        internal void ClearStatus() => status = "";

        internal void Build(VisualElement host, VisualTreeAsset tree, string uiFolder)
        {
            host.Clear();
            tree.CloneTree(host);
            root = host.Q<VisualElement>("gesture-root") ?? host;
            root.style.flexGrow = 1;
            RimeEmoLocalization.TranslateLayout(root);
            RimeEmoIcons.Apply(root, uiFolder);
            if (embedded)
            {
                // The main window already selects the project and avatar, and its Save covers assignments.
                root.AddToClassList("re-embedded");
                UI<VisualElement>("gesture-targets").style.display = DisplayStyle.None;
                UI<Button>("rules-save").style.display = DisplayStyle.None;
            }
            var projectField = UI<ObjectField>("gesture-project"); projectField.objectType = typeof(RimeEmoProject); projectField.allowSceneObjects = false;
            projectField.RegisterValueChangedCallback(e => { Run(() => setProject(e.newValue as RimeEmoProject)); projectField.SetValueWithoutNotify(Project); });
            var avatarField = UI<ObjectField>("gesture-avatar"); avatarField.objectType = typeof(GameObject); avatarField.allowSceneObjects = true;
            avatarField.RegisterValueChangedCallback(e => { Run(() => setAvatar(e.newValue as GameObject)); avatarField.SetValueWithoutNotify(Avatar); });
            UI<Button>("rule-add").clicked += () => Edit(() =>
            {
                var project = Project;
                bool known = InitialClip && (project.clips.Any(c => c.clip == InitialClip) || project.animations.Any(c => c.clip == InitialClip) || RimeEmoDynamicClips.Find(project, InitialClip));
                project.gestureRules.Add(new GestureRule
                {
                    left = 1, right = 1,
                    clip = known ? InitialClip : !InitialMotion ? project.clips.FirstOrDefault()?.clip : null,
                    motion = InitialMotion && InitialMotion.project == project ? InitialMotion : null
                });
            });
            UI<Button>("rules-import").clicked += () => Edit(ImportRules);
            UI<Button>("priority-left").clicked += () => SetPriority(GesturePriority.Left);
            UI<Button>("priority-right").clicked += () => SetPriority(GesturePriority.Right);
            UI<Button>("priority-manual").clicked += () => SetPriority(GesturePriority.Manual);
            UI<Button>("rules-save").clicked += AssetDatabase.SaveAssets;
            UI<FloatField>("gesture-blend").isDelayed = true;
            UI<Toggle>("source-gesture-override").RegisterValueChangedCallback(e => Edit(() => Project.overrideSourceGestureFaces = e.newValue));
            UI<Toggle>("expression-menu").RegisterValueChangedCallback(e => Edit(() => Project.expressionMenu = e.newValue));
            UI<FloatField>("gesture-blend").RegisterValueChangedCallback(e => Edit(() => Project.gestureBlendSeconds = Mathf.Clamp(float.IsNaN(e.newValue) || float.IsInfinity(e.newValue) ? 0 : e.newValue, 0, 10)));
            UI<Button>("fx-inspect").clicked += () => { var project = Project; if (project) { Selection.activeObject = project.generatedFX; EditorGUIUtility.PingObject(project.generatedFX); } };
            UI<Button>("fx-apply").clicked += () => Run(() => { RimeEmoApply.Apply(Project, Avatar); status = RimeEmoLocalization.T("アバターに適用しました。Sceneを保存してください。"); });
            UI<Button>("fx-remove").clicked += () => Run(() => { RimeEmoApply.Remove(Project, Avatar); status = RimeEmoLocalization.T("適用を解除しました。元FXは変更されていません。"); });
            Refresh();
        }

        private void Run(Action action)
        { try { action(); } catch (Exception e) { status = e.Message; Debug.LogException(e); } dirty = true; }

        private void Edit(Action action)
        {
            var project = Project;
            if (!project) return;
            Run(() => { Undo.RecordObject(project, "RimeEmo: Gesture Assignment"); action(); EditorUtility.SetDirty(project); });
            Edited?.Invoke();
        }

        private Button IconButton(string symbol, string fallback, string tooltip, Action action)
        {
            var button = new Button(action) { tooltip = tooltip };
            button.AddToClassList("re-button"); button.AddToClassList("re-button--icon"); button.AddToClassList("re-has-icon");
            button.Add(RimeEmoIcons.Glyph(symbol, fallback));
            return button;
        }

        private static Label Hint(string text) { var label = new Label(text); label.AddToClassList("re-hint"); return label; }

        internal void Refresh()
        {
            if (root == null) return;
            dirty = false;
            var project = Project; var avatar = Avatar;
            UI<ObjectField>("gesture-project").SetValueWithoutNotify(project); UI<ObjectField>("gesture-avatar").SetValueWithoutNotify(avatar);
            UI<FloatField>("gesture-blend").SetValueWithoutNotify(project ? project.gestureBlendSeconds : 0.1f);
            UI<Toggle>("expression-menu").SetValueWithoutNotify(project && project.expressionMenu); UI<Toggle>("expression-menu").SetEnabled(project);
            UI<Button>("rule-add").SetEnabled(project); UI<Button>("rules-import").SetEnabled(project && project.sourceFX is AnimatorController);
            UI<Button>("fx-inspect").SetEnabled(project && project.generatedFX);
            UI<Button>("fx-remove").SetEnabled(project && avatar && RimeEmoApply.FindApplied(project, avatar));
            UI<Label>("build-summary").text = "";
            var targets = project ? Targets(project) : new List<Object>();
            BuildHand(UI<VisualElement>("hand-left"), true, project);
            BuildHand(UI<VisualElement>("hand-right"), false, project);
            RefreshPriority(project);
            BuildMatrix(project);
            BuildCombinations(project);
            var conflicts = UI<VisualElement>("conflicts"); conflicts.Clear();
            if (project)
            {
                int defaults = 0, unresolved = 0;
                for (int l = 0; l < 8; l++) for (int r = 0; r < 8; r++)
                {
                    int left = l, right = r;
                    var choice = project.gestureChoices.FirstOrDefault(c => c.left == l && c.right == r);
                    bool resolved = RimeEmoAssignments.TryResolve(project, l, r, out var result);
                    if (!resolved) unresolved++; else if (result == null) defaults++;
                    // Only combinations that need or have an individual choice are listed.
                    if (choice == null && resolved) continue;
                    var row = new VisualElement(); row.AddToClassList("re-gesture-rule");
                    row.Add(new Label("L " + RimeEmoGestures.Names[l] + " / R " + RimeEmoGestures.Names[r]) { style = { width = 230 } });
                    var labels = new List<string> { RimeEmoLocalization.T(project.gesturePriority == GesturePriority.Manual ? "未解決" : "自動（割り当てと優先設定に従う）"), "Default Face" };
                    labels.AddRange(targets.Select((t, i) => (i + 1) + ". " + t.name));
                    int targetIndex = choice == null ? -1 : targets.IndexOf(choice.motion ? (Object)RimeEmoDynamicClips.EnsureClip(choice.motion) : choice.clip);
                    int selected = choice == null ? 0 : choice.useDefault ? 1 : targetIndex < 0 ? 0 : targetIndex + 2;
                    var selection = new DropdownField(labels, Mathf.Clamp(selected, 0, labels.Count - 1)); selection.style.flexGrow = 1;
                    selection.RegisterValueChangedCallback(e => SetChoice(left, right, selection.index <= 1 ? null : targets[selection.index - 2], selection.index == 1, selection.index == 0));
                    row.Add(selection); conflicts.Add(row);
                }
                if (conflicts.childCount == 0) conflicts.Add(Hint(RimeEmoLocalization.T("個別に設定した組み合わせはありません。")));
                UI<Label>("build-summary").text = RimeEmoLocalization.T("64組み合わせ中: 未解決 ") + unresolved + " / Default Face " + defaults + RimeEmoLocalization.T("\n制御する顔メッシュ: ") + RimeEmoFace.Path(project);
            }
            var buildErrors = RimeEmoBuild.Validate(project); var applyErrors = RimeEmoApply.Validate(project, avatar);
            UI<Button>("fx-apply").SetEnabled(applyErrors.Count == 0);
            UI<Label>("build-errors").text = string.Join("\n", buildErrors.Concat(applyErrors).Distinct().Take(20));
            RefreshSourceGestures(project);
            var state = RimeEmoBuildState.Describe(project, avatar);
            var applyState = UI<Label>("apply-state");
            applyState.text = RimeEmoLocalization.T(state == RimeEmoBuildState.State.Stale ? "前回の適用後に変更があります。「再生成して適用」で反映します。"
                : state == RimeEmoBuildState.State.UpToDate ? "アバターには最新の内容が適用されています。"
                : state == RimeEmoBuildState.State.NotApplied ? "まだアバターに適用していません。" : "");
            applyState.style.display = string.IsNullOrEmpty(applyState.text) ? DisplayStyle.None : DisplayStyle.Flex;
            // Stale reads as a banner; the other states stay quiet hints.
            applyState.EnableInClassList("re-banner", state == RimeEmoBuildState.State.Stale);
            applyState.EnableInClassList("re-hint", state != RimeEmoBuildState.State.Stale);
            UI<Label>("gesture-status").text = status;
            // An empty label still takes a line of height; together they left a blank band under the apply buttons.
            foreach (string id in new[] { "gesture-status", "build-errors", "build-summary" })
            {
                var label = UI<Label>(id);
                label.style.display = string.IsNullOrEmpty(label.text) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        // RimeEmo writes only the Shape Keys its expressions use. When the source FX's own hand-gesture layers move
        // others, say so, and offer to silence them instead of overriding them silently.
        private void RefreshSourceGestures(RimeEmoProject project)
        {
            var hint = UI<Label>("source-gesture-hint"); var toggle = UI<Toggle>("source-gesture-override");
            List<string> leaking;
            try { leaking = project ? RimeEmoBuild.LeakingShapes(project) : new List<string>(); }
            catch (Exception e) { Debug.LogException(e); leaking = new List<string>(); }
            bool on = project && project.overrideSourceGestureFaces;
            bool shown = leaking.Count > 0 || on;
            hint.style.display = toggle.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shown) return;
            hint.text = string.Format(RimeEmoLocalization.T(on
                ? "元FXの手の形のレイヤーが動かすShape Key {0}個もRimeEmoが書き込み、元の手の表情を止めています。"
                : "元FXの手の形で切り替わるレイヤーが、RimeEmoの表情で使っていないShape Keyを{0}個動かしています。手の形によっては、元の表情が混ざって見えます。"), leaking.Count);
            hint.tooltip = string.Join("\n", leaking.Take(30)) + (leaking.Count > 30 ? "\n…" : "");
            toggle.SetValueWithoutNotify(on);
        }

        private static List<Object> Targets(RimeEmoProject project)
        {
            var targets = new List<Object>();
            targets.AddRange(project.clips.Where(c => c.clip).Select(c => (Object)c.clip));
            targets.AddRange(project.animations.Where(c => c.clip).Select(c => (Object)c.clip));
            targets.AddRange(project.motions.Where(m => m).Select(m => (Object)RimeEmoDynamicClips.EnsureClip(m)).Where(c => c));
            return targets.Distinct().ToList();
        }

        private static bool IsSingle(GestureRule rule, bool leftHand, int gesture)
            => rule != null && (leftHand ? rule.left == gesture && rule.right < 0 : rule.right == gesture && rule.left < 0);

        // One row per hand shape: drop a clip to assign, × to clear. Stored as one-hand rules (other hand = ANY).
        private void BuildHand(VisualElement host, bool leftHand, RimeEmoProject project)
        {
            host.Clear();
            for (int g = 0; g < 8; g++)
            {
                int gesture = g;
                var row = new VisualElement(); row.AddToClassList("re-hand-row");
                var name = new Label(Hands[gesture + 1]); name.AddToClassList("re-hand-gesture"); row.Add(name);
                var rule = project ? project.gestureRules.FirstOrDefault(r => IsSingle(r, leftHand, gesture)) : null;
                var field = TargetField(rule?.clip, rule?.motion, (clip, motion) => SetHand(leftHand, gesture, clip, motion));
                field.SetEnabled(project);
                row.Add(field);
                var clear = IconButton("close", "×", RimeEmoLocalization.T("割り当てを外す"), () => SetHand(leftHand, gesture, null, null));
                clear.SetEnabled(rule != null);
                row.Add(clear);
                host.Add(row);
            }
        }

        private void SetHand(bool leftHand, int gesture, AnimationClip clip, RimeEmoMotion motion) => Edit(() =>
        {
            var project = Project;
            if (!clip && !motion) { project.gestureRules.RemoveAll(r => IsSingle(r, leftHand, gesture)); return; }
            var rule = project.gestureRules.FirstOrDefault(r => IsSingle(r, leftHand, gesture));
            if (rule == null) project.gestureRules.Add(new GestureRule { left = leftHand ? gesture : -1, right = leftHand ? -1 : gesture, clip = clip, motion = motion });
            else { rule.clip = clip; rule.motion = motion; }
        });

        private void RefreshPriority(RimeEmoProject project)
        {
            var current = project ? project.gesturePriority : GesturePriority.Left;
            foreach (var pair in new[] { ("priority-left", GesturePriority.Left), ("priority-right", GesturePriority.Right), ("priority-manual", GesturePriority.Manual) })
            {
                var button = UI<Button>(pair.Item1);
                button.EnableInClassList("is-active", current == pair.Item2);
                button.SetEnabled(project);
            }
            UI<Label>("priority-hint").text = RimeEmoLocalization.T(current == GesturePriority.Left
                ? "左手の表情を使います。左手がNeutralのときは右手の表情を使います。"
                : current == GesturePriority.Right
                    ? "右手の表情を使います。右手がNeutralのときは左手の表情を使います。"
                    : "両手の割り当てが重なる組み合わせを、表で1つずつ選びます。選ぶまでFXを生成できません。");
        }

        private void SetPriority(GesturePriority priority) => Edit(() => Project.gesturePriority = priority);

        // 8×8 table of the final result. Colour shows where each result comes from.
        private void BuildMatrix(RimeEmoProject project)
        {
            var host = UI<VisualElement>("gesture-matrix"); host.Clear();
            var header = new VisualElement(); header.AddToClassList("re-matrix-row");
            header.Add(MatrixLabel("L ＼ R", "re-matrix-corner"));
            for (int r = 0; r < 8; r++) header.Add(MatrixLabel(Hands[r + 1], "re-matrix-head"));
            host.Add(header);
            for (int l = 0; l < 8; l++)
            {
                var row = new VisualElement(); row.AddToClassList("re-matrix-row");
                row.Add(MatrixLabel(Hands[l + 1], "re-matrix-head"));
                for (int r = 0; r < 8; r++)
                {
                    int left = l, right = r;
                    GestureRule result = null;
                    var source = project ? RimeEmoAssignments.Describe(project, l, r, out result) : RimeEmoAssignments.Source.Default;
                    string text = source == RimeEmoAssignments.Source.Unresolved ? RimeEmoLocalization.T("未解決")
                        : result == null ? (source == RimeEmoAssignments.Source.Choice ? "Default Face" : "—") : TargetName(result);
                    var cell = new Button { text = text };
                    cell.tooltip = "L " + RimeEmoGestures.Names[l] + " / R " + RimeEmoGestures.Names[r] + "\n" + (result == null && source == RimeEmoAssignments.Source.Default ? "Default Face" : text) + "\n" + SourceLabel(source);
                    cell.clicked += () => ShowCellMenu(cell, left, right);
                    cell.AddToClassList("re-matrix-cell"); cell.AddToClassList("re-cell--" + source.ToString().ToLowerInvariant());
                    cell.SetEnabled(project);
                    row.Add(cell);
                }
                host.Add(row);
            }
            var legend = UI<VisualElement>("gesture-legend"); legend.Clear();
            foreach (RimeEmoAssignments.Source source in Enum.GetValues(typeof(RimeEmoAssignments.Source)))
            {
                var item = new Label(SourceLabel(source)); item.AddToClassList("re-legend-item"); item.AddToClassList("re-cell--" + source.ToString().ToLowerInvariant());
                legend.Add(item);
            }
        }

        private static Label MatrixLabel(string text, string cls) { var label = new Label(text); label.AddToClassList(cls); return label; }

        private static string TargetName(GestureRule rule)
            => rule.motion ? (rule.motion.animationClip ? rule.motion.animationClip.name : rule.motion.name) : rule.clip ? rule.clip.name
                : RimeEmoAssignments.Missing(rule.clip, rule.motion) ? RimeEmoLocalization.T("見つかりません") : "?";

        private static string SourceLabel(RimeEmoAssignments.Source source)
        {
            switch (source)
            {
                case RimeEmoAssignments.Source.Left: return RimeEmoLocalization.T("左手の割り当て");
                case RimeEmoAssignments.Source.Right: return RimeEmoLocalization.T("右手の割り当て");
                case RimeEmoAssignments.Source.Both: return RimeEmoLocalization.T("両手の組み合わせ");
                case RimeEmoAssignments.Source.Choice: return RimeEmoLocalization.T("個別に設定");
                case RimeEmoAssignments.Source.Unresolved: return RimeEmoLocalization.T("未解決");
                default: return "Default Face";
            }
        }

        private void ShowCellMenu(VisualElement cell, int left, int right)
        {
            var project = Project;
            if (!project) return;
            var choice = project.gestureChoices.FirstOrDefault(c => c.left == left && c.right == right);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(RimeEmoLocalization.T("自動（割り当てと優先設定に従う）")), choice == null, () => SetChoice(left, right, null, false, true));
            menu.AddItem(new GUIContent("Default Face"), choice != null && choice.useDefault, () => SetChoice(left, right, null, true, false));
            menu.AddSeparator("");
            foreach (var target in Targets(project))
            {
                var item = target;
                bool on = choice != null && !choice.useDefault && (choice.clip == item || (choice.motion && choice.motion.animationClip == item));
                menu.AddItem(new GUIContent(RimeEmoLocalization.MenuText(item.name)), on, () => SetChoice(left, right, item, false, false));
            }
            menu.DropDown(cell.worldBound);
        }

        private void SetChoice(int left, int right, Object target, bool useDefault, bool automatic) => Edit(() =>
        {
            var project = Project;
            project.gestureChoices.RemoveAll(c => c.left == left && c.right == right);
            if (automatic || (!useDefault && !target)) return;
            project.gestureChoices.Add(new GestureChoice { left = left, right = right, useDefault = useDefault, clip = target as AnimationClip, motion = target as RimeEmoMotion });
        });

        // Advanced: rules that name both hands (or neither). One-hand rules live in the hand lists above.
        private void BuildCombinations(RimeEmoProject project)
        {
            var rules = UI<VisualElement>("rules"); rules.Clear();
            if (!project) return;
            foreach (var rule in project.gestureRules.Where(r => r != null && (r.left >= 0) == (r.right >= 0)))
            {
                var row = new VisualElement(); row.AddToClassList("re-gesture-rule");
                var left = new DropdownField("LEFT", Hands, Mathf.Clamp(rule.left + 1, 0, 8)); left.AddToClassList("re-gesture-hand");
                var right = new DropdownField("RIGHT", Hands, Mathf.Clamp(rule.right + 1, 0, 8)); right.AddToClassList("re-gesture-hand");
                left.RegisterValueChangedCallback(e => Edit(() => rule.left = left.index - 1)); right.RegisterValueChangedCallback(e => Edit(() => rule.right = right.index - 1));
                var target = TargetField(rule.clip, rule.motion, (clip, motion) => Edit(() => { rule.clip = clip; rule.motion = motion; }));
                row.Add(left); row.Add(right); row.Add(target);
                row.Add(IconButton("close", "×", RimeEmoLocalization.T("削除"), () => Edit(() => project.gestureRules.Remove(rule))));
                rules.Add(row);
            }
            if (rules.childCount == 0) rules.Add(Hint(RimeEmoLocalization.T("両手の組み合わせはありません。通常は不要です。")));
        }

        private ObjectField TargetField(AnimationClip clip, RimeEmoMotion motion, Action<AnimationClip, RimeEmoMotion> change)
        {
            var field = new ObjectField { objectType = typeof(AnimationClip), allowSceneObjects = false }; field.style.flexGrow = 1;
            field.SetValueWithoutNotify(motion ? RimeEmoDynamicClips.EnsureClip(motion) : clip);
            field.RegisterValueChangedCallback(e =>
            {
                if (e.newValue && !(e.newValue is AnimationClip) && !(e.newValue is RimeEmoMotion)) { field.SetValueWithoutNotify(e.previousValue); return; }
                var project = Project;
                var assigned = e.newValue as AnimationClip;
                // A clip from outside the project is copied into the materials so it can be built immediately.
                if (assigned && project && !project.clips.Any(c => c.clip == assigned) && !project.animations.Any(c => c.clip == assigned) && !RimeEmoDynamicClips.Find(project, assigned))
                {
                    AnimationClip imported = null; status = "";
                    Run(() => imported = RimeEmoAssets.Import(project, assigned));
                    if (!imported)
                    {
                        if (string.IsNullOrEmpty(status)) status = RimeEmoClipClassification.Rejection(project, assigned) ?? RimeEmoLocalization.T("対象の顔メッシュのBlendShapeがないため除外しました。");
                        field.SetValueWithoutNotify(e.previousValue); return;
                    }
                    status = string.Format(RimeEmoLocalization.T("「{0}」を素材に取り込んで割り当てました。"), imported.name);
                    assigned = imported; field.SetValueWithoutNotify(imported);
                }
                change(assigned, e.newValue as RimeEmoMotion);
            });
            return field;
        }

        private void ImportRules()
        {
            var project = Project;
            if (!(project.sourceFX is AnimatorController controller)) { status = RimeEmoLocalization.T("読込元FXがありません。設定ページで指定してください。"); return; }
            int added = 0, taken = 0, rejected = 0;
            var rules = RimeEmoGestures.SourceRules(controller, out int conditional);
            foreach (var rule in rules)
            {
                var clip = project.clips.Concat(project.animations).FirstOrDefault(c => c != null && c.clip && c.source == rule.clip)?.clip;
                // Not a material yet: copied in, as dropping it on a hand shape does.
                if (!clip) clip = RimeEmoAssets.Import(project, rule.clip);
                if (!clip) { rejected++; continue; }
                // A hand shape assigned already (by hand, or by an earlier import) is left as it is.
                if (project.gestureRules.Any(x => x != null && x.left == rule.left && x.right == rule.right)) { taken++; continue; }
                project.gestureRules.Add(new GestureRule { left = rule.left, right = rule.right, clip = clip }); added++;
            }
            status = string.Format(RimeEmoLocalization.T("{0}件取り込みました。"), added)
                + (taken > 0 ? string.Format(RimeEmoLocalization.T(" 割り当て済みの手の形{0}件はそのままにしました。"), taken) : "")
                + (rejected > 0 ? string.Format(RimeEmoLocalization.T(" 顔の表情ではないClip{0}件は除外しました。"), rejected) : "")
                + (conditional > 0 ? string.Format(RimeEmoLocalization.T(" ギミックをオンにしたときなど、初期状態では使われない遷移{0}件は対象外です。必要なら手動で割り当ててください。"), conditional) : "");
        }
    }
}
