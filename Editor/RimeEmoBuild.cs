using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RimeEmo.Editor
{
    // Generates the RimeEmo gesture layer as one controller per avatar settings. Modular Avatar appends it on top
    // of the avatar's own FX, so the source FX is neither copied nor replaced.
    internal static class RimeEmoBuild
    {
        internal const string ControllerFile = "RimeEmoFX.controller";
        internal const string LayerName = "RimeEmo Gestures";
        // Bump when the generated content changes for the same settings, so applied avatars show "変更を適用".
        // 2: layer appended via MA instead of a copied FX. 3: only Shape Keys changed from Default Face are written.
        // 4: face lock, menu expressions and per-expression blink / lip sync pauses.
        internal const int GeneratorVersion = 5;

        internal static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            if (!machine) yield break;
            foreach (var child in machine.states) yield return child.state;
            foreach (var child in machine.stateMachines) foreach (var state in States(child.stateMachine)) yield return state;
        }
        internal static List<string> Validate(RimeEmoProject project)
        {
            var errors = RimeEmoAssignments.Validate(project);
            if (!project) return errors;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(project))) errors.Add(RimeEmoLocalization.T("Projectを保存してください。"));
            if (!RimeEmoFace.Shapes(project).Any()) errors.Add(RimeEmoLocalization.T("顔メッシュのShape Keyがありません。"));
            // The merged layer shares the avatar's Gesture parameters; MA cannot merge them with another type.
            if (project.sourceFX is AnimatorController source)
                foreach (string name in new[] { "GestureLeft", "GestureRight" })
                    if (source.parameters.Any(p => p.name == name && p.type != AnimatorControllerParameterType.Int)) errors.Add(name + RimeEmoLocalization.T("がInt以外で定義されています。"));
            if (float.IsNaN(project.gestureBlendSeconds) || float.IsInfinity(project.gestureBlendSeconds) || project.gestureBlendSeconds < 0) errors.Add(RimeEmoLocalization.T("Gestureの切替時間が不正です。"));
            errors.AddRange(RimeEmoFaceMenu.ValidateBuild(project));
            return errors;
        }

        // The single output folder of these settings. Older versions kept numbered "Build" folders inside it.
        internal static string OutputFolder(RimeEmoProject project)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(project));
            return RimeEmoAssets.GeneratedRoot + "/" + RimeEmoAssets.SafeName(project.avatarName) + "-" + guid.Substring(0, 8);
        }

        // Rebuilds the controller in place: the asset keeps its path and GUID, so the scene's MA Merge Animator
        // never needs to change and no folders pile up.
        // The avatar tells how it blinks and lip-syncs; without one, the settings' scene avatar is looked up.
        internal static AnimatorController Build(RimeEmoProject project, GameObject avatar = null)
        {
            var errors = Validate(project);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            var sync = RimeEmoFaceSync.Analyze(project, avatar);
            string fingerprint = RimeEmoBuildState.Fingerprint(project, sync);
            string folder = OutputFolder(project);
            RimeEmoAssets.Folder(RimeEmoAssets.GeneratedRoot); RimeEmoAssets.Folder(folder);
            string path = folder + "/" + ControllerFile;
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller) Clear(controller, path);
            else controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            while (controller.layers.Length > 0) controller.RemoveLayer(0); // A new controller starts with "Base Layer".

            controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
            controller.AddParameter("GestureRight", AnimatorControllerParameterType.Int);
            bool menu = project.expressionMenu;
            if (menu)
            {
                controller.AddParameter(RimeEmoFaceMenu.LockParameter, AnimatorControllerParameterType.Bool);
                controller.AddParameter(RimeEmoFaceMenu.FaceParameter, AnimatorControllerParameterType.Int);
            }
            var sm = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);
            controller.AddLayer(new AnimatorControllerLayer { name = LayerName, stateMachine = sm, defaultWeight = 1, blendingMode = AnimatorLayerBlendingMode.Override });
            var sources = Sources(project, out var keys, out var menuKeys);
            TrackingSetting Setting(Object key) => key ? RimeEmoFaceMenu.Tracking(project, RimeEmoFaceMenu.MaterialClip(key)) : null;
            // An avatar that blinks in its FX blinks in every expression that does not pause it, Default Face included.
            foreach (var pair in sources)
                if (Setting(pair.Key)?.stopBlink == true) sync.RemoveBlink(pair.Value.clip);
                else sync.AddBlink(pair.Value.clip);
            var defaultFace = RimeEmoClips.DefaultClip(project);
            sync.AddBlink(defaultFace);
            var written = WrittenShapes(project, sources);
            if (sync.AnimatedBlink) written.UnionWith(sync.BlinkShapes);
            if (project.overrideSourceGestureFaces) written.UnionWith(SourceGestureShapes(project));
            // Lip sync driven by the FX stays with its own layers; only a state that pauses it writes those keys.
            if (sync.AnimatedLipSync) written.ExceptWith(sync.lipShapes);
            AnimationClip Finish(Object key, AnimationClip clip)
            {
                Restrict(clip, project, written);
                if (Setting(key)?.stopLipSync == true) sync.PauseLipSync(clip);
                return SaveClip(clip, controller, key ? key.name : "Default Face");
            }
            var clips = sources.ToDictionary(p => p.Key, p => Finish(p.Key, p.Value.clip));
            AnimationClip defaults = Finish(null, defaultFace);
            // VRChat's blinking or lip sync is handed back in every state once some built expression pauses it.
            var pausing = sources.Keys.Select(Setting).Where(t => t != null).ToList();
            bool eyes = sync.vrcBlink && pausing.Any(t => t.stopBlink), mouth = sync.vrcLipSync && pausing.Any(t => t.stopLipSync);
            AnimatorStateTransition AddState(string name, Object key, Vector3 position)
            {
                var state = sm.AddState(name, position);
                state.motion = key ? clips[key] : defaults;
                var setting = Setting(key);
                RimeEmoFaceMenu.AddTrackingControl(state, eyes ? setting != null && setting.stopBlink : (bool?)null, mouth ? setting != null && setting.stopLipSync : (bool?)null);
                if (!sm.defaultState) sm.defaultState = state; // The first one: L0 R0, both hands resting.
                var transition = sm.AddAnyStateTransition(state);
                transition.hasExitTime = false; transition.hasFixedDuration = true; transition.duration = project.gestureBlendSeconds;
                transition.canTransitionToSelf = false; transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
                transition.orderedInterruption = false;
                return transition;
            }
            for (int l = 0; l < 8; l++) for (int r = 0; r < 8; r++)
            {
                var transition = AddState("L" + l + " R" + r, keys[l, r], new Vector3(r * 200, l * 65, 0));
                transition.AddCondition(AnimatorConditionMode.Equals, l, "GestureLeft");
                transition.AddCondition(AnimatorConditionMode.Equals, r, "GestureRight");
                // The hands only steer the face while it is not locked and no menu expression is chosen. Locking simply
                // stops these transitions, so the face shown at that moment stays.
                if (!menu) continue;
                transition.AddCondition(AnimatorConditionMode.IfNot, 0, RimeEmoFaceMenu.LockParameter);
                transition.AddCondition(AnimatorConditionMode.Equals, 0, RimeEmoFaceMenu.FaceParameter);
            }
            for (int i = 0; i < menuKeys.Count; i++)
                AddState("Menu " + (i + 1), menuKeys[i], new Vector3(-300, i * 65, 0)).AddCondition(AnimatorConditionMode.Equals, i + 1, RimeEmoFaceMenu.FaceParameter);
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
            Undo.RecordObject(project, "RimeEmo: Rebuild FX");
            project.generatedFX = controller; project.generatedFolder = folder; project.generatedFingerprint = fingerprint;
            EditorUtility.SetDirty(project); AssetDatabase.SaveAssets();
            return controller;
        }

        internal struct SourceClip { internal AnimationClip clip; internal bool compiled; }

        internal static Dictionary<Object, SourceClip> Sources(RimeEmoProject project, out Object[,] keys) => Sources(project, out keys, out _);

        // The clip of every assigned expression or motion and every menu expression, once each, which one each of the
        // 64 states plays, and the menu's expressions in order. Clips are new in-memory objects; the caller saves or
        // destroys them.
        internal static Dictionary<Object, SourceClip> Sources(RimeEmoProject project, out Object[,] keys, out List<Object> menuKeys)
        {
            var sources = new Dictionary<Object, SourceClip>();
            void Add(Object key, AnimationClip clip, RimeEmoMotion motion)
            {
                if (!key || sources.ContainsKey(key)) return;
                var dynamicSource = motion ? motion : RimeEmoDynamicClips.Find(project, clip);
                sources[key] = dynamicSource
                    ? new SourceClip { clip = RimeEmoCompiler.Compile(dynamicSource), compiled = true }
                    : new SourceClip { clip = Copy(clip) };
            }
            keys = new Object[8, 8];
            for (int l = 0; l < 8; l++) for (int r = 0; r < 8; r++)
            {
                RimeEmoAssignments.TryResolve(project, l, r, out var rule);
                Object key = rule == null ? null : rule.motion ? (Object)rule.motion : rule.clip;
                keys[l, r] = key;
                if (key) Add(key, rule.clip, rule.motion);
            }
            menuKeys = RimeEmoFaceMenu.MenuClips(project).Cast<Object>().ToList();
            foreach (AnimationClip clip in menuKeys) Add(clip, clip, null);
            return sources;
        }

        // Face Shape Keys the layer writes: those some assigned expression or motion moves away from Default Face.
        // The rest stay with the avatar's own FX (lip sync, blinking, other gimmicks). A Shape Key in the set is written
        // in every state, at Default Face where an expression leaves it, so no gesture leaks into the next.
        // Keys merely held at Default Face do not count: expressions imported from an FX typically list hundreds
        // of them (Point: 266 keys, 11 changed), and counting them overrode lip sync and AFK faces in the avatar's FX.
        internal static HashSet<string> WrittenShapes(RimeEmoProject project, Dictionary<Object, SourceClip> sources)
        {
            var written = new HashSet<string>();
            foreach (var shape in RimeEmoFace.Shapes(project))
            {
                var binding = RimeEmoClips.Binding(shape);
                foreach (var source in sources.Values)
                {
                    var curve = AnimationUtility.GetEditorCurve(source.clip, binding);
                    if (curve == null || RimeEmoClips.IsConstant(curve, shape.defaultValue)) continue;
                    written.Add(shape.shape);
                    break;
                }
            }
            return written;
        }

        // Keeps exactly the written face Shape Keys: missing ones get Default Face, others are dropped.
        // Curves for anything other than the face mesh are kept.
        private static AnimationClip Restrict(AnimationClip clip, RimeEmoProject project, HashSet<string> written)
        {
            foreach (var shape in RimeEmoFace.Shapes(project))
            {
                var binding = RimeEmoClips.Binding(shape);
                bool has = AnimationUtility.GetEditorCurve(clip, binding) != null;
                if (!written.Contains(shape.shape)) { if (has) AnimationUtility.SetEditorCurve(clip, binding, null); }
                else if (!has) AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, Mathf.Max(1f / 60, clip.length), shape.defaultValue));
            }
            return clip;
        }

        private static readonly string[] GestureParameters = { "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight" };

        // Face Shape Keys the source FX shows as hand expressions: set by a state that a hand gesture switches to
        // (a transition with a GestureLeft/Right condition) or by a Blend Tree on a gesture weight, and only where the
        // value differs from Default Face. Checking the whole layer was too wide: a blink layer that merely pauses
        // during a fist counted its blinking, and a key held at Default Face cannot show through anyway.
        internal static HashSet<string> SourceGestureShapes(RimeEmoProject project)
        {
            var result = new HashSet<string>();
            if (!project || !(project.sourceFX is AnimatorController)) return result;
            string face = RimeEmoFace.Path(project);
            var defaults = RimeEmoFace.Shapes(project).GroupBy(s => s.shape).ToDictionary(g => g.Key, g => g.First().defaultValue);
            foreach (var clip in SourceGestureClips(project))
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!RimeEmoClips.IsShape(binding) || binding.path != face) continue;
                    string name = binding.propertyName.Substring("blendShape.".Length);
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || !defaults.TryGetValue(name, out float value) || RimeEmoClips.IsConstant(curve, value)) continue;
                    result.Add(name);
                }
            return result;
        }

        // The clips the source FX shows for a hand shape: what its hand-gesture states and gesture-weight Blend Trees play.
        internal static HashSet<AnimationClip> SourceGestureClips(RimeEmoProject project)
        {
            var states = new HashSet<AnimatorState>(); var trees = new List<Motion>();
            if (project && project.sourceFX is AnimatorController fx) foreach (var layer in fx.layers) if (layer.stateMachine) CollectGestureStates(layer.stateMachine, states, trees);
            return new HashSet<AnimationClip>(states.SelectMany(s => Clips(s.motion)).Concat(trees.SelectMany(Clips)).Where(c => c));
        }

        // Source gesture Shape Keys that RimeEmo does not write: where the old expression can still show through.
        internal static List<string> LeakingShapes(RimeEmoProject project)
        {
            // Compiling every assignment and menu expression is the expensive part; most source FXs leave nothing to check.
            var sourceShapes = SourceGestureShapes(project);
            if (sourceShapes.Count == 0) return new List<string>();
            var sources = Sources(project, out _);
            try
            {
                var written = WrittenShapes(project, sources);
                return sourceShapes.Where(s => !written.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList();
            }
            finally { foreach (var source in sources.Values) if (source.clip) Object.DestroyImmediate(source.clip); }
        }

        // States a hand gesture switches to (including a sub-machine's default state), and gesture-weight Blend Trees.
        private static void CollectGestureStates(AnimatorStateMachine machine, HashSet<AnimatorState> states, List<Motion> trees)
        {
            void Add(IEnumerable<AnimatorTransitionBase> transitions)
            {
                foreach (var t in transitions)
                {
                    // "When this hand shape is NOT made" gates a layer (e.g. blinking that pauses on a fist) rather than
                    // choosing an expression, so only Equals/Greater/Less on a gesture parameter selects a state.
                    if (!t || !t.conditions.Any(c => GestureParameters.Contains(c.parameter) && c.mode != AnimatorConditionMode.NotEqual)) continue;
                    if (t.destinationState) states.Add(t.destinationState);
                    else if (t.destinationStateMachine && t.destinationStateMachine.defaultState) states.Add(t.destinationStateMachine.defaultState);
                }
            }
            Add(machine.anyStateTransitions); Add(machine.entryTransitions);
            foreach (var child in machine.states)
            {
                if (!child.state) continue;
                Add(child.state.transitions);
                if (TreeUsesGesture(child.state.motion)) trees.Add(child.state.motion);
            }
            foreach (var child in machine.stateMachines)
            {
                if (!child.stateMachine) continue;
                Add(machine.GetStateMachineTransitions(child.stateMachine));
                CollectGestureStates(child.stateMachine, states, trees);
            }
        }

        private static bool TreeUsesGesture(Motion motion)
            => motion is BlendTree tree && (GestureParameters.Contains(tree.blendParameter) || GestureParameters.Contains(tree.blendParameterY)
                || tree.children.Any(c => TreeUsesGesture(c.motion)));

        internal static IEnumerable<AnimationClip> Clips(Motion motion)
        {
            if (motion is AnimationClip clip) { yield return clip; yield break; }
            if (motion is BlendTree tree) foreach (var child in tree.children) foreach (var nested in Clips(child.motion)) yield return nested;
        }

        // Removes the previous layer, states, transitions and clips stored inside the controller.
        private static void Clear(AnimatorController controller, string path)
        {
            controller.layers = Array.Empty<AnimatorControllerLayer>();
            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub && sub != controller) Object.DestroyImmediate(sub, true);
        }

        // A plain curve copy. Object.Instantiate is avoided: it also copies editor-only state and is slow.
        private static AnimationClip Copy(AnimationClip source)
        {
            var copy = new AnimationClip { frameRate = source.frameRate };
            AnimationUtility.SetAnimationClipSettings(copy, AnimationUtility.GetAnimationClipSettings(source));
            var bindings = AnimationUtility.GetCurveBindings(source);
            AnimationUtility.SetEditorCurves(copy, bindings, bindings.Select(b => AnimationUtility.GetEditorCurve(source, b)).ToArray());
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
                AnimationUtility.SetObjectReferenceCurve(copy, binding, AnimationUtility.GetObjectReferenceCurve(source, binding));
            AnimationUtility.SetAnimationEvents(copy, AnimationUtility.GetAnimationEvents(source));
            return copy;
        }

        private static AnimationClip SaveClip(AnimationClip clip, AnimatorController controller, string name)
        {
            clip.name = name; clip.hideFlags = HideFlags.None;
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }
    }
}
