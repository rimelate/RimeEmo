using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    // Keeps motion editing from changing the clips used by the static material shelf.
    internal static class RimeEmoMotionMaterials
    {
        private const string MotionSourcesFolder = "MotionSources";

        internal static string Folder(RimeEmoProject project) => RimeEmoAssets.Root(project) + "/" + MotionSourcesFolder;

        internal static bool IsSource(RimeEmoProject project, AnimationClip clip)
            => project && clip && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(project))
            && AssetDatabase.GetAssetPath(clip).StartsWith(Folder(project) + "/", StringComparison.Ordinal);

        internal static AnimationClip Snapshot(RimeEmoProject project, AnimationClip clip)
        {
            if (!clip) return null;
            if (!project) return clip;

            string projectPath = AssetDatabase.GetAssetPath(project);
            if (string.IsNullOrEmpty(projectPath)) return clip;
            if (!RimeEmoAssets.Owns(project, clip)) return clip;
            return Copy(project, clip);
        }

        // Frozen copy of any clip used as a moving cue or import source. Copies in MotionSources are never
        // edited, so an identical existing copy is reused instead of adding another file on every open or drop.
        internal static AnimationClip SnapshotSource(RimeEmoProject project, AnimationClip clip)
        {
            if (!clip || !project || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(project))) return clip;
            if (IsSource(project, clip)) return clip;
            return Copy(project, clip);
        }

        private static AnimationClip Copy(RimeEmoProject project, AnimationClip clip)
        {
            string folder = Folder(project);
            RimeEmoAssets.Folder(folder);
            var existing = FindIdentical(folder, clip);
            if (existing) return existing;
            var copy = UnityEngine.Object.Instantiate(clip);
            copy.name = clip.name;
            copy.hideFlags = HideFlags.None;
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + RimeEmoAssets.SafeName(clip.name) + ".anim");
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        private static AnimationClip FindIdentical(string folder, AnimationClip clip)
        {
            string prefix = RimeEmoAssets.SafeName(clip.name);
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileNameWithoutExtension(path).StartsWith(prefix, StringComparison.Ordinal)) continue;
                var candidate = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (candidate && candidate != clip && SameContent(candidate, clip)) return candidate;
            }
            return null;
        }

        internal static bool SameContent(AnimationClip a, AnimationClip b)
        {
            if (!a || !b) return false;
            if (!Mathf.Approximately(a.frameRate, b.frameRate) || a.wrapMode != b.wrapMode) return false;
            var sa = AnimationUtility.GetAnimationClipSettings(a); var sb = AnimationUtility.GetAnimationClipSettings(b);
            if (sa.loopTime != sb.loopTime || sa.loopBlend != sb.loopBlend || sa.cycleOffset != sb.cycleOffset) return false;
            var fa = AnimationUtility.GetCurveBindings(a); var fb = AnimationUtility.GetCurveBindings(b);
            if (fa.Length != fb.Length || fa.Except(fb).Any()) return false;
            foreach (var binding in fa)
            {
                var ca = AnimationUtility.GetEditorCurve(a, binding); var cb = AnimationUtility.GetEditorCurve(b, binding);
                if (ca == null || cb == null || ca.preWrapMode != cb.preWrapMode || ca.postWrapMode != cb.postWrapMode) return false;
                var ka = ca.keys; var kb = cb.keys;
                if (ka.Length != kb.Length) return false;
                for (int i = 0; i < ka.Length; i++)
                    if (ka[i].time != kb[i].time || ka[i].value != kb[i].value || ka[i].inTangent != kb[i].inTangent || ka[i].outTangent != kb[i].outTangent
                        || ka[i].inWeight != kb[i].inWeight || ka[i].outWeight != kb[i].outWeight || ka[i].weightedMode != kb[i].weightedMode) return false;
            }
            var oa = AnimationUtility.GetObjectReferenceCurveBindings(a); var ob = AnimationUtility.GetObjectReferenceCurveBindings(b);
            if (oa.Length != ob.Length || oa.Except(ob).Any()) return false;
            foreach (var binding in oa)
            {
                var ka = AnimationUtility.GetObjectReferenceCurve(a, binding); var kb = AnimationUtility.GetObjectReferenceCurve(b, binding);
                if (ka.Length != kb.Length) return false;
                for (int i = 0; i < ka.Length; i++) if (ka[i].time != kb[i].time || ka[i].value != kb[i].value) return false;
            }
            var ea = AnimationUtility.GetAnimationEvents(a); var eb = AnimationUtility.GetAnimationEvents(b);
            if (ea.Length != eb.Length) return false;
            for (int i = 0; i < ea.Length; i++)
                if (ea[i].time != eb[i].time || ea[i].functionName != eb[i].functionName || ea[i].stringParameter != eb[i].stringParameter
                    || ea[i].floatParameter != eb[i].floatParameter || ea[i].intParameter != eb[i].intParameter || ea[i].objectReferenceParameter != eb[i].objectReferenceParameter) return false;
            return true;
        }

        internal static void Freeze(RimeEmoMotion motion)
        {
            if (!motion || !motion.project || motion.tracks == null) return;
            var snapshots = new Dictionary<AnimationClip, AnimationClip>();
            bool changed = false;
            foreach (var track in motion.tracks)
            {
                if (track == null || track.states == null) continue;
                foreach (var state in track.states)
                {
                    if (state == null || !state.clip) continue;
                    if (!snapshots.TryGetValue(state.clip, out var snapshot))
                    {
                        snapshot = Snapshot(motion.project, state.clip);
                        snapshots.Add(state.clip, snapshot);
                    }
                    if (snapshot == state.clip) continue;
                    state.clip = snapshot;
                    changed = true;
                }
            }
            if (changed) EditorUtility.SetDirty(motion);
        }
    }
}
