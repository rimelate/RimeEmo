using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoKeyframeStorageChecks
    {
        internal static void Run()
        {
            string root = "Assets/RimeEmoKeyframeStorageChecks-" + Guid.NewGuid().ToString("N");
            RimeEmoProject project = null; AnimationClip source = null; AnimationClip draft = null; AnimationClip saved = null; AnimationClip empty = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", root.Substring(7));
                project = ScriptableObject.CreateInstance<RimeEmoProject>(); AssetDatabase.CreateAsset(project, root + "/Project.asset");
                var shape = new ShapeEntry { path = "Face", shape = "Smile", part = FacePart.Mouth };
                project.shapes.Add(shape); var binding = RimeEmoClips.Binding(shape); source = new AnimationClip { name = "Source", frameRate = 24 };
                AnimationUtility.SetEditorCurve(source, binding, new AnimationCurve(new Keyframe(0, 10), new Keyframe(1, 80, 2, 3, .25f, .75f) { weightedMode = WeightedMode.Both }));
                var extra = EditorCurveBinding.FloatCurve("Face", typeof(Transform), "m_LocalPosition.x"); AnimationUtility.SetEditorCurve(source, extra, AnimationCurve.Linear(0, 0, 1, 2));
                var pptr = EditorCurveBinding.PPtrCurve("Face", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]"); var tex = new Texture2D(1, 1); AssetDatabase.CreateAsset(tex, root + "/Texture.asset");
                AnimationUtility.SetObjectReferenceCurve(source, pptr, new[] { new ObjectReferenceKeyframe { time = 0, value = tex } }); source.events = new[] { new AnimationEvent { time = .5f, functionName = "Keep" } };
                var settings = AnimationUtility.GetAnimationClipSettings(source); settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(source, settings);
                AssetDatabase.CreateAsset(source, root + "/Source.anim"); string sourceJson = EditorJsonUtility.ToJson(source);
                draft = UnityEngine.Object.Instantiate(source); saved = RimeEmoKeyframeClips.SaveTo(project, draft, root + "/Saved.anim");
                var savedId = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(saved));
                Check(RimeEmoKeyframeClips.Owns(project, saved) && RimeEmoClipClassification.Classify(project, saved) == RimeEmoClipKind.Dynamic, "new keyframe output ownership/classification");
                Check(saved.frameRate == 24 && AnimationUtility.GetAnimationClipSettings(saved).loopTime && saved.events.Length == 1 && AnimationUtility.GetEditorCurve(saved, extra) != null && AnimationUtility.GetObjectReferenceCurve(saved, pptr).Length == 1, "saved data was lost");
                var retained = AnimationUtility.GetEditorCurve(saved, binding)[1]; Check(retained.weightedMode == WeightedMode.Both && retained.outWeight == .75f && retained.inTangent == 2, "weighted key metadata lost"); Check(Mathf.Approximately(AnimationUtility.GetEditorCurve(saved, extra).Evaluate(.5f), 1) && AnimationUtility.GetObjectReferenceCurve(saved, pptr)[0].value == tex && saved.events[0].functionName == "Keep", "preserved curve/event values changed"); RimeEmoKeyframes.SetKey(draft, shape, 0, 55, Interpolation.Linear); draft.name = "Changed"; var overwritten = RimeEmoKeyframeClips.SaveTo(project, draft, root + "/Saved.anim"); Check(overwritten == saved && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(overwritten)) == savedId, "overwrite replaced asset identity"); Check(Mathf.Approximately(AnimationUtility.GetEditorCurve(saved, binding).Evaluate(0), 55), "overwrite ignored edited key"); Check(EditorJsonUtility.ToJson(source) == sourceJson, "source was modified");
                empty = new AnimationClip { name = "Empty" }; var emptySaved = RimeEmoKeyframeClips.SaveTo(project, empty, root + "/Empty.anim"); Check(RimeEmoKeyframeClips.Owns(project, emptySaved) && RimeEmoClipClassification.Classify(project, emptySaved) == RimeEmoClipKind.Dynamic, "empty keyframe output not recorded");
                bool rejected = false; try { RimeEmoKeyframeClips.SaveTo(project, draft, root + "/Source.anim"); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "original source overwrite accepted");
                var queueMotion = ScriptableObject.CreateInstance<RimeEmoMotion>(); var queueClip = new AnimationClip { name = "QueueOutput" }; AssetDatabase.CreateAsset(queueMotion, root + "/Queue.asset"); AssetDatabase.CreateAsset(queueClip, root + "/Queue.anim"); queueMotion.project = project; queueMotion.animationClip = queueClip; project.motions.Add(queueMotion); EditorUtility.SetDirty(project); AssetDatabase.SaveAssets();
                rejected = false; try { RimeEmoKeyframeClips.SaveTo(project, draft, root + "/Queue.anim"); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "queue output overwrite accepted");
                AnimationUtility.SetEditorCurve(empty, binding, AnimationCurve.Constant(0, 1, 42)); var constant = RimeEmoKeyframeClips.SaveTo(project, empty, root + "/Constant.anim"); Check(RimeEmoClipClassification.Classify(project, constant) == RimeEmoClipKind.Dynamic, "constant output classified static");
            }
            finally { if (empty) UnityEngine.Object.DestroyImmediate(empty); if (draft) UnityEngine.Object.DestroyImmediate(draft); AssetDatabase.DeleteAsset(root); }
        }
        private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("KeyframeStorage: " + message); }
    }
}
