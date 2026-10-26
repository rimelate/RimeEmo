using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal static class RimeEmoImportedChecks
    {
        internal static void Run()
        {
            string root = "Assets/RimeEmoImportedChecks-" + Guid.NewGuid().ToString("N");
            AnimationClip source = null; AnimationClip imported = null; AnimationClip snapshot = null;
            AnimationClip draft = null;
            RimeEmoWindow window = null;
            RimeEmoMotion motion = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", root.Substring(7));
                var project = ScriptableObject.CreateInstance<RimeEmoProject>();
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "Smile", defaultValue = 0, part = FacePart.Mouth });
                project.faceMeshSelected = true; project.faceMeshPath = "Face";
                AssetDatabase.CreateAsset(project, root + "/Project.asset");
                source = new AnimationClip { name = "Source", frameRate = 30 };
                var binding = RimeEmoClips.Binding(project.shapes[0]);
                var firstKey = new Keyframe(0, 0, 1, 2, 0.2f, 0.4f) { weightedMode = WeightedMode.Both };
                var curve = new AnimationCurve(firstKey, new Keyframe(1, 1, 2, 3));
                AnimationUtility.SetEditorCurve(source, binding, curve);
                var textureA = new Texture2D(1, 1); var textureB = new Texture2D(1, 1);
                AssetDatabase.CreateAsset(textureA, root + "/A.asset"); AssetDatabase.CreateAsset(textureB, root + "/B.asset");
                var ob = EditorCurveBinding.PPtrCurve("Face", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]");
                AnimationUtility.SetObjectReferenceCurve(source, ob, new[] { new ObjectReferenceKeyframe { time = 0, value = textureA }, new ObjectReferenceKeyframe { time = 1, value = textureB } });
                AnimationUtility.SetAnimationEvents(source, new[] { new AnimationEvent { time = .5f, functionName = "Ping" } });
                var settings = AnimationUtility.GetAnimationClipSettings(source); settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(source, settings);
                AssetDatabase.CreateAsset(source, root + "/Source.anim");
                Check(RimeEmoImportedClips.IsAnimated(source), "source was not classified animated");
                imported = RimeEmoImportedClips.Import(project, source);
                Check(imported != source && imported.events.Length == 1 && imported.frameRate == source.frameRate, "import lost source data");
                Check(AnimationUtility.GetEditorCurve(imported, binding).keys[1].outTangent == 3, "import lost tangent");
                Check(AnimationUtility.GetEditorCurve(imported, binding).keys[0].weightedMode == WeightedMode.Both && Mathf.Approximately(AnimationUtility.GetEditorCurve(imported, binding).keys[0].outWeight, 0.4f), "import lost weighted interpolation");
                Check(AnimationUtility.GetObjectReferenceCurve(imported, ob)[1].value == textureB, "import lost object reference");
                draft = RimeEmoImportedClips.CreateDraft(imported); RimeEmoClips.Set(draft, binding, 9); RimeEmoImportedClips.SaveTo(project, draft, imported);
                Check(Mathf.Approximately(AnimationUtility.GetEditorCurve(imported, binding).Evaluate(0), 9), "draft was not saved");
                Check(AnimationUtility.GetEditorCurve(source, binding).keys[1].outTangent == 3 && Mathf.Approximately(AnimationUtility.GetEditorCurve(source, binding).Evaluate(0), 0), "editing changed the original source");
                Check(imported.events.Length == 1 && imported.events[0].functionName == "Ping" && imported.events[0].time == .5f, "save changed events");
                Check(imported.frameRate == 30 && AnimationUtility.GetAnimationClipSettings(imported).loopTime, "save changed clip settings");
                Check(AnimationUtility.GetObjectReferenceCurve(imported, ob)[1].value == textureB, "float edit changed object curve");
                snapshot = RimeEmoImportedClips.CaptureFrame(project, imported, .75f);
                Check(RimeEmoAssets.Owns(project, imported) == false && RimeEmoAssets.Owns(project, snapshot), "ownership separation failed");
                Check(RimeEmoClips.IsConstant(AnimationUtility.GetEditorCurve(snapshot, binding), 9), "snapshot float curve is not constant");
                Check(AnimationUtility.GetObjectReferenceCurve(snapshot, ob)[0].value == textureA, "snapshot object sampling failed");
                Check(snapshot.events.Length == 0 && !AnimationUtility.GetAnimationClipSettings(snapshot).loopTime, "snapshot retained events or loop");
                motion = RimeEmoDrafts.Create(project);
                motion.tracks.Find(t => t.part == FacePart.Mouth).states.Add(new MotionState { clip = snapshot, hold = 1 });
                RimeEmoMotionMaterials.Freeze(motion);
                var isolated = motion.tracks.Find(t => t.part == FacePart.Mouth).states[0].clip;
                Check(isolated != snapshot && AssetDatabase.GetAssetPath(isolated).Contains("/MotionSources/"), "motion still references editable static clip");
                RimeEmoClips.Set(snapshot, binding, 25);
                Check(Mathf.Approximately(RimeEmoClips.Value(isolated, project.shapes[0]), 9), "static edit changed motion material");
                var output = RimeEmoDrafts.SaveTo(motion, root + "/Motion.anim");
                RimeEmoClips.Set(snapshot, binding, 50); AssetDatabase.SaveAssets();
                Check(Mathf.Approximately(RimeEmoClips.Value(output, project.shapes[0]), 9), "static save regenerated a different motion");
                bool rejected = false;
                try { RimeEmoDrafts.SaveTo(motion, AssetDatabase.GetAssetPath(snapshot)); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "motion was allowed to overwrite an expression");
                CheckQueueOverwrite(project, source, root);
            }
            finally
            {
                if (window) { window.DiscardChanges(); window.Close(); }
                if (draft) UnityEngine.Object.DestroyImmediate(draft);
                if (motion) UnityEngine.Object.DestroyImmediate(motion);
                if (source && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source))) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(source));
                if (snapshot && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(snapshot))) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(snapshot));
                AssetDatabase.DeleteAsset(root);
            }
        }
        private static void CheckQueueOverwrite(RimeEmoProject project, AnimationClip source, string root)
        {
            var queue = RimeEmoQueueImport.Create(project, source);
            try
            {
                string path = root + "/Existing.anim";
                var target = new AnimationClip { name = "Existing" };
                var unrelated = EditorCurveBinding.FloatCurve("Accessory", typeof(Transform), "m_LocalPosition.x");
                AnimationUtility.SetEditorCurve(target, unrelated, AnimationCurve.Constant(0, 1, 7));
                var objectBinding = AnimationUtility.GetObjectReferenceCurveBindings(source).Single();
                var sourceKeys = AnimationUtility.GetObjectReferenceCurve(source, objectBinding);
                AnimationUtility.SetObjectReferenceCurve(target, objectBinding, new[] { new ObjectReferenceKeyframe { time = 0, value = sourceKeys[1].value } });
                AnimationUtility.SetAnimationEvents(target, new[] { new AnimationEvent { time = .2f, functionName = "PreviousEvent" } });
                AssetDatabase.CreateAsset(target, path);
                string guid = AssetDatabase.AssetPathToGUID(path);

                for (int save = 0; save < 2; save++)
                {
                    var output = RimeEmoDrafts.SaveTo(queue, path);
                    Check(output == target && AssetDatabase.AssetPathToGUID(path) == guid && output.name == "Existing", "queue overwrite replaced clip identity");
                    var keys = AnimationUtility.GetObjectReferenceCurve(output, objectBinding);
                    Check(keys.Length == sourceKeys.Length && keys.Select(k => k.time).SequenceEqual(sourceKeys.Select(k => k.time))
                        && keys.Select(k => k.value).SequenceEqual(sourceKeys.Select(k => k.value)), "queue overwrite lost source object references");
                    var events = AnimationUtility.GetAnimationEvents(output);
                    Check(events.Length == 1 && events[0].functionName == "Ping" && events[0].time == .5f, "queue overwrite lost or duplicated source events");
                    Check(AnimationUtility.GetEditorCurve(output, unrelated).Evaluate(0) == 7, "queue overwrite removed unrelated target curve");
                    Check(AnimationUtility.GetAnimationClipSettings(output).loopTime, "queue overwrite lost loop setting");
                    Check(RimeEmoDynamicClips.Find(project, output).sourceClip == queue.sourceClip, "queue overwrite lost editing source");
                }
                var outputKeys = AnimationUtility.GetObjectReferenceCurve(target, objectBinding);
                outputKeys[0].value = sourceKeys[1].value;
                AnimationUtility.SetObjectReferenceCurve(target, objectBinding, outputKeys);
                var outputEvent = new AnimationEvent { time = .75f, functionName = "OutputOnlyEvent" };
                AnimationUtility.SetAnimationEvents(target, target.events.Concat(new[] { outputEvent }).ToArray());
                RimeEmoDrafts.SaveTo(queue, path);
                Check(AnimationUtility.GetObjectReferenceCurve(target, objectBinding)[0].value == sourceKeys[1].value
                    && target.events.Any(e => e.functionName == "OutputOnlyEvent"), "ordinary queue save replaced output-side edits");
                Check(source.events.Length == 1 && source.events[0].functionName == "Ping"
                    && AnimationUtility.GetObjectReferenceCurve(source, objectBinding).Length == sourceKeys.Length, "queue overwrite changed original source");
            }
            finally { Undo.ClearUndo(queue); UnityEngine.Object.DestroyImmediate(queue); }
        }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("ImportedClips: " + message); }
    }
}


