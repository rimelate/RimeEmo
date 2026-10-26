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
        internal const int GeneratorVersion = 3;

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
        internal static AnimatorController Build(RimeEmoProject project)
        {
            var errors = Validate(project);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            string fingerprint = RimeEmoBuildState.Fingerprint(project);
            string folder = OutputFolder(project);
            RimeEmoAssets.Folder(RimeEmoAssets.GeneratedRoot); RimeEmoAssets.Folder(folder);
            string path = folder + "/" + ControllerFile;
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller) Clear(controller, path);
            else controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            while (controller.layers.Length > 0) controller.RemoveLayer(0); // A new controller starts with "Base Layer".

            controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
            controller.AddParameter("GestureRight", AnimatorControllerParameterType.Int);
            var sm = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);
            controller.AddLayer(new AnimatorControllerLayer { name = LayerName, stateMachine = sm, defaultWeight = 1, blendingMode = AnimatorLayerBlendingMode.Override });
            var sources = Sources(project, out var keys);
            var written = WrittenShapes(project, sources);
            if (project.overrideSourceGestureFaces) written.UnionWith(SourceGestureShapes(project));
            var clips = sources.ToDictionary(p => p.Key, p => SaveClip(Restrict(p.Value.clip, project, written), controller, p.Key.name));
            AnimationClip defaults = SaveClip(Restrict(RimeEmoClips.DefaultClip(project), project, written), controller, "Default Face");
            for (int l = 0; l < 8; l++) for (int r = 0; r < 8; r++)
            {
                var key = keys[l, r];
                var clip = key ? clips[key] : defaults;
                var state = sm.AddState("L" + l + " R" + r, new Vector3(r * 200, l * 65, 0));
                state.motion = clip;
                if (l == 0 && r == 0) sm.defaultState = state;
                var transition = sm.AddAnyStateTransition(state);
                transition.hasExitTime = false; transition.hasFixedDuration = true; transition.duration = project.gestureBlendSeconds;
                transition.canTransitionToSelf = false; transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
                transition.orderedInterruption = false;
                transition.AddCondition(AnimatorConditionMode.Equals, l, "GestureLeft");
                transition.AddCondition(AnimatorConditionMode.Equals, r, "GestureRight");
            }
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
            Undo.RecordObject(project, "RimeEmo: Rebuild FX");
            project.generatedFX = controller; project.generatedFolder = folder; project.generatedFingerprint = fingerprint;
            EditorUtility.SetDirty(project); AssetDatabase.SaveAssets();
            return controller;
        }

        internal struct SourceClip { internal AnimationClip clip; internal bool compiled; }

        // The clip of every assigned expression or motion, once each, and which one each of the 64 states plays.
        // Clips are new in-memory objects; the caller saves or destroys them.
        internal static Dictionary<Object, SourceClip> Sources(RimeEmoProject project, out Object[,] keys)
        {
            var sources = new Dictionary<Object, SourceClip>();
            keys = new Object[8, 8];
            for (int l = 0; l < 8; l++) for (int r = 0; r < 8; r++)
            {
                RimeEmoAssignments.TryResolve(project, l, r, out var rule);
                Object key = rule == null ? null : rule.motion ? (Object)rule.motion : rule.clip;
                keys[l, r] = key;
                if (!key || sources.ContainsKey(key)) continue;
                var dynamicSource = rule.motion ? rule.motion : RimeEmoDynamicClips.Find(project, rule.clip);
                sources[key] = dynamicSource
                    ? new SourceClip { clip = RimeEmoCompiler.Compile(dynamicSource), compiled = true }
                    : new SourceClip { clip = Copy(rule.clip) };
            }
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
            if (!project || !(project.sourceFX is AnimatorController fx)) return result;
            string face = RimeEmoFace.Path(project);
            var defaults = RimeEmoFace.Shapes(project).GroupBy(s => s.shape).ToDictionary(g => g.Key, g => g.First().defaultValue);
            var states = new HashSet<AnimatorState>(); var trees = new List<Motion>();
            foreach (var layer in fx.layers) if (layer.stateMachine) CollectGestureStates(layer.stateMachine, states, trees);
            foreach (var clip in states.SelectMany(s => Clips(s.motion)).Concat(trees.SelectMany(Clips)).Distinct())
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

        // Source gesture Shape Keys that RimeEmo does not write: where the old expression can still show through.
        internal static List<string> LeakingShapes(RimeEmoProject project)
        {
            var sources = Sources(project, out _);
            try
            {
                var written = WrittenShapes(project, sources);
                return SourceGestureShapes(project).Where(s => !written.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList();
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

        private static IEnumerable<AnimationClip> Clips(Motion motion)
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
