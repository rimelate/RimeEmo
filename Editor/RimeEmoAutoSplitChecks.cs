using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoAutoSplitChecks
    {
        internal static void Run()
        {
            TestCoreSplit();
            TestShapeFilteringAndTolerance();
            TestMappingAndGuards();
            TestImportSplit();
            TestGapBoundaries();
        }

        private static void TestGapBoundaries()
        {
            foreach (int scenario in Enumerable.Range(0, 5))
            {
                var project = ScriptableObject.CreateInstance<RimeEmoProject>();
                var motion = RimeEmoDrafts.Create(project);
                var source = new AnimationClip(); var nextClip = new AnimationClip();
                AnimationClip before = null, after = null;
                try
                {
                    var shape = new ShapeEntry { path = "Face", shape = "Mouth", part = FacePart.Mouth, defaultValue = 10 };
                    project.shapes.Add(shape); var binding = RimeEmoClips.Binding(shape);
                    var sourceCurve = scenario == 0 ? AnimationCurve.Constant(0, 2, 70)
                        : scenario == 1 ? AnimationCurve.Constant(0, 2, 10)
                        : scenario == 2 ? new AnimationCurve(new Keyframe(0, 0, 0, float.PositiveInfinity),
                            new Keyframe(.5f, 100, 0, float.PositiveInfinity), new Keyframe(1, 50))
                        : AnimationCurve.Constant(0, 2, 50);
                    AnimationUtility.SetEditorCurve(source, binding, sourceCurve);
                    motion.sourceClip = source;
                    var track = motion.tracks.Single(t => t.part == FacePart.Mouth);
                    track.states.Add(new MotionState { clip = source, animated = true, hold = 2, transition = 0 });
                    if (scenario == 2) track.states[0].hold = 1;
                    if (scenario == 3) { track.startTime = 3; track.states[0].hold = 1; track.states[0].speed = 2; }
                    if (scenario == 4)
                    {
                        AnimationUtility.SetEditorCurve(nextClip, binding, AnimationCurve.Constant(0, 1, 100));
                        track.states[0].transition = 1;
                        track.states.Add(new MotionState { clip = nextClip, hold = .5f, transition = 0 });
                    }
                    float duration = RimeEmoCompiler.Duration(motion);
                    before = RimeEmoCompiler.Compile(motion);
                    var selection = new[] { new Vector2Int(motion.tracks.IndexOf(track), 0) };
                    var intervals = RimeEmoAutoSplit.Analyze(motion, selection, .2f, 0);
                    Assert(RimeEmoAutoSplit.GapCount(motion, intervals) > 0, "constant cue had no gap operation");
                    RimeEmoAutoSplit.Apply(motion, intervals);
                    Assert(track.states.Where(s => s.animated).All(s => s.hold == 0), "constant source retained a duration cue");
                    Assert(Mathf.Abs(RimeEmoCompiler.Duration(motion) - duration) < .00001f, "gap conversion shortened animation");
                    if (scenario == 0) Assert(track.states.Count == 1, "constant non-default pose needs only one point key");
                    if (scenario == 1) Assert(track.states.Count == 0, "constant default pose left an empty queue");
                    after = RimeEmoCompiler.Compile(motion);
                    var a = AnimationUtility.GetEditorCurve(before, binding); var b = AnimationUtility.GetEditorCurve(after, binding);
                    for (int i = 0; i <= 2000; i++)
                        Assert(Mathf.Abs(a.Evaluate(duration * i / 2000) - b.Evaluate(duration * i / 2000)) < .001f, "gap changed motion in scenario " + scenario);
                    foreach (var key in sourceCurve.keys)
                        foreach (float offset in new[] { -.00001f, 0, .00001f })
                            Assert(Mathf.Abs(a.Evaluate(key.time + offset) - b.Evaluate(key.time + offset)) < .001f, "gap changed step boundary");
                }
                finally
                {
                    if (before) UnityEngine.Object.DestroyImmediate(before);
                    if (after) UnityEngine.Object.DestroyImmediate(after);
                    UnityEngine.Object.DestroyImmediate(motion); UnityEngine.Object.DestroyImmediate(source);
                    UnityEngine.Object.DestroyImmediate(nextClip); UnityEngine.Object.DestroyImmediate(project);
                }
            }
        }

        private static void TestImportSplit()
        {
            string root = "Assets/RimeEmoAutoSplitImport-" + Guid.NewGuid().ToString("N");
            RimeEmoProject project = null; AnimationClip source = null; RimeEmoMotion motion = null, reopened = null; AnimationClip output = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", root.Substring(7));
                project = ScriptableObject.CreateInstance<RimeEmoProject>();
                var left = new ShapeEntry { path = "Face", shape = "Left", part = FacePart.LeftEye };
                var right = new ShapeEntry { path = "Face", shape = "Right", part = FacePart.RightEye };
                var other = new ShapeEntry { path = "Face", shape = "Other", part = FacePart.Other };
                project.shapes.AddRange(new[] { left, right, other }); project.faceMeshSelected = true; project.faceMeshPath = "Face";
                AssetDatabase.CreateAsset(project, root + "/Project.asset");
                source = new AnimationClip { name = "ImportSplitSource", frameRate = 60 };
                foreach (var shape in project.shapes)
                    AnimationUtility.SetEditorCurve(source, RimeEmoClips.Binding(shape),
                        new AnimationCurve(new Keyframe(0, 0), new Keyframe(.5f, 0), new Keyframe(1, 100), new Keyframe(1.5f, 100), new Keyframe(2, 0), new Keyframe(2.5f, 0)));
                var extra = EditorCurveBinding.FloatCurve("Face", typeof(Transform), "m_LocalPosition.x");
                AnimationUtility.SetEditorCurve(source, extra, AnimationCurve.Linear(0, 0, 2.5f, 1));
                AnimationUtility.SetAnimationEvents(source, new[] { new AnimationEvent { time = 1.25f, functionName = "KeepEvent" } });
                AssetDatabase.CreateAsset(source, root + "/Source.anim");
                var sourceDuration = source.length;
                string original = EditorJsonUtility.ToJson(source);
                motion = RimeEmoQueueImport.Create(project, source);
                Assert(motion.sourceClip && motion.sourceClip != source, "import split did not clone source");
                foreach (var shape in project.shapes)
                {
                    var track = motion.tracks.Single(t => t.part == shape.part);
                    Assert(track.states.Count == 2, "import split did not retain only moving cues per animated part");
                    var starts = RimeEmoTimeline.Times(track);
                    for (int i = 0; i < track.states.Count; i++)
                    {
                        var state = track.states[i];
                        Assert(Mathf.Abs(starts[i] - (i == 0 ? .5f : 1.5f)) < .0001f && Mathf.Abs(state.sourceStart - starts[i]) < .0001f
                            && Mathf.Abs(state.hold - .5f) < .0001f && state.animated && state.clip == motion.sourceClip && state.speed == 1
                            && state.transition == 0 && Mathf.Abs(state.endHold - .5f) < .0001f,
                            "import split changed boundaries or source mapping");
                    }
                    Assert(RimeEmoCompiler.Duration(motion) == sourceDuration, "import split changed timeline duration");
                }
                Assert(motion.tracks.Where(t => t.part == FacePart.Brow || t.part == FacePart.Mouth).All(t => t.states.Count == 0), "import created absent parts");
                Assert(Mathf.Abs(motion.sourceClip.length - sourceDuration) < .0001f && Mathf.Abs(source.length - sourceDuration) < .0001f, "import split changed source duration");
                Assert(!EditorUtility.IsDirty(motion), "import split left the draft dirty");
                output = RimeEmoCompiler.Compile(motion);
                foreach (var binding in AnimationUtility.GetCurveBindings(source))
                {
                    var before = AnimationUtility.GetEditorCurve(source, binding);
                    var after = AnimationUtility.GetEditorCurve(output, binding);
                    Assert(after != null, "import split lost a source curve");
                    for (int i = 0; i <= 100; i++) Assert(Mathf.Abs(before.Evaluate(sourceDuration * i / 100f) - after.Evaluate(sourceDuration * i / 100f)) < .001f, "import split changed source curve");
                }
                Assert(output.events.Length == 1 && output.events[0].functionName == "KeepEvent" && output.events[0].time == 1.25f, "import split lost source event");
                Assert(EditorJsonUtility.ToJson(source) == original, "import split modified original animation");
                Assert(RimeEmoTimeline.Split(motion.tracks[0], 0, .75f) == 1, "manual edit after import failed");
                RimeEmoDrafts.SaveTo(motion, root + "/Output.anim");
                reopened = RimeEmoDrafts.Copy(project.motions.Last());
                Assert(reopened.tracks.Select(t => t.states.Count).SequenceEqual(new[] { 3, 2, 0, 0, 2 })
                    && Mathf.Abs(reopened.tracks[0].states[1].sourceStart - .75f) < .0001f, "reopening lost manual split after import");
            }
            finally
            {
                if (output) UnityEngine.Object.DestroyImmediate(output);
                if (reopened) UnityEngine.Object.DestroyImmediate(reopened);
                if (motion) { Undo.ClearUndo(motion); UnityEngine.Object.DestroyImmediate(motion); }
                AssetDatabase.DeleteAsset(root);
            }
        }

        private static void TestCoreSplit()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var motion = ScriptableObject.CreateInstance<RimeEmoMotion>();
            var clip = new AnimationClip { name = "AutoSplit Source", frameRate = 60 };
            AnimationClip baseline = null, output = null;
            try
            {
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "Smile", part = FacePart.Mouth, defaultValue = 0 });
                motion.project = project;
                motion.sourceClip = clip;
                var binding = RimeEmoClips.Binding(project.shapes[0]);
                var curve = new AnimationCurve(
                    new Keyframe(0, 0, 0, 25, .2f, .45f) { weightedMode = WeightedMode.Out },
                    new Keyframe(.4f, 100, 75, 0, .15f, .3f) { weightedMode = WeightedMode.In },
                    new Keyframe(.5f, 100), new Keyframe(.6f, 100), new Keyframe(.7f, 100),
                    new Keyframe(.8f, 100, 0, -20, .3f, .4f) { weightedMode = WeightedMode.Out }, new Keyframe(1.2f, 0, -50, 0));
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { time = .5f, functionName = "UnchangedEvent" } });
                var track = new PartTrack { part = FacePart.Mouth };
                track.states.Add(new MotionState { clip = clip, animated = true, sourceStart = 0, speed = 1, hold = 1.2f, transition = 0 });
                motion.tracks.Add(track);

                var selected = new[] { new Vector2Int(0, 0) };
                var intervals = RimeEmoAutoSplit.Analyze(motion, selected, .2f, 0);
                Assert(intervals.Count == 1, "auto-split missed the quiet interval");
                Assert(RimeEmoAutoSplit.CutCount(motion, intervals) == 2, "auto-split cut count did not match interval boundaries");
                Assert(intervals.All(i => i.track == 0 && i.state == 0 && i.end - i.start >= .2f), "auto-split returned invalid interval");
                var before = RimeEmoCompiler.Duration(track);
                baseline = RimeEmoCompiler.Compile(motion);
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(motion, "Auto Split Check");
                Assert(RimeEmoAutoSplit.Apply(motion, intervals) == 2, "auto-split did not apply expected cuts");
                Assert(Mathf.Abs(RimeEmoCompiler.Duration(track) - before) < .0001f, "auto-split changed timeline duration");
                Assert(track.states.All(s => s.animated && Mathf.Abs(s.speed - 1) < .0001f), "auto-split changed source mapping");
                Assert(track.states.All(s => s.clip == clip), "auto-split replaced source curves");
                output = RimeEmoCompiler.Compile(motion);
                var originalCurve = AnimationUtility.GetEditorCurve(baseline, binding);
                var splitCurve = AnimationUtility.GetEditorCurve(output, binding);
                for (int i = 0; i <= 240; i++) Assert(Mathf.Abs(originalCurve.Evaluate(i / 200f) - splitCurve.Evaluate(i / 200f)) < .001f, "auto-split changed weighted motion");
                Assert(output.events.Length == 1 && output.events[0].functionName == "UnchangedEvent" && output.events[0].time == .5f, "auto-split changed source event");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert(motion.tracks[0].states.Count == 1, "auto-split undo did not restore original cue");
                Undo.PerformRedo(); track = motion.tracks[0];
                Assert(track.states.Count == 2, "auto-split redo did not restore cuts");
                Assert(Mathf.Abs(RimeEmoTimeline.Times(track)[0]) < .0001f && Mathf.Abs(track.states[0].hold - .4f) < .0001f
                    && track.states[0].transition == 0 && Mathf.Abs(track.states[0].endHold - .4f) < .0001f && Mathf.Abs(RimeEmoTimeline.Times(track)[1] - .8f) < .0001f,
                    "auto-split did not preserve the stationary gap");

                var repeated = RimeEmoAutoSplit.Analyze(motion, Enumerable.Range(0, track.states.Count).Select(i => new Vector2Int(0, i)), .2f, 0);
                Assert(RimeEmoAutoSplit.CutCount(motion, repeated) == 0, "repeated analysis proposed extra cuts");
                var invalid = RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(-1, 0), new Vector2Int(9, 9) }, .2f, 0);
                Assert(invalid.Count == 0, "invalid selection was not ignored");
            }
            finally
            {
                Undo.ClearUndo(motion);
                if (baseline) UnityEngine.Object.DestroyImmediate(baseline);
                if (output) UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(motion);
                UnityEngine.Object.DestroyImmediate(project);
            }
        }

        private static void TestShapeFilteringAndTolerance()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var motion = ScriptableObject.CreateInstance<RimeEmoMotion>();
            var clip = new AnimationClip();
            try
            {
                var smile = new ShapeEntry { path = "Face", shape = "Smile", part = FacePart.Mouth };
                var mouth = new ShapeEntry { path = "Face", shape = "Mouth", part = FacePart.Mouth };
                var other = new ShapeEntry { path = "Face", shape = "Other", part = FacePart.Other };
                project.shapes.AddRange(new[] { smile, mouth, other }); motion.project = project;
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(smile), AnimationCurve.Constant(0, 1, 0));
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(mouth), AnimationCurve.Linear(0, 0, 1, 1));
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(other), AnimationCurve.Linear(0, 0, 1, 1));
                var t = new PartTrack { part = FacePart.Mouth }; t.states.Add(new MotionState { clip = clip, animated = true, hold = 1 }); motion.tracks.Add(t);
                Assert(RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(0, 0) }, .2f, 0).Count == 0, "moving face curve did not block split");
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(mouth), AnimationCurve.Constant(0, 1, 0));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Accessory", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Linear(0, 0, 1, 100));
                Assert(RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(0, 0) }, .2f, 0).Count == 1, "other parts or non-face curves blocked a quiet mouth");
                var overshoot = new AnimationCurve(new Keyframe(0, 0, 0, 200, .2f, .4f) { weightedMode = WeightedMode.Both },
                    new Keyframe(1, 0, -200, 0, .4f, .2f) { weightedMode = WeightedMode.Both });
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(smile), overshoot);
                Assert(RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(0, 0) }, .2f, 0).Count == 0, "weighted overshoot was treated as flat");
                var jitter = new AnimationCurve(new Keyframe(0, 50), new Keyframe(.2f, 50.1f), new Keyframe(.4f, 49.9f), new Keyframe(.6f, 50));
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(smile), jitter);
                t.states[0].hold = .6f;
                Assert(RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(0, 0) }, .2f, 0).Count == 0, "jitter without tolerance was selected");
                var tolerant = RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(0, 0) }, .2f, .3f);
                Assert(tolerant.Count == 1 && Mathf.Abs(tolerant[0].start) < .0001f && Mathf.Abs(tolerant[0].end - .6f) < .0001f, "tolerance did not merge low-variation keys");
                var drift = new AnimationCurve(Enumerable.Range(0, 7).Select(i => new Keyframe(i * .1f, i * .1f, 1, 1)).ToArray());
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(smile), drift);
                var driftIntervals = RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(0, 0) }, .15f, .25f);
                Assert(driftIntervals.Count > 0 && driftIntervals.All(i => drift.Evaluate(i.end) - drift.Evaluate(i.start) <= .2501f), "tolerance accumulated excess drift across keys");
                t.states[0].animated = false;
                Assert(RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(0, 0) }, .2f, 1).Count == 0, "static cue was analyzed as imported motion");
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); UnityEngine.Object.DestroyImmediate(motion); UnityEngine.Object.DestroyImmediate(project); }
        }

        private static void TestMappingAndGuards()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>(); var motion = ScriptableObject.CreateInstance<RimeEmoMotion>(); var clip = new AnimationClip();
            try
            {
                var shape = new ShapeEntry { path = "Face", shape = "Smile", part = FacePart.Mouth }; project.shapes.Add(shape); motion.project = project;
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(shape), new AnimationCurve(new Keyframe(0, 1), new Keyframe(.4f, 0), new Keyframe(.8f, 0), new Keyframe(1, 1)));
                var t = new PartTrack { part = FacePart.Mouth, startTime = 1 }; t.states.Add(new MotionState { clip = clip, animated = true, sourceStart = .2f, speed = 2, hold = .5f }); motion.tracks.Add(t);
                var found = RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(0, 0) }, .15f, 0); Assert(found.Count > 0, "mapped quiet interval was not selected");
                Assert(found.Count == 1 && Mathf.Abs(found[0].start - 1.1f) < .0001f && Mathf.Abs(found[0].end - 1.3f) < .0001f, "source mapping produced wrong output times");
                Assert(RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(0, 0) }, .25f, 0).Count == 0, "minimum hold duration ignored playback speed");
                bool threw = false; try { RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(0, 0) }, float.NaN, 0); } catch (ArgumentOutOfRangeException) { threw = true; }
                Assert(threw, "NaN minimum duration was accepted");
                threw = false; try { RimeEmoAutoSplit.Analyze(motion, new[] { new Vector2Int(0, 0) }, .2f, float.NaN); } catch (ArgumentOutOfRangeException) { threw = true; }
                Assert(threw, "NaN tolerance was accepted");
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); UnityEngine.Object.DestroyImmediate(motion); UnityEngine.Object.DestroyImmediate(project); }
        }

        private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
