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

        // Describe runs on every edit and scene change; compiling each assigned motion again made slider drags and
        // scene edits slow. Contents are kept until one of their sources changes. Build never uses this cache.
        private sealed class CachedContent { internal string signature, text; }
        private static readonly Dictionary<(Object, Object), CachedContent> contentCache = new Dictionary<(Object, Object), CachedContent>();
        // Not every curve edit marks the clip dirty, so curve edits are counted as well.
        private static readonly Dictionary<int, int> curveEdits = new Dictionary<int, int>();

        static RimeEmoBuildState()
        {
            AnimationUtility.onCurveWasModified += (clip, binding, type) =>
            {
                if (!clip) return;
                curveEdits.TryGetValue(clip.GetInstanceID(), out int count); curveEdits[clip.GetInstanceID()] = count + 1;
            };
            // Undo and reimports can restore content without a new dirty count.
            Undo.undoRedoPerformed += () => contentCache.Clear();
            EditorApplication.projectChanged += () => contentCache.Clear();
        }

        internal static State Describe(RimeEmoProject project, GameObject avatar)
        {
            if (!project || !avatar || EditorUtility.IsPersistent(avatar)) return State.Unavailable;
            var applied = RimeEmoApply.AppliedController(project, avatar);
            if (!applied) return State.NotApplied;
            // An older build, a Replace-mode component from before, or a build never applied leaves the avatar behind.
            if (applied != project.generatedFX || !RimeEmoApply.AppliedAsAppend(project, avatar) || string.IsNullOrEmpty(project.generatedFingerprint)) return State.Stale;
            return project.generatedFingerprint == Fingerprint(project, true, RimeEmoFaceSync.Analyze(project, avatar)) ? State.UpToDate : State.Stale;
        }

        internal static string Fingerprint(RimeEmoProject project) => Fingerprint(project, false, RimeEmoFaceSync.Analyze(project, (GameObject)null));
        internal static string Fingerprint(RimeEmoProject project, RimeEmoFaceSync sync) => Fingerprint(project, false, sync);

        // Covers every input of RimeEmoBuild.Build: face shapes, the resolved 8×8 table, the content of each assigned
        // clip or motion, and how the avatar blinks and lip-syncs. The source FX only counts while its gesture faces
        // are overridden.
        private static string Fingerprint(RimeEmoProject project, bool cached, RimeEmoFaceSync sync)
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
                    if (key && !contents.ContainsKey(key)) contents[key] = cached ? CachedContentOf(project, rule, key) : Content(project, rule);
                }
                text.Append('\n');
            }
            text.Append(sync.Describe());
            text.Append("menu:").Append(project.expressionMenu).Append('\n');
            int index = 0;
            foreach (var clip in RimeEmoFaceMenu.MenuClips(project))
            {
                // The name is the menu label; a menu clip mirrors how Build picks its source, like an assignment by clip.
                text.Append('m').Append(++index).Append(':').Append(Id(clip)).Append(':').Append(clip.name).Append('\n');
                var rule = new GestureRule { clip = clip };
                if (!contents.ContainsKey(clip)) contents[clip] = cached ? CachedContentOf(project, rule, clip) : Content(project, rule);
            }
            foreach (var setting in project.tracking.Where(t => t != null && t.clip && (t.stopBlink || t.stopLipSync)).OrderBy(t => Id(t.clip), StringComparer.Ordinal))
                text.Append("tracking:").Append(Id(setting.clip)).Append(':').Append(setting.stopBlink).Append(':').Append(setting.stopLipSync).Append('\n');
            foreach (var pair in contents.OrderBy(p => Id(p.Key), StringComparer.Ordinal)) text.Append(Id(pair.Key)).Append('\n').Append(pair.Value);
            return Hash128.Compute(text.ToString()).ToString();
        }

        private static string CachedContentOf(RimeEmoProject project, GestureRule rule, Object key)
        {
            var dynamicSource = rule.motion ? rule.motion : RimeEmoDynamicClips.Find(project, rule.clip);
            var signature = new StringBuilder();
            void Source(Object source)
            {
                if (!source) { signature.Append("-;"); return; }
                int id = source.GetInstanceID();
                curveEdits.TryGetValue(id, out int edits);
                signature.Append(id).Append(':').Append(EditorUtility.GetDirtyCount(source)).Append(':').Append(edits).Append(';');
            }
            if (!dynamicSource) Source(rule.clip);
            else
            {
                // A compiled motion also reads the project's face shapes and every clip its blocks use.
                Source(project); Source(dynamicSource); Source(dynamicSource.project); Source(dynamicSource.sourceClip);
                foreach (var track in dynamicSource.tracks) foreach (var state in track.states) Source(state.clip);
            }
            string text = signature.ToString();
            if (contentCache.TryGetValue((project, key), out var entry) && entry.signature == text) return entry.text;
            contentCache[(project, key)] = entry = new CachedContent { signature = text, text = Content(project, rule) };
            return entry.text;
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
