using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal enum RimeEmoClipKind { Excluded, Static, Dynamic }
    internal static class RimeEmoClipClassification
    {
        internal static RimeEmoClipKind Classify(RimeEmoProject project, AnimationClip clip)
        {
            if (!project || !clip) return RimeEmoClipKind.Excluded;
            string path = RimeEmoFace.Path(project);
            bool found = false;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!RimeEmoClips.IsShape(binding) || binding.path != path) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0) continue;
                found = true;
                if (!RimeEmoClips.IsConstant(curve, curve.keys[0].value)) return RimeEmoClipKind.Dynamic;
            }
            if (found) return RimeEmoClipKind.Static;
            // A new expression has no curves until a Shape Key is moved; it is still an editable expression.
            return project.clips.Any(c => c != null && c.created && c.clip == clip) ? RimeEmoClipKind.Static : RimeEmoClipKind.Excluded;
        }

        // Another avatar's expression usually targets a face mesh also named "Body", so the mesh path alone lets it
        // in; the Shape Key names tell them apart. Most of the clip's face keys must exist on this avatar's face mesh.
        // Avatars edited from the same base share their names and still match.
        internal static bool FromOtherAvatar(RimeEmoProject project, AnimationClip clip)
        {
            if (!project || !clip) return false;
            string path = RimeEmoFace.Path(project);
            var names = ShapeNames(project, path);
            int total = 0, matched = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!RimeEmoClips.IsShape(binding) || binding.path != path) continue;
                total++;
                if (names.Contains(binding.propertyName.Substring("blendShape.".Length))) matched++;
            }
            return total > 0 && matched < total * 0.8f;
        }

        // Only this avatar's expressions and motions become materials; checked on every way in (setup, the add
        // list, drops onto the list or the timeline, Gesture fields).
        internal static bool CanImport(RimeEmoProject project, AnimationClip clip)
            => Classify(project, clip) != RimeEmoClipKind.Excluded && !FromOtherAvatar(project, clip);

        // Why a clip cannot become a material of these settings, or null when it can.
        internal static string Rejection(RimeEmoProject project, AnimationClip clip)
        {
            if (FromOtherAvatar(project, clip)) return RimeEmoLocalization.T("このアバターの表情ではないため取り込みませんでした（Shape Keyの名前が一致しません）。");
            if (Classify(project, clip) == RimeEmoClipKind.Excluded) return RimeEmoLocalization.T("対象の顔メッシュのBlendShapeがないため除外しました。");
            return null;
        }

        private static RimeEmoProject namesProject;
        private static string namesPath;
        private static int namesCount = -1;
        private static HashSet<string> names = new HashSet<string>();

        private static HashSet<string> ShapeNames(RimeEmoProject project, string path)
        {
            if (project != namesProject || path != namesPath || project.shapes.Count != namesCount)
            {
                names = new HashSet<string>(RimeEmoFace.Shapes(project).Select(s => s.shape), StringComparer.Ordinal);
                namesProject = project; namesPath = path; namesCount = project.shapes.Count;
            }
            return names;
        }
    }
}
