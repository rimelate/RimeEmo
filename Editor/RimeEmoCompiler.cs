using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoCompiler
    {
        internal static float Duration(PartTrack track)
        {
            float time = Mathf.Max(0, track.startTime);
            for (int i = 0; i < track.states.Count; i++)
            {
                time += Mathf.Max(0, track.states[i].hold);
                time += Mathf.Max(0, track.states[i].endHold);
                if (i + 1 < track.states.Count) time += Mathf.Max(0, track.states[i].transition);
            }
            return time;
        }
        internal static float Duration(RimeEmoMotion motion) => Mathf.Max(1f / 60,
            motion && motion.sourceClip ? motion.sourceClip.length : 0,
            motion && motion.tracks.Count > 0 ? motion.tracks.Max(Duration) : 0);

        internal static AnimationClip Compile(RimeEmoMotion motion)
        {
            BuildCurves(motion, out var bindings, out var curves);
            var clip = motion.sourceClip ? UnityEngine.Object.Instantiate(motion.sourceClip) : new AnimationClip { frameRate = 60 };
            clip.name = motion.name; clip.hideFlags = HideFlags.None;
            AnimationUtility.SetEditorCurves(clip, bindings, curves);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = motion.loop; AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        internal static void BuildCurves(RimeEmoMotion motion, out EditorCurveBinding[] resultBindings, out AnimationCurve[] resultCurves)
        {
            if (!motion || !motion.project) throw new InvalidOperationException(RimeEmoLocalization.T("MotionにProjectがありません。"));
            if (motion.tracks.GroupBy(t => t.part).Any(g => g.Count() > 1)) throw new InvalidOperationException(RimeEmoLocalization.T("同一パーツのトラックが重複しています。"));
            var bindings = new List<EditorCurveBinding>(); var curves = new List<AnimationCurve>();
            float duration = Duration(motion);
            foreach (var shape in RimeEmoFace.Shapes(motion.project))
            {
                var track = motion.tracks.FirstOrDefault(t => t.part == shape.part);
                if (motion.sourceClip && RimeEmoQueueCurves.SourceCurve(motion.sourceClip, shape) == null
                    && (track == null || track.states.All(s => s.animated))) continue;
                AnimationCurve curve;
                if (track == null || track.states.Count == 0) curve = AnimationCurve.Constant(0, duration, shape.defaultValue);
                else if (track.states.Any(s => s.animated)) curve = Temporal(shape, track, duration);
                else curve = Static(shape, track, duration);
                bindings.Add(RimeEmoClips.Binding(shape)); curves.Add(curve);
            }
            resultBindings = bindings.ToArray(); resultCurves = curves.ToArray();
        }

        private static float Value(MotionState state, ShapeEntry shape, float localTime)
        {
            if (!state.animated) return RimeEmoClips.Value(state.clip, shape);
            var curve = RimeEmoQueueCurves.SourceCurve(state.clip, shape);
            return curve == null ? shape.defaultValue : curve.Evaluate(state.sourceStart + localTime * Mathf.Max(.000001f, state.speed));
        }
        private static AnimationCurve Temporal(ShapeEntry shape, PartTrack track, float duration)
        {
            var keys = new List<Keyframe>(); float time = Mathf.Max(0, track.startTime);
            if (time > 0)
            {
                float firstValue = Value(track.states[0], shape, 0);
                keys.Add(new Keyframe(0, firstValue));
                // Append keeps this flat incoming tangent when it joins the first source key.
                keys.Add(new Keyframe(time, firstValue));
            }
            for (int i = 0; i < track.states.Count; i++)
            {
                var state = track.states[i]; float hold = RimeEmoTimeline.ContentDuration(state); int last = i;
                // A split is only an editing boundary, not a new interpolation boundary.
                while (state.animated && last + 1 < track.states.Count)
                {
                    var next = track.states[last + 1]; var previous = track.states[last];
                    if (previous.transition != 0 || previous.endHold != 0 || !next.animated || next.clip != state.clip || next.speed != state.speed
                        || Mathf.Abs(next.sourceStart - (state.sourceStart + hold * state.speed)) > .000001f) break;
                    hold += Mathf.Max(0, next.hold); last++;
                }
                var source = state.animated ? RimeEmoQueueCurves.SourceCurve(state.clip, shape) : null;
                if (source != null)
                {
                    if (i == 0 && last == track.states.Count - 1 && time == 0 && state.sourceStart == 0 && state.speed == 1
                        && track.states[last].endHold == 0 && Mathf.Abs(hold - duration) < .000001f && state.clip && Mathf.Abs(hold - state.clip.length) < .000001f)
                        return RimeEmoQueueCurves.Copy(source);
                    RimeEmoQueueCurves.AddMapped(keys, source, time, hold, state.sourceStart, state.speed);
                }
                else
                {
                    RimeEmoQueueCurves.Append(keys, new Keyframe(time, Value(state, shape, 0)));
                    RimeEmoQueueCurves.Append(keys, new Keyframe(time + hold, Value(state, shape, 0)));
                }
                time += hold; i = last;
                float keep = RimeEmoTimeline.HoldDuration(track.states[i]);
                if (keep > 0)
                {
                    var end = keys[keys.Count - 1];
                    bool jump = i + 1 < track.states.Count && track.states[i].transition <= 0
                        && Mathf.Abs(end.value - Value(track.states[i + 1], shape, 0)) > .00001f;
                    end.outTangent = jump ? float.PositiveInfinity : 0;
                    end.weightedMode &= ~WeightedMode.Out; keys[keys.Count - 1] = end;
                    time += keep;
                    if (!jump) RimeEmoQueueCurves.Append(keys, new Keyframe(time, end.value));
                }
                if (i + 1 < track.states.Count)
                {
                    float dt = Mathf.Max(0, track.states[i].transition);
                    if (dt > 0)
                    {
                        float from = keys[keys.Count - 1].value, to = Value(track.states[i + 1], shape, 0);
                        float slope = track.states[i].interpolation == Interpolation.Linear ? (to - from) / dt
                            : track.states[i].interpolation == Interpolation.Step ? float.PositiveInfinity : 0;
                        var end = keys[keys.Count - 1]; end.outTangent = slope; end.weightedMode &= ~WeightedMode.Out; keys[keys.Count - 1] = end;
                        RimeEmoQueueCurves.Append(keys, new Keyframe(time + dt, to, slope, 0)); time += dt;
                    }
                }
            }
            if (keys.Count == 0) keys.Add(new Keyframe(0, shape.defaultValue));
            if (keys[keys.Count - 1].time < duration)
            {
                var end = keys[keys.Count - 1]; end.outTangent = 0; end.weightedMode &= ~WeightedMode.Out; keys[keys.Count - 1] = end;
                keys.Add(new Keyframe(duration, end.value));
            }
            return new AnimationCurve(keys.ToArray());
        }
        private static AnimationCurve Static(ShapeEntry shape, PartTrack track, float duration)
        {
            var keys = new List<Keyframe>(); float time = Mathf.Max(0, track.startTime);
            Add(keys, 0, RimeEmoClips.Value(track.states[0].clip, shape));
            for (int i = 0; i < track.states.Count; i++)
            {
                var state = track.states[i]; float from = RimeEmoClips.Value(state.clip, shape);
                time += Mathf.Max(0, state.hold) + Mathf.Max(0, state.endHold); Add(keys, time, from);
                if (i + 1 == track.states.Count) continue;
                float to = RimeEmoClips.Value(track.states[i + 1].clip, shape), dt = Mathf.Max(0, state.transition);
                if (dt == 0) { if (keys.Count > 1) SetOut(keys, keys.Count - 2, float.PositiveInfinity); Add(keys, time, to); continue; }
                float slope = state.interpolation == Interpolation.Linear ? (to - from) / dt : state.interpolation == Interpolation.Step ? float.PositiveInfinity : 0;
                SetOut(keys, keys.Count - 1, slope); time += dt; keys.Add(new Keyframe(time, to, slope, 0));
            }
            if (keys[keys.Count - 1].time < duration) Add(keys, duration, keys[keys.Count - 1].value);
            return new AnimationCurve(keys.ToArray());
        }
        private static void SetOut(List<Keyframe> keys, int index, float tangent) { var k = keys[index]; k.outTangent = tangent; keys[index] = k; }
        private static void Add(List<Keyframe> keys, float time, float value)
        {
            if (keys.Count > 0 && keys[keys.Count - 1].time == time) { var k = keys[keys.Count - 1]; k.value = value; keys[keys.Count - 1] = k; }
            else keys.Add(new Keyframe(time, value, 0, 0));
        }
    }
}
