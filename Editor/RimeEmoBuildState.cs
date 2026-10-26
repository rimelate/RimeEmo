using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RimeEmo.Editor
{
    // Whether the FX applied to the avatar still reflects the current assignments and materials.
    internal static class RimeEmoBuildState
    {
        internal enum State { Unavailable, NotApplied, UpToDate, Stale }

        internal static State Describe(RimeEmoProject project, GameObject avatar)
        {
            if (!project || !avatar || EditorUtility.IsPersistent(avatar)) return State.Unavailable;
            var applied = RimeEmoApply.AppliedController(project, avatar);
            if (!applied) return State.NotApplied;
            // An older build, a Replace-mode component from before, or a build never applied leaves the avatar behind.
            if (applied != project.generatedFX || !RimeEmoApply.AppliedAsAppend(project, avatar) || string.IsNullOrEmpty(project.generatedFingerprint)) return State.Stale;
            return project.generatedFingerprint == Fingerprint(project) ? State.UpToDate : State.Stale;
        }

        // Covers every input of RimeEmoBuild.Build: face shapes, the resolved 8×8 table and the content of
        // each assigned clip or motion. The source FX only counts while its gesture faces are overridden.
        internal static string Fingerprint(RimeEmoProject project)
        {
            if (!project) return "";
            var text = new StringBuilder();
            text.Append("generator:").Append(RimeEmoBuild.GeneratorVersion).Append('\n');
            text.Append("override:").Append(project.overrideSourceGestureFaces).Append('\n');
            if (project.overrideSourceGestureFaces)
            {
                string fxPath = project.sourceFX ? AssetDatabase.GetAssetPath(project.sourceFX) : "";
                text.Append("fx:").Append(Id(project.sourceFX)).Append(':')
                    .Append(string.IsNullOrEmpty(fxPath) ? "" : AssetDatabase.GetAssetDependencyHash(fxPath).ToString()).Append('\n');
            }
            text.Append("face:").Append(RimeEmoFace.Path(project)).Append('\n');
            foreach (var shape in RimeEmoFace.Shapes(project))
                text.Append(shape.path).Append('/').Append(shape.shape).Append('=').Append(Number(shape.defaultValue)).Append('\n');
            text.Append("blend:").Append(Number(project.gestureBlendSeconds)).Append('\n');
            var contents = new Dictionary<Object, string>();
            for (int l = 0; l < 8; l++) for (int r = 0; r < 8; r++)
            {
                text.Append(l).Append(',').Append(r).Append(':');
                if (!RimeEmoAssignments.TryResolve(project, l, r, out var rule)) text.Append("unresolved");
                else if (rule != null)
                {
                    Object key = rule.motion ? (Object)rule.motion : rule.clip;
                    text.Append(Id(key));
                    if (key && !contents.ContainsKey(key)) contents[key] = Content(project, rule);
                }
                text.Append('\n');
            }
            foreach (var pair in contents.OrderBy(p => Id(p.Key), StringComparer.Ordinal)) text.Append(Id(pair.Key)).Append('\n').Append(pair.Value);
            return Hash128.Compute(text.ToString()).ToString();
        }

        // Mirrors how Build picks its source: a motion is compiled, a static clip is copied as is.
        private static string Content(RimeEmoProject project, GestureRule rule)
        {
            var dynamicSource = rule.motion ? rule.motion : RimeEmoDynamicClips.Find(project, rule.clip);
            if (!dynamicSource) return Clip(rule.clip);
            AnimationClip compiled = null;
            try { compiled = RimeEmoCompiler.Compile(dynamicSource); return Clip(compiled); }
            catch (Exception e) { return "error:" + e.Message + "\n"; }
            finally { if (compiled) Object.DestroyImmediate(compiled); }
        }

        private static string Clip(AnimationClip clip)
        {
            if (!clip) return "none\n";
            var text = new StringBuilder();
            text.Append("loop:").Append(AnimationUtility.GetAnimationClipSettings(clip).loopTime).Append(" rate:").Append(Number(clip.frameRate)).Append('\n');
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                Binding(text, binding);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null)
                    foreach (var key in curve.keys)
                        text.Append(Number(key.time)).Append(',').Append(Number(key.value)).Append(',').Append(Number(key.inTangent)).Append(',').Append(Number(key.outTangent))
                            .Append(',').Append((int)key.weightedMode).Append(',').Append(Number(key.inWeight)).Append(',').Append(Number(key.outWeight)).Append(';');
                text.Append('\n');
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                Binding(text, binding);
                foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding)) text.Append(Number(key.time)).Append(',').Append(Id(key.value)).Append(';');
                text.Append('\n');
            }
            foreach (var e in AnimationUtility.GetAnimationEvents(clip))
                text.Append("event:").Append(Number(e.time)).Append(',').Append(e.functionName).Append(',').Append(e.stringParameter).Append(',')
                    .Append(Number(e.floatParameter)).Append(',').Append(e.intParameter).Append(',').Append(Id(e.objectReferenceParameter)).Append('\n');
            return text.ToString();
        }

        private static void Binding(StringBuilder text, EditorCurveBinding binding)
            => text.Append(binding.path).Append('|').Append(binding.type != null ? binding.type.FullName : "").Append('|').Append(binding.propertyName).Append(':');

        private static string Id(Object value)
        {
            if (!value) return "-";
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long local) ? guid + ":" + local : "i" + value.GetInstanceID();
        }

        private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
