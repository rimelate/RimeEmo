using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoKeyframeChecks
    {
        internal static void Run()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>(); var clip = new AnimationClip();
            var a = new ShapeEntry { path = "Face", shape = "A", part = FacePart.Mouth }; var b = new ShapeEntry { path = "Face", shape = "B", part = FacePart.Mouth }; project.shapes.AddRange(new[] { a, b });
            try
            {
                var ca = new AnimationCurve(new Keyframe(.2f, 1), new Keyframe(1, 2)); var cb = new AnimationCurve(new Keyframe(.5f, 3), new Keyframe(1, 4));
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(a), ca); AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(b), cb);
                var moved = new HashSet<Vector2Int>(); Check(RimeEmoKeyframes.Move(clip, project.shapes, new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) }, -1, out moved), "group move failed");
                Check(Mathf.Abs(AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(a)).keys[0].time) < .0001f && Mathf.Abs(AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(b)).keys[0].time - .3f) < .0001f, "group move changed spacing");
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(a), new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 2))); AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(b), new AnimationCurve(new Keyframe(.5f, 3), new Keyframe(1, 4)));
                var before = EditorJsonUtility.ToJson(clip); Check(!RimeEmoKeyframes.Move(clip, project.shapes, new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) }, .5f, out moved), "collision move succeeded"); Check(EditorJsonUtility.ToJson(clip) == before, "collision move was not atomic"); Check(!RimeEmoKeyframes.Move(clip, project.shapes, new[] { new Vector2Int(0, 0) }, 0, out moved), "zero move changed");
                var key = new Keyframe(.2f, 9, 2, 3, .25f, .75f) { weightedMode = WeightedMode.Both }; AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(a), new AnimationCurve(key, new Keyframe(.7f, 4)));
                var copied = RimeEmoKeyframes.Copy(clip, project.shapes, new[] { new Vector2Int(0, 0) }); Check(copied.Count == 1 && copied[0].key.weightedMode == WeightedMode.Both && copied[0].key.inWeight == .25f, "copy lost key metadata");
                var pasted = RimeEmoKeyframes.Paste(clip, project.shapes, copied, 1); Check(pasted.Count == 1 && AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(a)).keys.Any(k => Mathf.Abs(k.time - 1) < .0001f), "paste failed");
                var invalid = RimeEmoKeyframes.Paste(clip, project.shapes, new[] { new RimeEmoKeyframes.CopiedKey { path = "Face", shape = "A", key = new Keyframe(float.NaN, 1) } }, 0); Check(invalid.Count == 0, "invalid paste accepted");
                RimeEmoKeyframes.SetInterpolation(clip, project.shapes, new[] { new Vector2Int(0, 0) }, Interpolation.Linear); RimeEmoKeyframes.SetInterpolation(clip, project.shapes, new[] { new Vector2Int(0, 0) }, Interpolation.Step);
                RimeEmoKeyframes.Delete(clip, project.shapes, new[] { new Vector2Int(0, 0) }); Check(AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(a)) != null && AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(b)) != null, "delete removed wrong shape");
                AnimationUtility.SetEditorCurve(clip, RimeEmoClips.Binding(a), new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 100), new Keyframe(2, 0)));
                var all = new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2) };
                RimeEmoKeyframes.SetInterpolation(clip, project.shapes, all, Interpolation.Linear);
                Check(Mathf.Abs(AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(a)).Evaluate(.25f) - 25) < .001f, "linear interpolation is not linear");
                RimeEmoKeyframes.SetInterpolation(clip, project.shapes, all, Interpolation.Step);
                Check(AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(a)).Evaluate(.99f) == 0 && AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(a)).Evaluate(1.5f) == 100, "step did not hold until next key");
                RimeEmoKeyframes.SetInterpolation(clip, project.shapes, all, Interpolation.EaseInOut);
                var eased = AnimationUtility.GetEditorCurve(clip, RimeEmoClips.Binding(a));
                Check(eased.Evaluate(.75f) > 75 && Mathf.Abs(eased[1].outTangent) < .001f && AnimationUtility.GetKeyRightTangentMode(eased, 1) == AnimationUtility.TangentMode.ClampedAuto, "smooth interpolation did not flatten at extremum");
                Check(RimeEmoKeyframes.Move(clip, project.shapes, new[] { new Vector2Int(0, 0) }, 1.5f, out moved) && moved.SetEquals(new[] { new Vector2Int(0, 1) }), "moving past another key lost selection identity");
                before = EditorJsonUtility.ToJson(clip);
                Check(RimeEmoKeyframes.SetKey(clip, a, 601, 5, Interpolation.Linear) == -1 && EditorJsonUtility.ToJson(clip) == before, "invalid time wrote a key");
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); UnityEngine.Object.DestroyImmediate(project); }
        }
        private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("Keyframe: " + message); }
    }
}
