using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public static class RimeEmoSelfTests
    {
        [MenuItem("Tools/RimeEmo/Run Core Checks")]
        public static void Run()
        {
            try
            {
                RimeEmoUpdateChecks.Run();
                TestRemoveDefaults();
                TestFaceMeshSelection();
                TestTimelineEditing();
                TestTimelineDrag();
                TestBlockDrag();
                TestBlockDeletion();
                TestDragUndo();
                TestCompiler();
                TestIndependentPartTracks();
                TestEyelidClassificationRepair();
                TestClassification();
                TestPreviewIsolation();
                TestToolkitUI();
                TestStaticToolkitUI();
                RimeEmoBuildChecks.Run();
                RimeEmoAvatarStorageChecks.Run();
                RimeEmoDefaultFaceChecks.Run();
                RimeEmoWorkflowChecks.Run();
                RimeEmoLocalizationChecks.Run();
                RimeEmoGuideChecks.Run();
                RimeEmoImportedChecks.Run();
                RimeEmoQueueChecks.Run();
                RimeEmoAutoSplitChecks.Run();
                RimeEmoAutoSplitUIChecks.Run();
                RimeEmoTimingChecks.Run();
                RimeEmoTimingUIChecks.Run();
                RimeEmoMaterialChecks.Run();
                RimeEmoShapeGroupChecks.Run();
                Debug.Log("RimeEmo core checks passed.");
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("RimeEmo core checks failed: " + e.Message, e);
            }
        }

        private static void TestBlockDeletion()
        {
            for (int removed = 0; removed < 3; removed++)
            {
                var track = new PartTrack { startTime = 0.2f };
                track.states.Add(new MotionState { hold = 0.3f, transition = 0.4f });
                track.states.Add(new MotionState { hold = 0.5f, transition = 0.6f });
                track.states.Add(new MotionState { hold = 0.7f });
                var starts = RimeEmoTimeline.Times(track);
                var original = track.states.ToArray();
                var holds = new[] { 0.3f, 0.5f, 0.7f };
                RimeEmoTimeline.Remove(track, removed);
                var after = RimeEmoTimeline.Times(track);
                int remaining = 0;
                for (int i = 0; i < 3; i++)
                {
                    if (i == removed) continue;
                    Assert(track.states[remaining] == original[i], "deletion changed surviving block identity");
                    AssertNear(after[remaining], starts[i], "deletion moved surviving block start");
                    AssertNear(track.states[remaining].hold, holds[i], "deletion resized surviving block");
                    remaining++;
                }
                if (removed == 2) AssertNear(RimeEmoCompiler.Duration(track), starts[1] + holds[1], "final deletion extended previous block");
            }
            var single = new PartTrack { startTime = 0.5f };
            single.states.Add(new MotionState { hold = 1 });
            RimeEmoTimeline.Remove(single, 0);
            Assert(single.states.Count == 0, "single block deletion failed");
            AssertNear(RimeEmoCompiler.Duration(single), 0, "empty track retained duration");
        }

        private static void TestRemoveDefaults()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var clip = new AnimationClip();
            var mesh = new Mesh();
            try
            {
                var shape = new ShapeEntry { path = "Face", shape = "Smile", defaultValue = 0.5f, part = FacePart.Mouth };
                project.shapes.Add(shape);
                var binding = RimeEmoClips.Binding(shape);
                var equalWithTangents = new AnimationCurve(new Keyframe(0, 0.5f, 1, 1), new Keyframe(1, 0.5f, 1, 1));
                AnimationUtility.SetEditorCurve(clip, binding, equalWithTangents);
                Assert(RimeEmoClips.RemoveDefaults(clip, project) == 0, "nonconstant equal endpoint curve was removed");
                Assert(AnimationUtility.GetEditorCurve(clip, binding) != null, "nonconstant equal endpoint curve was lost");

                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, 0.5f));
                Assert(RimeEmoClips.RemoveDefaults(clip, project) == 1, "true default curve was not removed");
                Assert(AnimationUtility.GetEditorCurve(clip, binding) == null, "true default curve remains");

                var transformBinding = EditorCurveBinding.FloatCurve("Face", typeof(Transform), "m_LocalPosition.x");
                AnimationUtility.SetEditorCurve(clip, transformBinding, AnimationCurve.Linear(0, 0, 1, 0));
                var objectBinding = EditorCurveBinding.PPtrCurve("Face", typeof(MeshFilter), "m_Mesh");
                AnimationUtility.SetObjectReferenceCurve(clip, objectBinding, new[] { new ObjectReferenceKeyframe { time = 0, value = mesh } });
                RimeEmoClips.RemoveDefaults(clip, project);
                Assert(AnimationUtility.GetEditorCurve(clip, transformBinding) != null, "unsupported Transform curve was removed");
                Assert(AnimationUtility.GetObjectReferenceCurve(clip, objectBinding) != null, "object reference curve was removed");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(project);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static void TestTimelineEditing()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var firstClip = new AnimationClip();
            var secondClip = new AnimationClip();
            var thirdClip = new AnimationClip();
            var motion = ScriptableObject.CreateInstance<RimeEmoMotion>();
            AnimationClip compiled = null;
            try
            {
                var shape = new ShapeEntry { path = "Face", shape = "Timeline", defaultValue = 0, part = FacePart.Mouth };
                project.shapes.Add(shape);
                SetValue(firstClip, shape, 0); SetValue(secondClip, shape, 1); SetValue(thirdClip, shape, 2);
                var track = new PartTrack { part = FacePart.Mouth, startTime = 0.3f };
                track.states.Add(new MotionState { clip = firstClip, hold = 0.2f, transition = 0.1f, interpolation = Interpolation.Linear });
                track.states.Add(new MotionState { clip = secondClip, hold = 0.4f, transition = 0.2f, interpolation = Interpolation.Linear });
                track.states.Add(new MotionState { clip = thirdClip, hold = 0.7f });
                motion.project = project; motion.tracks.Add(track);
                AssertTimes(RimeEmoTimeline.Times(track), 0.3f, 0.6f, 1.2f);

                var moved = RimeEmoTimeline.Move(track, 1, 0.8f);
                Assert(track.states[moved].clip == secondClip, "Move lost moved state");
                AssertTimes(RimeEmoTimeline.Times(track), 0.3f, 0.8f, 1.2f);
                Assert(track.states[0].clip == firstClip && track.states[2].clip == thirdClip, "Move changed other key order");

                int inserted = RimeEmoTimeline.Insert(track, 0.8f, thirdClip);
                Assert(inserted == 1 && track.states.Count == 3 && track.states[1].clip == thirdClip, "Insert did not replace existing timestamp");
                AssertTimes(RimeEmoTimeline.Times(track), 0.3f, 0.8f, 1.2f);
                RimeEmoTimeline.Remove(track, 1);
                Assert(track.states.Count == 2 && track.states[0].clip == firstClip && track.states[1].clip == thirdClip, "Remove changed surviving states");
                AssertTimes(RimeEmoTimeline.Times(track), 0.3f, 1.2f);

                AssertNear(RimeEmoCompiler.Duration(track), 1.9f, "Remove moved track end");
                RimeEmoTimeline.Move(track, 0, 0.5f);
                AssertTimes(RimeEmoTimeline.Times(track), 0.5f, 1.2f);
                compiled = RimeEmoCompiler.Compile(motion);
                var curve = AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(shape));
                AssertNear(curve.Evaluate(0.1f), 0, "compiler did not hold first value before startTime");
                AssertNear(curve.Evaluate(0.5f), 0, "compiler changed first value at startTime");
                AssertNear(curve.Evaluate(0.95f), 1, "compiler interpolation changed at surviving key midpoint");
                AssertNear(curve.Evaluate(1.2f), 2, "compiler changed surviving end value");

                var zero = new PartTrack { startTime = 0.4f };
                zero.states.Add(new MotionState { clip = firstClip, hold = 0, transition = 0 });
                zero.states.Add(new MotionState { clip = thirdClip, hold = 0.2f });
                AssertTimes(RimeEmoTimeline.Times(zero), 0.4f, 0.4f);

                var crossing = new PartTrack();
                var a = new MotionState { clip = firstClip, hold = 0, transition = 1 };
                var b = new MotionState { clip = secondClip, hold = 0, transition = 1 };
                var c = new MotionState { clip = thirdClip, hold = 1 };
                crossing.states.Add(a); crossing.states.Add(b); crossing.states.Add(c);
                int crossed = RimeEmoTimeline.Move(crossing, 0, 1.5f);
                Assert(crossed == 1 && crossing.states[0] == b && crossing.states[1] == a && crossing.states[2] == c, "crossing move lost identity/order");
                AssertTimes(RimeEmoTimeline.Times(crossing), 1, 1.5f, 2);
                RimeEmoTimeline.Insert(crossing, 1.25f, firstClip);
                AssertTimes(RimeEmoTimeline.Times(crossing), 1, 1.25f, 1.5f, 2);
                AssertNear(RimeEmoCompiler.Duration(crossing), 3, "Insert changed existing end");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(compiled);
                UnityEngine.Object.DestroyImmediate(motion);
                UnityEngine.Object.DestroyImmediate(firstClip);
                UnityEngine.Object.DestroyImmediate(secondClip);
                UnityEngine.Object.DestroyImmediate(thirdClip);
                UnityEngine.Object.DestroyImmediate(project);
            }
        }

        private static void TestTimelineDrag()
        {
            var track = new PartTrack { startTime = 0.3f };
            var a = new MotionState { hold = 0.2f, transition = 0.1f };
            var b = new MotionState { hold = 0.4f, transition = 0.2f };
            var c = new MotionState { hold = 0.7f };
            track.states.Add(a); track.states.Add(b); track.states.Add(c);
            var drag = new RimeEmoTimeline.Drag(track, 1);
            Assert(!drag.CanMove(0.6f), "stationary drag changed key");
            drag.MoveTo(0.35f);
            AssertNear(a.hold, 0.05f, "drag did not shorten adjacent hold");
            drag.MoveTo(3);
            Assert(track.states[2] == b, "drag crossing lost selected state");
            Assert(!drag.CanMove(1.2f), "drag allowed overlapping keys");
            drag.MoveTo(0.6f);
            AssertTimes(RimeEmoTimeline.Times(track), 0.3f, 0.6f, 1.2f);
            Assert(track.states[1] == b, "return drag lost selected state");
            AssertNear(a.hold, 0.2f, "return drag permanently shortened hold");
            AssertNear(b.hold, 0.4f, "return drag changed selected hold");
            AssertNear(b.transition, 0.2f, "return drag changed transition");
            AssertNear(RimeEmoCompiler.Duration(track), 1.9f, "return drag permanently extended ending");
            Assert(!drag.CanMove(0.6f), "repeated drag input changed key");
        }

        private static void TestBlockDrag()
        {
            var track = new PartTrack();
            track.states.Add(new MotionState { animated = true, hold = 0.5f, transition = 0.5f, sourceStart = 1 });
            track.states.Add(new MotionState { animated = true, hold = 0.5f, transition = 0.5f, sourceStart = 1 });
            track.states.Add(new MotionState { animated = true, hold = 0.5f, sourceStart = 1 });
            var move = new RimeEmoTimeline.BlockDrag(track, 1, RimeEmoTimeline.BlockEdge.Move);
            move.Apply(1.3f);
            AssertTimes(RimeEmoTimeline.Times(track), 0, 1.3f, 2);
            AssertNear(track.states[1].hold, 0.5f, "block move changed length");
            move.Apply(5);
            AssertTimes(RimeEmoTimeline.Times(track), 0, 1.5f, 2);
            move.Apply(1);
            var left = new RimeEmoTimeline.BlockDrag(track, 1, RimeEmoTimeline.BlockEdge.Start);
            left.Apply(0);
            AssertTimes(RimeEmoTimeline.Times(track), 0, 0.5f, 2);
            AssertNear(track.states[1].hold, 1, "left resize did not preserve end");
            left.Apply(1);
            var right = new RimeEmoTimeline.BlockDrag(track, 1, RimeEmoTimeline.BlockEdge.End);
            right.Apply(1.2f);
            AssertTimes(RimeEmoTimeline.Times(track), 0, 1, 2);
            AssertNear(track.states[1].hold, 0.2f, "right resize changed start");
            AssertNear(track.states[1].transition, 0.8f, "gap is not interpolation duration");
        }

        private static void TestDragUndo()
        {
            var motion = ScriptableObject.CreateInstance<RimeEmoMotion>();
            try
            {
                var track = new PartTrack();
                track.states.Add(new MotionState { hold = 0.2f, transition = 0.8f });
                track.states.Add(new MotionState { hold = 0.5f });
                motion.tracks.Add(track);
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(motion, "RimeEmo drag test");
                var drag = new RimeEmoTimeline.Drag(track, 1);
                drag.MoveTo(2);
                drag.MoveTo(0.5f);
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                Undo.PerformUndo();
                AssertTimes(RimeEmoTimeline.Times(motion.tracks[0]), 0, 1);
                AssertNear(RimeEmoCompiler.Duration(motion.tracks[0]), 1.5f, "drag undo lost ending");
                Undo.PerformRedo();
                AssertTimes(RimeEmoTimeline.Times(motion.tracks[0]), 0, 0.5f);
                AssertNear(RimeEmoCompiler.Duration(motion.tracks[0]), 1.5f, "drag redo lost ending");
            }
            finally
            {
                Undo.ClearUndo(motion);
                UnityEngine.Object.DestroyImmediate(motion);
            }
        }

        private static void AssertTimes(float[] actual, params float[] expected)
        {
            Assert(actual.Length == expected.Length, "timeline key count changed");
            for (int i = 0; i < expected.Length; i++) AssertNear(actual[i], expected[i], "timeline key time " + i);
        }

        private static void TestIndependentPartTracks()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var motion = ScriptableObject.CreateInstance<RimeEmoMotion>();
            var clips = new[] { new AnimationClip(), new AnimationClip(), new AnimationClip() };
            AnimationClip compiled = null;
            try
            {
                motion.project = project;
                var parts = new[] { FacePart.LeftEye, FacePart.RightEye, FacePart.Mouth };
                for (int i = 0; i < parts.Length; i++)
                {
                    var shape = new ShapeEntry { path = "Body", shape = parts[i].ToString(), part = parts[i] };
                    project.shapes.Add(shape);
                    // Every material affects all parts; only its assigned part may be sampled.
                    for (int c = 0; c < clips.Length; c++) SetValue(clips[c], shape, 10 * (c + 1) + i);
                    var track = new PartTrack { part = parts[i] };
                    track.states.Add(new MotionState { clip = clips[i], hold = 0.2f, transition = 0.4f, interpolation = Interpolation.Linear });
                    track.states.Add(new MotionState { clip = clips[(i + 1) % 3], hold = 0.5f });
                    motion.tracks.Add(track);
                }
                compiled = RimeEmoCompiler.Compile(motion);
                var rightBefore = AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(project.shapes[1]));
                var mouthBefore = AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(project.shapes[2]));
                AssertNear(AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(project.shapes[0])).Evaluate(0), 10, "left part sampled another track");
                AssertNear(rightBefore.Evaluate(0), 21, "right part sampled another track");
                AssertNear(mouthBefore.Evaluate(0), 32, "mouth part sampled another track");
                motion.tracks[0].states[0].clip = clips[2];
                RimeEmoTimeline.Remove(motion.tracks[0], 1);
                UnityEngine.Object.DestroyImmediate(compiled);
                compiled = RimeEmoCompiler.Compile(motion);
                AssertNear(AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(project.shapes[0])).Evaluate(0), 30, "left track edit not applied");
                for (float t = 0; t <= 1.1f; t += 0.1f)
                {
                    AssertNear(AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(project.shapes[1])).Evaluate(t), rightBefore.Evaluate(t), "left edit changed right eye");
                    AssertNear(AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(project.shapes[2])).Evaluate(t), mouthBefore.Evaluate(t), "left edit changed mouth");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(compiled);
                foreach (var clip in clips) UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(motion); UnityEngine.Object.DestroyImmediate(project);
            }
        }

        private static void TestEyelidClassificationRepair()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var motion = ScriptableObject.CreateInstance<RimeEmoMotion>();
            var point = new AnimationClip();
            AnimationClip compiled = null;
            try
            {
                var right = new ShapeEntry { path = "Body", shape = "LowerLid_Up_mid_R", part = FacePart.Other };
                var left = new ShapeEntry { path = "Body", shape = "UpperLid_Down_L", part = FacePart.Other };
                var manual = new ShapeEntry { path = "Body", shape = "LowerLid_Down_L", part = FacePart.Other, classificationOverridden = true };
                project.shapes.AddRange(new[] { right, left, manual });
                Assert(RimeEmoClips.RepairAutomaticClassification(project) == 2, "legacy eyelid classification was not repaired");
                Assert(right.part == FacePart.RightEye && left.part == FacePart.LeftEye && manual.part == FacePart.Other, "eyelid repair changed wrong parts");
                Assert(RimeEmoClips.RepairAutomaticClassification(project) == 0, "eyelid repair repeats");
                SetValue(point, right, 50.6f); SetValue(point, left, 100);
                motion.project = project;
                foreach (FacePart part in Enum.GetValues(typeof(FacePart)))
                {
                    var track = new PartTrack { part = part };
                    track.states.Add(new MotionState { clip = part == FacePart.RightEye ? point : null, hold = 1 });
                    motion.tracks.Add(track);
                }
                compiled = RimeEmoCompiler.Compile(motion);
                AssertNear(AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(right)).Evaluate(0.19125f), 50.6f, "Point right lower eyelid missing from composition");
                AssertNear(AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(left)).Evaluate(0.19125f), 0, "right Point affected left eyelid");
                Assert(RimeEmoClips.Classify("LowerLid_Up_mid") == FacePart.Other, "unsided eyelid was assigned to one eye");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(compiled); UnityEngine.Object.DestroyImmediate(point);
                UnityEngine.Object.DestroyImmediate(motion); UnityEngine.Object.DestroyImmediate(project);
            }
        }

        private static void TestCompiler()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var first = new AnimationClip();
            var second = new AnimationClip();
            var motion = ScriptableObject.CreateInstance<RimeEmoMotion>();
            AnimationClip compiled = null;
            try
            {
                var shape = new ShapeEntry { path = "Face", shape = "Smile", defaultValue = 0.25f, part = FacePart.Mouth };
                project.shapes.Add(shape);
                SetValue(first, shape, 0);
                SetValue(second, shape, 1);
                motion.project = project;
                motion.tracks.Add(new PartTrack { part = FacePart.Mouth });
                motion.tracks[0].states.Add(new MotionState { clip = first, hold = 0.2f, transition = 0.4f, interpolation = Interpolation.Linear });
                motion.tracks[0].states.Add(new MotionState { clip = second, hold = 0.5f, transition = 0 });
                compiled = RimeEmoCompiler.Compile(motion);
                var curve = AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(shape));
                AssertNear(curve.Evaluate(0.1f), 0, "linear hold");
                AssertNear(curve.Evaluate(0.4f), 0.5f, "linear midpoint");
                AssertNear(curve.Evaluate(0.6f), 1, "linear transition end");

                var longShape = new ShapeEntry { path = "Face", shape = "Long", defaultValue = 0.1f, part = FacePart.Brow };
                project.shapes.Add(longShape);
                var longTrack = new PartTrack { part = FacePart.Brow };
                longTrack.states.Add(new MotionState { clip = first, hold = 2.0f, transition = 0 });
                motion.tracks.Add(longTrack);
                var missingShape = new ShapeEntry { path = "Face", shape = "Missing", defaultValue = 0.7f, part = FacePart.Mouth };
                project.shapes.Add(missingShape);
                UnityEngine.Object.DestroyImmediate(compiled);
                compiled = RimeEmoCompiler.Compile(motion);
                AssertNear(AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(missingShape)).Evaluate(0), 0.7f, "missing property default");
                AssertNear(AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(shape)).Evaluate(1.8f), 1, "short track holds through longer track");

                UnityEngine.Object.DestroyImmediate(compiled);
                motion.tracks[0].states[0].transition = 0;
                compiled = RimeEmoCompiler.Compile(motion);
                curve = AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(shape));
                AssertNear(curve.Evaluate(0.2f), 1, "zero transition instantaneous change");

                project.shapes.Remove(missingShape);
                motion.tracks[0].states[0].transition = 0.4f;
                motion.tracks[0].states[0].interpolation = Interpolation.EaseInOut;
                UnityEngine.Object.DestroyImmediate(compiled);
                compiled = RimeEmoCompiler.Compile(motion);
                curve = AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(shape));
                AssertNear(curve.Evaluate(0.3f), 0.15625f, "ease-in-out quarter");

                motion.tracks[0].states[0].interpolation = Interpolation.Step;
                UnityEngine.Object.DestroyImmediate(compiled);
                compiled = RimeEmoCompiler.Compile(motion);
                curve = AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(shape));
                AssertNear(curve.Evaluate(0.2f), 0, "step transition start");
                AssertNear(curve.Evaluate(0.3f), 0, "step transition midpoint");
                AssertNear(curve.Evaluate(0.6f), 1, "step transition end");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(compiled);
                UnityEngine.Object.DestroyImmediate(motion);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(project);
            }
        }

        private static void TestFaceMeshSelection()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var clip = new AnimationClip();
            AnimationClip defaults = null;
            AnimationClip compiled = null;
            var motion = ScriptableObject.CreateInstance<RimeEmoMotion>();
            try
            {
                var body = new ShapeEntry { path = "Avatar/Body", shape = "BodySmile", defaultValue = 0.25f, part = FacePart.Mouth };
                var hair = new ShapeEntry { path = "Avatar/Hair", shape = "HairSmile", defaultValue = 0.5f, part = FacePart.Brow };
                var nestedBody = new ShapeEntry { path = "Avatar/Pet/Body", shape = "PetFace", defaultValue = 0.75f, part = FacePart.Mouth };
                project.shapes.Add(body); project.shapes.Add(hair); project.shapes.Add(nestedBody);
                Assert(RimeEmoFace.Path(project) == "Avatar/Body", "shallow Body was not preferred");

                project.faceMeshSelected = true;
                project.faceMeshPath = "Avatar/Hair";
                Assert(RimeEmoFace.Path(project) == "Avatar/Hair", "explicit face mesh override was ignored");
                project.faceMeshSelected = false;
                SetValue(clip, body, body.defaultValue);
                SetValue(clip, hair, hair.defaultValue);
                var removed = RimeEmoClips.RemoveDefaults(clip, project);
                Assert(removed == 1, "selected face default removal count");
                Assert(AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(body)) == null, "selected Body default was retained");
                Assert(AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(hair)) != null, "unselected Hair default was removed");

                defaults = RimeEmoClips.DefaultClip(project);
                Assert(AnimationUtility.GetEditorCurve(defaults, RimeEmoClips.Binding(body)) != null, "DefaultClip omitted selected Body");
                Assert(AnimationUtility.GetEditorCurve(defaults, RimeEmoClips.Binding(hair)) == null, "DefaultClip included unselected Hair");
                motion.project = project;
                motion.tracks.Add(new PartTrack { part = FacePart.Mouth, states = { new MotionState { clip = clip } } });
                compiled = RimeEmoCompiler.Compile(motion);
                Assert(AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(body)) != null, "Compiler omitted selected Body");
                Assert(AnimationUtility.GetEditorCurve(compiled, RimeEmoClips.Binding(hair)) == null, "Compiler included unselected Hair");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(defaults);
                UnityEngine.Object.DestroyImmediate(compiled);
                UnityEngine.Object.DestroyImmediate(motion);
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(project);
            }
        }

        private static void TestClassification()
        {
            Assert(RimeEmoClips.Classify("brow_L") == FacePart.Brow, "brow classification");
            Assert(RimeEmoClips.Classify("mouth_smile") == FacePart.Mouth, "mouth classification");
            Assert(RimeEmoClips.Classify("eye_blink_left") == FacePart.LeftEye, "left eye classification");
            Assert(RimeEmoClips.Classify("eye_blink_right") == FacePart.RightEye, "right eye classification");
            foreach (string name in new[] { "eyeBlinkLeft", "L_Upper_Lid_Up", "LowerLid_L_Up", "まぶた左", "Eye_Close.L.001", "CatchLight_Main_Front_L", "Ｌ＿Ｅｙｅ＿Ｃｌｏｓｅ" })
                Assert(RimeEmoClips.Classify(name) == FacePart.LeftEye, "left naming variant: " + name);
            foreach (string name in new[] { "eyeBlinkRight", "R_LowerLash", "Pupil_R_Small", "右目閉じ" })
                Assert(RimeEmoClips.Classify(name) == FacePart.RightEye, "right naming variant: " + name);
            foreach (string name in new[] { "vrc.v_aa", "jawOpen", "tongueOut", "viseme_aa" })
                Assert(RimeEmoClips.Classify(name) == FacePart.Mouth, "mouth naming variant: " + name);
            foreach (string name in new[] { "EyeLook_Left", "EyeLook_Right", "Iris_Left", "Pupil_Right", "Eye_L_R", "Eyelid_close", "Bright", "", null })
                Assert(RimeEmoClips.Classify(name) == FacePart.Other, "ambiguous name assigned to eye: " + name);
        }

        private static void TestPreviewIsolation()
        {
            var avatar = new GameObject("RimeEmo Test Avatar");
            var sampleClip = new AnimationClip();
            var mesh = new Mesh();
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            RimeEmoPreview preview = null;
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.AddBlendShapeFrame("Smile", 100, new[] { Vector3.up, Vector3.up, Vector3.up }, new Vector3[3], new Vector3[3]);
                var face = new GameObject("Face"); face.transform.SetParent(avatar.transform, false);
                var renderer = face.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh;
                renderer.SetBlendShapeWeight(0, 31);
                // Bone is in a later sibling branch: cloning must use a second pass.
                var bone = new GameObject("Bone"); bone.transform.SetParent(avatar.transform, false);
                renderer.bones = new[] { bone.transform, null };
                avatar.AddComponent<Camera>();
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "Smile", defaultValue = 12 });
                preview = new RimeEmoPreview(); preview.SetAvatar(avatar); preview.SetDefaults(project);
                var clone = (GameObject)typeof(RimeEmoPreview).GetField("clone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(preview);
                var clonedRenderer = clone.transform.Find("Face").GetComponent<SkinnedMeshRenderer>();
                AssertNear(renderer.GetBlendShapeWeight(0), 31, "preview changed source BlendShape");
                AssertNear(clonedRenderer.GetBlendShapeWeight(0), 12, "preview did not apply saved default");
                Assert(clonedRenderer.bones[0] == clone.transform.Find("Bone"), "preview bone remained on source or was lost");
                Assert(clonedRenderer.bones[1] == null, "null bone changed");
                Assert(clone.GetComponent<Camera>() == null, "preview copied source behaviour");
                Assert(avatar.activeSelf && avatar.GetComponent<Camera>().enabled, "preview changed source activation");
                var binding = RimeEmoClips.Binding(project.shapes[0]);
                AnimationUtility.SetEditorCurve(sampleClip, binding, AnimationCurve.Constant(0, 1, 20));
                var getBindings = typeof(RimeEmoPreview).GetMethod("GetBindings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var cached = (System.Collections.IList)getBindings.Invoke(preview, new object[] { sampleClip });
                Assert(cached.Count == 1, "preview binding missing");
                AnimationUtility.SetEditorCurve(sampleClip, binding, AnimationCurve.Constant(0, 1, 73));
                preview.InvalidateCurve(sampleClip, binding);
                Assert(ReferenceEquals(cached, getBindings.Invoke(preview, new object[] { sampleClip })), "single shape edit rebuilt all preview bindings");
                var updatedCurve = (AnimationCurve)cached[0].GetType().GetField("curve").GetValue(cached[0]);
                AssertNear(updatedCurve.Evaluate(0), 73, "preview cache retained the old shape value");
                AnimationUtility.SetEditorCurve(sampleClip, binding, null);
                preview.InvalidateCurve(sampleClip, binding);
                Assert(((System.Collections.IList)getBindings.Invoke(preview, new object[] { sampleClip })).Count == 0, "removed shape remained in preview cache");
                preview.SetAvatar(null);
                Assert(!clone, "preview did not destroy old clone");
            }
            finally
            {
                preview?.Dispose();
                UnityEngine.Object.DestroyImmediate(sampleClip);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(project);
            }
        }

        private static void TestToolkitUI()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            RimeEmoMotion motion = null;
            var source = new AnimationClip { name = "Imported UI Source", frameRate = 30 };
            var material = new AnimationClip { name = "Shelf Test" };
            RimeEmoWindow window = null;
            try
            {

                project.shapes.Add(new ShapeEntry { path = "Face", shape = "Smile", part = FacePart.Mouth });
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "Blink", part = FacePart.LeftEye });
                var smile = RimeEmoClips.Binding(project.shapes[0]);
                var blink = RimeEmoClips.Binding(project.shapes[1]);
                AnimationUtility.SetEditorCurve(source, smile, AnimationCurve.Linear(0, 0, 1, 1));
                AnimationUtility.SetEditorCurve(source, blink, AnimationCurve.Linear(0, 10, 1, 20));
                AnimationUtility.SetAnimationEvents(source, new[] { new AnimationEvent { time = .5f, functionName = "KeepMe" } });
                var settings = AnimationUtility.GetAnimationClipSettings(source); settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(source, settings);
                AnimationUtility.SetEditorCurve(material, smile, AnimationCurve.Constant(0, 1, 10));
                project.clips.Add(new ClipEntry { clip = material });
                window = ScriptableObject.CreateInstance<RimeEmoWindow>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(RimeEmoWindow).GetField("project", flags).SetValue(window, project);
                typeof(RimeEmoWindow).GetField("mode", flags).SetValue(window, 1);
                window.Show();
                window.CreateGUI();
                motion = (RimeEmoMotion)typeof(RimeEmoWindow).GetField("motion", flags).GetValue(window);
                Assert(motion && !EditorUtility.IsPersistent(motion), "new motion must stay an unsaved queue draft");
                Assert(typeof(RimeEmoWindow).GetField("importedDraft", flags).GetValue(window) == null, "new motion was converted to curve editing");
                var root = window.rootVisualElement;
                var block = root.Q<VisualElement>("block-editor");
                window.position = new Rect(0, 0, 1000, 760);
                root.panel.visualTree.style.width = 1000; root.panel.visualTree.style.height = 760;
                root.style.width = 1000; root.style.height = 760;
                root.panel.GetType().GetMethod("ValidateLayout", flags | System.Reflection.BindingFlags.Public).Invoke(root.panel, null);
                Assert(block != null && block.style.display.value == DisplayStyle.Flex, "block editor is not visible");
                Assert(root.Q<VisualElement>("imported-curves").style.display.value == DisplayStyle.None, "imported curve editor is visible");
                Assert(root.Q<VisualElement>("timeline-host").childCount > 0, "part timeline was not populated");
                Assert(motion.tracks.Count == 5, "new motion did not create five part tracks");
                root.Q<FloatField>("time").value = .5f;
                AssertNear((float)typeof(RimeEmoWindow).GetField("time", flags).GetValue(window), .5f, "dynamic time input failed");
                root.Q<Toggle>("loop").value = true;
                Assert(motion.loop, "dynamic loop setting failed");
                var materialRow = root.Q<Label>("clip-name").parent;
                using (var enter = PointerEnterEvent.GetPooled()) { enter.target = materialRow; materialRow.SendEvent(enter); }
                Assert((float)typeof(RimeEmoWindow).GetField("time", flags).GetValue(window) == .5f, "static material hover sought draft");
                var preview = new object[] { null, null, 0f };
                typeof(RimeEmoWindow).GetMethod("ResolveToolkitPreview", flags).Invoke(window, preview);
                Assert(ReferenceEquals(preview[0], material), "material hover preview missing");
                using (var leave = PointerLeaveEvent.GetPooled()) { leave.target = materialRow; materialRow.SendEvent(leave); }
                typeof(RimeEmoWindow).GetMethod("ResolveToolkitPreview", flags).Invoke(window, preview);
                Assert(ReferenceEquals(preview[1], motion), "dynamic preview did not use motion");
                typeof(RimeEmoWindow).GetMethod("AddTimelineKey", flags).Invoke(window, new object[] { 0, .5f, material });
                bool hasBlock = false; foreach (var track in motion.tracks) if (track.states.Count > 0) hasBlock = true;
                Assert(hasBlock, "key add operation did not create a block");
                typeof(RimeEmoWindow).GetMethod("SwitchMode", flags).Invoke(window, new object[] { 0 });
                typeof(RimeEmoWindow).GetMethod("RefreshToolkit", flags).Invoke(window, null);
                Assert(ReferenceEquals((RimeEmoMotion)typeof(RimeEmoWindow).GetField("motion", flags).GetValue(window), motion), "static switch discarded motion");
                typeof(RimeEmoWindow).GetMethod("SwitchMode", flags).Invoke(window, new object[] { 1 });
                Assert(ReferenceEquals((RimeEmoMotion)typeof(RimeEmoWindow).GetField("motion", flags).GetValue(window), motion), "dynamic switch recreated motion");
            }
            finally
            {
                if (window) { window.DiscardChanges(); window.Close(); }
                Undo.ClearUndo(motion);
                DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(motion);
                UnityEngine.Object.DestroyImmediate(project);
            }
        }

        private static void TestStaticToolkitUI()
        {
            string folder = "Assets/RimeEmoStaticCheck-" + Guid.NewGuid().ToString("N");
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var clip = new AnimationClip();
            RimeEmoWindow window = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                AssetDatabase.CreateFolder(folder, "Animations");
                AssetDatabase.CreateAsset(project, folder + "/Project.asset");
                AssetDatabase.CreateAsset(clip, folder + "/Animations/Test.anim");
                var shape = new ShapeEntry { path = "Body", shape = "Smile" };
                project.shapes.Add(shape); project.shapes.Add(new ShapeEntry { path = "Body", shape = "Blink" });
                project.clips.Add(new ClipEntry { clip = clip }); SetValue(clip, shape, 20);
                window = ScriptableObject.CreateInstance<RimeEmoWindow>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(RimeEmoWindow).GetField("project", flags).SetValue(window, project);
                typeof(RimeEmoWindow).GetField("selected", flags).SetValue(window, clip);
                typeof(RimeEmoWindow).GetField("facePaths", flags).SetValue(window, new[] { "Body" });
                window.Show(); window.CreateGUI();
                var root = window.rootVisualElement;
                root.panel.visualTree.style.width = 1000; root.panel.visualTree.style.height = 760;
                root.style.width = 1000; root.style.height = 760;
                root.panel.GetType().GetMethod("ValidateLayout", flags | System.Reflection.BindingFlags.Public).Invoke(root.panel, null);
                Assert(root.Q<VisualElement>("static-controls").resolvedStyle.display == DisplayStyle.Flex, "static UXML not visible");
                // Compare with the window root: a floating test window places it below its tab (y > 0).
                Assert(root.Q<VisualElement>("static-controls").worldBound.yMax <= root.worldBound.yMax + 1, "static editor overflows minimum height: "
                    + string.Join(", ", new[] { "utility-toolbar", "notice", "editor-page", "editor", "preview-panel", "static-controls", "gesture-page", "settings-page" }
                        .Select(id => root.Q<VisualElement>(id)).Where(e => e != null)
                        .Select(e => e.name + " y=" + e.worldBound.yMin.ToString("0") + "-" + e.worldBound.yMax.ToString("0") + " " + e.resolvedStyle.display))
                        + ", root y=" + root.worldBound.yMin.ToString("0") + "-" + root.worldBound.yMax.ToString("0"));
                var shapeList = root.Q<ListView>("shape-list");
                Assert(shapeList.worldBound.height > 80, "static shape list has no usable height");
                Assert(shapeList.itemsSource.Count == 2, "single shape list did not show included and unset shapes");
                Assert(Included(shapeList.itemsSource[0]) && !Included(shapeList.itemsSource[1]), "shape rows lost their clip state");
                Assert(root.Query<IMGUIContainer>().ToList().Count == 1, "static editor still has an IMGUI UI bridge");
                var slider = root.Q<Slider>("shape-value"); Assert(slider != null, "static slider not realized");
                slider.value = 65;
                AssertNear(RimeEmoClips.Value(clip, shape), 65, "static slider did not update clip");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                AssertNear(RimeEmoClips.Value(clip, shape), 20, "static value undo failed");
                // Dragging must not repeatedly write curves or create Undo snapshots.
                typeof(RimeEmoWindow).GetMethod("RefreshToolkit", flags).Invoke(window, null);
                root.panel.GetType().GetMethod("ValidateLayout", flags | System.Reflection.BindingFlags.Public).Invoke(root.panel, null);
                slider = root.Q<Slider>("shape-value");
                using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = slider.worldBound.center }))
                { down.target = slider; slider.SendEvent(down); }
                slider.value = 40; slider.value = 60; slider.value = 80;
                AssertNear(RimeEmoClips.Value(clip, shape), 20, "drag wrote intermediate values to the asset");
                using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = slider.worldBound.center }))
                { up.target = slider; slider.SendEvent(up); }
                AssertNear(RimeEmoClips.Value(clip, shape), 80, "drag release lost the final value");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                AssertNear(RimeEmoClips.Value(clip, shape), 20, "drag did not undo as one change");
                root.Q<TextField>("shape-search").value = "Blink";
                Assert(shapeList.itemsSource.Count == 1 && !Included(shapeList.itemsSource[0]), "shape search did not filter");
                root.Q<TextField>("shape-search").value = "";
                var selectState = typeof(RimeEmoWindow).GetMethod("SelectShapeState", flags);
                selectState.Invoke(window, new object[] { ShapeState.Included });
                Assert(shapeList.itemsSource.Count == 1 && Included(shapeList.itemsSource[0]), "included filter did not filter");
                selectState.Invoke(window, new object[] { ShapeState.Unset });
                Assert(shapeList.itemsSource.Count == 1 && !Included(shapeList.itemsSource[0]), "unset filter did not filter");
                // Moving an unset slider adds the shape, and the row stays listed under the current filter.
                root.panel.GetType().GetMethod("ValidateLayout", flags | System.Reflection.BindingFlags.Public).Invoke(root.panel, null);
                var blink = project.shapes[1];
                root.Q<Slider>("shape-value").value = 30;
                AssertNear(RimeEmoClips.Value(clip, blink), 30, "unset slider did not add the shape");
                Assert(AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(blink)) != null, "unset slider did not create a curve");
                typeof(RimeEmoWindow).GetMethod("RefreshStaticToolkit", flags).Invoke(window, null);
                Assert(shapeList.itemsSource.Count == 1, "an edited row vanished from the current filter");
                selectState.Invoke(window, new object[] { ShapeState.Changed });
                Assert(shapeList.itemsSource.Count == 2, "changed filter missed values that differ from Default Face");
                selectState.Invoke(window, new object[] { ShapeState.All });
                Assert(RimeEmoWindow.IsSeparatorShape("---------EyeLook--------") && RimeEmoWindow.IsSeparatorShape("====") && !RimeEmoWindow.IsSeparatorShape("Eyelid_close_R"), "separator rule misclassified");
                Assert(RimeEmoWindow.IsVrcShape("vrc.v_aa") && RimeEmoWindow.IsVrcShape("vrc_lookup") && !RimeEmoWindow.IsVrcShape("Smile"), "VRChat key rule misclassified");
            }
            finally
            {
                if (window) { window.DiscardChanges(); window.Close(); }
                Undo.ClearUndo(clip); Undo.ClearUndo(project);
                AssetDatabase.DeleteAsset(folder);
                if (clip) UnityEngine.Object.DestroyImmediate(clip);
                if (project) UnityEngine.Object.DestroyImmediate(project);
            }
        }

        private static bool Included(object row)
            => (bool)row.GetType().GetField("included", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(row);

        private static void SetValue(AnimationClip clip, ShapeEntry shape, float value)
        {
            AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(shape), AnimationCurve.Constant(0, 1f / 60, value));
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void AssertNear(float actual, float expected, string message)
        {
            if (Mathf.Abs(actual - expected) > 0.01f)
                throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
        }
    }
}







