using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        [SerializeField] private bool keyframeMode;
        [SerializeField] private float queueModeTime, keyframeModeTime;
        private bool QueueDraftDirty => motion && !EditorUtility.IsPersistent(motion) && EditorUtility.IsDirty(motion);
        private bool KeyframeDraftDirty => importedDraft && EditorUtility.IsDirty(importedDraft);
        private void RefreshDraftDirty() { hasUnsavedChanges = QueueDraftDirty || KeyframeDraftDirty; }

        private void EnsureKeyframeDraft()
        {
            if (importedDraft || !project) return;
            importedDraft = new AnimationClip { name = RimeEmoLocalization.T("新しいキーフレーム"), frameRate = 60, hideFlags = HideFlags.HideAndDontSave };
            importedTarget = null; EditorUtility.ClearDirty(importedDraft);
        }

        private void SetAnimationEditor(int editor)
        {
            bool next = editor == 1;
            if (next == keyframeMode || !project) return;
            retainedTimeline?.EndInteraction(); keyframeTimeline?.EndInteraction();
            if (keyframeMode) keyframeModeTime = time; else queueModeTime = time;
            ResetKeyframeValue(); PreviewMaterial(null); playing = false; showDefault = false;
            keyframeMode = next;
            if (next) EnsureKeyframeDraft(); else EnsureDraft();
            time = next ? keyframeModeTime : queueModeTime;
            dynamicCursor = time; RefreshDraftDirty(); toolkitDirty = true; preview?.Invalidate();
        }

        private bool FinishKeyframeDraft()
        {
            if (!KeyframeDraftDirty) return true;
            int choice = EditorUtility.DisplayDialogComplex("RimeEmo", RimeEmoLocalization.T("キーフレームの変更を保存しますか？"),
                RimeEmoLocalization.T("保存"), RimeEmoLocalization.T("キャンセル"), RimeEmoLocalization.T("保存せず進む"));
            return choice == 0 ? SaveImportedDraft(false) : choice == 2;
        }

        private void NewAnimationDraft()
        {
            if (keyframeMode)
            {
                if (!FinishKeyframeDraft()) return;
                ClearImportedDraft(); EnsureKeyframeDraft(); time = keyframeModeTime = 0; RefreshDraftDirty();
            }
            else if (FinishDraft()) ReplaceDraft(RimeEmoDrafts.Create(project));
        }

        private void UseKeyframeDraft(AnimationClip draft, AnimationClip target, bool dirty, float at)
        {
            retainedTimeline?.EndInteraction(); keyframeTimeline?.EndInteraction();
            if (!keyframeMode) queueModeTime = time;
            ClearImportedDraft(); importedDraft = draft; importedTarget = target;
            importedDraft.hideFlags = HideFlags.HideAndDontSave;
            EditorUtility.ClearDirty(importedDraft); if (dirty) EditorUtility.SetDirty(importedDraft);
            keyframeMode = true; ResetKeyframeValue(); SwitchMode(1); time = dynamicCursor = keyframeModeTime = SafeTime(at);
            keyframeTimeline?.SelectedKeys.Clear(); if (keyframeTimeline != null) keyframeTimeline.ActiveShape = -1;
            showDefault = false; playing = false; RefreshDraftDirty(); Changed();
        }

        private void CopyQueueToKeyframes()
        {
            if (!motion || motion.project != project || !FinishKeyframeDraft()) return;
            var compiled = RimeEmoCompiler.Compile(motion);
            compiled.name = motion.name + " " + RimeEmoLocalization.T("キーフレーム");
            UseKeyframeDraft(compiled, null, true, keyframeMode ? queueModeTime : time);
        }

        private void OpenKeyframeClip(AnimationClip clip)
        {
            if (!project || !clip) return;
            string rejection = RimeEmoClipClassification.Rejection(project, clip);
            if (rejection != null) { ShowNotification(new GUIContent(rejection)); return; }
            if (!FinishKeyframeDraft()) return;
            var draft = RimeEmoImportedClips.CreateDraft(clip);
            bool own = RimeEmoKeyframeClips.Owns(project, clip);
            if (!own) draft.name += " " + RimeEmoLocalization.T("キーフレーム");
            UseKeyframeDraft(draft, own ? clip : null, false, 0);
            motionMaterial = clip;
        }

        private bool SaveKeyframeToPath(string path)
        {
            if (!importedDraft || !project) return false;
            importedTarget = RimeEmoKeyframeClips.SaveTo(project, importedDraft, path);
            importedDraft.name = importedTarget.name;
            EditorUtility.ClearDirty(importedDraft); RefreshDraftDirty(); toolkitDirty = true;
            return true;
        }

        private bool SaveAllAnimationDrafts()
        {
            bool previousMode = keyframeMode;
            try
            {
                if (KeyframeDraftDirty && !SaveImportedDraft(false)) return false;
                if (QueueDraftDirty) { keyframeMode = false; if (!SaveDraftAs(false)) return false; }
                RefreshDraftDirty(); return !hasUnsavedChanges;
            }
            finally { keyframeMode = previousMode; toolkitDirty = true; }
        }
    }
}
