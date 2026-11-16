using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        [SerializeField] private AnimationClip importedTarget, importedDraft;
        private int importedCurveIndex;
        private Vector2 importedScroll;
        private bool ImportedEditing => keyframeMode && importedDraft && project;
        private float EditingDuration => ImportedEditing ? Mathf.Max(1f / 60, importedDraft.length) : RimeEmoCompiler.Duration(motion);
        private bool EditingLoop => ImportedEditing ? AnimationUtility.GetAnimationClipSettings(importedDraft).loopTime : motion && motion.loop;

        private void ClearImportedDraft()
        {
            keyframeTimeline?.EndInteraction(); ResetKeyframeValue();
            if (importedDraft) { Undo.ClearUndo(importedDraft); DestroyImmediate(importedDraft); }
            importedDraft = importedTarget = null;
            keyframePinnedShape = null;
        }

        private void OpenImportedClip(AnimationClip clip)
        {
            if (!project || !clip) return;
            if (keyframeMode || RimeEmoKeyframeClips.Owns(project, clip)) { OpenKeyframeClip(clip); return; }
            var kind = RimeEmoClipClassification.Classify(project, clip);
            if (kind == RimeEmoClipKind.Excluded) { ShowNotification(new GUIContent(RimeEmoLocalization.T("対象の顔メッシュのBlendShapeがないため除外しました。"))); return; }
            if (kind == RimeEmoClipKind.Static) { OpenStaticMaterial(clip); return; }
            if (!FinishDraft()) return;
            var saved = RimeEmoDynamicClips.Find(project, clip);
            var next = saved ? RimeEmoDrafts.Copy(saved) : RimeEmoQueueImport.Create(project, clip);
            ReplaceDraft(next); motionMaterial = clip; dynamicCursor = 0; SwitchMode(1); time = 0;
            Changed();
        }
        private void ImportFromShelf(AnimationClip clip)
        {
            if (!project || !clip) return;
            bool known = project.clips.Concat(project.animations).Any(c => c.clip == clip) || RimeEmoDynamicClips.Find(project, clip);
            if (!known)
            {
                string rejection = RimeEmoClipClassification.Rejection(project, clip);
                if (rejection != null) { ShowNotification(new GUIContent(rejection)); return; }
            }
            MaterialSelection = known ? clip : RimeEmoAssets.Import(project, clip);
            RefreshMaterials();
        }

        private void OpenStaticMaterial(AnimationClip clip)
        {
            if (!project || !clip || RimeEmoClipClassification.Classify(project, clip) != RimeEmoClipKind.Static) return;
            // Static editing never modifies an animation output or an old imported-motion asset.
            selected = RimeEmoAssets.Owns(project, clip) ? clip : RimeEmoAssets.Import(project, clip);
            SwitchMode(0); showDefault = false; rowsDirty = true; RefreshMaterials(); toolkitDirty = true;
        }

        private void OpenSelectedMaterial()
        {
            var clip = MaterialSelection;
            if (RimeEmoClipClassification.Classify(project, clip) == RimeEmoClipKind.Dynamic) OpenImportedClip(clip);
            else OpenStaticMaterial(clip);
        }
        private void RefreshImportedCurveHost(bool show)
        {
            var host = UI<VisualElement>("imported-curves");
            if (!show) { keyframeTimeline?.EndInteraction(); host.Clear(); keyframeTimeline = null; return; }
            RefreshKeyframeEditor();
        }

        private bool SaveImportedDraft(bool saveAs)
        {
            if (!importedDraft || !project) return false;
            keyframeTimeline?.EndInteraction();
            string current = importedTarget ? AssetDatabase.GetAssetPath(importedTarget) : "";
            string folder = RimeEmoAssets.Root(project) + "/Keyframes";
            if (saveAs || string.IsNullOrEmpty(current)) RimeEmoAssets.Folder(folder);
            string path = !saveAs && !string.IsNullOrEmpty(current) ? current : EditorUtility.SaveFilePanelInProject(
                RimeEmoLocalization.T("アニメーションを保存"), importedDraft.name, "anim", RimeEmoLocalization.T("新規保存先、または上書きする.animを選択してください。"),
                string.IsNullOrEmpty(current) ? folder : System.IO.Path.GetDirectoryName(current).Replace('\\', '/'));
            if (string.IsNullOrEmpty(path)) return false;
            if (!saveAs && !string.IsNullOrEmpty(current)) path = RenameSavedMotion(importedTarget, importedDraft.name, path);
            if (!SaveKeyframeToPath(path)) return false;
            EditorGUIUtility.PingObject(importedTarget);
            return true;
        }

        private void EditImported(Action action)
        {
            if (!ImportedEditing) return;
            string before = EditorJsonUtility.ToJson(importedDraft);
            Undo.RecordObject(importedDraft, "RimeEmo: Edit Animation");
            action(); if (EditorJsonUtility.ToJson(importedDraft) == before) return;
            EditorUtility.SetDirty(importedDraft); hasUnsavedChanges = true;
            preview?.Invalidate(); toolkitDirty = true; RequestToolkitPreview();
        }

    }
}

