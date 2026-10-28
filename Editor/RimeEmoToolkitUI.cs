using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        private VisualElement toolkitRoot;
        private IMGUIContainer previewUI;
        private RimeEmoTimelineElement retainedTimeline;
        private bool toolkitDirty = true;
        private bool lastToolkitMode;
        private bool previewPending;
        // 0 = editing (mode picks expression or motion), 1 = Gesture, 2 = Settings.
        [SerializeField] private int page;
        private RimeEmoGesturePanel gesturePanel;
        private double nextStaticPreview;
        private T UI<T>(string name) where T : VisualElement => toolkitRoot.Q<T>(name);

        public void CreateGUI()
        {
            CancelAutoSplit();
            autoSplitPanel = null;
            EndStaticGesture();
            retainedTimeline?.EndInteraction();
            rootVisualElement.Clear();
            string script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            string folder = Path.GetDirectoryName(script).Replace('\\', '/') + "/UI/";
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "RimeEmoWindow.uxml");
            if (!layout) { rootVisualElement.Add(new Label(RimeEmoLocalization.T("RimeEmo UI/RimeEmoWindow.uxml が見つかりません。"))); return; }
            toolkitRoot = layout.CloneTree();
            toolkitRoot.style.flexGrow = 1;
            foreach (string file in new[] { "RimeEmoTheme.uss", "RimeEmoCustom.uss" })
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(folder + file);
                if (sheet) toolkitRoot.styleSheets.Add(sheet);
            }
            rootVisualElement.Add(toolkitRoot);
            BindPanelWidths();
            BindPanelHeights();
            RimeEmoLocalization.MarkLanguage(toolkitRoot);
            RimeEmoLocalization.TranslateLayout(toolkitRoot);
            RimeEmoLocalization.BindLanguage(toolkitRoot);
            RimeEmoIcons.Apply(toolkitRoot, folder);
            BindObject<GameObject>("avatar", () => avatar, value =>
            {
                try { SelectAvatar(value); }
                finally { UI<ObjectField>("avatar").SetValueWithoutNotify(avatar); }
            }, true);
            BindObject<RimeEmoProject>("project", () => project, value =>
            {
                try { SelectProject(value); }
                finally { UI<ObjectField>("project").SetValueWithoutNotify(project); }
            });
            BindObject<RuntimeAnimatorController>("fx", () => fx, value =>
            {
                fx = value;
                if (project) { Undo.RecordObject(project, "RimeEmo: Source FX"); project.sourceFX = value; EditorUtility.SetDirty(project); }
            });
            BindMaterials();
            BindStaticToolkit();
            Button("static-mode", () => SwitchMode(0));
            Button("dynamic-mode", () => SwitchMode(1));
            Button("save", () => { if (page == 0 && mode == 1) SaveDraft(); else AssetDatabase.SaveAssets(); });
            Button("gesture-settings", () => ShowPage(1));
            Button("settings", () => ShowPage(2));
            Button("gesture-save-draft", () => SaveDraft());
            Button("part-settings", () => RimeEmoClassificationWindow.Open(project));
            BindShapeGroupSettings();
            Button("setup", () =>
            {
                if (!avatar) return;
                // Re-running setup on a configured avatar silently orphaned the previous Working folder.
                var existing = project ? project : RimeEmoAvatarStorage.Load(avatar);
                if (existing && !EditorUtility.DisplayDialog("RimeEmo",
                    string.Format(RimeEmoLocalization.T("このアバターには設定「{0}」があります。新しく作ると、アバターは新しい設定に切り替わります。今までの素材は削除されずに残ります。"), AssetDatabase.GetAssetPath(existing)),
                    RimeEmoLocalization.T("新しく作る"), RimeEmoLocalization.T("キャンセル"))) return;
                if (!FinishDraft()) return;
                SetProject(RimeEmoAssets.Create(avatar, fx));
                SuggestShapeGroups();
                page = 0; // Setup done: continue straight to editing.
            });
            Button("cleanup", () => RimeEmoCleanup.Run(project, motion));
            Button("new-motion", () => { if (FinishDraft()) ReplaceDraft(RimeEmoDrafts.Create(project)); });
            Button("save-as", () => SaveDraftAs(true));
            Button("capture-frame", CaptureCurrentFrame);
            Button("split-queue", SplitSelectedQueues);
            Button("duplicate-queue", DuplicateSelectedQueues);
            Button("auto-split", OpenAutoSplit);
            Button("delete-queue", DeleteSelectedQueues);
            Button("play", ToggleToolkitPlayback);
            Button("stop", () => { playing = false; time = 0; SyncToolkitTime(); RequestToolkitPreview(); });
            Button("previous", () => { JumpKey(-1); SyncToolkitTime(); RequestToolkitPreview(); });
            Button("next", () => { JumpKey(1); SyncToolkitTime(); RequestToolkitPreview(); });
            Button("add", () => ShowAddKeyMenu(activeTrack, time));
            Button("fit", () => retainedTimeline.Fit());
            Button("default-preview", () => { showDefault = true; RequestToolkitPreview(); });
            Button("motion-preview", () => { showDefault = false; RequestToolkitPreview(); });
            Button("reload-preview", RefreshAvatar);
            UI<TextField>("motion-name").isDelayed = true;
            UI<TextField>("motion-name").RegisterValueChangedCallback(e =>
            {
                // A label's ChangeEvent<string> also reaches this callback, retargeted to the field; only the field's own value names the motion.
                if (e.newValue != ((TextField)e.currentTarget).value || string.IsNullOrWhiteSpace(e.newValue)) return;
                if (ImportedEditing) { EditImported(() => importedDraft.name = RimeEmoAssets.SafeName(e.newValue)); return; }
                if (!motion) return;
                EditMotion(() => motion.name = RimeEmoAssets.SafeName(e.newValue));
            });
            UI<Toggle>("loop").RegisterValueChangedCallback(e =>
            {
                if (ImportedEditing) EditImported(() => { var settings = AnimationUtility.GetAnimationClipSettings(importedDraft); settings.loopTime = e.newValue; AnimationUtility.SetAnimationClipSettings(importedDraft, settings); });
                else if (motion) EditMotion(() => motion.loop = e.newValue);
            });
            foreach (string prefix in new[] { "static-", "motion-" })
            {
                string owner = prefix;
                foreach (string part in new[] { "stop-blink", "stop-lipsync" })
                    UI<Toggle>(owner + part).RegisterValueChangedCallback(_ => Run(() =>
                    {
                        var clip = TrackingTarget;
                        if (!clip) return;
                        RimeEmoFaceMenu.SetTracking(project, clip, UI<Toggle>(owner + "stop-blink").value, UI<Toggle>(owner + "stop-lipsync").value);
                        InvalidateApplyState(); RefreshTracking();
                    }));
            }
            UI<Toggle>("snap").RegisterValueChangedCallback(e => { frameSnap = e.newValue; retainedTimeline.FrameSnap = frameSnap; });
            UI<Toggle>("magnet").RegisterValueChangedCallback(e => { cueSnap = e.newValue; retainedTimeline.CueSnap = cueSnap; });
            UI<FloatField>("time").RegisterValueChangedCallback(e => { time = SafeTime(e.newValue); playing = false; showDefault = false; SyncToolkitTime(); RequestToolkitPreview(); });
            UI<FloatField>("time").formatString = "0.###"; // Frame times like 13/60 otherwise read 0.216666.
            UI<DropdownField>("face").RegisterValueChangedCallback(e =>
            {
                // The label's ChangeEvent<string> also reaches this callback, retargeted to the field (it once stored "Face Mesh"
                // as the face mesh path); a real selection always equals the field's value.
                if (!project || e.newValue != ((DropdownField)e.currentTarget).value) return;
                Undo.RecordObject(project, "RimeEmo: Select Face Mesh"); project.faceMeshPath = e.newValue; project.faceMeshSelected = true; EditorUtility.SetDirty(project); Changed();
            });
            retainedTimeline = new RimeEmoTimelineElement();
            UI<VisualElement>("timeline-host").Add(retainedTimeline);
            retainedTimeline.SelectionChanged += () =>
            {
                activeTrack = retainedTimeline.ActiveTrack; activeState = retainedTimeline.ActiveState;
                transitionSelected = retainedTimeline.TransitionSelected; time = retainedTimeline.Time;
                playing = false; showDefault = false; SyncToolkitTime();
                if (!retainedTimeline.IsInteracting) RefreshToolkitInspector();
                RefreshQueueButtons();
                RequestToolkitPreview(retainedTimeline.IsInteracting);
            };
            retainedTimeline.Edited += () => { CancelAutoSplit(); hasUnsavedChanges = true; preview?.InvalidateMotion(); RequestToolkitPreview(true); };
            retainedTimeline.InteractionEnded += () => { RefreshToolkitInspector(); RequestToolkitPreview(); };
            retainedTimeline.AddRequested += ShowAddKeyMenu;
            retainedTimeline.ContextRequested += ShowTimelineMenu;
            retainedTimeline.ClipDropped += (track, at, clip) => Run(() =>
            {
                // A clip already among the materials is always placeable; a new one must be this avatar's.
                bool known = project && (project.clips.Exists(c => c.clip == clip) || project.animations.Exists(c => c.clip == clip) || RimeEmoDynamicClips.Find(project, clip));
                string rejection = known ? null : RimeEmoClipClassification.Rejection(project, clip);
                if (rejection != null) { ShowNotification(new GUIContent(rejection)); return; }
                var material = ManagedMaterial(clip);
                if (material) { AddTimelineKey(track, at, material); RefreshMaterials(); }
            });
            retainedTimeline.SplitRequested += () => Run(SplitSelectedQueues);
            retainedTimeline.DuplicateRequested += () => Run(DuplicateSelectedQueues);
            retainedTimeline.DeleteRequested += () => Run(DeleteSelectedQueues);
            retainedTimeline.PlayToggleRequested += () => Run(ToggleToolkitPlayback);
            retainedTimeline.JumpRequested += direction => Run(() => { if (!motion) return; JumpKey(direction); SyncToolkitTime(); retainedTimeline.FollowTime(); RequestToolkitPreview(); });
            retainedTimeline.CopyRequested += () => Run(() => CopySelectedQueues());
            retainedTimeline.CutRequested += () => Run(() => { if (CopySelectedQueues()) DeleteSelectedQueues(); });
            retainedTimeline.PasteRequested += () => Run(PasteQueues);
            retainedTimeline.NudgeRequested += delta => Run(() => NudgeSelectedQueues(delta));
            retainedTimeline.SnapToggled += () => { cueSnap = retainedTimeline.CueSnap; UI<Toggle>("magnet").SetValueWithoutNotify(cueSnap); };
            previewUI = new IMGUIContainer(DrawToolkitPreview); previewUI.style.flexGrow = 1;
            UI<VisualElement>("preview-host").Add(previewUI);
            var gestureTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "RimeEmoGestureWindow.uxml");
            gesturePanel = null;
            if (gestureTree)
            {
                // A failure in the Gesture page must not stop the editing pages from being laid out.
                try
                {
                    gesturePanel = new RimeEmoGesturePanel(() => project, value => Run(() => SelectProject(value)), () => avatar, value => Run(() => SelectAvatar(value)), true);
                    gesturePanel.Edited += () => { InvalidateApplyState(); if (project) RefreshMaterials(); };
                    gesturePanel.Build(UI<VisualElement>("gesture-host"), gestureTree, folder);
                }
                catch (Exception ex) { Debug.LogException(ex); gesturePanel = null; }
            }
            BindApplyState();
            toolkitDirty = true; RefreshToolkit();
            BindGuide();
            BindUpdates();
            BindSupportLinks();
        }

        private void BindSupportLinks()
        {
            Button("support-github", () => Application.OpenURL(RimeEmoUpdates.RepositoryUrl));
            Button("support-booth", () => Application.OpenURL("https://rimerime.booth.pm/items/9056771"));
            Button("support-x", () => Application.OpenURL("https://x.com/rimevrc"));
            Button("support-contact", () => Application.OpenURL("https://rimerime.booth.pm/"));
        }

        private void ShowPage(int next)
        {
            EndStaticGesture(); retainedTimeline?.EndInteraction(); PreviewMaterial(null);
            playing = false; page = next; toolkitDirty = true;
            if (next == 1 && gesturePanel != null)
            {
                // "＋ 割り当て" starts from what is being edited.
                gesturePanel.InitialClip = mode == 0 ? selected : ImportedEditing ? importedTarget : motion ? motion.animationClip : null;
                gesturePanel.ClearStatus(); gesturePanel.MarkDirty();
            }
        }

        private void Button(string name, Action action)
        {
            UI<Button>(name).clicked += () => Run(() => { action(); toolkitDirty = true; });
        }

        private void BindObject<T>(string name, Func<T> read, Action<T> write, bool scene = false) where T : UnityEngine.Object
        {
            var field = UI<ObjectField>(name); field.objectType = typeof(T); field.allowSceneObjects = scene;
            field.SetValueWithoutNotify(read());
            field.RegisterValueChangedCallback(e => Run(() => { write(e.newValue as T); toolkitDirty = true; }));
        }

        private void RefreshToolkit()
        {
            if (toolkitRoot == null) return;
            toolkitDirty = false;
            bool dynamic = mode == 1;
            if (dynamic) EnsureDraft();
            int shown = project ? page : 2; // Without a project only Settings is meaningful.
            UI<VisualElement>("editor-page").style.display = shown == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            UI<VisualElement>("gesture-page").style.display = shown == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            UI<VisualElement>("settings-page").style.display = shown == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            RimeEmoIcons.SetActive(UI<Button>("static-mode"), shown == 0 && !dynamic);
            RimeEmoIcons.SetActive(UI<Button>("dynamic-mode"), shown == 0 && dynamic);
            RimeEmoIcons.SetActive(UI<Button>("gesture-settings"), shown == 1);
            RimeEmoIcons.SetActive(UI<Button>("settings"), shown == 2);
            UI<Button>("static-mode").SetEnabled(project); UI<Button>("dynamic-mode").SetEnabled(project);
            UI<VisualElement>("gesture-unsaved").style.display = shown == 1 && dynamic && hasUnsavedChanges ? DisplayStyle.Flex : DisplayStyle.None;
            if (shown == 1) gesturePanel?.MarkDirty();
            UI<VisualElement>("motion-toolbar").style.display = dynamic ? DisplayStyle.Flex : DisplayStyle.None;
            UI<VisualElement>("static-controls").style.display = dynamic ? DisplayStyle.None : DisplayStyle.Flex;
            if (dynamic) UI<Slider>("static-time").style.display = DisplayStyle.None;
            lastToolkitMode = dynamic;
            UI<ObjectField>("avatar").SetValueWithoutNotify(avatar);
            UI<ObjectField>("project").SetValueWithoutNotify(project);
            UI<ObjectField>("fx").SetValueWithoutNotify(fx);
            UI<Button>("setup").SetEnabled(avatar && !EditorUtility.IsPersistent(avatar));
            // The next step only before setup; afterwards it starts over (with a confirmation), so it steps back to tonal.
            UI<Button>("setup").EnableInClassList("re-button--filled", !project);
            UI<Button>("setup").EnableInClassList("re-button--tonal", project);
            UI<Button>("gesture-settings").SetEnabled(project);
            UI<Button>("part-settings").SetEnabled(project);
            UI<VisualElement>("editor").SetEnabled(project);
            RefreshMaterials();
            bool valid = ImportedEditing || (motion && motion.project == project);
            UI<Button>("capture-frame").style.display = dynamic ? DisplayStyle.Flex : DisplayStyle.None;
            UI<Button>("material-default").SetEnabled(project);
            UI<VisualElement>("block-editor").style.display = ImportedEditing ? DisplayStyle.None : DisplayStyle.Flex;
            UI<VisualElement>("imported-curves").style.display = ImportedEditing ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshImportedCurveHost(dynamic && ImportedEditing);
            UI<Label>("block-hint").style.display = ImportedEditing ? DisplayStyle.None : DisplayStyle.Flex;
            foreach (string id in new[] { "previous", "next", "add", "fit", "snap", "magnet" })
                UI<VisualElement>(id).style.display = ImportedEditing ? DisplayStyle.None : DisplayStyle.Flex;
            UI<VisualElement>("motion-controls").style.display = dynamic && valid ? DisplayStyle.Flex : DisplayStyle.None;
            UI<Button>("capture-frame").SetEnabled(valid);
            UI<Button>("save").SetEnabled(shown == 0 && dynamic ? valid : project != null);
            SetButtonCaption(UI<Button>("save"), RimeEmoLocalization.T("保存"));
            SetButtonCaption(UI<Button>("motion-preview"), RimeEmoLocalization.T(dynamic ? "編集中の動き" : "編集中の表情"));
            UI<Button>("cleanup").SetEnabled(project);
            if (shown == 2) RefreshShapeGroupSettings();
            UI<Label>("motion-status").text = valid ? (ImportedEditing ? (importedTarget ? RimeEmoLocalization.T("取り込んだ動き") + ": " + importedTarget.name : RimeEmoLocalization.T("未保存のアニメーション")) : motion.animationClip ? RimeEmoLocalization.T("保存先: ") + motion.animationClip.name : RimeEmoLocalization.T("未保存のアニメーション")) + (hasUnsavedChanges ? RimeEmoLocalization.T(" · 変更あり") : "") : "";
            var notice = UI<Label>("notice");
            notice.text = !project ? (avatar ? RimeEmoLocalization.T("このアバターはまだセットアップされていません。下の「新規セットアップ」を押してください。") : RimeEmoLocalization.T("上のAvatar欄にSceneのアバターを指定してください。設定済みなら自動で読み込みます。"))
                : !avatar ? RimeEmoLocalization.T("Avatarを指定するとプレビューできます。") : "";
            notice.style.display = string.IsNullOrEmpty(notice.text) ? DisplayStyle.None : DisplayStyle.Flex;
            UI<TextField>("motion-name").SetValueWithoutNotify(ImportedEditing ? importedDraft.name : valid ? motion.name : "");
            UI<Toggle>("loop").SetValueWithoutNotify(valid && EditingLoop);
            RefreshTracking();
            UI<Toggle>("snap").SetValueWithoutNotify(frameSnap);
            UI<Toggle>("magnet").SetValueWithoutNotify(cueSnap);
            var face = UI<DropdownField>("face"); face.choices = facePaths.ToList();
            // The menu shows each mesh with its Shape Key count; a full-width slash keeps nested paths from opening submenus.
            face.formatListItemCallback = path => path == null ? "" : RimeEmoLocalization.MenuText(path) + "  (" + (faceShapeCounts.TryGetValue(path, out int count) ? count : 0) + ")";
            face.SetValueWithoutNotify(project ? RimeEmoFace.Path(project) : "");
            bool changedMotion = retainedTimeline.Motion != (valid ? motion : null);
            if (changedMotion) retainedTimeline.SelectedStates.Clear();
            retainedTimeline.Motion = valid ? motion : null; retainedTimeline.ActiveTrack = activeTrack; retainedTimeline.ActiveState = activeState;
            retainedTimeline.TransitionSelected = transitionSelected; retainedTimeline.FrameSnap = frameSnap; retainedTimeline.CueSnap = cueSnap;
            retainedTimeline.Refresh();
            if (changedMotion) retainedTimeline.Fit();
            SyncToolkitTime(); RefreshToolkitInspector(); RequestToolkitPreview();
            if (!dynamic) RefreshStaticToolkit();
            UpdateGuideHighlight();
            RefreshApplyState();
        }

        // The material whose blink and lip sync pauses the editor shows: the selected expression, or the open motion once
        // it is saved (the setting belongs to the saved clip that Gesture and the menu use).
        private AnimationClip TrackingTarget => !project ? null
            : mode == 0 ? (EditingDefaultFace ? null : selected)
            : ImportedEditing ? importedTarget
            : motion && motion.project == project ? motion.animationClip : null;

        private void RefreshTracking()
        {
            if (toolkitRoot == null) return;
            var clip = TrackingTarget;
            var setting = RimeEmoFaceMenu.Tracking(project, clip);
            var sync = avatar && project ? RimeEmoFaceSync.Analyze(project, avatar) : new RimeEmoFaceSync();
            foreach (string prefix in new[] { "static-", "motion-" })
            {
                foreach (string part in new[] { "stop-blink", "stop-lipsync" })
                {
                    var toggle = UI<Toggle>(prefix + part);
                    bool eyes = part == "stop-blink", supported = eyes ? sync.CanPauseBlink : sync.CanPauseLipSync;
                    toggle.SetValueWithoutNotify(setting != null && (eyes ? setting.stopBlink : setting.stopLipSync));
                    toggle.SetEnabled(clip && supported);
                    // The layout's own tooltip (VRChat's blinking and lip sync), kept to show again for such an avatar.
                    if (toggle.userData == null) toggle.userData = toggle.tooltip;
                    toggle.tooltip = !supported ? RimeEmoLocalization.T(eyes
                            ? "このアバターのまばたきが見つからないため、ここでは止められません（VRChatのまばたき設定がなく、元FXの普段の表情にもまばたきがありません）。"
                            : "このアバターの口パクはFXで動いていて、Write DefaultsがOnか口パクのレイヤーが見つからないため、ここでは止められません。")
                        : eyes && sync.AnimatedBlink ? RimeEmoLocalization.T("このアバターはアニメーションでまばたきします。オフの表情には元FXの普段の表情のまばたきを付け、オンの表情ではまばたきさせません。")
                        : !eyes && sync.AnimatedLipSync ? RimeEmoLocalization.T("このアバターは口パクをFXで動かしています。オンにすると、この表情の間は口パクのShape KeyをDefault Faceの値に固定します。")
                        : (string)toggle.userData;
                }
            }
            UI<VisualElement>("static-tracking").style.display = EditingDefaultFace ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void SyncToolkitTime()
        {
            if (toolkitRoot == null) return;
            retainedTimeline.SetTime(time); UI<FloatField>("time").SetValueWithoutNotify(time);
            RefreshQueueButtons();
            UI<Label>("preview-time").text = hoveredMaterial ? RimeEmoLocalization.T("素材プレビュー · ") + hoveredMaterial.name : mode == 1 ? time.ToString("0.###") + RimeEmoLocalization.T(" 秒") : selected ? selected.name : EditingDefaultFace ? "Default Face" : RimeEmoLocalization.T("表情を選択してください");
            RimeEmoIcons.SetPlayback(UI<Button>("play"), playing);
            UI<Button>("default-preview").EnableInClassList("is-active", !hoveredMaterial && showDefault);
            UI<Button>("motion-preview").EnableInClassList("is-active", !hoveredMaterial && !showDefault);
            UI<Button>("capture-frame").SetEnabled(mode == 1 && (ImportedEditing || (motion && motion.project == project)) && !hoveredMaterial && !showDefault);
        }

        private void RequestToolkitPreview(bool defer = false)
        {
            // Coalesce to the next repaint; never postpone it while input continues.
            previewPending = true;
            previewUI?.MarkDirtyRepaint();
        }

        private void TickToolkit(double now)
        {
            if (toolkitRoot == null) return;
            toolkitRoot.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
            if (toolkitDirty || lastToolkitMode != (mode == 1)) RefreshToolkit();
            if (mode == 0 && (rowsDirty || rowsClip != selected)) RefreshStaticToolkit();
            if (previewPending) { previewPending = false; previewUI.MarkDirtyRepaint(); }
            if (page == 1) gesturePanel?.Tick();
            if (staticAdjusting && now >= nextStaticPreview) previewUI.MarkDirtyRepaint();
        }

        private void DrawToolkitPreview()
        {
            Rect rect = GUILayoutUtility.GetRect(1, 100000, 1, 100000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            ResolveToolkitPreview(out var clip, out var previewMotion, out float previewTime);
            bool refresh = !staticAdjusting || EditorApplication.timeSinceStartup >= nextStaticPreview;
            if (refresh && Event.current.type == EventType.Repaint) nextStaticPreview = EditorApplication.timeSinceStartup + 1.0 / 30;
            if (avatar) preview?.Draw(rect, clip, previewTime, refresh, previewMotion);
            else GUI.Box(rect, RimeEmoLocalization.T("Avatar未指定"));
        }

        private void ToggleToolkitPlayback()
        {
            if (mode != 1 || (!ImportedEditing && (!motion || motion.project != project))) return;
            retainedTimeline?.EndInteraction();
            bool start = !playing;
            PreviewMaterial(null);
            showDefault = false;
            if (start && time >= EditingDuration) time = 0;
            preview?.InvalidateMotion();
            playing = start; lastTick = EditorApplication.timeSinceStartup;
            SyncToolkitTime(); RequestToolkitPreview();
        }

        private void ResolveToolkitPreview(out AnimationClip clip, out RimeEmoMotion previewMotion, out float previewTime)
        {
            clip = null; previewMotion = null; previewTime = time;
            if (mode == 1 && ImportedEditing && playing) { clip = importedDraft; return; }
            // Active playback always uses the live composition, never the material shelf or saved output.
            if (playing && mode == 1 && motion && motion.project == project) { previewMotion = motion; return; }
            if (hoveredMaterial) { clip = hoveredMaterial; previewTime = 0; return; }
            if (showDefault) { clip = defaults; return; }
            if (mode == 0) clip = EditingDefaultFace ? defaults : selected;
            else if (ImportedEditing) clip = importedDraft;
            else if (motion && motion.project == project) previewMotion = motion;
        }

        private void RefreshToolkitInspector()
        {
            var host = UI<ScrollView>("inspector"); host.Clear();
            if (!TryGetState(out var track, out var state)) { host.Add(new Label(RimeEmoLocalization.T("キーを選択して表情を編集します。"))); return; }
            host.Add(new Label(PartLabels[(int)track.part] + (transitionSelected ? RimeEmoLocalization.T(" · 保持・移行") : RimeEmoLocalization.T(" · 表情キー"))));
            DrawQueueTimingInspector(host, track, state);
            if (!transitionSelected)
            {
                DrawQueueSourceInspector(host, track, state);
                var clips = project.clips.Where(c => c.clip && !RimeEmoImportedClips.IsAnimated(c.clip, project)).Select(c => c.clip)
                    .Concat(state.clip ? new[] { state.clip } : Array.Empty<AnimationClip>()).Distinct().ToArray();
                // The placed key uses a frozen copy that shares the material's name; label it so it is not mistaken for a duplicate.
                var names = new[] { "Default Face" }.Concat(clips.Select((c, i) => (i + 1) + ". "
                    + (c == state.clip && !project.clips.Any(e => e.clip == c) ? RimeEmoLocalization.T("配置中: ") + c.name : c.name))).ToList();
                var expression = new DropdownField(RimeEmoLocalization.T("表情"), names, Mathf.Max(0, Array.IndexOf(clips, state.clip) + 1)) { name = "key-expression" };
                expression.RegisterValueChangedCallback(e =>
                {
                    var selectedClip = expression.index == 0 ? null : clips[expression.index - 1];
                    if (selectedClip == state.clip) return;
                    EditMotion(() => { state.clip = RimeEmoMotionMaterials.Snapshot(project, selectedClip); state.hold += state.endHold; state.endHold = 0; state.animated = false; state.sourceStart = 0; state.speed = 1; });
                    // Changing one part's material must not seek all other parts to a different time.
                    playing = false; showDefault = false; PreviewMaterial(null);
                    SyncToolkitTime(); RequestToolkitPreview();
                }); host.Add(expression);
                var at = new FloatField(RimeEmoLocalization.T("キーの時刻（秒）")) { value = RimeEmoTimeline.Times(track)[activeState], isDelayed = true, formatString = "0.###" };
                at.RegisterValueChangedCallback(e => { EditMotion(() => new RimeEmoTimeline.BlockDrag(track, activeState, RimeEmoTimeline.BlockEdge.Move).Apply(SnapTime(e.newValue))); RequestToolkitPreview(); }); host.Add(at);
                if (state.animated)
                {
                    host.Add(new Label(RimeEmoLocalization.T("右端で終了時刻、左端で開始時刻、中央で長さを保った移動ができます。")));
                }
            }
        }
    }
}







