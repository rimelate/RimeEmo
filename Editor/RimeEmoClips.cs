using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoClips
    {
        internal static EditorCurveBinding Binding(ShapeEntry s) =>
            EditorCurveBinding.FloatCurve(s.path, typeof(SkinnedMeshRenderer), "blendShape." + s.shape);

        internal static bool IsShape(EditorCurveBinding b) => b.type == typeof(SkinnedMeshRenderer)
            && b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);

        internal static bool HasPart(RimeEmoProject project, AnimationClip clip, FacePart part)
        {
            if (!project || !clip) return false;
            return RimeEmoFace.Shapes(project).Where(s => s.part == part).Select(Binding)
                .Select(b => AnimationUtility.GetEditorCurve(clip, b)).Any(c => c != null && c.length > 0);
        }

        internal static bool IsConstant(AnimationCurve curve, float value)
        {
            if (curve == null || curve.length == 0 || float.IsNaN(value) || float.IsInfinity(value)) return false;
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (float.IsNaN(keys[i].value) || Mathf.Abs(keys[i].value - value) > 0.0001f) return false;
                if (i == keys.Length - 1) continue;
                // Equal endpoints alone are insufficient: cubic tangents can overshoot.
                if (float.IsNaN(keys[i].outTangent) || float.IsNaN(keys[i + 1].inTangent)) return false;
                if (float.IsInfinity(keys[i].outTangent) || float.IsInfinity(keys[i + 1].inTangent)) continue;
                bool outActive = (keys[i].weightedMode & WeightedMode.Out) == 0 || keys[i].outWeight != 0;
                bool inActive = (keys[i + 1].weightedMode & WeightedMode.In) == 0 || keys[i + 1].inWeight != 0;
                if ((outActive && keys[i].outTangent != 0) || (inActive && keys[i + 1].inTangent != 0)) return false;
            }
            return true;
        }

        internal static float Value(AnimationClip clip, ShapeEntry shape)
        {
            if (!clip) return shape.defaultValue;
            var curve = AnimationUtility.GetEditorCurve(clip, Binding(shape));
            return curve == null || curve.length == 0 ? shape.defaultValue : curve.Evaluate(0);
        }

        internal static void Set(AnimationClip clip, EditorCurveBinding binding, float value)
        {
            Undo.RecordObject(clip, "RimeEmo: Change BlendShape");
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1f / 60, value));
            EditorUtility.SetDirty(clip);
        }

        internal static void Remove(AnimationClip clip, EditorCurveBinding binding)
        {
            Undo.RecordObject(clip, "RimeEmo: Remove Property");
            AnimationUtility.SetEditorCurve(clip, binding, null);
            EditorUtility.SetDirty(clip);
        }

        internal static int RemoveDefaults(AnimationClip clip, RimeEmoProject project)
        {
            Undo.RecordObject(clip, "RimeEmo: Remove Default Values");
            int count = 0;
            foreach (var shape in RimeEmoFace.Shapes(project))
            {
                var b = Binding(shape);
                if (!IsConstant(AnimationUtility.GetEditorCurve(clip, b), shape.defaultValue)) continue;
                AnimationUtility.SetEditorCurve(clip, b, null);
                count++;
            }
            EditorUtility.SetDirty(clip);
            return count;
        }

        internal static FacePart Classify(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return FacePart.Other;
            string normalized = name.Normalize(NormalizationForm.FormKC);
            string tokens = Regex.Replace(normalized, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
            string n = Regex.Replace(tokens, "[\\s_.\\-]+", "");
            if (n.Contains("brow") || n.Contains("mayu") || n.Contains("眉")) return FacePart.Brow;
            if (n.Contains("mouth") || n.Contains("lip") || n.Contains("kuchi") || n.Contains("口")
                || n.Contains("jaw") || n.Contains("tongue") || n.Contains("teeth") || n.Contains("viseme")
                || tokens.StartsWith("vrc.v_", StringComparison.Ordinal)) return FacePart.Mouth;
            bool eye = new[] { "eye", "blink", "upperlid", "lowerlid", "lash", "iris", "pupil", "catchlight", "mabuta", "まぶた", "瞼", "目", "瞳" }.Any(n.Contains);
            if (!eye) return FacePart.Other;
            // These exact names describe gaze direction, not the side of the face.
            if (Regex.IsMatch(n, "^(eyelook|iris|pupil)(left|right)[0-9]*$")) return FacePart.Other;
            bool left = Regex.IsMatch(tokens, @"(^|[^a-z0-9])(l|left)(?=$|[^a-z0-9]|[0-9])") || n.Contains("左") || n.EndsWith("left", StringComparison.Ordinal);
            bool right = Regex.IsMatch(tokens, @"(^|[^a-z0-9])(r|right)(?=$|[^a-z0-9]|[0-9])") || n.Contains("右") || n.EndsWith("right", StringComparison.Ordinal);
            if (left != right) return left ? FacePart.LeftEye : FacePart.RightEye;
            return FacePart.Other;
        }

        internal static int RepairAutomaticClassification(RimeEmoProject project)
        {
            if (!project) return 0;
            var missing = project.shapes.Where(s => !s.classificationOverridden && s.part != Classify(s.shape)).ToArray();
            if (missing.Length == 0) return 0;
            Undo.RecordObject(project, "RimeEmo: Update Automatic Classification");
            foreach (var shape in missing) shape.part = Classify(shape.shape);
            EditorUtility.SetDirty(project);
            return missing.Length;
        }

        internal static AnimationClip DefaultClip(RimeEmoProject project)
        {
            var clip = new AnimationClip { name = "Default Face", frameRate = 60 };
            foreach (var shape in RimeEmoFace.Shapes(project))
                AnimationUtility.SetEditorCurve(clip, Binding(shape), AnimationCurve.Constant(0, 1f / 60, shape.defaultValue));
            return clip;
        }

        internal static void ValidateAvatar(GameObject avatar)
        {
            if (!avatar || EditorUtility.IsPersistent(avatar)) throw new InvalidOperationException(RimeEmoLocalization.T("Scene上のアバターのルートを指定してください。"));
            var paths = avatar.GetComponentsInChildren<Transform>(true)
                .Select(t => AnimationUtility.CalculateTransformPath(t, avatar.transform)).ToArray();
            if (paths.Distinct().Count() != paths.Length)
                throw new InvalidOperationException(RimeEmoLocalization.T("同じ階層パスのオブジェクトがあります。Animationの対象を一意にするため名前を変更してください。"));
        }
    }
}
