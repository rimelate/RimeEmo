using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoDrafts
    {
        internal static RimeEmoMotion Create(RimeEmoProject project)
        {
            var draft = ScriptableObject.CreateInstance<RimeEmoMotion>();
            draft.name = RimeEmoLocalization.T("新しいアニメーション"); draft.project = project;
            draft.hideFlags = HideFlags.HideAndDontSave;
            foreach (FacePart part in Enum.GetValues(typeof(FacePart))) draft.tracks.Add(new PartTrack { part = part });
            EditorUtility.ClearDirty(draft);
            return draft;
        }
        internal static RimeEmoMotion Copy(RimeEmoMotion source)
        {
            var draft = UnityEngine.Object.Instantiate(source);
            draft.name = source.animationClip ? source.animationClip.name : source.name;
            draft.hideFlags = HideFlags.HideAndDontSave;
            RimeEmoTimeline.NormalizeKeeps(draft);
            RimeEmoMotionMaterials.Freeze(draft);
            EditorUtility.ClearDirty(draft);
            return draft;
        }
        internal static AnimationClip SaveTo(RimeEmoMotion draft, string path)
        {
            if (!draft || !draft.project) throw new InvalidOperationException(RimeEmoLocalization.T("Projectを指定してください。"));
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(RimeEmoLocalization.T("Assets内の.animファイルを指定してください。"));
            var target = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (AssetDatabase.LoadMainAssetAtPath(path) && !target) throw new InvalidOperationException(RimeEmoLocalization.T("AnimationClip以外には上書きできません。"));
            if (target && draft.tracks.SelectMany(t => t.states).Any(s => s.clip == target))
                throw new InvalidOperationException(RimeEmoLocalization.T("素材として使用中のClipには上書きできません。別名で保存してください。"));
            if (target && (draft.project.clips.Any(c => c.clip == target) || draft.project.animations.Any(c => c.clip == target)))
                throw new InvalidOperationException(RimeEmoLocalization.T("表情や取り込んだ動きには上書きできません。別名で保存してください。"));
            RimeEmoMotionMaterials.Freeze(draft);
            // Evaluate before changing either the output asset or saved editing data.
            var compiled = RimeEmoCompiler.Compile(draft);
            try
            {
                var saved = target ? RimeEmoDynamicClips.Find(draft.project, target) : null;
                if (!target)
                {
                    target = UnityEngine.Object.Instantiate(compiled); target.name = Path.GetFileNameWithoutExtension(path);
                    AssetDatabase.CreateAsset(target, path);
                }
                else
                {
                    Undo.RecordObject(target, "RimeEmo: Save Animation");
                    var oldBindings = saved ? saved.generatedShapePaths.Select((p, i) => EditorCurveBinding.FloatCurve(p, typeof(SkinnedMeshRenderer), saved.generatedShapeProperties[i])).ToArray()
                        : Array.Empty<EditorCurveBinding>();
                    foreach (var binding in oldBindings) AnimationUtility.SetEditorCurve(target, binding, null);
                    var bindings = AnimationUtility.GetCurveBindings(compiled);
                    AnimationUtility.SetEditorCurves(target, bindings, bindings.Select(b => AnimationUtility.GetEditorCurve(compiled, b)).ToArray());
                    if (draft.sourceClip && (!saved || saved.sourceClip != draft.sourceClip))
                    {
                        // Populate a different output from the imported source; ordinary saves retain output-side edits.
                        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(compiled))
                            AnimationUtility.SetObjectReferenceCurve(target, binding, AnimationUtility.GetObjectReferenceCurve(compiled, binding));
                        AnimationUtility.SetAnimationEvents(target, AnimationUtility.GetAnimationEvents(compiled));
                    }
                    var settings = AnimationUtility.GetAnimationClipSettings(target); settings.loopTime = draft.loop;
                    AnimationUtility.SetAnimationClipSettings(target, settings);
                    EditorUtility.SetDirty(target);
                }
                if (!saved)
                {
                    saved = UnityEngine.Object.Instantiate(draft); saved.hideFlags = HideFlags.None;
                    string folder = RimeEmoAssets.Root(draft.project) + "/Motions"; RimeEmoAssets.Folder(folder);
                    AssetDatabase.CreateAsset(saved, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + RimeEmoAssets.SafeName(target.name) + ".asset"));
                    Undo.RecordObject(draft.project, "RimeEmo: Save Animation"); draft.project.motions.Add(saved); EditorUtility.SetDirty(draft.project);
                }
                else { Undo.RecordObject(saved, "RimeEmo: Save Animation"); EditorUtility.CopySerialized(draft, saved); saved.hideFlags = HideFlags.None; }
                saved.name = target.name; saved.animationClip = target;
                RimeEmoCompiler.BuildCurves(draft, out var generated, out _);
                saved.generatedShapePaths = generated.Select(b => b.path).ToList();
                saved.generatedShapeProperties = generated.Select(b => b.propertyName).ToList();
                EditorUtility.SetDirty(saved);
                draft.animationClip = target; draft.name = target.name;
                AssetDatabase.SaveAssets();
                return target;
            }
            finally { UnityEngine.Object.DestroyImmediate(compiled); }
        }
    }

    public sealed partial class RimeEmoWindow
    {
        private void EnsureDraft()
        {
            if (!project) return;
            if (importedDraft)
            {
                // Recover an open draft from the former import-only curve screen.
                bool wasEdited = hasUnsavedChanges || EditorUtility.IsDirty(importedDraft);
                var next = RimeEmoQueueImport.Create(project, importedDraft);
                ReplaceDraft(next);
                if (wasEdited) { EditorUtility.SetDirty(next); hasUnsavedChanges = true; }
            }
            if (!motion || motion.project != project) motion = RimeEmoDrafts.Create(project);
            else if (EditorUtility.IsPersistent(motion)) motion = RimeEmoDrafts.Copy(motion);
            RimeEmoTimeline.NormalizeKeeps(motion);

        }
        private bool SaveDraft() => SaveDraftAs(false);

        private bool SaveDraftAs(bool saveAs)
        {
            if (importedDraft) return SaveImportedDraft(saveAs);
            if (!motion || !project) return false;
            retainedTimeline?.EndInteraction();
            string current = motion.animationClip ? AssetDatabase.GetAssetPath(motion.animationClip) : "";
            bool overwriteCurrent = !saveAs && !string.IsNullOrEmpty(current);
            string path = overwriteCurrent ? current : EditorUtility.SaveFilePanelInProject(RimeEmoLocalization.T("アニメーションを保存"), motion.name, "anim", RimeEmoLocalization.T("新規保存先、または上書きする.animを選択してください。"),
                string.IsNullOrEmpty(current) ? RimeEmoAssets.Root(project) + "/Animations" : Path.GetDirectoryName(current).Replace('\\', '/'));
            if (string.IsNullOrEmpty(path)) return false;
            if (overwriteCurrent) path = RenameSavedMotion(motion.animationClip, motion.name, path);
            else
            {
                // Saving into an .anim that RimeEmo did not create replaces its face curves; confirm explicitly.
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing && !RimeEmoDynamicClips.Find(project, existing) && !EditorUtility.DisplayDialog("RimeEmo",
                    string.Format(RimeEmoLocalization.T("「{0}」はRimeEmoで作った動きではありません。上書きすると、このClipの顔のBlendShapeカーブが置き換わります。上書きしますか？"), path),
                    RimeEmoLocalization.T("上書き"), RimeEmoLocalization.T("キャンセル"))) return false;
            }
            var target = RimeEmoDrafts.SaveTo(motion, path);
            EditorUtility.ClearDirty(motion);
            hasUnsavedChanges = false; toolkitDirty = true;
            EditorGUIUtility.PingObject(target); return true;
        }
        // The name field edits the draft; apply it to the saved .anim and its editing data instead of reverting it on save.
        private string RenameSavedMotion(AnimationClip clip, string name, string path)
        {
            string next = RimeEmoAssets.SafeName(name);
            if (!clip || clip.name == next) return path;
            string error = AssetDatabase.RenameAsset(path, next);
            if (!string.IsNullOrEmpty(error))
            {
                EditorUtility.DisplayDialog("RimeEmo", RimeEmoLocalization.T("名前を変更できなかったため、元の名前で保存します。") + "\n" + error, "OK");
                return path;
            }
            var saved = RimeEmoDynamicClips.Find(project, clip);
            string savedPath = saved ? AssetDatabase.GetAssetPath(saved) : "";
            if (!string.IsNullOrEmpty(savedPath) && Path.GetFileNameWithoutExtension(savedPath) != next)
            {
                // Editing data keeps its old file name if another asset already uses the new one.
                string unique = AssetDatabase.GenerateUniqueAssetPath(Path.GetDirectoryName(savedPath).Replace('\\', '/') + "/" + next + ".asset");
                AssetDatabase.RenameAsset(savedPath, Path.GetFileNameWithoutExtension(unique));
            }
            return AssetDatabase.GetAssetPath(clip);
        }
        private bool FinishDraft()
        {
            if (!hasUnsavedChanges) return true;
            int choice = EditorUtility.DisplayDialogComplex(RimeEmoLocalization.T("編集中のアニメーション"), RimeEmoLocalization.T("変更を保存しますか？"), RimeEmoLocalization.T("保存"), RimeEmoLocalization.T("キャンセル"), RimeEmoLocalization.T("保存せず進む"));
            return choice == 0 ? SaveDraft() : choice == 2;
        }
        private void ReplaceDraft(RimeEmoMotion next)
        {
            ClearImportedDraft();
            retainedTimeline?.EndInteraction();
            if (motion && !EditorUtility.IsPersistent(motion)) { Undo.ClearUndo(motion); DestroyImmediate(motion); }
            retainedTimeline?.SelectedStates.Clear();
            motion = next; activeTrack = activeState = 0; time = dynamicCursor = 0; playing = false; hasUnsavedChanges = false;
            Changed();
        }
        public override void SaveChanges() { if (SaveDraft()) base.SaveChanges(); }
        private void OnDestroy()
        {
            ClearImportedDraft();
            if (motion && !EditorUtility.IsPersistent(motion)) { Undo.ClearUndo(motion); DestroyImmediate(motion); }
        }
    }
}



