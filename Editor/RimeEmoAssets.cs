using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoAssets
    {
        // User data lives outside the tool folder so replacing Assets/rimerime/RimeEmo on update never deletes it.
        internal const string DataRoot = "Assets/RimeEmoData";
        internal const string WorkingRoot = DataRoot + "/Working";
        internal const string GeneratedRoot = DataRoot + "/Generated";

        internal static string Root(RimeEmoProject project)
        {
            string path = project ? AssetDatabase.GetAssetPath(project) : "";
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException(RimeEmoLocalization.T("保存済みのRimeEmoProjectを指定してください。"));
            return Path.GetDirectoryName(path).Replace('\\', '/');
        }
        internal static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Untitled" : name;
        }

        internal static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }

        // Reflection keeps the editor usable without a mandatory SDK dependency.
        internal static RuntimeAnimatorController FindFX(GameObject avatar)
        {
            if (!avatar) return null;
            var descriptor = avatar.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (descriptor)
            {
                var layers = descriptor.GetType().GetField("baseAnimationLayers")?.GetValue(descriptor) as Array;
                if (layers != null) foreach (var layer in layers)
                {
                    var type = layer.GetType();
                    if (type.GetField("type")?.GetValue(layer)?.ToString() != "FX") continue;
                    if (type.GetField("isDefault")?.GetValue(layer) is bool isDefault && isDefault) return null;
                    return type.GetField("animatorController")?.GetValue(layer) as RuntimeAnimatorController;
                }
            }
            var animator = avatar.GetComponent<Animator>();
            return animator ? animator.runtimeAnimatorController : null;
        }

        internal static RimeEmoProject Create(GameObject avatar, RuntimeAnimatorController fx)
        {
            RimeEmoClips.ValidateAvatar(avatar);
            RimeEmoAvatarStorage.Find(avatar); // Reject ambiguous settings before creating assets.
            Folder(WorkingRoot);
            string dir = AssetDatabase.GenerateUniqueAssetPath(WorkingRoot + "/" + SafeName(avatar.name));
            Folder(dir);
            Folder(dir + "/Animations");
            Folder(dir + "/Motions");
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            project.avatarName = avatar.name;
            project.avatarId = GlobalObjectId.GetGlobalObjectIdSlow(avatar).ToString();
            project.sourceFX = fx;
            project.gesturePriority = GesturePriority.Left; // New setups resolve overlaps automatically.
            foreach (var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!renderer.sharedMesh) continue;
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, avatar.transform);
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    string shape = renderer.sharedMesh.GetBlendShapeName(i);
                    project.shapes.Add(new ShapeEntry { path = path, shape = shape, defaultValue = renderer.GetBlendShapeWeight(i), part = RimeEmoClips.Classify(shape) });
                }
            }
            RimeEmoFace.Resolve(project, avatar);
            AssetDatabase.CreateAsset(project, dir + "/RimeEmoProject.asset");
            if (fx) foreach (var clip in fx.animationClips.Where(c => c).Distinct()) Import(project, clip);
            if (project.clips.Count == 0) NewDefault(project);
            AssetDatabase.SaveAssets();
            RimeEmoAvatarStorage.Attach(avatar, project);
            return project;
        }

        internal static AnimationClip Import(RimeEmoProject project, AnimationClip source)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            if (!RimeEmoClipClassification.CanImport(project, source)) return null;
            if (RimeEmoImportedClips.IsAnimated(source, project)) return RimeEmoImportedClips.Import(project, source);
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name;
            copy.hideFlags = HideFlags.None;
            // A copy of a new, still empty expression must stay listed like its source.
            bool created = project.clips.Any(c => c != null && c.created && c.clip == source);
            AddClip(project, copy, source, RimeEmoGestures.Describe(project.sourceFX, source), created);
            return copy;
        }

        internal static AnimationClip NewDefault(RimeEmoProject project)
        {
            // Starts empty: only the Shape Keys the user moves are written. Unset ones show and build as Default Face,
            // so the clip stays small and "Included" lists exactly what the expression changes.
            // ("Default Face" is reserved for the preview baseline; a new editable expression gets its own name.)
            var clip = new AnimationClip { name = RimeEmoLocalization.T("新しい表情"), frameRate = 60 };
            AddClip(project, clip, null, "未割り当て", true);
            return clip;
        }

        internal static AnimationClip CaptureFrame(RimeEmoMotion motion, float time)
        {
            if (!motion || !motion.project) throw new InvalidOperationException(RimeEmoLocalization.T("編集中のアニメーションがありません。"));
            if (float.IsNaN(time) || float.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time));
            time = Mathf.Clamp(time, 0, RimeEmoCompiler.Duration(motion));
            RimeEmoCompiler.BuildCurves(motion, out var bindings, out var curves);
            var clip = new AnimationClip { name = motion.name + " Frame " + Mathf.RoundToInt(time * 60), frameRate = 60 };
            AnimationUtility.SetEditorCurves(clip, bindings, curves.Select(c => AnimationCurve.Constant(0, 1f / 60, c.Evaluate(time))).ToArray());
            AddClip(motion.project, clip, null, "未割り当て");
            return clip;
        }

        internal static void AddClip(RimeEmoProject project, AnimationClip clip, AnimationClip source, string gesture, bool created = false)
        {
            string folder = Root(project) + "/Animations";
            Folder(folder);
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SafeName(clip.name) + ".anim"));
            Undo.RecordObject(project, "RimeEmo: Add Clip");
            project.clips.Add(new ClipEntry { clip = clip, source = source, gesture = gesture, created = created });
            EditorUtility.SetDirty(project);
            AssetDatabase.SaveAssets();
        }

        internal static bool Owns(RimeEmoProject project, AnimationClip clip)
        {
            if (!project || !clip) return false;
            if (RimeEmoImportedClips.IsAnimated(clip, project) || RimeEmoDynamicClips.Find(project, clip) || project.animations.Any(c => c.clip == clip)) return false;
            return AssetDatabase.GetAssetPath(clip).StartsWith(Root(project) + "/Animations/", StringComparison.Ordinal)
                && project.clips.Any(c => c.clip == clip);
        }

        internal static RimeEmoMotion NewMotion(RimeEmoProject project, AnimationClip clip)
        {
            var motion = ScriptableObject.CreateInstance<RimeEmoMotion>();
            motion.project = project;
            motion.name = (clip ? clip.name : "Default") + " Motion";
            foreach (FacePart part in Enum.GetValues(typeof(FacePart)))
                motion.tracks.Add(new PartTrack { part = part, states = RimeEmoClips.HasPart(project, clip, part)
                    ? new List<MotionState> { new MotionState { clip = clip } } : new List<MotionState>() });
            RimeEmoMotionMaterials.Freeze(motion);
            string folder = Root(project) + "/Motions";
            Folder(folder);
            AssetDatabase.CreateAsset(motion, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SafeName(motion.name) + ".asset"));
            Undo.RecordObject(project, "RimeEmo: Add Motion");
            project.motions.Add(motion);
            EditorUtility.SetDirty(project);
            RimeEmoDynamicClips.EnsureClip(motion);
            AssetDatabase.SaveAssets();
            return motion;
        }

        internal static string Export(RimeEmoProject project, AnimationClip clip, bool generated)
        {
            if (!clip) throw new InvalidOperationException(RimeEmoLocalization.T("書き出すAnimationClipがありません。"));
            string dir = GeneratedRoot + "/" + SafeName(Path.GetFileName(Root(project))) + "/Animations";
            Folder(dir);
            string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + SafeName(clip.name) + ".anim");
            var copy = UnityEngine.Object.Instantiate(clip);
            copy.hideFlags = HideFlags.None;
            copy.name = clip.name;
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(copy);
            return path;
        }
    }

    internal static class RimeEmoGestures
    {
        internal static readonly string[] Names = { "Neutral", "Fist", "HandOpen", "FingerPoint", "Victory", "RockNRoll", "HandGun", "ThumbsUp" };

        // Deliberately report only simple direct root transitions. Never infer from filenames.
        internal static string Describe(RuntimeAnimatorController source, AnimationClip clip)
        {
            var controller = source as AnimatorController;
            if (!controller) return "Unknown";
            var results = new List<string>();
            foreach (var layer in controller.layers)
            {
                var sm = layer.stateMachine;
                foreach (var transition in sm.anyStateTransitions.Concat(sm.states.SelectMany(s => s.state.transitions)))
                {
                    if (!transition.destinationState || transition.destinationState.motion != clip) continue;
                    var conditions = transition.conditions;
                    if (transition.hasExitTime || conditions.Length == 0 || conditions.Any(c =>
                        (c.parameter != "GestureLeft" && c.parameter != "GestureRight") || c.mode != AnimatorConditionMode.Equals ||
                        c.threshold < 0 || c.threshold > 7 || c.threshold != Mathf.Floor(c.threshold))) continue;
                    results.Add(string.Join(" + ", conditions.Select(c => (c.parameter == "GestureLeft" ? "Left: " : "Right: ") + Names[(int)c.threshold])));
                }
            }
            return results.Count == 0 ? "Unknown" : string.Join(" / ", results.Distinct()) + " (直接条件・参考)";
        }
    }
}


