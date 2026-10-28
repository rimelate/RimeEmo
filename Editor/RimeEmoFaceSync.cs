using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RimeEmo.Editor
{
    // How an avatar blinks and lip-syncs, and so how an expression pauses them. VRChat's own blinking (eyelids set in
    // the Avatar Descriptor) and lip sync are paused with a tracking control on the state. Some avatars do both in their
    // FX instead (SiuSiu: a blink inside each looping face clip, and a Talk layer driven from the Viseme parameter);
    // for those the blink becomes part of the clips RimeEmo builds, and lip sync is held only where it is paused.
    internal sealed class RimeEmoFaceSync
    {
        internal bool vrcBlink = true, vrcLipSync = true;
        // The blink of the source FX's neutral face: its moving eye Shape Key curves, one loop long.
        internal AnimationClip blinkClip;
        internal EditorCurveBinding[] blinkBindings = Array.Empty<EditorCurveBinding>();
        internal AnimationCurve[] blinkCurves = Array.Empty<AnimationCurve>();
        internal float blinkLength;
        // Face Shape Keys the FX's lip sync layers animate.
        internal HashSet<string> lipShapes = new HashSet<string>();
        internal bool writeDefaultsOff;
        private Dictionary<string, float> defaults = new Dictionary<string, float>();
        private string face;

        internal bool AnimatedBlink => !vrcBlink && blinkClip && blinkLength > 0;
        // A state can leave lip sync to the lower layers only by not animating its keys, which needs Write Defaults off;
        // with it on, every state would write them and stop lip sync everywhere.
        internal bool AnimatedLipSync => !vrcLipSync && lipShapes.Count > 0 && writeDefaultsOff;
        internal bool CanPauseBlink => vrcBlink || AnimatedBlink;
        internal bool CanPauseLipSync => vrcLipSync || AnimatedLipSync;
        internal IEnumerable<string> BlinkShapes => blinkBindings.Select(b => b.propertyName.Substring("blendShape.".Length));

        internal static RimeEmoFaceSync Analyze(RimeEmoProject project, GameObject avatar)
        {
            if (!avatar && project) avatar = RimeEmoAvatarStorage.FindAvatar(project);
            RimeEmoFaceMenu.TrackingSupport(avatar, out bool blink, out bool lipSync);
            return Analyze(project, blink, lipSync);
        }

        internal static RimeEmoFaceSync Analyze(RimeEmoProject project, bool vrcBlink, bool vrcLipSync)
        {
            var sync = new RimeEmoFaceSync { vrcBlink = vrcBlink, vrcLipSync = vrcLipSync };
            if (!project || !(project.sourceFX is AnimatorController fx) || (vrcBlink && vrcLipSync)) return sync;
            sync.face = RimeEmoFace.Path(project);
            sync.defaults = RimeEmoFace.Shapes(project).GroupBy(s => s.shape).ToDictionary(g => g.Key, g => g.First().defaultValue);
            if (!vrcBlink) sync.FindBlink(project, fx);
            if (!vrcLipSync) sync.FindLipSync(fx);
            return sync;
        }

        // The neutral face (both hands resting first) of a looping clip whose eye Shape Keys move.
        private void FindBlink(RimeEmoProject project, AnimatorController fx)
        {
            var eyes = new HashSet<string>(RimeEmoFace.Shapes(project).Where(s => s.part == FacePart.LeftEye || s.part == FacePart.RightEye).Select(s => s.shape));
            var neutral = RimeEmoGestures.SourceRules(fx, out _).Where(r => r.left <= 0 && r.right <= 0).OrderByDescending(r => (r.left == 0 ? 1 : 0) + (r.right == 0 ? 1 : 0));
            foreach (var rule in neutral)
            {
                if (!AnimationUtility.GetAnimationClipSettings(rule.clip).loopTime) continue;
                var moving = AnimationUtility.GetCurveBindings(rule.clip)
                    .Where(b => RimeEmoClips.IsShape(b) && b.path == face && eyes.Contains(b.propertyName.Substring("blendShape.".Length)))
                    .Select(b => (binding: b, curve: AnimationUtility.GetEditorCurve(rule.clip, b)))
                    .Where(p => p.curve != null && p.curve.length > 1 && !RimeEmoClips.IsConstant(p.curve, p.curve.keys[0].value)).ToList();
                if (moving.Count == 0) continue;
                blinkClip = rule.clip; blinkLength = rule.clip.length;
                blinkBindings = moving.Select(p => p.binding).ToArray(); blinkCurves = moving.Select(p => p.curve).ToArray();
                return;
            }
        }

        // Layers reached from the Viseme parameter: one that reads it, then any that reads a parameter such a layer
        // drives (SiuSiu: Talk Control reads Viseme and drives Talk and VisemeWeight, which the Talk layer plays by).
        private void FindLipSync(AnimatorController fx)
        {
            var layers = fx.layers.Where(l => l.stateMachine).Select(l => l.stateMachine).ToList();
            var driven = new HashSet<string> { "Viseme" };
            var lip = new HashSet<AnimatorStateMachine>();
            for (bool changed = true; changed;)
            {
                changed = false;
                foreach (var machine in layers.Where(m => !lip.Contains(m) && Parameters(m).Overlaps(driven)))
                {
                    lip.Add(machine); changed = true;
                    driven.UnionWith(DrivenParameters(machine));
                }
            }
            foreach (var clip in lip.SelectMany(RimeEmoBuild.States).SelectMany(s => RimeEmoBuild.Clips(s.motion)).Where(c => c).Distinct())
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!RimeEmoClips.IsShape(binding) || binding.path != face) continue;
                    string name = binding.propertyName.Substring("blendShape.".Length);
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && defaults.TryGetValue(name, out float value) && !RimeEmoClips.IsConstant(curve, value)) lipShapes.Add(name);
                }
            writeDefaultsOff = layers.SelectMany(RimeEmoBuild.States).All(s => !s.writeDefaultValues);
        }

        private static HashSet<string> Parameters(AnimatorStateMachine root)
        {
            var result = new HashSet<string>();
            void Add(AnimatorTransitionBase transition) { if (transition) foreach (var c in transition.conditions) result.Add(c.parameter); }
            void Tree(Motion motion)
            {
                if (!(motion is BlendTree tree)) return;
                result.Add(tree.blendParameter); result.Add(tree.blendParameterY);
                foreach (var child in tree.children) { result.Add(child.directBlendParameter); Tree(child.motion); }
            }
            void Machine(AnimatorStateMachine machine)
            {
                foreach (var t in machine.anyStateTransitions) Add(t);
                foreach (var t in machine.entryTransitions) Add(t);
                foreach (var child in machine.states)
                {
                    var state = child.state;
                    if (!state) continue;
                    foreach (var t in state.transitions) Add(t);
                    if (state.speedParameterActive) result.Add(state.speedParameter);
                    if (state.timeParameterActive) result.Add(state.timeParameter);
                    if (state.cycleOffsetParameterActive) result.Add(state.cycleOffsetParameter);
                    if (state.mirrorParameterActive) result.Add(state.mirrorParameter);
                    Tree(state.motion);
                }
                foreach (var child in machine.stateMachines)
                {
                    if (!child.stateMachine) continue;
                    foreach (var t in machine.GetStateMachineTransitions(child.stateMachine)) Add(t);
                    Machine(child.stateMachine);
                }
            }
            Machine(root);
            result.Remove(""); result.Remove(null);
            return result;
        }

        // Names a VRChat Avatar Parameter Driver sets, read by name so RimeEmo compiles without the SDK.
        private static IEnumerable<string> DrivenParameters(AnimatorStateMachine root)
        {
            IEnumerable<AnimatorStateMachine> Machines(AnimatorStateMachine m) => new[] { m }.Concat(m.stateMachines.Where(c => c.stateMachine).SelectMany(c => Machines(c.stateMachine)));
            var behaviours = Machines(root).SelectMany(m => m.behaviours).Concat(RimeEmoBuild.States(root).SelectMany(s => s.behaviours));
            foreach (var behaviour in behaviours.Where(b => b && b.GetType().Name == "VRCAvatarParameterDriver"))
                using (var serialized = new SerializedObject(behaviour))
                {
                    var list = serialized.FindProperty("parameters");
                    for (int i = 0; list != null && i < list.arraySize; i++)
                    {
                        string name = list.GetArrayElementAtIndex(i).FindPropertyRelative("name")?.stringValue;
                        if (!string.IsNullOrEmpty(name)) yield return name;
                    }
                }
        }

        // Inputs of a build, for the applied-state fingerprint.
        internal string Describe()
        {
            string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
            var text = new StringBuilder("sync:").Append(vrcBlink).Append(',').Append(vrcLipSync).Append(',').Append(writeDefaultsOff).Append('\n');
            if (AnimatedBlink)
            {
                text.Append("blink:").Append(Number(blinkLength)).Append('\n');
                for (int i = 0; i < blinkBindings.Length; i++)
                    text.Append(blinkBindings[i].propertyName).Append('=').Append(string.Join(";", blinkCurves[i].keys.Select(k => Number(k.time) + "," + Number(k.value) + "," + Number(k.inTangent) + "," + Number(k.outTangent)))).Append('\n');
            }
            text.Append("lip:").Append(string.Join(",", lipShapes.OrderBy(s => s, StringComparer.Ordinal))).Append('\n');
            return text.ToString();
        }

        // Gives an expression the avatar's blink, unless it sets the blink keys itself (its own blink, or eyes held shut).
        // A still expression becomes one blink loop long and loops; a motion blinks along its own length.
        internal void AddBlink(AnimationClip clip)
        {
            if (!AnimatedBlink || !clip) return;
            for (int i = 0; i < blinkBindings.Length; i++)
            {
                var own = AnimationUtility.GetEditorCurve(clip, blinkBindings[i]);
                if (own != null && !RimeEmoClips.IsConstant(own, Default(blinkBindings[i]))) return;
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            bool still = !settings.loopTime && AnimationUtility.GetCurveBindings(clip).All(b => AnimationUtility.GetEditorCurve(clip, b) is AnimationCurve c && RimeEmoClips.IsConstant(c, c.keys[0].value))
                && AnimationUtility.GetObjectReferenceCurveBindings(clip).All(b => AnimationUtility.GetObjectReferenceCurve(clip, b).Length <= 1);
            // A motion that plays once holds its last pose; the clip runs on with the blink until it ends on open eyes.
            bool once = !still && !settings.loopTime;
            float length = still ? blinkLength : once ? Mathf.Ceil((clip.length + OnceBlinkTime) / blinkLength) * blinkLength : clip.length;
            for (int i = 0; i < blinkBindings.Length; i++)
            {
                var curve = Tile(blinkCurves[i], blinkLength, length);
                if (once) { var keys = curve.keys; keys[keys.Length - 1].value = Resting(blinkCurves[i]); curve.keys = keys; }
                AnimationUtility.SetEditorCurve(clip, blinkBindings[i], curve);
            }
            if (still) { settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, settings); }
        }

        // How long a played-once motion keeps blinking after it ends.
        internal const float OnceBlinkTime = 600;

        // An expression that blinks by itself holds its eyes where they rest between blinks instead.
        internal void RemoveBlink(AnimationClip clip)
        {
            if (!AnimatedBlink || !clip) return;
            foreach (var binding in blinkBindings)
            {
                var own = AnimationUtility.GetEditorCurve(clip, binding);
                if (own == null || RimeEmoClips.IsConstant(own, own.keys[0].value)) continue;
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, Mathf.Max(1f / 60, clip.length), Resting(own)));
            }
        }

        // The value a blink curve holds longest; blinks often start shut, so its first frame is no rest.
        private static float Resting(AnimationCurve curve)
        {
            float start = curve.keys[0].time, span = curve.keys[curve.length - 1].time - start;
            const int samples = 240;
            return Enumerable.Range(0, samples).Select(i => Mathf.Round(curve.Evaluate(start + span * (i + .5f) / samples) * 10) / 10)
                .GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        }

        // Holds the mouth's lip sync keys at Default Face, over the lip sync layers, only in this expression's state.
        internal void PauseLipSync(AnimationClip clip)
        {
            if (!AnimatedLipSync || !clip) return;
            foreach (string shape in lipShapes)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(face, typeof(SkinnedMeshRenderer), "blendShape." + shape),
                    AnimationCurve.Constant(0, Mathf.Max(1f / 60, clip.length), defaults.TryGetValue(shape, out float value) ? value : 0));
        }

        private float Default(EditorCurveBinding binding) => defaults.TryGetValue(binding.propertyName.Substring("blendShape.".Length), out float value) ? value : 0;

        private static AnimationCurve Tile(AnimationCurve source, float period, float length)
        {
            var keys = new List<Keyframe>();
            for (float offset = 0; offset < length - 0.0001f; offset += period)
                foreach (var key in source.keys)
                {
                    var shifted = key; shifted.time += offset;
                    if (shifted.time > length + 0.0001f) break;
                    if (keys.Count > 0 && Mathf.Abs(keys[keys.Count - 1].time - shifted.time) < 0.0001f) keys[keys.Count - 1] = shifted;
                    else keys.Add(shifted);
                }
            return new AnimationCurve(keys.ToArray());
        }
    }
}
