using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoKeyframes
    {
        private const float Epsilon = .00001f;

        internal sealed class CopiedKey
        {
            internal string path, shape;
            internal Keyframe key;
        }

        internal static int SetKey(AnimationClip clip, ShapeEntry shape, float time, float value, Interpolation mode)
        {
            if (!clip || shape == null || !Finite(time) || !Finite(value) || time < 0 || time > 600) return -1;
            var binding = RimeEmoClips.Binding(shape); var curve = AnimationUtility.GetEditorCurve(clip, binding) ?? new AnimationCurve();
            var keys = curve.keys.ToList(); int index = keys.FindIndex(k => Mathf.Abs(k.time - time) <= Epsilon);
            var key = index >= 0 ? keys[index] : new Keyframe(time, value);
            key.time = time; key.value = value;
            if (index >= 0) keys[index] = key; else { keys.Add(key); keys.Sort((a, b) => a.time.CompareTo(b.time)); index = keys.FindIndex(k => Mathf.Abs(k.time - time) <= Epsilon); }
            curve.keys = keys.ToArray(); ApplyMode(curve, index, mode); AnimationUtility.SetEditorCurve(clip, binding, curve); return index;
        }

        internal static void SetInterpolation(AnimationClip clip, IList<ShapeEntry> shapes, IEnumerable<Vector2Int> selection, Interpolation mode)
        {
            if (!clip || shapes == null || selection == null) return;
            foreach (var group in selection.Distinct().GroupBy(p => p.x))
            {
                if (group.Key < 0 || group.Key >= shapes.Count) continue;
                var binding = RimeEmoClips.Binding(shapes[group.Key]); var curve = AnimationUtility.GetEditorCurve(clip, binding); if (curve == null) continue;
                var keys = curve.keys;
                curve.keys = keys;
                int valid = 0; foreach (var p in group) if (p.y >= 0 && p.y < keys.Length) { ApplyMode(curve, p.y, mode); valid++; }
                if (valid > 0) AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
        }

        internal static void Delete(AnimationClip clip, IList<ShapeEntry> shapes, IEnumerable<Vector2Int> selection)
        {
            if (!clip || shapes == null || selection == null) return;
            foreach (var group in selection.Distinct().GroupBy(p => p.x))
            {
                if (group.Key < 0 || group.Key >= shapes.Count) continue;
                var binding = RimeEmoClips.Binding(shapes[group.Key]); var curve = AnimationUtility.GetEditorCurve(clip, binding); if (curve == null) continue;
                var remove = new HashSet<int>(group.Select(p => p.y).Where(i => i >= 0 && i < curve.length));
                if (remove.Count == 0) continue;
                var keys = curve.keys.Where((k, i) => !remove.Contains(i)).ToArray(); curve.keys = keys; AnimationUtility.SetEditorCurve(clip, binding, keys.Length == 0 ? null : curve);
            }
        }

        internal static bool Move(AnimationClip clip, IList<ShapeEntry> shapes, IEnumerable<Vector2Int> selection, float delta, out HashSet<Vector2Int> moved)
        {
            moved = new HashSet<Vector2Int>(); if (!clip || shapes == null || selection == null || !Finite(delta) || Mathf.Abs(delta) <= Epsilon) return false;
            var selected = selection.Distinct().ToArray(); var edits = new List<Tuple<EditorCurveBinding, AnimationCurve, HashSet<int>>>();
            float minTime = float.PositiveInfinity, maxTime = float.NegativeInfinity;
            foreach (var group in selected.GroupBy(p => p.x))
            {
                if (group.Key < 0 || group.Key >= shapes.Count) continue;
                var binding = RimeEmoClips.Binding(shapes[group.Key]); var curve = AnimationUtility.GetEditorCurve(clip, binding); if (curve == null) continue;
                var indices = new HashSet<int>(group.Select(p => p.y).Where(i => i >= 0 && i < curve.length)); if (indices.Count == 0) continue;
                foreach (var i in indices) { minTime = Mathf.Min(minTime, curve.keys[i].time); maxTime = Mathf.Max(maxTime, curve.keys[i].time); }
                edits.Add(Tuple.Create(binding, curve, indices));
            }
            if (edits.Count == 0) return false;
            float shift = Mathf.Clamp(delta, -minTime, 600f - maxTime);
            if (Mathf.Abs(shift) <= Epsilon) return false;
            foreach (var edit in edits) for (int i = 0; i < edit.Item2.length; i++) if (!edit.Item3.Contains(i)) foreach (var j in edit.Item3) if (Mathf.Abs(edit.Item2.keys[i].time - (edit.Item2.keys[j].time + shift)) <= Epsilon) return false;
            foreach (var edit in edits)
            {
                var keys = edit.Item2.keys; int shapeIndex = Enumerable.Range(0, shapes.Count).First(i => RimeEmoClips.Binding(shapes[i]).Equals(edit.Item1));
                var targetTimes = edit.Item3.Select(i => keys[i].time + shift).ToArray();
                for (int i = 0; i < keys.Length; i++) if (edit.Item3.Contains(i)) keys[i].time += shift;
                Array.Sort(keys, (a, b) => a.time.CompareTo(b.time)); edit.Item2.keys = keys; AnimationUtility.SetEditorCurve(clip, edit.Item1, edit.Item2);
                for (int i = 0; i < keys.Length; i++) if (targetTimes.Any(t => Mathf.Abs(t - keys[i].time) <= Epsilon)) moved.Add(new Vector2Int(shapeIndex, i));
            }
            return moved.Count > 0;
        }

        internal static List<CopiedKey> Copy(AnimationClip clip, IList<ShapeEntry> shapes, IEnumerable<Vector2Int> selection)
        {
            var result = new List<CopiedKey>(); if (!clip || shapes == null || selection == null) return result;
            foreach (var p in selection.Distinct()) if (p.x >= 0 && p.x < shapes.Count)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(shapes[p.x])); if (curve == null || p.y < 0 || p.y >= curve.length) continue;
                result.Add(new CopiedKey { path = shapes[p.x].path, shape = shapes[p.x].shape, key = curve.keys[p.y] });
            }
            return result;
        }

        internal static HashSet<Vector2Int> Paste(AnimationClip clip, IList<ShapeEntry> shapes, IEnumerable<CopiedKey> copied, float at)
        {
            var result = new HashSet<Vector2Int>(); if (!clip || shapes == null || copied == null || !Finite(at)) return result;
            var items = copied.Where(x => x != null).ToArray(); if (items.Length == 0) return result;
            if (items.Any(x => !Finite(x.key.time) || !Finite(x.key.value) || float.IsNaN(x.key.inTangent) || float.IsNaN(x.key.outTangent)
                || !Finite(x.key.inWeight) || !Finite(x.key.outWeight))) return result;
            float origin = items.Min(x => x.key.time);
            if (items.Any(x => x.key.time - origin + at < 0 || x.key.time - origin + at > 600)) return result;
            foreach (var group in items.GroupBy(x => new { x.path, x.shape }))
            {
                var shapeIndex = Enumerable.Range(0, shapes.Count).FirstOrDefault(i => shapes[i].path == group.Key.path && shapes[i].shape == group.Key.shape);
                if (!shapes.Any(s => s.path == group.Key.path && s.shape == group.Key.shape)) continue;
                if (shapeIndex < 0 || shapeIndex >= shapes.Count) continue;
                var binding = RimeEmoClips.Binding(shapes[shapeIndex]); var curve = AnimationUtility.GetEditorCurve(clip, binding) ?? new AnimationCurve();
                var keys = curve.keys.ToList(); foreach (var item in group) { var key = item.key; key.time = at + item.key.time - origin; int old = keys.FindIndex(k => Mathf.Abs(k.time - key.time) <= Epsilon); if (old >= 0) keys[old] = key; else keys.Add(key); }
                keys.Sort((a, b) => a.time.CompareTo(b.time)); curve.keys = keys.ToArray(); AnimationUtility.SetEditorCurve(clip, binding, curve);
                foreach (var item in group) result.Add(new Vector2Int(shapeIndex, keys.FindIndex(k => Mathf.Abs(k.time - (at + item.key.time - origin)) <= Epsilon)));
            }
            return result;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static void ApplyMode(AnimationCurve curve, int index, Interpolation mode)
        {
            var keys = curve.keys; var key = keys[index]; key.weightedMode = WeightedMode.None; keys[index] = key; curve.keys = keys;
            var tangentMode = mode == Interpolation.Linear ? AnimationUtility.TangentMode.Linear
                : mode == Interpolation.Step ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.ClampedAuto;
            AnimationUtility.SetKeyBroken(curve, index, true);
            AnimationUtility.SetKeyLeftTangentMode(curve, index, tangentMode);
            AnimationUtility.SetKeyRightTangentMode(curve, index, tangentMode);
        }
    }
}
