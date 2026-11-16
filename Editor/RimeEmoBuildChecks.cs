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
            TestSourceRules();
            TestFaceSync();
            TestBuildLayer();
        }

        // An avatar that blinks and lip-syncs in its FX, shaped like SiuSiu: the resting face is a looping clip with a
        // blink in it, Talk Control reads Viseme and drives Talk, and the Talk layer plays the mouth while Talk is on.
        static void TestFaceSync()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var controller = new AnimatorController();
            var made = new System.Collections.Generic.List<UnityEngine.Object> { project, controller };
            T Make<T>(T o) where T : UnityEngine.Object { made.Add(o); return o; }
            try
            {
                project.faceMeshSelected = true; project.faceMeshPath = "Face";
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "eye_blink", part = FacePart.LeftEye });
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "mouth_a", part = FacePart.Mouth });
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "smile", part = FacePart.Mouth });
                EditorCurveBinding Shape(string name) => EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "blendShape." + name);
                project.sourceFX = controller;
                controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
                controller.AddParameter("Viseme", AnimatorControllerParameterType.Int);
                controller.AddParameter("Talk", AnimatorControllerParameterType.Bool);
                var idle = Make(new AnimationClip { name = "Idle" });
                AnimationUtility.SetEditorCurve(idle, Shape("eye_blink"), new AnimationCurve(new Keyframe(0, 100), new Keyframe(.05f, 100), new Keyframe(.25f, 0), new Keyframe(3.95f, 0), new Keyframe(4, 20)));
                var loop = AnimationUtility.GetAnimationClipSettings(idle); loop.loopTime = true; AnimationUtility.SetAnimationClipSettings(idle, loop);
                controller.AddLayer("Hands");
                var hands = controller.layers[0].stateMachine;
                var rest = hands.AddState("Idle"); rest.motion = idle;
                hands.AddAnyStateTransition(rest).AddCondition(AnimatorConditionMode.Equals, 0, "GestureLeft");
                var talkClip = Make(new AnimationClip { name = "TalkPattern" });
                AnimationUtility.SetEditorCurve(talkClip, Shape("mouth_a"), new AnimationCurve(new Keyframe(0, 0), new Keyframe(.5f, 100)));
                var driverType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver")).FirstOrDefault(t => t != null);
                controller.AddLayer("Talk Control");
                var control = controller.layers[1].stateMachine;
                var speaking = control.AddState("Speaking");
                control.AddAnyStateTransition(speaking).AddCondition(AnimatorConditionMode.Greater, 0, "Viseme");
                if (driverType != null)
                    using (var driver = new SerializedObject(speaking.AddStateMachineBehaviour(driverType)))
                    {
                        var list = driver.FindProperty("parameters"); list.arraySize = 1;
                        list.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = "Talk";
                        driver.ApplyModifiedPropertiesWithoutUndo();
                    }
                controller.AddLayer("Talk");
                var talk = controller.layers[2].stateMachine;
                var mouth = talk.AddState("Mouth"); mouth.motion = talkClip;
                talk.AddAnyStateTransition(mouth).AddCondition(AnimatorConditionMode.If, 0, "Talk");
                foreach (var layer in controller.layers) foreach (var state in RimeEmoBuild.States(layer.stateMachine)) state.writeDefaultValues = false;

                Assert(!RimeEmoFaceSync.Analyze(project, true, true).AnimatedBlink, "VRChat's own blinking was treated as animated");
                var sync = RimeEmoFaceSync.Analyze(project, false, false);
                Assert(sync.AnimatedBlink && sync.BlinkShapes.SequenceEqual(new[] { "eye_blink" }) && Mathf.Approximately(sync.blinkLength, 4), "the resting face's blink was not found");
                if (driverType != null) Assert(sync.AnimatedLipSync && sync.lipShapes.SetEquals(new[] { "mouth_a" }), "the FX lip sync keys were not found through the driven Talk parameter");
                // A still expression (even a few frames long) gets the whole blink and loops; one with its eyes held shut keeps them shut.
                var smile = Make(new AnimationClip()); AnimationUtility.SetEditorCurve(smile, Shape("smile"), AnimationCurve.Constant(0, 1f / 6, 100));
                sync.AddBlink(smile);
                Assert(AnimationUtility.GetEditorCurve(smile, Shape("eye_blink")) is AnimationCurve blink && blink.Evaluate(.02f) > 99 && Mathf.Approximately(blink.keys[blink.length - 1].time, 4)
                    && AnimationUtility.GetAnimationClipSettings(smile).loopTime, "a still expression did not get the blink");
                // A motion played once keeps blinking after it ends, holding its last pose, and finally rests with open eyes.
                var once = Make(new AnimationClip()); AnimationUtility.SetEditorCurve(once, Shape("smile"), new AnimationCurve(new Keyframe(0, 0), new Keyframe(1f / 6, 100)));
                sync.AddBlink(once);
                Assert(!AnimationUtility.GetAnimationClipSettings(once).loopTime && once.length >= RimeEmoFaceSync.OnceBlinkTime
                    && AnimationUtility.GetEditorCurve(once, Shape("eye_blink")) is AnimationCurve onceBlink && onceBlink.Evaluate(8.02f) > 99 && Mathf.Approximately(onceBlink.keys[onceBlink.length - 1].value, 0)
                    && Mathf.Approximately(AnimationUtility.GetEditorCurve(once, Shape("smile")).Evaluate(8), 100), "a motion played once stopped blinking when it ended");
                var shut = Make(new AnimationClip()); AnimationUtility.SetEditorCurve(shut, Shape("eye_blink"), AnimationCurve.Constant(0, 1f / 60, 100));
                sync.AddBlink(shut);
                Assert(RimeEmoClips.IsConstant(AnimationUtility.GetEditorCurve(shut, Shape("eye_blink")), 100), "a blink was added over eyes held shut");
                // Pausing: a blinking expression holds its eyes open (not the shut first frame), and the lip sync keys are held only where asked.
                var own = Make(UnityEngine.Object.Instantiate(idle));
                sync.RemoveBlink(own);
                Assert(RimeEmoClips.IsConstant(AnimationUtility.GetEditorCurve(own, Shape("eye_blink")), 0), "pausing did not stop an expression's own blink");
                if (driverType != null)
                {
                    sync.PauseLipSync(smile);
                    Assert(RimeEmoClips.IsConstant(AnimationUtility.GetEditorCurve(smile, Shape("mouth_a")), 0), "pausing lip sync did not hold its keys");
                }
                // With Write Defaults on, a state without the keys would still write them, so the pause is not offered.
                mouth.writeDefaultValues = true;
                Assert(!RimeEmoFaceSync.Analyze(project, false, false).CanPauseLipSync, "lip sync pause was offered with Write Defaults on");
            }
            finally { foreach (var o in made) if (o) UnityEngine.Object.DestroyImmediate(o); }
        }

        // Hand expressions are found in the shapes avatars build them with, not only as direct AnyState transitions.
        static void TestSourceRules()
        {
            var controller = new AnimatorController();
            var clips = Enumerable.Range(0, 7).Select(i => new AnimationClip { name = "Hand" + i }).ToArray();
            try
            {
                foreach (var name in new[] { "GestureLeft", "GestureRight" }) controller.AddParameter(name, AnimatorControllerParameterType.Int);
                controller.AddParameter("GestureLeftWeight", AnimatorControllerParameterType.Float);
                controller.AddParameter("Toggle", AnimatorControllerParameterType.Bool);
                controller.AddLayer("Left Hand");
                var left = controller.layers[0].stateMachine;
                var idle = left.AddState("Idle");
                // State-to-state transition, as many per-hand layers are wired.
                var open = left.AddState("Open"); open.motion = clips[0];
                idle.AddTransition(open).AddCondition(AnimatorConditionMode.Equals, 2, "GestureLeft");
                // A fist Blend Tree on the trigger weight: the fully pressed child is the expression.
                var tree = new BlendTree { blendType = BlendTreeType.Simple1D, blendParameter = "GestureLeftWeight" };
                tree.AddChild(clips[1], 0); tree.AddChild(clips[2], 1);
                var fist = left.AddState("Fist"); fist.motion = tree;
                left.AddAnyStateTransition(fist).AddCondition(AnimatorConditionMode.Equals, 1, "GestureLeft");
                // A sub-state machine reached by a gesture, entering its default state.
                var nested = left.AddStateMachine("Point");
                var point = nested.AddState("PointFace"); point.motion = clips[3]; nested.defaultState = point;
                idle.AddTransition(nested).AddCondition(AnimatorConditionMode.Equals, 3, "GestureLeft");
                // A gimmick that also needs a toggle is counted, not imported; "not this hand shape" gates are ignored.
                var gimmick = left.AddState("Gimmick"); gimmick.motion = clips[4];
                var gated = left.AddAnyStateTransition(gimmick);
                gated.AddCondition(AnimatorConditionMode.Equals, 6, "GestureLeft"); gated.AddCondition(AnimatorConditionMode.If, 0, "Toggle");
                var pause = left.AddState("Pause"); pause.motion = clips[5];
                idle.AddTransition(pause).AddCondition(AnimatorConditionMode.NotEqual, 1, "GestureLeft");
                // A face lock that is off by default gates the hand layer (mao, anima): the face is still the hand's.
                var locked = left.AddState("Peace"); locked.motion = clips[6];
                var gate = left.AddAnyStateTransition(locked);
                gate.AddCondition(AnimatorConditionMode.Equals, 4, "GestureLeft"); gate.AddCondition(AnimatorConditionMode.IfNot, 0, "Toggle");
                var rules = RimeEmoGestures.SourceRules(controller, out int conditional);
                string Rule(int l, int r, AnimationClip c) => l + "," + r + "," + c.name;
                var found = rules.Select(r => Rule(r.left, r.right, r.clip)).OrderBy(s => s).ToArray();
                var expected = new[] { Rule(2, -1, clips[0]), Rule(1, -1, clips[2]), Rule(3, -1, clips[3]), Rule(4, -1, clips[6]) }.OrderBy(s => s).ToArray();
                Assert(found.SequenceEqual(expected), "source hand expressions were not found: " + string.Join(" / ", found));
                Assert(conditional == 1, "a gimmick transition with another condition was not counted");
            }
            finally
            {
                foreach (var clip in clips) UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(controller);
            }
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
                project.expressionMenu = false; // The plain gesture layer first; TestMenuAndTracking adds the menu.
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
                TestMenuAndTracking(project, sourceClip);
                TestSourceGestureOverride(project, source);
                TestSettingsWindows(project);
                TestDirectFx(project, source, root);
            }
            finally
            {
                // Only this check's own output folder; never the shared Generated root.
                if (first != null && AssetDatabase.IsValidFolder(first)) AssetDatabase.DeleteAsset(first);
                AssetDatabase.DeleteAsset(root);
                AssetDatabase.Refresh();
            }
        }

        static void TestDirectFx(RimeEmoProject project, AnimatorController source, string root)
        {
            Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            var descriptorType = Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (descriptorType == null) return;
            var avatar = new GameObject("Direct FX Check");
            var mesh = new Mesh { vertices = new[] { Vector3.zero } };
            GameObject prefabInstance = null;
            try
            {
                var face = new GameObject("Face");
                face.transform.SetParent(avatar.transform, false);
                foreach (var shape in project.shapes)
                    mesh.AddBlendShapeFrame(shape.shape, 100, new[] { Vector3.one }, new Vector3[1], new Vector3[1]);
                face.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
                var descriptor = avatar.AddComponent(descriptorType);
                var sourceLayers = source.layers.Length;
                // A user's own layer can already have this name.
                var layers = source.layers;
                layers[0].name = RimeEmoBuild.LayerName;
                source.layers = layers;
                foreach (var layer in source.layers)
                    foreach (var state in RimeEmoBuild.States(layer.stateMachine))
                    {
                        state.writeDefaultValues = false;
                        EditorUtility.SetDirty(state);
                    }
                AssetDatabase.SaveAssets();

                Assert(project.fxApplyMode == FxApplyMode.ModularAvatarAppend, "default apply mode must stay MA Append");
                project.fxApplyMode = FxApplyMode.CreateFx;
                project.expressionMenu = true;
                for (int i = 0; i < 10; i++) RimeEmoAssets.NewDefault(project);

                var nativeMenu = ScriptableObject.CreateInstance(Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu"));
                var nativeParams = ScriptableObject.CreateInstance(Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters"));
                AssetDatabase.CreateAsset(nativeMenu, root + "/User Menu.asset");
                AssetDatabase.CreateAsset(nativeParams, root + "/User Parameters.asset");
                using (var serialized = new SerializedObject(nativeMenu))
                {
                    var controls = serialized.FindProperty("controls");
                    controls.arraySize = 1;
                    controls.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = "User Control";
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                using (var serialized = new SerializedObject(nativeParams))
                {
                    var entries = serialized.FindProperty("parameters");
                    entries.arraySize = 1;
                    var entry = entries.GetArrayElementAtIndex(0);
                    entry.FindPropertyRelative("name").stringValue = "User Parameter";
                    entry.FindPropertyRelative("saved").boolValue = true;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                using (var serialized = new SerializedObject(descriptor))
                {
                    serialized.FindProperty("customExpressions").boolValue = true;
                    serialized.FindProperty("expressionsMenu").objectReferenceValue = nativeMenu;
                    serialized.FindProperty("expressionParameters").objectReferenceValue = nativeParams;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.SaveAssets();

                RimeEmoApply.Apply(project, avatar, false);
                var created = RimeEmoDirectFx.Find(project, avatar);
                var directController = RimeEmoAssets.FindFX(avatar) as AnimatorController;
                Assert(created && created.CompareTag("EditorOnly") && directController && directController == created.appliedFx
                    && directController.layers.Length == 1 && directController != project.generatedFX, "create did not install an owned direct FX");
                Assert(RimeEmoBuildState.Describe(project, avatar) == RimeEmoBuildState.State.UpToDate, "new direct application is stale");
                Assert(created.appliedMenu != nativeMenu && created.appliedParameters != nativeParams, "native assets were not copied");
                using (var serialized = new SerializedObject(created.appliedMenu))
                {
                    var controls = serialized.FindProperty("controls");
                    Assert(controls.arraySize == 2 && controls.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue == "User Control",
                        "native root controls were lost or duplicated");
                }
                using (var serialized = new SerializedObject(created.appliedParameters))
                {
                    var entries = serialized.FindProperty("parameters");
                    Assert(entries.arraySize == 3 && entries.GetArrayElementAtIndex(0).FindPropertyRelative("saved").boolValue,
                        "native parameters and settings were not preserved");
                }
                Assert(created.submenus.Length >= 3, "expression menu was not paginated");
                foreach (var menu in created.submenus)
                    using (var serialized = new SerializedObject(menu))
                        Assert(serialized.FindProperty("controls").arraySize <= 8, "a generated expression page exceeded the SDK limit");
                RimeEmoApply.Apply(project, avatar, false);
                Assert(RimeEmoDirectFx.Find(project, avatar) == created, "unchanged reapply replaced the direct output");

                project.fxApplyMode = FxApplyMode.OverwriteFx;
                project.overwriteFX = source;
                project.overrideSourceGestureFaces = true;
                var originalParameters = source.parameters;
                var conflicting = source.parameters;
                conflicting.Single(p => p.name == "GestureLeft").type = AnimatorControllerParameterType.Float;
                source.parameters = conflicting;
                var errors = RimeEmoApply.Validate(project, avatar);
                Assert(errors.Any(error => error.Contains("GestureLeft")), "controller parameter type conflict was not found: "
                    + string.Join(" / ", errors) + " | " + AssetDatabase.GetAssetPath(source) + " | "
                    + string.Join(", ", source.parameters.Select(p => p.name + "=" + p.type)));
                source.parameters = originalParameters;
                AssetDatabase.SaveAssets();
                RimeEmoApply.Apply(project, avatar, false);
                var installed = RimeEmoAssets.FindFX(avatar) as AnimatorController;
                Assert(installed && installed != source && installed.layers.Length == sourceLayers + 1 && source.layers.Length == sourceLayers
                    && installed.parameters.Any(p => p.name == "Unrelated"), "overwrite lost unrelated source layers or parameters");
                var owned = installed.layers.Last();
                Assert(owned.name != installed.layers[0].name && owned.stateMachine.states.Length >= 64
                    && owned.stateMachine.states.All(s => s.state.motion && !s.state.writeDefaultValues)
                    && owned.stateMachine.anyStateTransitions.All(t => t.destinationState
                        && AssetDatabase.GetAssetPath(t.destinationState) == AssetDatabase.GetAssetPath(installed)),
                    "generated graph was not copied independently or did not match the target Write Defaults");

                source.AddParameter("Added to Source", AnimatorControllerParameterType.Bool);
                EditorUtility.SetDirty(source);
                Assert(RimeEmoBuildState.Describe(project, avatar) == RimeEmoBuildState.State.Stale, "unsaved source FX edit did not invalidate the application");
                RimeEmoApply.Apply(project, avatar, false);
                installed = RimeEmoAssets.FindFX(avatar) as AnimatorController;
                Assert(installed && installed.layers.Length == sourceLayers + 1 && installed.parameters.Any(p => p.name == "Added to Source"),
                    "overwrite reapply duplicated the generated layer or missed a source edit");
                Assert(RimeEmoBuildState.Describe(project, avatar) == RimeEmoBuildState.State.UpToDate, "saving the source during the build left the result stale");

                var editedState = installed.layers.Last().stateMachine.states[0].state;
                editedState.name = "User edited state";
                EditorUtility.SetDirty(editedState);
                AssetDatabase.SaveAssets();
                Assert(RimeEmoBuildState.Describe(project, avatar) == RimeEmoBuildState.State.Stale && RimeEmoApply.Validate(project, avatar).Count != 0,
                    "manual edit to the installed FX was not protected");
                RimeEmoApply.Remove(project, avatar);
                Assert(!RimeEmoApply.HasApplied(project, avatar) && !RimeEmoAssets.FindFX(avatar) && installed,
                    "remove did not restore the default FX or retained ownership");
                using (var serialized = new SerializedObject(descriptor))
                    Assert(serialized.FindProperty("expressionsMenu").objectReferenceValue == nativeMenu
                        && serialized.FindProperty("expressionParameters").objectReferenceValue == nativeParams,
                        "remove did not restore native expression references");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert(RimeEmoDirectFx.Find(project, avatar) && RimeEmoAssets.FindFX(avatar) == installed,
                    "Undo of remove did not restore the direct installation");
                RimeEmoApply.Remove(project, avatar);

                // The blank overwrite field uses the existing customized descriptor FX. Use a distinct slot mask
                // and disable custom expressions while leaving its serialized menu and parameter references set.
                var mask = new AvatarMask { name = "Descriptor Mask" };
                AssetDatabase.CreateAsset(mask, root + "/Descriptor Mask.mask");
                SerializedProperty Fx(SerializedObject serialized)
                {
                    var slots = serialized.FindProperty("baseAnimationLayers");
                    for (int i = 0; i < slots.arraySize; i++)
                    {
                        var slot = slots.GetArrayElementAtIndex(i);
                        var type = slot.FindPropertyRelative("type");
                        if (type.enumNames[type.enumValueIndex] == "FX") return slot;
                    }
                    throw new InvalidOperationException("FX slot was not initialized");
                }
                using (var serialized = new SerializedObject(descriptor))
                {
                    serialized.FindProperty("customizeAnimationLayers").boolValue = true;
                    var fx = Fx(serialized);
                    fx.FindPropertyRelative("animatorController").objectReferenceValue = source;
                    fx.FindPropertyRelative("mask").objectReferenceValue = mask;
                    fx.FindPropertyRelative("isDefault").boolValue = false;
                    serialized.FindProperty("customExpressions").boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                project.overwriteFX = null;
                RimeEmoApply.Apply(project, avatar, false);
                var fallback = RimeEmoDirectFx.Find(project, avatar);
                Assert(fallback && fallback.baseFx == source && fallback.previousFx == source && fallback.appliedMask == mask,
                    "current descriptor FX or its mask was not preserved as the overwrite basis");
                using (var serialized = new SerializedObject(fallback.appliedParameters))
                {
                    var entries = serialized.FindProperty("parameters");
                    Assert(entries.arraySize == 5 && entries.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue == "VRCEmote",
                        "default SDK parameters were not preserved when activating custom expressions");
                }
                using (var serialized = new SerializedObject(fallback.appliedMenu))
                    Assert(serialized.FindProperty("controls").arraySize == 1, "inactive menu entries were added to the new custom menu");

                // The record is part of the prefab after saving. Reapply on an instance should reuse it.
                AssetDatabase.CreateAsset(mesh, root + "/Face Mesh.asset");
                var prefab = PrefabUtility.SaveAsPrefabAsset(avatar, root + "/Direct Avatar.prefab");
                prefabInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var inherited = RimeEmoDirectFx.Find(project, prefabInstance);
                project.expressionMenu = false;
                RimeEmoApply.Apply(project, prefabInstance, false);
                var reapplied = RimeEmoDirectFx.Find(project, prefabInstance);
                Assert(reapplied && reapplied == inherited && !reapplied.appliedMenu && !reapplied.appliedParameters,
                    "reapply did not reuse the inherited record with a disabled expression menu");
                RimeEmoApply.Remove(project, prefabInstance);
                Assert(!RimeEmoApply.HasApplied(project, prefabInstance) && RimeEmoAssets.FindFX(prefabInstance) == source,
                    "removing an inherited direct installation did not restore the avatar");

                if (RimeEmoApply.MergeType != null)
                {
                    project.fxApplyMode = FxApplyMode.ModularAvatarAppend;
                    RimeEmoApply.Apply(project, avatar, false);
                    Assert(!RimeEmoDirectFx.Find(project, avatar) && RimeEmoApply.AppliedAsAppend(project, avatar)
                        && RimeEmoAssets.FindFX(avatar) == source, "switching to MA did not restore the descriptor");
                    project.fxApplyMode = FxApplyMode.OverwriteFx;
                    RimeEmoApply.Apply(project, avatar, false);
                    Assert(RimeEmoDirectFx.Find(project, avatar) && !RimeEmoApply.FindApplied(project, avatar),
                        "switching from MA left a second application");
                }
                RimeEmoApply.Remove(project, avatar);
                using (var serialized = new SerializedObject(descriptor))
                    Assert(Fx(serialized).FindPropertyRelative("mask").objectReferenceValue == mask && RimeEmoAssets.FindFX(avatar) == source
                        && !serialized.FindProperty("customExpressions").boolValue, "remove did not restore the original descriptor settings");
            }
            finally
            {
                if (prefabInstance) UnityEngine.Object.DestroyImmediate(prefabInstance);
                UnityEngine.Object.DestroyImmediate(avatar);
                if (mesh && !EditorUtility.IsPersistent(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
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

        static void TestMenuAndTracking(RimeEmoProject project, AnimationClip assignedClip)
        {
            string recorded = RimeEmoBuildState.Fingerprint(project);
            project.expressionMenu = true;
            RimeEmoFaceMenu.SetTracking(project, assignedClip, true, false);
            try
            {
                Assert(RimeEmoBuildState.Fingerprint(project) != recorded, "the menu and the blink pause were not part of the fingerprint");
                var built = RimeEmoBuild.Build(project);
                Assert(built.parameters.Any(p => p.name == RimeEmoFaceMenu.LockParameter && p.type == AnimatorControllerParameterType.Bool)
                    && built.parameters.Any(p => p.name == RimeEmoFaceMenu.FaceParameter && p.type == AnimatorControllerParameterType.Int), "menu parameters were not declared");
                var layer = built.layers[0].stateMachine;
                var menuClips = RimeEmoFaceMenu.MenuClips(project);
                // A copy of a source FX clip played outside the hand layers (lip sync, talk, AFK) is not offered as a face.
                var talk = new AnimationClip { name = "Talk" };
                AnimationUtility.SetEditorCurve(talk, RimeEmoClips.Binding(project.shapes[0]), new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 100)));
                project.animations.Add(new ClipEntry { clip = talk, source = assignedClip });
                try { Assert(!RimeEmoFaceMenu.MenuClips(project).Contains(talk), "a source FX clip outside the hand layers was offered in the menu"); }
                finally { project.animations.RemoveAt(project.animations.Count - 1); UnityEngine.Object.DestroyImmediate(talk); }
                Assert(menuClips.Count > 0 && layer.states.Length == 64 + menuClips.Count, "menu expressions did not get their own states");
                var gesture = layer.anyStateTransitions.Where(t => t.conditions.Any(c => c.parameter == "GestureLeft")).ToArray();
                // Locked or with a menu expression chosen, the hands no longer change the face.
                Assert(gesture.Length == 64 && gesture.All(t => t.conditions.Any(c => c.parameter == RimeEmoFaceMenu.LockParameter && c.mode == AnimatorConditionMode.IfNot)
                    && t.conditions.Any(c => c.parameter == RimeEmoFaceMenu.FaceParameter && c.mode == AnimatorConditionMode.Equals && c.threshold == 0)), "hand transitions ignore the lock or the menu");
                for (int i = 0; i < menuClips.Count; i++)
                {
                    var state = layer.states.Single(s => s.state.name == "Menu " + (i + 1)).state;
                    Assert(state.motion && layer.anyStateTransitions.Any(t => t.destinationState == state && t.conditions.Length == 1
                        && t.conditions[0].parameter == RimeEmoFaceMenu.FaceParameter && t.conditions[0].threshold == i + 1), "menu expression " + (i + 1) + " is not selectable");
                }
                var trackingType = RimeEmoFaceMenu.TrackingControlType;
                if (trackingType != null)
                {
                    string Eyes(string name) => layer.states.Single(s => s.state.name == name).state.behaviours.Where(trackingType.IsInstanceOfType)
                        .Select(b => trackingType.GetField("trackingEyes").GetValue(b).ToString() + "/" + trackingType.GetField("trackingMouth").GetValue(b)).SingleOrDefault();
                    // The paused expression pauses blinking, every other state gives it back, and lip sync is left alone.
                    Assert(Eyes("L3 R4") == "Animation/NoChange", "an expression set to pause blinking did not pause it: " + Eyes("L3 R4"));
                    Assert(Eyes("L0 R0") == "Tracking/NoChange", "other states did not give blinking back: " + Eyes("L0 R0"));
                }
            }
            finally
            {
                RimeEmoFaceMenu.SetTracking(project, assignedClip, false, false);
                project.expressionMenu = false;
            }
            Assert(project.tracking.Count == 0 && RimeEmoBuildState.Fingerprint(project) == recorded, "turning the menu and pause off did not restore the fingerprint");
            RimeEmoBuild.Build(project);
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
                Assert(root.Q<VisualElement>("rule-add") == null && root.Q<VisualElement>("rules") == null, "separate both-hand assignment UI is still present");
                Assert(root.Q<VisualElement>("conflicts").Query<DropdownField>().ToList().Count == 2, "existing exact rules did not appear in the individual assignments");
                Assert(root.Q<VisualElement>("common-assignments").childCount == 0, "an empty common assignment is editable");
                Assert(root.Q<VisualElement>("gesture-legend").Query<Label>(className: "re-cell--choice").ToList().Count == 1, "individual assignments have more than one legend entry");
                var hover = root.Q<VisualElement>("gesture-hover-preview");
                Assert(hover != null && hover.style.display.value == DisplayStyle.None && hover.pickingMode == PickingMode.Ignore
                    && hover.Children().All(child => child.pickingMode == PickingMode.Ignore),
                    "the Gesture preview can intercept assignment input or starts visible without a hovered target");
                TestTouchGestureGuide(root);
                var previousMode = project.fxApplyMode;
                var previousOverwrite = project.overwriteFX;
                var temporaryController = new AnimatorController { name = "UI Mode Check" };
                try
                {
                    var modeField = root.Q<DropdownField>("fx-apply-mode");
                    var overwriteField = root.Q<UnityEditor.UIElements.ObjectField>("fx-overwrite-controller");
                    Assert(modeField.choices.Count == 3, "Gesture apply mode dropdown is missing an option");
                    Assert(!string.IsNullOrWhiteSpace(root.Q<Label>("fx-mode-help").text)
                        && !root.Q<Label>("fx-mode-help").text.Contains("説明"), "Gesture apply mode help is blank or still a translation placeholder");
                    modeField.index = (int)FxApplyMode.CreateFx;
                    Assert(project.fxApplyMode == FxApplyMode.CreateFx, "Create FX mode was not saved from the dropdown");
                    gesture.CreateGUI(); root = gesture.rootVisualElement;
                    overwriteField = root.Q<UnityEditor.UIElements.ObjectField>("fx-overwrite-controller");
                    var createHelp = root.Q<Label>("fx-mode-help").text;
                    Assert(overwriteField.style.display.value == DisplayStyle.None && createHelp.Length > 40,
                        "Create FX mode did not update its help or hide the optional FX field");
                    modeField = root.Q<DropdownField>("fx-apply-mode");
                    modeField.index = (int)FxApplyMode.OverwriteFx;
                    Assert(project.fxApplyMode == FxApplyMode.OverwriteFx, "Overwrite FX mode was not saved from the dropdown");
                    gesture.CreateGUI(); root = gesture.rootVisualElement;
                    overwriteField = root.Q<UnityEditor.UIElements.ObjectField>("fx-overwrite-controller");
                    Assert(overwriteField.style.display.value == DisplayStyle.Flex
                        && root.Q<Label>("fx-mode-help").text.Length > 40
                        && root.Q<Label>("fx-mode-help").text != createHelp, "Overwrite FX mode did not show its optional field and mode-specific help");
                    overwriteField.value = temporaryController;
                    Assert(project.overwriteFX == temporaryController, "Overwrite FX field did not save its controller");
                }
                finally
                {
                    project.fxApplyMode = previousMode;
                    project.overwriteFX = previousOverwrite;
                    gesture.CreateGUI(); root = gesture.rootVisualElement;
                    Assert(root.Q<DropdownField>("fx-apply-mode").index == (int)previousMode
                        && root.Q<UnityEditor.UIElements.ObjectField>("fx-overwrite-controller").value == previousOverwrite,
                        "Gesture apply mode UI did not restore prior project settings");
                    UnityEngine.Object.DestroyImmediate(temporaryController);
                }
                root.Q<UnityEngine.UIElements.FloatField>("gesture-blend").value = 0.25f;
                Assert(Mathf.Approximately(project.gestureBlendSeconds, 0.25f), "gesture blend field not saved");
                TestIndividualGestureAssignments(project, gesture);
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

        static void TestTouchGestureGuide(VisualElement root)
        {
            string[] expectedNames = { "Neutral", "Fist", "HandOpen", "FingerPoint", "Victory", "RockNRoll", "HandGun", "ThumbsUp" };
            Assert(RimeEmoGestures.Names.SequenceEqual(expectedNames), "the eight canonical VRChat gesture names or IDs changed");
            var off = RimeEmoTouchGestureDiagram.Input.Off;
            var touch = RimeEmoTouchGestureDiagram.Input.Touch;
            var press = RimeEmoTouchGestureDiagram.Input.Press;
            var expectedInputs = new[] { (off, off, off), (touch, press, press), (off, off, off), (touch, off, press),
                (touch, off, off), (touch, press, off), (off, off, press), (off, press, press) };
            Assert(RimeEmoTouchGestureDiagram.Inputs.SequenceEqual(expectedInputs), "Quest Touch input states do not match the gesture IDs");
            var ids = Enumerable.Range(0, expectedNames.Length);
            var diagrams = root.Q<VisualElement>("touch-poses").Query<RimeEmoTouchGestureDiagram>().ToList();
            Assert(diagrams.Select(diagram => diagram.Gesture).SequenceEqual(ids)
                && diagrams.All(diagram => diagram.LeftHand && !string.IsNullOrEmpty(diagram.tooltip)),
                "Quest Touch diagrams did not bind to the guide");
            var guide = root.Q<Foldout>("touch-guide");
            Assert(!guide.value && guide.text.Contains("Quest 3") && guide.text.Contains("Touch Plus"), "Quest 3 guide should start collapsed");
            guide.value = true;
            Assert(guide.value && guide.Q<VisualElement>("touch-poses") != null, "Quest 3 guide cannot be expanded");
            guide.value = false;
            var matrix = root.Q<ScrollView>("gesture-matrix-scroll").Q<VisualElement>("gesture-matrix");
            foreach (var (axis, leftHand) in new[] { ("hand-left-", true), ("hand-right-", false) })
            {
                foreach (int id in ids)
                {
                    var field = matrix.Q<UnityEditor.UIElements.ObjectField>(axis + id);
                    var header = field?.parent?.parent;
                    var diagram = header?.Q<RimeEmoTouchGestureDiagram>();
                    Assert(field != null && field.enabledSelf && diagram != null && diagram.LeftHand == leftHand && diagram.Gesture == id
                        && !string.IsNullOrEmpty(diagram.tooltip) && header.Q<Label>(className: "re-matrix-name").text == id + " " + expectedNames[id],
                        "the combination table has an incorrect assignment header for " + axis + id);
                }
            }
        }

        static void TestIndividualGestureAssignments(RimeEmoProject project, RimeEmoGestureWindow gesture)
        {
            // The preceding UI checks recorded this same project in the current editor frame.
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            var originalRules = project.gestureRules.ToList();
            var originalChoices = project.gestureChoices.ToList();
            var originalPriority = project.gesturePriority;
            var legacy = originalRules.Single(r => r.left == 3 && r.right == 4).clip;
            var motion = project.motions.First(m => m && m.animationClip && m.animationClip != legacy);
            var imported = new AnimationClip { name = "Imported Dynamic A" };
            AnimationUtility.SetEditorCurve(imported, RimeEmoClips.Binding(project.shapes[0]), AnimationCurve.Linear(0, 0, 1, 100));
            string importedPath = AssetDatabase.GenerateUniqueAssetPath(
                System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(project)).Replace('\\', '/') + "/Animations/Imported Dynamic A.anim");
            AssetDatabase.CreateAsset(imported, importedPath);
            var entry = new ClipEntry { clip = imported, source = null, gesture = "未割り当て" };
            project.animations.Add(entry);
            project.gestureRules.Add(new GestureRule { left = 3, right = -1, clip = imported });
            project.gestureRules.Add(new GestureRule { left = -1, right = -1, motion = motion });
            project.gesturePriority = GesturePriority.Left;
            try
            {
                var prior = ResolveGrid();
                int dirtyCount = EditorUtility.GetDirtyCount(project);
                gesture.CreateGUI();
                var root = gesture.rootVisualElement;
                var field = PairField();
                Assert(field.value.EndsWith(legacy.name), "existing exact assignment was not selected");
                var cell = root.Q<VisualElement>("gesture-matrix").Query<Button>(className: "re-matrix-cell").ToList()[3 * 8 + 4];
                Assert(cell.text == legacy.name && cell.ClassListContains("re-cell--choice"), "existing exact assignment does not appear as an individual matrix cell");
                var common = root.Q<VisualElement>("common-assignments").Q<UnityEditor.UIElements.ObjectField>();
                Assert(common != null && common.value == motion.animationClip, "existing default for all combinations is inaccessible");
                Assert(ResolveGrid().SequenceEqual(prior) && EditorUtility.GetDirtyCount(project) == dirtyCount, "opening the Gesture page changed the project or its resolutions");

                foreach (var (label, clip, auto) in new[] { (imported.name, imported, false), ("Default Face", (AnimationClip)null, false), ("", imported, true), (motion.animationClip.name, motion.animationClip, false) })
                {
                    Undo.IncrementCurrentGroup();
                    field = PairField();
                    field.index = auto ? 0 : field.choices.FindIndex(value => value.EndsWith(label));
                    var choice = project.gestureChoices.FirstOrDefault(c => c.left == 3 && c.right == 4);
                    Assert(!project.gestureRules.Any(r => r.left == 3 && r.right == 4), "editing a pair left its exact rule behind");
                    Assert(auto ? choice == null : choice != null && (clip ? choice.clip == clip : choice.useDefault), "pair edit was not recorded correctly");
                    var expected = prior.ToArray();
                    expected[3 * 8 + 4] = (true, clip, null);
                    Assert(ResolveGrid().SequenceEqual(expected), "pair edit changed other cells or resolved to the wrong target");
                    Undo.FlushUndoRecordObjects();
                    Undo.PerformUndo();
                    string edit = auto ? "Auto" : label;
                    Assert(project.gestureRules.Any(r => r.left == 3 && r.right == 4 && r.clip == legacy), "undo did not restore the exact rule after " + edit);
                    var restored = ResolveGrid();
                    for (int i = 0; i < restored.Length; i++)
                    {
                        if (restored[i].Equals(prior[i])) continue;
                        string rules = string.Join(", ", project.gestureRules.Select(r => $"({r.left}, {r.right}) clip={r.clip} motion={r.motion}"));
                        throw new InvalidOperationException($"undo changed cell L{i / 8} R{i % 8} after {edit} from {prior[i]} to {restored[i]}. Rules = {rules}");
                    }
                    gesture.CreateGUI();
                }

                project.gesturePriority = GesturePriority.Manual;
                gesture.CreateGUI();
                Assert(!RimeEmoAssignments.TryResolve(project, 3, 4, out _) && PairField().value.Contains(legacy.name),
                    "manual mode hid the existing exact assignment in an unresolved cell");
                project.gesturePriority = GesturePriority.Left;
                gesture.CreateGUI();
                Undo.IncrementCurrentGroup();
                common = gesture.rootVisualElement.Q<VisualElement>("common-assignments").Q<UnityEditor.UIElements.ObjectField>();
                common.value = imported;
                Assert(RimeEmoAssignments.TryResolve(project, 0, 0, out var commonRule) && commonRule.clip == imported, "common assignment could not be edited");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                gesture.CreateGUI();
                Undo.IncrementCurrentGroup();
                common = gesture.rootVisualElement.Q<VisualElement>("common-assignments").Q<UnityEditor.UIElements.ObjectField>();
                common.value = null;
                Assert(RimeEmoAssignments.TryResolve(project, 0, 0, out commonRule) && commonRule == null, "common assignment could not be removed");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert(ResolveGrid().SequenceEqual(prior), "undo did not restore the common assignment");

                foreach (var (name, index) in new[] { ("hand-left-3", 3 * 8), ("hand-right-4", 4) })
                {
                    gesture.CreateGUI();
                    var headerField = gesture.rootVisualElement.Q<UnityEditor.UIElements.ObjectField>(name);
                    Undo.IncrementCurrentGroup();
                    headerField.value = motion.animationClip;
                    var edited = ResolveGrid();
                    Assert(edited[index].clip == motion.animationClip && edited[3 * 8 + 4].clip == legacy, "editing " + name + " did not respect its axis and the exact assignment");
                    Undo.FlushUndoRecordObjects();

                    gesture.CreateGUI();
                    headerField = gesture.rootVisualElement.Q<UnityEditor.UIElements.ObjectField>(name);
                    Undo.IncrementCurrentGroup();
                    headerField.value = null;
                    Assert(ResolveGrid()[index].motion == motion, "clearing " + name + " did not restore the common assignment");
                    Undo.FlushUndoRecordObjects();
                    Undo.PerformUndo();
                    Assert(ResolveGrid().SequenceEqual(edited), "undo did not restore the cleared assignment for " + name);
                    Undo.PerformUndo();
                    Assert(ResolveGrid().SequenceEqual(prior), "undo did not restore the original grid for " + name);
                }
            }
            finally
            {
                project.gesturePriority = originalPriority;
                project.gestureRules.Clear(); project.gestureRules.AddRange(originalRules);
                project.gestureChoices.Clear(); project.gestureChoices.AddRange(originalChoices);
                project.animations.RemoveAll(e => e.clip == imported);
                AssetDatabase.DeleteAsset(importedPath);
            }

            DropdownField PairField() => gesture.rootVisualElement.Q<VisualElement>("conflicts").Children()
                .Single(row => row.Q<Label>()?.text == "L FingerPoint / R Victory").Q<DropdownField>();

            (bool resolved, AnimationClip clip, RimeEmoMotion motion)[] ResolveGrid()
            {
                var grid = new (bool, AnimationClip, RimeEmoMotion)[64];
                for (int left = 0; left < 8; left++) for (int right = 0; right < 8; right++)
                {
                    bool resolved = RimeEmoAssignments.TryResolve(project, left, right, out var rule);
                    // Undo can return Unity's serialized null object wrappers. Compare usable asset references.
                    grid[left * 8 + right] = (resolved, rule?.clip ? rule.clip : null, rule?.motion ? rule.motion : null);
                }
                return grid;
            }
        }

        static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
