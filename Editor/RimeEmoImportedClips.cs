using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoImportedClips
    {
        private const float SnapshotLength = 1f / 60f;

        internal static bool IsAnimated(AnimationClip clip, RimeEmoProject project = null)
        {
            if (project) return RimeEmoClipClassification.Classify(project, clip) == RimeEmoClipKind.Dynamic;
            if (!clip) return false;
            return AnimationUtility.GetCurveBindings(clip).Where(RimeEmoClips.IsShape).Any(binding =>
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                return curve != null && curve.length > 0 && !RimeEmoClips.IsConstant(curve, curve.keys[0].value);
            });
        }
        internal static AnimationClip Import(RimeEmoProject project, AnimationClip source)
        {
            if (!project) throw new ArgumentNullException(nameof(project));
            if (!source) throw new ArgumentNullException(nameof(source));
            if (!RimeEmoClipClassification.CanImport(project, source)) return null;
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name;
            copy.hideFlags = HideFlags.None;
            string folder = RimeEmoAssets.Root(project) + "/ImportedAnimations";
            RimeEmoAssets.Folder(folder);
            AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + RimeEmoAssets.SafeName(copy.name) + ".anim"));
            Undo.RecordObject(project, "RimeEmo: Import Animation");
            project.animations.Add(new ClipEntry { clip = copy, source = EditorUtility.IsPersistent(source) ? source : null, gesture = "未割り当て" });
            EditorUtility.SetDirty(project);
            AssetDatabase.SaveAssets();
            return copy;
        }

        internal static bool Owns(RimeEmoProject project, AnimationClip clip)
        {
            if (!project || !clip) return false;
            string prefix = RimeEmoAssets.Root(project) + "/ImportedAnimations/";
            return AssetDatabase.GetAssetPath(clip).StartsWith(prefix, StringComparison.Ordinal)
                && project.animations.Any(c => c != null && c.clip == clip);
        }

        internal static AnimationClip CreateDraft(AnimationClip clip)
        {
            if (!clip) return null;
            var draft = UnityEngine.Object.Instantiate(clip);
            draft.name = clip.name;
            draft.hideFlags = HideFlags.HideAndDontSave;
            return draft;
        }

        internal static void SaveTo(RimeEmoProject project, AnimationClip draft, AnimationClip target)
        {
            if (!Owns(project, target)) throw new InvalidOperationException("Target is not an imported animation owned by the project.");
            if (!draft) throw new ArgumentNullException(nameof(draft));
            Undo.RecordObject(target, "RimeEmo: Save Imported Animation");
            string name = target.name;
            EditorUtility.CopySerialized(draft, target);
            target.name = name;
            target.hideFlags = HideFlags.None;
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
        }

        internal static AnimationClip CaptureFrame(RimeEmoProject project, AnimationClip clip, float time)
        {
            if (!project) throw new ArgumentNullException(nameof(project));
            if (!clip) throw new ArgumentNullException(nameof(clip));
            if (float.IsNaN(time) || float.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time));
            var result = new AnimationClip { name = clip.name + " Frame " + Mathf.RoundToInt(time * 60), frameRate = 60 };
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null) AnimationUtility.SetEditorCurve(result, binding, AnimationCurve.Constant(0, SnapshotLength, curve.Evaluate(time)));
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keys == null || keys.Length == 0) continue;
                ObjectReferenceKeyframe? selected = null;
                foreach (var key in keys) if (key.time <= time) selected = key;
                if (selected.HasValue) AnimationUtility.SetObjectReferenceCurve(result, binding, new[] { new ObjectReferenceKeyframe { time = 0, value = selected.Value.value } });
            }
            var settings = AnimationUtility.GetAnimationClipSettings(result);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(result, settings);
            RimeEmoAssets.AddClip(project, result, null, "未割り当て");
            return result;
        }

        internal static IEnumerable<AnimationClip> Available(RimeEmoProject project)
        {
            if (!project) return Enumerable.Empty<AnimationClip>();
            return project.animations.Where(c => c != null && c.clip).Select(c => c.clip)
                .Concat(project.clips.Where(c => c != null && c.clip && IsAnimated(c.clip, project)).Select(c => c.clip)).Distinct().Where(c => RimeEmoClipClassification.Classify(project, c) != RimeEmoClipKind.Excluded);
        }
    }
}


