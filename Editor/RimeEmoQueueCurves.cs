using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoQueueCurves
    {
        internal static AnimationCurve SourceCurve(AnimationClip clip, ShapeEntry shape)
            => clip ? AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(shape)) : null;

        internal static AnimationCurve Copy(AnimationCurve curve)
            => new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };

        internal static void Append(List<Keyframe> keys, Keyframe key)
        {
            if (keys.Count > 0 && Mathf.Abs(keys[keys.Count - 1].time - key.time) < 0.000001f)
            {
                var previous = keys[keys.Count - 1];
                if (Mathf.Abs(previous.value - key.value) > 0.00001f)
                {
                    // Represent a discontinuous edit with the closest practical left-limit key.
                    float epsilon = Mathf.Max(0.000001f, Mathf.Abs(key.time) * 0.0000002f);
                    previous.time = key.time - epsilon; previous.outTangent = float.PositiveInfinity;
                    if (keys.Count == 1 || previous.time > keys[keys.Count - 2].time)
                    { keys[keys.Count - 1] = previous; keys.Add(key); return; }
                }
                key.inTangent = previous.inTangent; key.inWeight = previous.inWeight;
                key.weightedMode = (key.weightedMode & WeightedMode.Out) | (previous.weightedMode & WeightedMode.In);
                keys[keys.Count - 1] = key;
            }
            else keys.Add(key);
        }

        internal static void AddMapped(List<Keyframe> destination, AnimationCurve source,
            float outputStart, float outputDuration, float sourceStart, float speed)
        {
            if (source == null || source.length == 0) return;
            speed = Mathf.Max(.000001f, speed);
            float end = sourceStart + outputDuration * speed;
            var keys = source.keys;
            var sliced = new List<Keyframe>();
            if (sourceStart < keys[0].time)
            {
                Append(sliced, new Keyframe(sourceStart, source.Evaluate(sourceStart)));
                Append(sliced, new Keyframe(Mathf.Min(end, keys[0].time), source.Evaluate(Mathf.Min(end, keys[0].time))));
            }
            for (int i = 0; i + 1 < keys.Length; i++)
            {
                float from = Mathf.Max(sourceStart, keys[i].time), to = Mathf.Min(end, keys[i + 1].time);
                if (to <= from) continue;
                Slice(keys[i], keys[i + 1], from, to, out var left, out var right);
                Append(sliced, left); Append(sliced, right);
            }
            if (end > keys[keys.Length - 1].time)
            {
                float from = Mathf.Max(sourceStart, keys[keys.Length - 1].time);
                Append(sliced, new Keyframe(from, source.Evaluate(from)));
                Append(sliced, new Keyframe(end, source.Evaluate(end)));
            }
            if (sliced.Count == 0) Append(sliced, new Keyframe(sourceStart, source.Evaluate(sourceStart)));
            foreach (var original in sliced)
            {
                var key = original; key.time = outputStart + (key.time - sourceStart) / speed;
                key.inTangent *= speed; key.outTangent *= speed;
                Append(destination, key);
            }
        }

        // Subdivide the time/value Bezier, including weighted time handles.
        private static void Slice(Keyframe a, Keyframe b, float from, float to, out Keyframe left, out Keyframe right)
        {
            left = a; right = b;
            if (from == a.time && to == b.time) return;
            if (float.IsInfinity(a.outTangent) || float.IsInfinity(b.inTangent))
            {
                left = new Keyframe(from, a.value, 0, float.PositiveInfinity);
                right = new Keyframe(to, to == b.time ? b.value : a.value, float.PositiveInfinity, 0);
                if (from == a.time) { left.inTangent = a.inTangent; left.inWeight = a.inWeight; }
                if (to == b.time) { right.outTangent = b.outTangent; right.outWeight = b.outWeight; }
                return;
            }
            float span = b.time - a.time;
            float ow = (a.weightedMode & WeightedMode.Out) != 0 ? a.outWeight : 1f / 3;
            float iw = (b.weightedMode & WeightedMode.In) != 0 ? b.inWeight : 1f / 3;
            var p = new[] { new Vector2(a.time, a.value), new Vector2(a.time + span * ow, a.value + span * ow * a.outTangent),
                new Vector2(b.time - span * iw, b.value - span * iw * b.inTangent), new Vector2(b.time, b.value) };
            if (to < b.time) { Split(p, Parameter(p, to), out var before, out _); p = before; }
            if (from > a.time) { Split(p, Parameter(p, from), out _, out var after); p = after; }
            float duration = to - from;
            left.time = from; left.value = p[0].y;
            left.outTangent = Slope(p[0], p[1]); left.outWeight = Mathf.Clamp01((p[1].x - p[0].x) / duration);
            left.weightedMode |= WeightedMode.Out;
            right.time = to; right.value = p[3].y;
            right.inTangent = Slope(p[2], p[3]); right.inWeight = Mathf.Clamp01((p[3].x - p[2].x) / duration);
            right.weightedMode |= WeightedMode.In;
        }
        private static float Slope(Vector2 a, Vector2 b) => Mathf.Abs(b.x - a.x) < 1e-12f ? 0 : (b.y - a.y) / (b.x - a.x);
        private static float Parameter(Vector2[] p, float time)
        {
            float lo = 0, hi = 1;
            for (int i = 0; i < 40; i++)
            {
                float t = (lo + hi) * .5f, u = 1 - t;
                float x = u*u*u*p[0].x + 3*u*u*t*p[1].x + 3*u*t*t*p[2].x + t*t*t*p[3].x;
                if (x < time) lo = t; else hi = t;
            }
            return (lo + hi) * .5f;
        }
        private static void Split(Vector2[] p, float t, out Vector2[] left, out Vector2[] right)
        {
            var a = Vector2.LerpUnclamped(p[0], p[1], t); var b = Vector2.LerpUnclamped(p[1], p[2], t); var c = Vector2.LerpUnclamped(p[2], p[3], t);
            var d = Vector2.LerpUnclamped(a, b, t); var e = Vector2.LerpUnclamped(b, c, t); var f = Vector2.LerpUnclamped(d, e, t);
            left = new[] { p[0], a, d, f }; right = new[] { f, e, c, p[3] };
        }
    }
}
