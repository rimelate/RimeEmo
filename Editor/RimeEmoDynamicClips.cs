using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    // Block editing information stays in its existing asset; Unity consumes the linked .anim.
    internal static class RimeEmoDynamicClips
    {
        internal static RimeEmoMotion Find(RimeEmoProject project, AnimationClip clip)
            => project && clip ? project.motions.FirstOrDefault(m => m && m.animationClip == clip) : null;

        internal static AnimationClip EnsureClip(RimeEmoMotion motion)
        {
            if (!motion || !motion.project) return null;
            if (motion.animationClip) return motion.animationClip;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(motion))) return null;
            string folder = RimeEmoAssets.Root(motion.project) + "/Animations";
            RimeEmoAssets.Folder(folder);
            var clip = RimeEmoCompiler.Compile(motion);
            clip.name = motion.name;
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + RimeEmoAssets.SafeName(motion.name) + ".anim"));
            motion.animationClip = clip;
            RimeEmoCompiler.BuildCurves(motion, out var generated, out _);
            foreach (var binding in generated)
            { motion.generatedShapePaths.Add(binding.path); motion.generatedShapeProperties.Add(binding.propertyName); }
            EditorUtility.SetDirty(motion);
            return clip;
        }

        internal static void Save(RimeEmoProject project, bool dirtyOnly = false)
        {
            if (!project) return;
            foreach (var motion in project.motions.Where(m => m && m.project == project))
            {
                if (dirtyOnly && motion.animationClip && !EditorUtility.IsDirty(motion) && !EditorUtility.IsDirty(project)
                    && !motion.tracks.SelectMany(t => t.states).Any(s => s.clip && EditorUtility.IsDirty(s.clip))) continue;
                var target = EnsureClip(motion);
                if (!target) continue;
                RimeEmoCompiler.BuildCurves(motion, out var bindings, out var curves);
                // Replace only the generated face curves; retain unrelated properties and events.
                for (int i = 0; i < Math.Min(motion.generatedShapePaths.Count, motion.generatedShapeProperties.Count); i++)
                {
                    var old = EditorCurveBinding.FloatCurve(motion.generatedShapePaths[i], typeof(SkinnedMeshRenderer), motion.generatedShapeProperties[i]);
                    if (!bindings.Any(b => b.Equals(old))) AnimationUtility.SetEditorCurve(target, old, null);
                }
                AnimationUtility.SetEditorCurves(target, bindings, curves);
                motion.generatedShapePaths = bindings.Select(b => b.path).ToList();
                motion.generatedShapeProperties = bindings.Select(b => b.propertyName).ToList();
                EditorUtility.SetDirty(motion);
                var settings = AnimationUtility.GetAnimationClipSettings(target);
                settings.loopTime = motion.loop;
                AnimationUtility.SetAnimationClipSettings(target, settings);
                EditorUtility.SetDirty(target);
            }
        }
    }

    internal sealed class RimeEmoDynamicClipSave : AssetModificationProcessor
    {
        private static bool saving;
        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (saving) return paths;
            saving = true;
            try
            {
                var result = new HashSet<string>(paths);
                foreach (var project in Resources.FindObjectsOfTypeAll<RimeEmoProject>())
                {
                    if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(project))) continue;
                    // A scene-only save must not rewrite animation assets.
                    if (!paths.Contains(AssetDatabase.GetAssetPath(project)) && !project.motions.Any(m => m && paths.Contains(AssetDatabase.GetAssetPath(m)))
                        && !project.clips.Any(c => c.clip && paths.Contains(AssetDatabase.GetAssetPath(c.clip)))) continue;
                    RimeEmoDynamicClips.Save(project, true);
                    foreach (var motion in project.motions.Where(m => m))
                    {
                        result.Add(AssetDatabase.GetAssetPath(motion));
                        if (motion.animationClip) result.Add(AssetDatabase.GetAssetPath(motion.animationClip));
                    }
                }
                return result.Where(p => !string.IsNullOrEmpty(p)).ToArray();
            }
            finally { saving = false; }
        }
    }
}

