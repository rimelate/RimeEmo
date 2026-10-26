using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    // Read source curves, split editing boundaries, and represent constant spans as native gaps.
    internal static class RimeEmoAutoSplit
    {
        internal const float DefaultMinimumDuration = .2f;
        internal const float DefaultTolerance = 0;
        private const double ValueEpsilon = .0001;
        private const float TimeEpsilon = .00001f;
        internal sealed class Interval
        {
            internal int track, state;
            internal float start, end;
            internal bool constant;
        }

        internal static List<Interval> Analyze(RimeEmoMotion motion, IEnumerable<Vector2Int> selection, float minimumDuration, float tolerance)
        {
            if (!Finite(minimumDuration) || minimumDuration <= 0) throw new ArgumentOutOfRangeException(nameof(minimumDuration));
            if (!Finite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            var result = new List<Interval>();
            if (!motion || !motion.project || selection == null) return result;
            var cache = new Dictionary<AnimationClip, Dictionary<FacePart, CurveData[]>>();
            foreach (var selected in selection.Distinct().OrderBy(p => p.x).ThenBy(p => p.y))
            {
                if (!Valid(motion, selected.x, selected.y)) continue;
                var track = motion.tracks[selected.x]; var state = track.states[selected.y];
                if (!state.animated || !state.clip || !Finite(state.hold) || state.hold <= 0 || !Finite(state.speed) || state.speed <= 0
                    || !Finite(state.sourceStart) || state.sourceStart < 0) continue;
                float at = RimeEmoTimeline.Times(track)[selected.y];
                if (!Finite(at) || !Finite(at + state.hold)) continue;
                if (!cache.TryGetValue(state.clip, out var parts)) cache[state.clip] = parts = new Dictionary<FacePart, CurveData[]>();
                if (!parts.TryGetValue(track.part, out var curves))
                {
                    curves = RimeEmoFace.Shapes(motion.project).Where(s => s.part == track.part)
                        .Select(s => AnimationUtility.GetEditorCurve(state.clip, RimeEmoClips.Binding(s)))
                        .Where(c => c != null && c.length > 0).Select(c => new CurveData(c)).ToArray();
                    parts[track.part] = curves;
                }
                double from = state.sourceStart, to = from + (double)state.hold * state.speed;
                var boundaries = new SortedSet<double> { from, to };
                foreach (var curve in curves)
                    foreach (var key in curve.keys) if (key.time > from && key.time < to) boundaries.Add(key.time);
                var times = boundaries.ToArray();
                var low = new double[curves.Length]; var high = new double[curves.Length];
                var segmentLow = new double[curves.Length]; var segmentHigh = new double[curves.Length];
                double runStart = from, runEnd = from; bool running = false;
                for (int i = 0; i + 1 < times.Length; i++)
                {
                    bool quiet = true, extends = running;
                    for (int c = 0; c < curves.Length; c++)
                    {
                        if (!curves[c].Range(times[i], times[i + 1], out segmentLow[c], out segmentHigh[c])
                            || segmentHigh[c] - segmentLow[c] > tolerance + ValueEpsilon) quiet = false;
                        if (running && Math.Max(high[c], segmentHigh[c]) - Math.Min(low[c], segmentLow[c]) > tolerance + ValueEpsilon) extends = false;
                    }
                    if (running && (!quiet || !extends))
                    {
                        AddInterval(result, selected, at, state, runStart, runEnd, minimumDuration, low, high);
                        running = false;
                    }
                    if (!quiet) continue;
                    if (!running)
                    {
                        runStart = times[i];
                        Array.Copy(segmentLow, low, low.Length); Array.Copy(segmentHigh, high, high.Length);
                        running = true;
                    }
                    else for (int c = 0; c < curves.Length; c++) { low[c] = Math.Min(low[c], segmentLow[c]); high[c] = Math.Max(high[c], segmentHigh[c]); }
                    runEnd = times[i + 1];
                }
                if (running) AddInterval(result, selected, at, state, runStart, runEnd, minimumDuration, low, high);
            }
            return result;
        }

        private static void AddInterval(List<Interval> result, Vector2Int selected, float at, MotionState state, double from, double to, float minimum, double[] low, double[] high)
        {
            if ((to - from) / state.speed + .000001 < minimum) return;
            result.Add(new Interval { track = selected.x, state = selected.y,
                start = at + (float)((from - state.sourceStart) / state.speed), end = at + (float)((to - state.sourceStart) / state.speed),
                constant = low.Select((value, index) => high[index] - value).All(range => range <= ValueEpsilon) });
        }

        internal static int CutCount(RimeEmoMotion motion, IEnumerable<Interval> intervals) => Cuts(motion, intervals).Sum(p => p.Value.Count);
        internal static int GapCount(RimeEmoMotion motion, IEnumerable<Interval> intervals) => intervals == null ? 0
            : intervals.Count(i => i != null && i.constant && i.end - i.start > TimeEpsilon && Valid(motion, i.track, i.state));

        internal static int Apply(RimeEmoMotion motion, IEnumerable<Interval> intervals)
        {
            var candidates = intervals?.Where(i => i != null).ToArray() ?? Array.Empty<Interval>();
            float duration = motion ? RimeEmoCompiler.Duration(motion) : 0;
            int count = 0;
            // Descending original indices and times keep every remaining source index valid.
            foreach (var entry in Cuts(motion, candidates).OrderByDescending(p => p.Key.x).ThenByDescending(p => p.Key.y))
                foreach (float time in entry.Value.OrderByDescending(t => t))
                    if (RimeEmoTimeline.Split(motion.tracks[entry.Key.x], entry.Key.y, time) >= 0) count++;
            foreach (var interval in candidates.Where(i => i.constant).OrderByDescending(i => i.track).ThenByDescending(i => i.start))
            {
                if (!motion || interval.track < 0 || interval.track >= motion.tracks.Count) continue;
                var track = motion.tracks[interval.track]; var starts = RimeEmoTimeline.Times(track);
                for (int i = starts.Length - 1; i >= 0; i--)
                    if (track.states[i].animated && track.states[i].hold > TimeEpsilon
                        && Mathf.Abs(starts[i] - interval.start) <= TimeEpsilon
                        && Mathf.Abs(starts[i] + track.states[i].hold - interval.end) <= TimeEpsilon)
                        RimeEmoQueueGaps.Compact(motion, track, i);
            }
            RimeEmoTimeline.NormalizeKeeps(motion);
            if (motion)
                foreach (int t in candidates.Select(i => i.track).Distinct().Where(t => t >= 0 && t < motion.tracks.Count))
                {
                    var track = motion.tracks[t];
                    if (track.states.Count == 0) continue;
                    track.states[track.states.Count - 1].endHold += Mathf.Max(0, duration - RimeEmoCompiler.Duration(track));
                }
            return count;
        }

        private static Dictionary<Vector2Int, List<float>> Cuts(RimeEmoMotion motion, IEnumerable<Interval> intervals)
        {
            var result = new Dictionary<Vector2Int, List<float>>();
            if (!motion || intervals == null) return result;
            foreach (var interval in intervals)
            {
                if (interval == null || !Valid(motion, interval.track, interval.state)) continue;
                var track = motion.tracks[interval.track]; var state = track.states[interval.state];
                if (!state.animated || !state.clip) continue;
                float start = RimeEmoTimeline.Times(track)[interval.state], end = start + state.hold;
                var id = new Vector2Int(interval.track, interval.state);
                foreach (float time in new[] { interval.start, interval.end })
                {
                    if (!Finite(time) || time > 600 || time <= start + TimeEpsilon || time >= end - TimeEpsilon) continue;
                    if (!result.TryGetValue(id, out var cuts)) result[id] = cuts = new List<float>();
                    if (!cuts.Any(t => Mathf.Abs(t - time) <= TimeEpsilon)) cuts.Add(time);
                }
            }
            return result;
        }
        private static bool Valid(RimeEmoMotion motion, int track, int state) => motion && track >= 0 && track < motion.tracks.Count
            && motion.tracks[track] != null && state >= 0 && state < motion.tracks[track].states.Count && motion.tracks[track].states[state] != null;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class CurveData
        {
            internal readonly Keyframe[] keys;
            private readonly AnimationCurve curve;
            internal CurveData(AnimationCurve curve) { this.curve = curve; keys = curve.keys; }

            // Analyze the continuous Bezier, including extrema between authored keys.
            // The union of all curve key times ensures this interval crosses no key.
            internal bool Range(double from, double to, out double min, out double max)
            {
                min = max = 0;
                if (to <= keys[0].time)
                {
                    if (Repeats(curve.preWrapMode) && keys.Length > 1) return false;
                    min = max = keys[0].value; return Finite(keys[0].value);
                }
                if (from >= keys[keys.Length - 1].time)
                {
                    if (Repeats(curve.postWrapMode) && keys.Length > 1) return false;
                    min = max = keys[keys.Length - 1].value; return Finite(keys[keys.Length - 1].value);
                }
                int lo = 0, hi = keys.Length - 1;
                while (lo + 1 < hi) { int mid = (lo + hi) / 2; if (keys[mid].time <= from) lo = mid; else hi = mid; }
                var a = keys[lo]; var b = keys[lo + 1];
                if (!Finite(a.value) || !Finite(b.value) || float.IsNaN(a.outTangent) || float.IsNaN(b.inTangent)) return false;
                if (float.IsInfinity(a.outTangent) || float.IsInfinity(b.inTangent))
                {
                    // A stepped segment holds a up to (but not including) the next key.
                    min = max = a.value; return true;
                }
                double span = b.time - a.time;
                if (span <= 0) return false;
                double ow = (a.weightedMode & WeightedMode.Out) != 0 ? a.outWeight : 1.0 / 3;
                double iw = (b.weightedMode & WeightedMode.In) != 0 ? b.inWeight : 1.0 / 3;
                if (double.IsNaN(ow) || double.IsNaN(iw) || ow < 0 || ow > 1 || iw < 0 || iw > 1) return false;
                double u0 = Parameter((from - a.time) / span, ow, 1 - iw), u1 = Parameter((to - a.time) / span, ow, 1 - iw);
                double y0 = a.value, y1 = a.value + span * ow * a.outTangent, y2 = b.value - span * iw * b.inTangent, y3 = b.value;
                double left = Bezier(y0, y1, y2, y3, u0), right = Bezier(y0, y1, y2, y3, u1);
                min = Math.Min(left, right); max = Math.Max(left, right);
                double qa = 3 * (-y0 + 3 * y1 - 3 * y2 + y3), qb = 6 * (y0 - 2 * y1 + y2), qc = 3 * (y1 - y0);
                if (Math.Abs(qa) < 1e-12)
                {
                    if (Math.Abs(qb) > 1e-12) Include(-qc / qb, u0, u1, y0, y1, y2, y3, ref min, ref max);
                }
                else
                {
                    double discriminant = qb * qb - 4 * qa * qc;
                    if (discriminant >= 0)
                    {
                        double root = Math.Sqrt(discriminant);
                        Include((-qb - root) / (2 * qa), u0, u1, y0, y1, y2, y3, ref min, ref max);
                        Include((-qb + root) / (2 * qa), u0, u1, y0, y1, y2, y3, ref min, ref max);
                    }
                }
                return !double.IsNaN(min) && !double.IsNaN(max) && !double.IsInfinity(min) && !double.IsInfinity(max);
            }
            private static bool Repeats(WrapMode wrap) => wrap == WrapMode.Loop || wrap == WrapMode.PingPong;
            private static double Bezier(double a, double b, double c, double d, double t)
            { double u = 1 - t; return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d; }
            private static double Parameter(double time, double outWeight, double inPosition)
            {
                if (time <= 0) return 0; if (time >= 1) return 1;
                double lo = 0, hi = 1;
                for (int i = 0; i < 40; i++) { double mid = (lo + hi) * .5; if (Bezier(0, outWeight, inPosition, 1, mid) < time) lo = mid; else hi = mid; }
                return (lo + hi) * .5;
            }
            private static void Include(double t, double from, double to, double a, double b, double c, double d, ref double min, ref double max)
            {
                if (t <= from || t >= to) return;
                double value = Bezier(a, b, c, d, t); min = Math.Min(min, value); max = Math.Max(max, value);
            }
        }
    }
}
