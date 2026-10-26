using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // Focused persistence/build checks. RimeEmoSelfTests.Run invokes these from the editor.
    internal static class RimeEmoBuildChecks
    {
        internal static void Run()
        {
            TestAssignments();
            TestBuildLayer();
        }

        static void TestAssignments()
        {
            var p = ScriptableObject.CreateInstance<RimeEmoProject>();
            var a = new AnimationClip { name = "A" }; var b = new AnimationClip { name = "B" };
            try
            {
                p.gestureRules.Add(new GestureRule { left = 1, right = -1, clip = a });
                p.gestureRules.Add(new GestureRule { left = -1, right = 2, clip = b });
                Assert(!RimeEmoAssignments.TryResolve(p, 1, 2, out _), "overlapping ANY rules resolved without a choice");
                p.gestureChoices.Add(new GestureChoice { left = 1, right = 2, useDefault = true });
                Assert(RimeEmoAssignments.TryResolve(p, 1, 2, out var resolved) && resolved == null, "saved default choice did not resolve");
                Assert(RimeEmoAssignments.TryResolve(p, 1, 3, out resolved) && resolved == p.gestureRules[0], "specific ANY rule did not resolve");
                p.gestureChoices.Clear();
                p.gesturePriority = GesturePriority.Left;
                Assert(RimeEmoAssignments.TryResolve(p, 1, 2, out resolved) && resolved == p.gestureRules[0], "left priority did not pick the left hand");
                p.gesturePriority = GesturePriority.Right;
                Assert(RimeEmoAssignments.TryResolve(p, 1, 2, out resolved) && resolved == p.gestureRules[1], "right priority did not pick the right hand");
                var neutralRight = new GestureRule { left = -1, right = 0, clip = b };
                p.gestureRules.Add(neutralRight);
                Assert(RimeEmoAssignments.TryResolve(p, 1, 0, out resolved) && resolved == p.gestureRules[0], "a neutral priority hand hid the other hand");
                p.gestureRules.Remove(neutralRight);
                var combo = new GestureRule { left = 1, right = 2, clip = a };
                p.gestureRules.Add(combo);
                Assert(RimeEmoAssignments.TryResolve(p, 1, 2, out resolved) && resolved == combo, "both-hand rule did not beat one-hand rules");
                p.gestureRules.Remove(combo); p.gesturePriority = GesturePriority.Manual;
                // A deleted expression is reported with its hand shapes, not as an empty assignment.
                var gone = new AnimationClip { name = "Gone" };
                p.gestureRules.Add(new GestureRule { left = 6, right = -1, clip = gone });
                UnityEngine.Object.DestroyImmediate(gone);
                Assert(RimeEmoAssignments.Missing(p.gestureRules.Last().clip, null) && RimeEmoAssignments.Validate(p).Any(m => m.Contains("L " + RimeEmoGestures.Names[6])),
                    "a deleted assignment was not reported with its hand shape");
                p.gestureRules.RemoveAt(p.gestureRules.Count - 1);
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(p); }
        }

        static void TestBuildLayer()
        {
            string root = "Assets/RimeEmoBuildChecks-" + Guid.NewGuid().ToString("N");
            RimeEmoProject project = null; AnimatorController source = null; AnimationClip sourceClip = null;
            string first = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", root.Substring("Assets/".Length));
                AssetDatabase.CreateFolder(root, "Animations");
                AssetDatabase.CreateFolder(root, "Motions");
                project = ScriptableObject.CreateInstance<RimeEmoProject>(); project.avatarName = "Build Check";
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "Smile", defaultValue = 12, part = FacePart.Mouth });
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "Blink", defaultValue = 4, part = FacePart.LeftEye });
                AssetDatabase.CreateAsset(project, root + "/Project.asset");
                source = AnimatorController.CreateAnimatorControllerAtPath(root + "/Source.controller");
                source.AddParameter("Unrelated", AnimatorControllerParameterType.Float);
                sourceClip = new AnimationClip { name = "SourceClip" };
                var face = RimeEmoClips.Binding(project.shapes[0]);
                AnimationUtility.SetEditorCurve(sourceClip, face, AnimationCurve.Constant(0, 1, 88));
                var other = EditorCurveBinding.FloatCurve("Face", typeof(Transform), "m_LocalPosition.x");
                AnimationUtility.SetEditorCurve(sourceClip, other, AnimationCurve.Constant(0, 1, 3));
                AssetDatabase.AddObjectToAsset(sourceClip, source);
                var sm = source.layers[0].stateMachine;
                var tree = new BlendTree { name = "SourceTree", blendType = BlendTreeType.Simple1D, blendParameter = "Unrelated" };
                AssetDatabase.AddObjectToAsset(tree, source); tree.AddChild(sourceClip, 0); tree.AddChild(sourceClip, 1);
                var state = sm.AddState("SourceState"); state.motion = tree;
                var nested = sm.AddStateMachine("Nested");
                var nestedA = nested.AddState("NestedA");
                var nestedB = nested.AddState("NestedB");
                nestedA.AddTransition(nestedB);
                project.sourceFX = source; project.clips.Add(new ClipEntry { clip = sourceClip });
                project.gestureRules.Add(new GestureRule { left = 3, right = 4, clip = sourceClip });
                var motion = RimeEmoAssets.NewMotion(project, sourceClip);
                TestDynamicClipSave(project, motion);
                TestDraftSave(project);
                project.gestureRules.Add(new GestureRule { left = 5, right = 6, clip = motion.animationClip });
                // A new expression starts with no curves; it is still listed and builds as Default Face.
                var fresh = RimeEmoAssets.NewDefault(project);
                Assert(AnimationUtility.GetCurveBindings(fresh).Length == 0 && RimeEmoClipClassification.Classify(project, fresh) == RimeEmoClipKind.Static, "new expression is not an empty, listed expression");
                project.gestureRules.Add(new GestureRule { left = 1, right = 1, clip = fresh });
                EditorUtility.SetDirty(project);
                AssetDatabase.SaveAssets();

                var before = AnimationUtility.GetEditorCurve(sourceClip, face).Evaluate(0);
                var built = RimeEmoBuild.Build(project);
                first = project.generatedFolder;
                string path = RimeEmoBuild.OutputFolder(project) + "/" + RimeEmoBuild.ControllerFile;
                Assert(built && built != source && AssetDatabase.GetAssetPath(built) == path, "build did not create the RimeEmo controller");
                var loadedProject = AssetDatabase.LoadAssetAtPath<RimeEmoProject>(root + "/Project.asset");
                Assert(loadedProject && loadedProject.generatedFolder == first && loadedProject.generatedFX == built, "build result was not persisted");
                TestBuildFingerprint(project, sourceClip, face);
                // Only the gesture layer: Modular Avatar appends it on top of the avatar's own FX.
                Assert(built.layers.Length == 1 && built.layers[0].name == RimeEmoBuild.LayerName, "generated controller is not a single gesture layer");
                Assert(built.parameters.Length == 2 && built.parameters.All(p => p.type == AnimatorControllerParameterType.Int), "gesture parameters are not the two Int parameters");
                var gesture = built.layers[0].stateMachine;
                Assert(gesture.states.Length == 64 && gesture.anyStateTransitions.Length == 64, "gesture layer did not contain 64 states/transitions");
                Assert(gesture.states.All(s => s.state.motion), "generated gesture state has no motion");
                var assigned = gesture.states.Single(s => s.state.name == "L3 R4").state.motion as AnimationClip;
                Assert(assigned && Mathf.Approximately(AnimationUtility.GetEditorCurve(assigned, face).Evaluate(0), 88), "assigned face was lost");
                // Only Shape Keys some expression uses are written; Blink is left to the avatar's own FX.
                Assert(AnimationUtility.GetEditorCurve(assigned, RimeEmoClips.Binding(project.shapes[1])) == null, "a Shape Key no expression uses was written");
                Assert(AnimationUtility.GetEditorCurve(assigned, other) != null, "non-face curve of an assigned clip was lost");
                Assert(AssetDatabase.GetAssetPath(assigned) == path, "generated clips are not stored inside the controller");
                var freshBuilt = gesture.states.Single(s => s.state.name == "L1 R1").state.motion as AnimationClip;
                Assert(freshBuilt && AnimationUtility.GetEditorCurve(freshBuilt, face) is AnimationCurve c && Mathf.Approximately(c.Evaluate(0), project.shapes[0].defaultValue)
                    && AnimationUtility.GetEditorCurve(freshBuilt, RimeEmoClips.Binding(project.shapes[1])) == null,
                    "empty new expression did not reset the written Shape Keys to Default Face");
                var defaultState = gesture.states.Single(s => s.state.name == "L0 R0").state.motion as AnimationClip;
                Assert(defaultState && AnimationUtility.GetCurveBindings(defaultState).Length == 1, "Default Face state wrote Shape Keys no expression uses");
                var dynamicClip = gesture.states.Single(s => s.state.name == "L5 R6").state.motion as AnimationClip;
                Assert(dynamicClip && Mathf.Approximately(AnimationUtility.GetEditorCurve(dynamicClip, face).Evaluate(0), 88), "assigned Motion was not compiled");
                Assert(gesture.anyStateTransitions.All(t => t.conditions.All(c => c.mode == AnimatorConditionMode.Equals)), "gesture condition mode is not Equals");
                Assert(gesture.anyStateTransitions.Select(t => string.Join(",", t.conditions.Select(c => c.parameter + "=" + c.threshold))).Distinct().Count() == 64, "gesture condition pairs are not unique");
                Assert(gesture.anyStateTransitions.All(t => t.conditions.Length == 2 && t.conditions.Any(c => c.parameter == "GestureLeft") && t.conditions.Any(c => c.parameter == "GestureRight")), "gesture transitions did not preserve both conditions");
                Assert(gesture.anyStateTransitions.Any(t => t.conditions.Any(c => c.parameter == "GestureLeft" && c.threshold == 3) && t.conditions.Any(c => c.parameter == "GestureRight" && c.threshold == 4)), "assigned gesture transition was not generated");
                // The source FX is read for nothing and left untouched.
                Assert(source.layers.Length == 1 && state.motion == tree && tree.children[0].motion == sourceClip && nestedA.transitions[0].destinationState == nestedB, "source graph changed");
                Assert(Mathf.Abs(AnimationUtility.GetEditorCurve(sourceClip, face).Evaluate(0) - before) < 0.01f, "source clip was modified");
                // A rebuild overwrites the same asset: the scene reference stays valid and nothing piles up.
                string guid = AssetDatabase.AssetPathToGUID(path);
                int clipCount = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Count();
                var builtAgain = RimeEmoBuild.Build(project);
                Assert(builtAgain == built && AssetDatabase.AssetPathToGUID(path) == guid && project.generatedFolder == first, "rebuild did not reuse the controller asset");
                Assert(builtAgain.layers.Length == 1 && builtAgain.layers[0].stateMachine.states.Length == 64, "rebuild kept the previous layer");
                Assert(AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Count() == clipCount, "rebuild left previous clips behind");
                project.gestureRules.RemoveAll(r => r.clip == fresh); // The settings-window checks below count the original rules.
                Assert(AssetDatabase.GetSubFolders(first).Length == 0, "rebuild created a build folder");
                TestWrittenShapes(project);
                TestSourceGestureOverride(project, source);
                TestSettingsWindows(project);
            }
            finally
            {
                // Only this check's own output folder; never the shared Generated root.
                if (first != null && AssetDatabase.IsValidFolder(first)) AssetDatabase.DeleteAsset(first);
                AssetDatabase.DeleteAsset(root);
                AssetDatabase.Refresh();
            }
        }

        static void TestWrittenShapes(RimeEmoProject project)
        {
            // An imported expression lists many keys at Default Face; only the ones it changes are written,
            // so lip sync and other face animation in the avatar's FX keep working.
            var clip = new AnimationClip();
            try
            {
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(project.shapes[0]), AnimationCurve.Constant(0, 1f / 60, 50));
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(project.shapes[1]), AnimationCurve.Constant(0, 1f / 60, project.shapes[1].defaultValue));
                var sources = new System.Collections.Generic.Dictionary<UnityEngine.Object, RimeEmoBuild.SourceClip> { { clip, new RimeEmoBuild.SourceClip { clip = clip } } };
                Assert(RimeEmoBuild.WrittenShapes(project, sources).SetEquals(new[] { project.shapes[0].shape }), "a key held at Default Face was written");
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
        }

        static void TestSourceGestureOverride(RimeEmoProject project, AnimatorController source)
        {
            // A hand-gesture layer in the source FX that moves Blink, which no RimeEmo expression uses.
            var blink = RimeEmoClips.Binding(project.shapes[1]);
            source.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
            // A blink layer that only checks a gesture to pause is not a hand-expression layer: its blinking is reached
            // by time, and the pause state holds Blink at Default Face.
            var blinking = new AnimationClip { name = "SourceBlinking" };
            AnimationUtility.SetEditorCurve(blinking, blink, new AnimationCurve(new Keyframe(0, 4), new Keyframe(.1f, 100), new Keyframe(.2f, 4)));
            var paused = new AnimationClip { name = "SourcePaused" };
            AnimationUtility.SetEditorCurve(paused, blink, AnimationCurve.Constant(0, 1, project.shapes[1].defaultValue));
            AssetDatabase.AddObjectToAsset(blinking, source); AssetDatabase.AddObjectToAsset(paused, source);
            source.AddLayer("Blink");
            var blinkLayer = source.layers.Last().stateMachine;
            var blinkState = blinkLayer.AddState("Blinking"); blinkState.motion = blinking; blinkLayer.defaultState = blinkState;
            var pauseState = blinkLayer.AddState("Paused"); pauseState.motion = paused;
            blinkState.AddTransition(pauseState).AddCondition(AnimatorConditionMode.Equals, 2, "GestureLeft");
            Assert(RimeEmoBuild.SourceGestureShapes(project).Count == 0, "a blink layer that pauses on a gesture was counted as a hand expression");
            var handClip = new AnimationClip { name = "SourceHand" };
            AnimationUtility.SetEditorCurve(handClip, blink, AnimationCurve.Constant(0, 1, 100));
            AssetDatabase.AddObjectToAsset(handClip, source);
            source.AddLayer("Hands");
            var hands = source.layers.Last().stateMachine;
            var fist = hands.AddState("Fist"); fist.motion = handClip;
            hands.AddAnyStateTransition(fist).AddCondition(AnimatorConditionMode.Equals, 1, "GestureLeft");
            Assert(RimeEmoBuild.SourceGestureShapes(project).SetEquals(new[] { "Blink" }), "source hand-gesture Shape Keys were not found");
            Assert(RimeEmoBuild.LeakingShapes(project).SequenceEqual(new[] { "Blink" }), "a source hand-gesture Shape Key RimeEmo leaves alone was not reported");
            project.overrideSourceGestureFaces = true;
            try
            {
                var built = RimeEmoBuild.Build(project);
                var assigned = built.layers[0].stateMachine.states.Single(s => s.state.name == "L3 R4").state.motion as AnimationClip;
                Assert(AnimationUtility.GetEditorCurve(assigned, blink) is AnimationCurve c && Mathf.Approximately(c.Evaluate(0), project.shapes[1].defaultValue),
                    "overriding the source hand expressions did not write their Shape Keys at Default Face");
            }
            finally { project.overrideSourceGestureFaces = false; }
        }

        static void TestBuildFingerprint(RimeEmoProject project, AnimationClip assignedClip, EditorCurveBinding face)
        {
            string recorded = project.generatedFingerprint;
            Assert(!string.IsNullOrEmpty(recorded) && recorded == RimeEmoBuildState.Fingerprint(project), "build did not record its inputs");
            Assert(RimeEmoBuildState.Describe(project, null) == RimeEmoBuildState.State.Unavailable, "apply state without an avatar was not unavailable");
            var original = AnimationUtility.GetEditorCurve(assignedClip, face);
            AnimationUtility.SetEditorCurve(assignedClip, face, AnimationCurve.Constant(0, 1, 50));
            Assert(RimeEmoBuildState.Fingerprint(project) != recorded, "editing an assigned clip was not detected");
            AnimationUtility.SetEditorCurve(assignedClip, face, original);
            Assert(RimeEmoBuildState.Fingerprint(project) == recorded, "restoring the assigned clip still reported changes");
            float blend = project.gestureBlendSeconds;
            project.gestureBlendSeconds = blend + 0.25f;
            Assert(RimeEmoBuildState.Fingerprint(project) != recorded, "changing the blend time was not detected");
            project.gestureBlendSeconds = blend;
            var rule = project.gestureRules[0]; int left = rule.left;
            rule.left = left == 7 ? 6 : 7;
            Assert(RimeEmoBuildState.Fingerprint(project) != recorded, "moving an assignment was not detected");
            rule.left = left;
            Assert(RimeEmoBuildState.Fingerprint(project) == recorded, "restored assignments still reported changes");
        }

        static void TestDraftSave(RimeEmoProject project)
        {
            var draft = RimeEmoDrafts.Create(project);
            RimeEmoMotion reopened = null;
            try
            {
                int count = project.motions.Count;
                Assert(draft.tracks.Count == 5 && draft.tracks.All(t => t.states.Count == 0), "new draft is not an empty five-part timeline");
                Assert(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(draft)) && !draft.animationClip && project.motions.Count == count, "new draft created assets before save");
                RimeEmoTimeline.Insert(draft.tracks[0], 0.2f, project.clips[0].clip);
                string path = RimeEmoAssets.Root(project) + "/Animations/Draft.anim";
                var clip = RimeEmoDrafts.SaveTo(draft, path);
                string guid = AssetDatabase.AssetPathToGUID(path);
                var saved = RimeEmoDynamicClips.Find(project, clip);
                Assert(saved && saved != draft && project.motions.Count == count + 1, "draft save did not persist editing data separately");
                reopened = RimeEmoDrafts.Copy(saved);
                reopened.tracks[0].states[0].hold = 0.8f;
                AssertNearForDraft(saved.tracks[0].states[0].hold, 0.1f, "draft edit mutated saved data");
                var result = RimeEmoDrafts.SaveTo(reopened, path);
                Assert(result == clip && AssetDatabase.AssetPathToGUID(path) == guid, "overwrite replaced animation identity");
                AssertNearForDraft(saved.tracks[0].states[0].hold, 0.8f, "overwrite lost block editing data");
                AssertNearForDraft(clip.length, 1, "overwrite generated wrong duration");
            }
            finally { UnityEngine.Object.DestroyImmediate(draft); UnityEngine.Object.DestroyImmediate(reopened); }
        }

        static void AssertNearForDraft(float actual, float expected, string message)
            => Assert(Mathf.Abs(actual - expected) < 0.001f, message);

        static void TestDynamicClipSave(RimeEmoProject project, RimeEmoMotion motion)
        {
            var clip = motion.animationClip;
            Assert(clip && AssetDatabase.GetAssetPath(clip).EndsWith(".anim"), "dynamic editor did not create AnimationClip");
            Assert(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(clip)) is AnimationClip, "dynamic target is not a standard clip asset");
            string path = AssetDatabase.GetAssetPath(clip);
            string guid = AssetDatabase.AssetPathToGUID(path);
            var other = EditorCurveBinding.FloatCurve("Accessory", typeof(Transform), "m_LocalPosition.x");
            AnimationUtility.SetEditorCurve(clip, other, AnimationCurve.Constant(0, 0.1f, 7));
            Undo.IncrementCurrentGroup(); Undo.RecordObject(motion, "Test dynamic clip edit");
            motion.tracks.Single(t => t.part == FacePart.Mouth).states[0].hold = 0.75f;
            EditorUtility.SetDirty(motion); Undo.FlushUndoRecordObjects();
            AssetDatabase.SaveAssets();
            Assert(Mathf.Approximately(clip.length, 0.75f), "normal asset save did not update dynamic clip");
            Assert(AssetDatabase.AssetPathToGUID(path) == guid && motion.animationClip == clip, "saving replaced the clip identity");
            Assert(AnimationUtility.GetEditorCurve(clip, other) != null, "dynamic save removed unrelated curves");
            Undo.PerformUndo(); RimeEmoDynamicClips.Save(project);
            Assert(Mathf.Approximately(clip.length, 0.1f), "undo did not restore dynamic clip duration");
            Assert(RimeEmoDynamicClips.Find(project, clip) == motion, "clip selection did not resolve editing data");

            var legacy = ScriptableObject.CreateInstance<RimeEmoMotion>();
            legacy.project = project; legacy.name = "Legacy";
            legacy.tracks.Add(new PartTrack { part = FacePart.Mouth, startTime = 0.2f });
            legacy.tracks[0].states.Add(new MotionState { hold = 0.4f });
            string legacyPath = RimeEmoAssets.Root(project) + "/Motions/Legacy.asset";
            AssetDatabase.CreateAsset(legacy, legacyPath); project.motions.Add(legacy);
            var migrated = RimeEmoDynamicClips.EnsureClip(legacy);
            Assert(migrated && Mathf.Approximately(migrated.length, 0.6f), "legacy timeline migration changed duration");
            Assert(AssetDatabase.LoadAssetAtPath<RimeEmoMotion>(legacyPath) == legacy && Mathf.Approximately(legacy.tracks[0].startTime, 0.2f), "legacy editing data changed");
        }

        static void TestSettingsWindows(RimeEmoProject project)
        {
            RimeEmoGestureWindow gesture = null;
            RimeEmoClassificationWindow classification = null;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            // The settings belong to a scene avatar; a temporary scene keeps the user's scene untouched.
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
            try
            {
                var avatar = new GameObject("Gesture Check Avatar"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(avatar, scene);
                RimeEmoAvatarStorage.Attach(avatar, project);
                gesture = ScriptableObject.CreateInstance<RimeEmoGestureWindow>();
                typeof(RimeEmoGestureWindow).GetField("project", flags).SetValue(gesture, project);
                gesture.Show(); gesture.CreateGUI();
                // Opened with settings but no avatar (as after a restart), the window finds the avatar again.
                Assert((GameObject)typeof(RimeEmoGestureWindow).GetField("avatar", flags).GetValue(gesture) == avatar, "the Gesture window did not find its avatar from the settings");
                var root = gesture.rootVisualElement;
                Assert(RimeEmoBuild.Validate(project).Count == 0, "valid project reported build errors");
                Assert(!root.Q<UnityEngine.UIElements.Button>("fx-apply").enabledSelf, "apply enabled without avatar");
                Assert(root.Q<UnityEngine.UIElements.VisualElement>("rules").childCount == 2, "gesture rule UI did not bind");
                root.Q<UnityEngine.UIElements.FloatField>("gesture-blend").value = 0.25f;
                Assert(Mathf.Approximately(project.gestureBlendSeconds, 0.25f), "gesture blend field not saved");
                TestImportedAnimationConflictChoice(project, gesture);
                classification = ScriptableObject.CreateInstance<RimeEmoClassificationWindow>();
                classification.Show();
                typeof(RimeEmoClassificationWindow).GetMethod("SetProject", flags).Invoke(classification, new object[] { project });
                var list = classification.rootVisualElement.Q<UnityEngine.UIElements.ListView>();
                Assert(list.itemsSource.Count == 2, "classification did not restrict to face shapes");
                var row = list.makeItem(); list.bindItem(row, 0);
                classification.rootVisualElement.Add(row);
                var field = row.Q<UnityEngine.UIElements.DropdownField>("part-field");
                var previous = project.shapes[0].part;
                Undo.IncrementCurrentGroup(); field.index = (int)FacePart.Brow;
                Assert(project.shapes[0].part == FacePart.Brow, "classification did not persist");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert(project.shapes[0].part == previous, "classification undo failed");
                // Avatar and settings stay paired: another avatar brings its own settings (none here).
                var other = new GameObject("Unconfigured"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(other, scene);
                gesture.rootVisualElement.Q<UnityEditor.UIElements.ObjectField>("gesture-avatar").value = other;
                Assert(typeof(RimeEmoGestureWindow).GetField("project", flags).GetValue(gesture) == null, "switching the Gesture window's avatar kept the previous avatar's settings");
            }
            finally
            {
                if (gesture) gesture.Close(); if (classification) classification.Close();
                if (scene.IsValid() && scene.isLoaded) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void TestImportedAnimationConflictChoice(RimeEmoProject project, RimeEmoGestureWindow gesture)
        {
            var originalRules = project.gestureRules.ToList();
            var originalChoices = project.gestureChoices.ToList();
            var imported = new AnimationClip { name = "Imported Dynamic A" };
            AnimationUtility.SetEditorCurve(imported, RimeEmoClips.Binding(project.shapes[0]), AnimationCurve.Linear(0, 0, 1, 100));
            var entry = new ClipEntry { clip = imported, source = null, gesture = "未割り当て" };
            project.animations.Add(entry);
            project.gestureRules.Add(new GestureRule { left = 1, right = -1, clip = imported });
            project.gestureRules.Add(new GestureRule { left = -1, right = 1, clip = project.clips.First(c => c.clip).clip });
            try
            {
                gesture.CreateGUI();
                var dropdown = gesture.rootVisualElement.Q<VisualElement>("conflicts").Children()
                    .SelectMany(row => row.Query<DropdownField>().ToList())
                    .FirstOrDefault(field => field.choices.Any(choice => choice.Contains(imported.name)));
                Assert(dropdown != null, "imported animation missing from conflict targets");
                dropdown.value = dropdown.choices.First(choice => choice.Contains(imported.name));
                var choice = project.gestureChoices.FirstOrDefault(c => c.left == 1 && c.right == 1);
                Assert(choice != null && choice.clip == imported && RimeEmoAssignments.TryResolve(project, 1, 1, out var resolved) && resolved.clip == imported, "imported animation conflict choice did not persist");
                gesture.CreateGUI();
                var refreshed = gesture.rootVisualElement.Q<VisualElement>("conflicts").Query<DropdownField>().ToList()
                    .First(field => field.choices.Any(value => value.Contains(imported.name)));
                Assert(refreshed.value.Contains(imported.name), "imported animation conflict choice was lost on refresh");
            }
            finally
            {
                project.gestureRules.Clear(); project.gestureRules.AddRange(originalRules);
                project.gestureChoices.Clear(); project.gestureChoices.AddRange(originalChoices);
                project.animations.Remove(entry); UnityEngine.Object.DestroyImmediate(imported);
            }
        }

        static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
