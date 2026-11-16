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
        private static readonly string[] Hands = RimeEmoGestures.Names.Select((name, gesture) => gesture + " " + name).ToArray();

        private readonly Func<RimeEmoProject> getProject;
        private readonly Action<RimeEmoProject> setProject;
        private readonly Func<GameObject> getAvatar;
        private readonly Action<GameObject> setAvatar;
        private readonly bool embedded;
        private VisualElement root;
        private RimeEmoGestureHoverPreview hoverPreview;
        private bool dirty;
        private string status = "";

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
        internal void Tick()
        {
            if (dirty && root != null && root.panel != null) Refresh();
            hoverPreview?.Tick(Project, Avatar);
        }
        internal void ReleaseHoverPreview() => hoverPreview?.Release();
        internal void ClearStatus() => status = "";

        internal void Build(VisualElement host, VisualTreeAsset tree, string uiFolder)
        {
            ReleaseHoverPreview();
            host.Clear();
            tree.CloneTree(host);
            root = host.Q<VisualElement>("gesture-root") ?? host;
            root.style.flexGrow = 1;
            RimeEmoLocalization.TranslateLayout(root);
            RimeEmoIcons.Apply(root, uiFolder);
            hoverPreview = new RimeEmoGestureHoverPreview(root);
            BuildTouchGuide();
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
            UI<Button>("rules-import").clicked += () => Edit(ImportRules);
            UI<Button>("priority-left").clicked += () => SetPriority(GesturePriority.Left);
            UI<Button>("priority-right").clicked += () => SetPriority(GesturePriority.Right);
            UI<Button>("priority-manual").clicked += () => SetPriority(GesturePriority.Manual);
            UI<Button>("rules-save").clicked += AssetDatabase.SaveAssets;
            UI<FloatField>("gesture-blend").isDelayed = true;
            UI<Toggle>("source-gesture-override").RegisterValueChangedCallback(e => Edit(() => Project.overrideSourceGestureFaces = e.newValue));
            UI<Toggle>("expression-menu").RegisterValueChangedCallback(e => Edit(() => Project.expressionMenu = e.newValue));
            var applyMode = UI<DropdownField>("fx-apply-mode");
            applyMode.choices = FxApplyModeLabels();
            applyMode.RegisterValueChangedCallback(e => Edit(() => Project.fxApplyMode = (FxApplyMode)Mathf.Max(0, applyMode.choices.IndexOf(e.newValue))));
            var overwriteField = UI<ObjectField>("fx-overwrite-controller");
            overwriteField.objectType = typeof(RuntimeAnimatorController);
            overwriteField.allowSceneObjects = false;
            overwriteField.RegisterValueChangedCallback(e => Edit(() => Project.overwriteFX = e.newValue as RuntimeAnimatorController));
            UI<FloatField>("gesture-blend").RegisterValueChangedCallback(e => Edit(() => Project.gestureBlendSeconds = Mathf.Clamp(float.IsNaN(e.newValue) || float.IsInfinity(e.newValue) ? 0 : e.newValue, 0, 10)));
            UI<Button>("fx-inspect").clicked += () => { var project = Project; if (project) { var controller = RimeEmoApply.AppliedController(project, Avatar); if (!controller) controller = project.generatedFX; if (controller) { Selection.activeObject = controller; EditorGUIUtility.PingObject(controller); } } };
            UI<Button>("fx-apply").clicked += () => Run(() => { RimeEmoApply.Apply(Project, Avatar); status = RimeEmoLocalization.T("アバターに適用しました。Sceneを保存してください。"); });
            UI<Button>("fx-remove").clicked += () => Run(() => { RimeEmoApply.Remove(Project, Avatar); status = RimeEmoLocalization.T("適用を解除し、元のFX設定に戻しました。"); });
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

        private static List<string> FxApplyModeLabels() => new List<string>
        {
            RimeEmoLocalization.T("Modular Avatarで追加"),
            RimeEmoLocalization.T("新しいFXに置き換え"),
            RimeEmoLocalization.T("既存FXに追加して置き換え")
        };

        internal void Refresh()
        {
            if (root == null) return;
            hoverPreview.Hide();
            dirty = false;
            var project = Project; var avatar = Avatar;
            UI<ObjectField>("gesture-project").SetValueWithoutNotify(project); UI<ObjectField>("gesture-avatar").SetValueWithoutNotify(avatar);
            UI<FloatField>("gesture-blend").SetValueWithoutNotify(project ? project.gestureBlendSeconds : 0.1f);
            UI<Toggle>("expression-menu").SetValueWithoutNotify(project && project.expressionMenu); UI<Toggle>("expression-menu").SetEnabled(project);
            var mode = project ? project.fxApplyMode : FxApplyMode.ModularAvatarAppend;
            var modeField = UI<DropdownField>("fx-apply-mode");
            modeField.choices = FxApplyModeLabels();
            modeField.SetValueWithoutNotify(modeField.choices[Mathf.Clamp((int)mode, 0, modeField.choices.Count - 1)]);
            modeField.SetEnabled(project);
            var overwriteField = UI<ObjectField>("fx-overwrite-controller");
            overwriteField.SetValueWithoutNotify(project ? project.overwriteFX : null);
            overwriteField.style.display = mode == FxApplyMode.OverwriteFx ? DisplayStyle.Flex : DisplayStyle.None;
            overwriteField.SetEnabled(project);
            var modeHelp = mode == FxApplyMode.ModularAvatarAppend
                ? "Modular AvatarのMerge Animatorを使い、既存FXの上にRimeEmoのGestureレイヤーを追加します。Avatar DescriptorのFX設定は変更しません。"
                : mode == FxApplyMode.CreateFx
                    ? "RimeEmoのGestureレイヤー1枚だけのFXを作成し、Avatar Descriptorに割り当てます。"
                    : "指定したFXをコピーし、RimeEmoのGestureレイヤーを加えたFXをAvatar Descriptorに割り当てます。空欄ではアバターの現在のFXを使います。再適用時は元FXの最新内容からコピーします。";
            UI<Label>("fx-mode-help").text = RimeEmoLocalization.T(modeHelp);
            UI<Button>("rules-import").SetEnabled(project && project.sourceFX is AnimatorController);
            UI<Button>("fx-inspect").SetEnabled(project && (RimeEmoApply.AppliedController(project, avatar) || project.generatedFX));
            UI<Button>("fx-remove").SetEnabled(project && avatar && RimeEmoApply.HasApplied(project, avatar));
            UI<Label>("build-summary").text = "";
            var targets = project ? Targets(project) : new List<Object>();
            RefreshPriority(project);
            BuildMatrix(project);
            BuildCommonAssignments(project);
            var conflicts = UI<VisualElement>("conflicts"); conflicts.Clear();
            if (project)
            {
                int defaults = 0, unresolved = 0;
                for (int l = 0; l < 8; l++) for (int r = 0; r < 8; r++)
                {
                    int left = l, right = r;
                    bool resolved = RimeEmoAssignments.TryResolve(project, l, r, out var result);
                    if (!resolved) unresolved++; else if (result == null) defaults++;
                    var selection = PairSelection(project, left, right, targets, resolved);
                    if (selection.index == 0) continue;
                    var row = new VisualElement(); row.AddToClassList("re-gesture-rule");
                    row.Add(new Label(PairLabel(left, right)) { style = { width = 230 } });
                    var labels = new List<string> { RimeEmoLocalization.T("自動（割り当てと優先設定に従う）"), "Default Face" };
                    labels.AddRange(targets.Select((t, i) => (i + 1) + ". " + t.name));
                    if (selection.status != null) labels.Add(selection.status);
                    var field = new DropdownField(labels, selection.index) { style = { flexGrow = 1 } };
                    field.RegisterValueChangedCallback(e =>
                    {
                        int index = field.index;
                        if (index >= 0 && index < targets.Count + 2)
                            SetChoice(left, right, index < 2 ? null : targets[index - 2], index == 1);
                    });
                    if (resolved) hoverPreview.Bind(field, result?.clip, result?.motion, result == null);
                    row.Add(field); conflicts.Add(row);
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
            try { leaking = project ? RimeEmoBuild.LeakingShapes(project, Avatar) : new List<string>(); }
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
            targets.AddRange(project.motions.Where(m => m).Select(m => Target(null, m)));
            return targets.Distinct().ToList();
        }

        private static bool IsSingle(GestureRule rule, bool leftHand, int gesture)
            => rule != null && (leftHand ? rule.left == gesture && rule.right < 0 : rule.right == gesture && rule.left < 0);

        private void BuildTouchGuide()
        {
            var guide = UI<Foldout>("touch-guide");
            guide.text = RimeEmoLocalization.T("Quest 3 / Touch Plus 操作ガイド");
            var poses = new VisualElement { name = "touch-poses" };
            poses.AddToClassList("re-touch-poses");
            for (int gesture = 0; gesture < RimeEmoGestures.Names.Length; gesture++)
            {
                var pose = new VisualElement();
                pose.AddToClassList("re-touch-pose");
                pose.Add(new RimeEmoTouchGestureDiagram(gesture, true));
                var info = new VisualElement();
                info.AddToClassList("re-touch-pose-info");
                info.Add(new Label(Hands[gesture]));
                info.Add(Hint(RimeEmoTouchGestureDiagram.Describe(gesture)));
                pose.Add(info);
                poses.Add(pose);
            }
            guide.Add(poses);
            guide.Add(Hint(RimeEmoLocalization.T("VRChatの標準操作を掲載しています。入力の割り当てやハンドトラッキングによって操作が変わります。")));
            var source = new Button(() => Application.OpenURL("https://docs.vrchat.com/docs/touch"))
            {
                text = RimeEmoLocalization.T("VRChat公式の手の形ガイド")
            };
            source.AddToClassList("re-button");
            source.AddToClassList("re-button--text");
            source.AddToClassList("re-touch-source");
            guide.Add(source);
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

        private static string PairLabel(int left, int right) => "L " + RimeEmoGestures.Names[left] + " / R " + RimeEmoGestures.Names[right];

        // 8×8 table of the final result. Colour shows where each result comes from.
        private void BuildMatrix(RimeEmoProject project)
        {
            var host = UI<VisualElement>("gesture-matrix"); host.Clear();
            var header = new VisualElement(); header.AddToClassList("re-matrix-row");
            header.Add(MatrixLabel(RimeEmoLocalization.T("右手 → / 左手 ↓"), "re-matrix-corner"));
            for (int r = 0; r < 8; r++) header.Add(MatrixGesture(r, false, project));
            host.Add(header);
            for (int l = 0; l < 8; l++)
            {
                var row = new VisualElement(); row.AddToClassList("re-matrix-row");
                row.Add(MatrixGesture(l, true, project));
                for (int r = 0; r < 8; r++)
                {
                    int left = l, right = r;
                    GestureRule result = null;
                    var source = project ? RimeEmoAssignments.Describe(project, l, r, out result) : RimeEmoAssignments.Source.Default;
                    string text = source == RimeEmoAssignments.Source.Unresolved ? RimeEmoLocalization.T("未解決")
                        : result == null ? (source == RimeEmoAssignments.Source.Choice ? "Default Face" : "—") : TargetName(result);
                    var cell = new Button { text = text };
                    cell.tooltip = PairLabel(left, right) + "\n" + (result == null && source == RimeEmoAssignments.Source.Default ? "Default Face" : text) + "\n" + SourceLabel(source);
                    cell.clicked += () => ShowCellMenu(cell, left, right);
                    cell.AddToClassList("re-matrix-cell"); cell.AddToClassList(SourceClass(source));
                    cell.SetEnabled(project);
                    if (project && source != RimeEmoAssignments.Source.Unresolved)
                        hoverPreview.Bind(cell, result?.clip, result?.motion, result == null);
                    row.Add(cell);
                }
                host.Add(row);
            }
            var legend = UI<VisualElement>("gesture-legend"); legend.Clear();
            foreach (RimeEmoAssignments.Source source in Enum.GetValues(typeof(RimeEmoAssignments.Source)))
            {
                if (source == RimeEmoAssignments.Source.Both) continue;
                var item = new Label(SourceLabel(source)); item.AddToClassList("re-legend-item"); item.AddToClassList(SourceClass(source));
                legend.Add(item);
            }
        }

        private static Label MatrixLabel(string text, string cls) { var label = new Label(text); label.AddToClassList(cls); return label; }

        private VisualElement MatrixGesture(int gesture, bool leftHand, RimeEmoProject project)
        {
            var diagram = new RimeEmoTouchGestureDiagram(gesture, leftHand);
            var heading = new VisualElement { tooltip = diagram.tooltip };
            heading.AddToClassList("re-matrix-head");
            heading.AddToClassList(leftHand ? "re-matrix-side" : "re-matrix-top");
            var label = new VisualElement();
            label.AddToClassList("re-matrix-heading-label");
            label.Add(diagram);
            label.Add(MatrixLabel(Hands[gesture], "re-matrix-name"));
            heading.Add(label);

            var assignment = new VisualElement();
            assignment.AddToClassList("re-matrix-assignment");
            var rule = project ? project.gestureRules.FirstOrDefault(r => IsSingle(r, leftHand, gesture)) : null;
            var field = TargetField(rule?.clip, rule?.motion, (clip, motion) => SetHand(leftHand, gesture, clip, motion));
            field.name = (leftHand ? "hand-left-" : "hand-right-") + gesture;
            field.SetEnabled(project);
            assignment.Add(field);
            var clear = IconButton("close", "×", RimeEmoLocalization.T("割り当てを外す"), () => SetHand(leftHand, gesture, null, null));
            clear.AddToClassList("re-matrix-clear");
            clear.SetEnabled(rule != null);
            assignment.Add(clear);
            heading.Add(assignment);
            return heading;
        }

        private static Object Target(AnimationClip clip, RimeEmoMotion motion)
            => motion ? (motion.animationClip ? (Object)motion.animationClip : motion) : clip;

        private static string TargetName(AnimationClip clip, RimeEmoMotion motion)
        {
            var target = Target(clip, motion);
            return target ? target.name : RimeEmoLocalization.T("見つかりません");
        }

        private static string TargetName(GestureRule rule) => TargetName(rule.clip, rule.motion);

        private static string SourceClass(RimeEmoAssignments.Source source)
            => "re-cell--" + (source == RimeEmoAssignments.Source.Both ? RimeEmoAssignments.Source.Choice : source).ToString().ToLowerInvariant();

        private static string SourceLabel(RimeEmoAssignments.Source source)
        {
            switch (source)
            {
                case RimeEmoAssignments.Source.Left: return RimeEmoLocalization.T("左手の割り当て");
                case RimeEmoAssignments.Source.Right: return RimeEmoLocalization.T("右手の割り当て");
                case RimeEmoAssignments.Source.Both:
                case RimeEmoAssignments.Source.Choice: return RimeEmoLocalization.T("個別に設定");
                case RimeEmoAssignments.Source.Unresolved: return RimeEmoLocalization.T("未解決");
                default: return "Default Face";
            }
        }

        // Read a choice first, falling back to an older exact-pair rule without migrating the project.
        private static (int index, string status) PairSelection(RimeEmoProject project, int left, int right, List<Object> targets, bool resolved)
        {
            var choice = project.gestureChoices.FirstOrDefault(c => c != null && c.left == left && c.right == right);
            if (choice != null && choice.useDefault) return (1, null);
            var rules = project.gestureRules.Where(r => r != null && r.left == left && r.right == right).ToList();
            if (!resolved)
            {
                string status = RimeEmoLocalization.T("未解決");
                if (rules.Count > 0) status += " · " + string.Join(", ", rules.Select(TargetName));
                return (targets.Count + 2, status);
            }
            var rule = rules.FirstOrDefault();
            if (choice == null && rule == null) return (0, null);
            var clip = choice != null ? choice.clip : rule.clip;
            var motion = choice != null ? choice.motion : rule.motion;
            int index = targets.IndexOf(Target(clip, motion));
            return index >= 0 ? (index + 2, null) : (targets.Count + 2, TargetName(clip, motion));
        }

        private void ShowCellMenu(VisualElement cell, int left, int right)
        {
            var project = Project;
            if (!project) return;
            var targets = Targets(project);
            var selection = PairSelection(project, left, right, targets, RimeEmoAssignments.TryResolve(project, left, right, out _));
            var menu = new GenericMenu();
            if (selection.status != null)
                menu.AddDisabledItem(new GUIContent(RimeEmoLocalization.MenuText(selection.status)), true);
            menu.AddItem(new GUIContent(RimeEmoLocalization.T("自動（割り当てと優先設定に従う）")), selection.index == 0, () => SetChoice(left, right, null, false));
            menu.AddItem(new GUIContent("Default Face"), selection.index == 1, () => SetChoice(left, right, null, true));
            menu.AddSeparator("");
            for (int i = 0; i < targets.Count; i++)
            {
                var item = targets[i];
                menu.AddItem(new GUIContent(RimeEmoLocalization.MenuText(item.name)), selection.index == i + 2, () => SetChoice(left, right, item, false));
            }
            menu.DropDown(cell.worldBound);
        }

        private void SetChoice(int left, int right, Object target, bool useDefault) => Edit(() =>
        {
            var project = Project;
            project.gestureRules.RemoveAll(r => r != null && r.left == left && r.right == right);
            project.gestureChoices.RemoveAll(c => c != null && c.left == left && c.right == right);
            if (!useDefault && !target) return;
            project.gestureChoices.Add(new GestureChoice { left = left, right = right, useDefault = useDefault, clip = target as AnimationClip, motion = target as RimeEmoMotion });
        });

        // Earlier projects could assign a target with no gesture conditions.
        private void BuildCommonAssignments(RimeEmoProject project)
        {
            var host = UI<VisualElement>("common-assignments");
            host.Clear();
            if (!project) return;
            foreach (var rule in project.gestureRules.Where(r => r != null && r.left < 0 && r.right < 0))
            {
                var row = new VisualElement(); row.AddToClassList("re-gesture-rule");
                row.Add(new Label(RimeEmoLocalization.T("全組み合わせの共通設定")) { style = { width = 230 } });
                row.Add(TargetField(rule.clip, rule.motion, (clip, motion) => Edit(() =>
                {
                    if (!clip && !motion) project.gestureRules.Remove(rule);
                    else { rule.clip = clip; rule.motion = motion; }
                })));
                row.Add(IconButton("close", "×", RimeEmoLocalization.T("割り当てを外す"), () => Edit(() => project.gestureRules.Remove(rule))));
                host.Add(row);
            }
        }

        private ObjectField TargetField(AnimationClip clip, RimeEmoMotion motion, Action<AnimationClip, RimeEmoMotion> change)
        {
            var field = new ObjectField { objectType = typeof(AnimationClip), allowSceneObjects = false }; field.style.flexGrow = 1;
            field.SetValueWithoutNotify(motion ? motion.animationClip : clip);
            hoverPreview.Bind(field, clip, motion);
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
                if (project.gestureRules.Any(x => x != null && x.left == rule.left && x.right == rule.right)
                    || project.gestureChoices.Any(x => x != null && x.left == rule.left && x.right == rule.right)) { taken++; continue; }
                project.gestureRules.Add(new GestureRule { left = rule.left, right = rule.right, clip = clip }); added++;
            }
            status = string.Format(RimeEmoLocalization.T("{0}件取り込みました。"), added)
                + (taken > 0 ? string.Format(RimeEmoLocalization.T(" 割り当て済みの手の形{0}件はそのままにしました。"), taken) : "")
                + (rejected > 0 ? string.Format(RimeEmoLocalization.T(" 顔の表情ではないClip{0}件は除外しました。"), rejected) : "")
                + (conditional > 0 ? string.Format(RimeEmoLocalization.T(" ギミックをオンにしたときなど、初期状態では使われない遷移{0}件は対象外です。必要なら手動で割り当ててください。"), conditional) : "");
        }
    }
}
