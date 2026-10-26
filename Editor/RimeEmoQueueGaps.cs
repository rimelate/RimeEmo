using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RimeEmo.Editor
{
    // Compact exact quiet spans to boundary poses; NormalizeKeeps turns their Step gaps into holds.
    internal static class RimeEmoQueueGaps
    {
        internal static void Compact(RimeEmoMotion motion, PartTrack track, int index)
        {
            var state = track.states[index];
            var shapes = RimeEmoFace.Shapes(motion.project).Where(s => s.part == track.part).ToArray();
            var curves = new Dictionary<AnimationClip, AnimationCurve[]>();
            Func<MotionState, float, float[]> sample = (s, local) =>
            {
                if (!s.clip) return shapes.Select(shape => shape.defaultValue).ToArray();
                if (!curves.TryGetValue(s.clip, out var source))
                    curves[s.clip] = source = shapes.Select(shape => RimeEmoQueueCurves.SourceCurve(s.clip, shape)).ToArray();
                return shapes.Select((shape, i) => source[i] == null || source[i].length == 0 ? shape.defaultValue
                    : source[i].Evaluate(s.animated ? s.sourceStart + local * s.speed : 0)).ToArray();
            };
            sample(state, 0);
            if (!curves.TryGetValue(state.clip, out var bound) || bound.All(c => c == null || c.length == 0))
            {
                RimeEmoTimeline.Remove(track, index);
                return;
            }
            // Keep both sides of a possible step at the end of the quiet span.
            var end = new MotionState { clip = state.clip, animated = state.animated,
                sourceStart = state.sourceStart + state.hold * state.speed, speed = state.speed,
                hold = 0, endHold = state.endHold, transition = state.transition, interpolation = state.interpolation };
            state.transition = state.hold; state.hold = 0; state.endHold = 0; state.interpolation = Interpolation.Step;
            track.states.Insert(index + 1, end);
            var points = new HashSet<MotionState> { state, end };
            bool changed;
            do
            {
                changed = false;
                for (int i = track.states.Count - 1; i >= 0; i--)
                {
                    var point = track.states[i];
                    if (!points.Contains(point)) continue;
                    var value = sample(point, 0);
                    var previous = i > 0 ? track.states[i - 1] : null;
                    var next = i + 1 < track.states.Count ? track.states[i + 1] : null;
                    if (next != null && Same(value, sample(next, 0)) && (i == 0 || (point.transition == 0 && point.endHold == 0)))
                    {
                        RimeEmoTimeline.Remove(track, i); changed = true; continue;
                    }
                    if (previous != null && Same(value, sample(previous, previous.hold)))
                    {
                        if (next != null && previous.transition == 0 && previous.endHold == 0 && point.endHold == 0)
                        {
                            previous.interpolation = point.interpolation;
                            RimeEmoTimeline.Remove(track, i); changed = true; continue;
                        }
                        if (next == null && TailCovered(motion, track, i))
                        {
                            RimeEmoTimeline.Remove(track, i); changed = true; continue;
                        }
                    }
                    if (previous == null && next == null && Same(value, shapes.Select(s => s.defaultValue).ToArray())
                        && TailCovered(motion, track, i))
                    {
                        RimeEmoTimeline.Remove(track, i); changed = true;
                    }
                }
            } while (changed);
        }

        private static bool Same(float[] a, float[] b) => a.Length == b.Length
            && a.Select((value, i) => !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value - b[i]) <= .0001f).All(v => v);

        private static bool TailCovered(RimeEmoMotion motion, PartTrack track, int index)
        {
            var starts = RimeEmoTimeline.Times(track);
            float remaining = Mathf.Max(1f / 60, motion.sourceClip ? motion.sourceClip.length : 0,
                index > 0 ? starts[index - 1] + track.states[index - 1].hold + track.states[index - 1].endHold : 0);
            foreach (var other in motion.tracks) if (other != track) remaining = Mathf.Max(remaining, RimeEmoCompiler.Duration(other));
            return starts[index] <= remaining + .000001f;
        }
    }
}
