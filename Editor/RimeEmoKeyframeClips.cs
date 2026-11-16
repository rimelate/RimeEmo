using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoKeyframeClips
    {
        internal static bool Owns(RimeEmoProject project, AnimationClip clip)
            => project && clip && project.animations.Any(c => c != null && c.clip == clip && c.keyframeEdited);

        internal static AnimationClip SaveTo(RimeEmoProject project, AnimationClip draft, string path)
        {
            if (!project || !draft) throw new InvalidOperationException("Project and keyframe draft are required.");
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(RimeEmoLocalization.T("Assets内の.animファイルを指定してください。"));
            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            var target = existing as AnimationClip;
            if (existing && (!target || !Owns(project, target) || RimeEmoDynamicClips.Find(project, target)))
                throw new InvalidOperationException(RimeEmoLocalization.T("元の素材や部位別の動きには上書きできません。別名で保存してください。"));
            if (!target)
            {
                target = UnityEngine.Object.Instantiate(draft); target.hideFlags = HideFlags.None;
                target.name = Path.GetFileNameWithoutExtension(path); AssetDatabase.CreateAsset(target, path);
                Undo.RecordObject(project, "RimeEmo: Save Keyframe Animation");
                project.animations.Add(new ClipEntry { clip = target, keyframeEdited = true, gesture = "未割り当て" });
                EditorUtility.SetDirty(project);
            }
            else
            {
                Undo.RecordObject(target, "RimeEmo: Save Keyframe Animation");
                EditorUtility.CopySerialized(draft, target); target.hideFlags = HideFlags.None;
                target.name = Path.GetFileNameWithoutExtension(path); EditorUtility.SetDirty(target);
            }
            AssetDatabase.SaveAssets(); return target;
        }
    }
}
