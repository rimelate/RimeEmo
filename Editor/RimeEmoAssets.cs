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

        // Read from the transition conditions only. Never infer from filenames.
        internal static string Describe(RuntimeAnimatorController source, AnimationClip clip)
        {
            var controller = source as AnimatorController;
            if (!controller || !clip) return "Unknown";
            string Hand(int index) => index < 0 ? "" : Names[index];
            var results = SourceRules(controller, out _).Where(r => r.clip == clip)
                .Select(r => r.left >= 0 && r.right >= 0 ? "Left: " + Hand(r.left) + " + Right: " + Hand(r.right) : r.left >= 0 ? "Left: " + Hand(r.left) : "Right: " + Hand(r.right))
                .Distinct().ToList();
            return results.Count == 0 ? "Unknown" : string.Join(" / ", results) + " (直接条件・参考)";
        }

        internal struct SourceRule { internal int left, right; internal AnimationClip clip; }

        // The hand-gesture expressions of a source FX. Avatars build them in many shapes: AnyState transitions, one
        // layer per hand, Entry transitions, state-to-state transitions, sub-state machines, and a Blend Tree on the
        // gesture weight for the fist. Every transition, at any depth, that selects "GestureLeft/Right equals n"
        // counts; it leads to a clip, or to a weight Blend Tree whose fully pressed clip is taken.
        // Other conditions on the transition must hold with every parameter at its default: hand layers are often gated
        // by a face lock or a menu mode that is off by default (mao: FaceLock < 0.5, anima: setudan_global off), and
        // that is the face the avatar shows. A gimmick that needs a toggle turned on (MAYO: eyes that change on a hand
        // shape only while Jelly_Eye is on) is counted in "conditional" instead: it would clash with the hand
        // layer's own expression for the same hand shape.
        internal static List<SourceRule> SourceRules(AnimatorController controller, out int conditional)
        {
            var rules = new List<SourceRule>();
            int skipped = 0;
            // Parameters an FX uses without declaring them (VRChat's AFK, InStation…) are 0 until VRChat sets them.
            var defaults = (controller ? controller.parameters : Array.Empty<AnimatorControllerParameter>()).GroupBy(p => p.name).ToDictionary(g => g.Key, g =>
            {
                var p = g.First();
                return p.type == AnimatorControllerParameterType.Float ? p.defaultFloat : p.type == AnimatorControllerParameterType.Int ? p.defaultInt : p.defaultBool ? 1f : 0f;
            });
            bool AtDefault(AnimatorCondition c)
            {
                defaults.TryGetValue(c.parameter, out float value);
                switch (c.mode)
                {
                    case AnimatorConditionMode.If: return value != 0;
                    case AnimatorConditionMode.IfNot: return value == 0;
                    case AnimatorConditionMode.Greater: return value > c.threshold;
                    case AnimatorConditionMode.Less: return value < c.threshold;
                    case AnimatorConditionMode.Equals: return value == c.threshold;
                    case AnimatorConditionMode.NotEqual: return value != c.threshold;
                    default: return false;
                }
            }
            void Transitions(IEnumerable<AnimatorTransitionBase> transitions)
            {
                foreach (var transition in transitions)
                {
                    if (!transition) continue;
                    var conditions = transition.conditions;
                    var hands = conditions.Where(c => c.parameter == "GestureLeft" || c.parameter == "GestureRight").ToArray();
                    // "While this hand shape is NOT made" gates a layer rather than choosing an expression.
                    if (hands.Length == 0 || hands.Any(c => c.mode != AnimatorConditionMode.Equals || c.threshold < 0 || c.threshold > 7 || c.threshold != Mathf.Floor(c.threshold))) continue;
                    if (hands.GroupBy(c => c.parameter).Any(g => g.Select(c => c.threshold).Distinct().Count() > 1)) continue;
                    if (!conditions.Except(hands).All(AtDefault)) { skipped++; continue; }
                    var state = transition.destinationState ? transition.destinationState
                        : transition.destinationStateMachine ? transition.destinationStateMachine.defaultState : null;
                    var clip = state ? PressedClip(state.motion) : null;
                    if (!clip) continue;
                    int left = hands.Where(c => c.parameter == "GestureLeft").Select(c => (int)c.threshold).DefaultIfEmpty(-1).First();
                    int right = hands.Where(c => c.parameter == "GestureRight").Select(c => (int)c.threshold).DefaultIfEmpty(-1).First();
                    if (!rules.Any(r => r.left == left && r.right == right && r.clip == clip)) rules.Add(new SourceRule { left = left, right = right, clip = clip });
                }
            }
            void Machine(AnimatorStateMachine machine)
            {
                if (!machine) return;
                Transitions(machine.anyStateTransitions); Transitions(machine.entryTransitions);
                foreach (var child in machine.states) if (child.state) Transitions(child.state.transitions);
                foreach (var child in machine.stateMachines)
                {
                    if (!child.stateMachine) continue;
                    Transitions(machine.GetStateMachineTransitions(child.stateMachine));
                    Machine(child.stateMachine);
                }
            }
            if (controller) foreach (var layer in controller.layers) Machine(layer.stateMachine);
            conditional = skipped;
            return rules;
        }

        // A fist that follows the trigger is a Blend Tree from the resting face to the clenched one; the expression is the
        // clenched end. Other Blend Trees mix several faces and have no single expression.
        private static AnimationClip PressedClip(Motion motion)
        {
            if (motion is AnimationClip clip) return clip;
            if (!(motion is BlendTree tree) || tree.blendType != BlendTreeType.Simple1D || tree.children.Length == 0
                || (tree.blendParameter != "GestureLeftWeight" && tree.blendParameter != "GestureRightWeight")) return null;
            return PressedClip(tree.children.OrderByDescending(c => c.threshold).First().motion);
        }
    }
}


