using System;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoTimingChecks
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("Timing: " + message); }
        static void Near(float a, float b, string message) => Check(Mathf.Abs(a - b) < .001f, message + " (" + a + " / " + b + ")");
        static AnimationCurve Curve(RimeEmoMotion motion)
        {
            RimeEmoCompiler.BuildCurves(motion, out var bindings, out var curves);
            Check(curves.Length == 1, "fixture curve missing"); return curves[0];
        }
        internal static void Run()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var motion = ScriptableObject.CreateInstance<RimeEmoMotion>();
            var poses = ScriptableObject.CreateInstance<RimeEmoMotion>();
            var clone = ScriptableObject.CreateInstance<RimeEmoMotion>();
            var source = new AnimationClip(); var pose = new AnimationClip(); var next = new AnimationClip();
            try
            {
                var shape = new ShapeEntry { path = "Face", shape = "Smile", part = FacePart.Mouth };
                project.shapes.Add(shape); motion.project = poses.project = project;
                var binding = RimeEmoClips.Binding(shape);
                AnimationUtility.SetEditorCurve(source, binding, AnimationCurve.Linear(0, 0, .5f, 80));
                AnimationUtility.SetEditorCurve(pose, binding, AnimationCurve.Constant(0, 1, 80));
                AnimationUtility.SetEditorCurve(next, binding, AnimationCurve.Constant(0, 1, 20));
                var track = new PartTrack { part = FacePart.Mouth };
                var animated = new MotionState { clip = source, animated = true, hold = .5f, endHold = .7f, transition = .3f, interpolation = Interpolation.Linear };
                track.states.Add(animated); track.states.Add(new MotionState { clip = next, hold = .2f }); motion.tracks.Add(track);
                var staticTrack = new PartTrack { part = FacePart.Mouth, startTime = .5f };
                staticTrack.states.Add(new MotionState { clip = pose, hold = .7f, transition = .3f, interpolation = Interpolation.Linear });
                staticTrack.states.Add(new MotionState { clip = next, hold = .2f }); poses.tracks.Add(staticTrack);
                Near(RimeEmoTimeline.ContentDuration(staticTrack.states[0]), 0, "static cue is not a point");
                Near(RimeEmoTimeline.HoldDuration(animated), .7f, "animated hold includes playback");
                var a = Curve(motion); var b = Curve(poses);
                for (int i = 0; i <= 1200; i++) Near(a.Evaluate(.5f + i / 1000f), b.Evaluate(.5f + i / 1000f), "static/animated timing differs");
                Near(a.Evaluate(.25f), 40, "source playback changed");
                Near(a.Evaluate(1.1f), 80, "end pose is not held");
                Near(a.Evaluate(1.35f), 50, "linear transition midpoint");
                RimeEmoTimeline.SetHoldDuration(track, 0, .9f);
                Near(animated.transition, .1f, "hold did not consume transition");
                Near(animated.hold, .5f, "hold edit trimmed source"); Near(animated.sourceStart, 0, "hold edit moved source");
                Near(RimeEmoTimeline.Times(track)[1], 1.5f, "hold edit moved next cue");
                RimeEmoTimeline.SetHoldDuration(track, 0, 0);
                Near(Curve(motion).Evaluate(1), 50, "zero hold does not immediately transition");
                RimeEmoTimeline.SetTransitionDuration(track, 0, 0);
                a = Curve(motion); Near(a.Evaluate(1.49999f), 80, "zero transition did not hold"); Near(a.Evaluate(1.5f), 20, "zero transition did not jump");
                RimeEmoTimeline.MoveHoldKey(track, 0, 1.2f);
                Near(animated.endHold, .7f, "hold handle mapping"); Near(animated.hold, .5f, "hold handle trimmed source");
                Near(RimeEmoTimeline.Times(track)[1], 1.5f, "hold handle moved next cue");
                RimeEmoTimeline.SetHoldDuration(track, 1, .6f);
                Near(RimeEmoCompiler.Duration(track), 2.1f, "final hold missing"); Near(Curve(motion).Evaluate(2.05f), 20, "final hold value");

                animated.endHold = .2f; animated.transition = .8f; animated.interpolation = Interpolation.Step;
                a = Curve(motion); RimeEmoTimeline.NormalizeKeeps(motion); b = Curve(motion);
                for (int i = 0; i <= 2100; i++) Near(a.Evaluate(i / 1000f), b.Evaluate(i / 1000f), "legacy Step migration changed motion");
                Near(animated.endHold, 1, "legacy Step not converted"); Near(animated.transition, 0, "legacy Step still a transition");
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(motion), clone);
                Near(clone.tracks[0].states[0].endHold, 1, "end hold not serialized");
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(motion, "Timing check");
                RimeEmoTimeline.SetHoldDuration(track, 0, .4f); EditorUtility.SetDirty(motion); Undo.FlushUndoRecordObjects();
                Undo.PerformUndo(); Near(motion.tracks[0].states[0].endHold, 1, "hold undo");
                Undo.PerformRedo(); Near(motion.tracks[0].states[0].endHold, .4f, "hold redo");
                track = motion.tracks[0];
                Check(RimeEmoTimeline.Split(track, 0, .25f) == 1, "source split failed");
                Near(track.states[0].endHold, 0, "split duplicated hold"); Near(track.states[1].endHold, .4f, "split lost hold");
                Near(RimeEmoTimeline.Times(track)[2], 1.5f, "split moved following cue");
                int duplicated = RimeEmoTimeline.Duplicate(track, 1); Near(track.states[duplicated].endHold, .4f, "duplicate lost hold");
                staticTrack.states[0].hold = 1; staticTrack.states[0].transition = 0;
                staticTrack.states[1] = new MotionState { clip = source, animated = true, hold = .5f, endHold = .3f, transition = 0 };
                b = Curve(poses); Near(b.Evaluate(1.49999f), 80, "mixed static/source hold ramped");
                Near(b.Evaluate(1.5f), 0, "mixed source start"); Near(b.Evaluate(2.2f), 80, "final animated hold");
                var move = new RimeEmoTimeline.BlockDrag(staticTrack, 1, RimeEmoTimeline.BlockEdge.Move);
                move.Apply(1);
                Near(RimeEmoTimeline.Times(staticTrack)[1], 1, "next cue cannot move inside static hold");
                Near(RimeEmoTimeline.HoldDuration(staticTrack.states[0]), .5f, "static hold not shortened by next cue");
                Near(staticTrack.states[1].hold, .5f, "moving next cue trimmed source");
                move.Apply(1.5f);
                Near(RimeEmoTimeline.HoldDuration(staticTrack.states[0]), 1, "return drag lost static hold");
                staticTrack.states[1].interpolation = Interpolation.Step;
                staticTrack.states[1].transition = .9f; // The last cue's unused transition must remain unused.
                float duration = RimeEmoCompiler.Duration(poses);
                RimeEmoTimeline.NormalizeKeeps(poses);
                Near(RimeEmoCompiler.Duration(poses), duration, "migration applied the final unused transition");
            }
            finally
            {
                Undo.ClearUndo(motion); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(pose); UnityEngine.Object.DestroyImmediate(next);
                UnityEngine.Object.DestroyImmediate(motion); UnityEngine.Object.DestroyImmediate(poses); UnityEngine.Object.DestroyImmediate(clone); UnityEngine.Object.DestroyImmediate(project);
            }
        }
    }
}
