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
        private bool ImportedEditing => importedDraft && project;
        private float EditingDuration => ImportedEditing ? Mathf.Max(1f / 60, importedDraft.length) : RimeEmoCompiler.Duration(motion);
        private bool EditingLoop => ImportedEditing ? AnimationUtility.GetAnimationClipSettings(importedDraft).loopTime : motion && motion.loop;

        private void ClearImportedDraft()
        {
            if (importedDraft) { Undo.ClearUndo(importedDraft); DestroyImmediate(importedDraft); }
            importedDraft = importedTarget = null;
        }

        private void OpenImportedClip(AnimationClip clip)
        {
            if (!project || !clip) return;
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
            if (!show) { host.Clear(); return; }
            if (host.childCount == 0) host.Add(new IMGUIContainer(DrawImportedCurves) { style = { flexGrow = 1 } });
        }

        private bool SaveImportedDraft(bool saveAs)
        {
            if (!ImportedEditing) return false;
            if (saveAs || !importedTarget)
            {
                // A new managed copy keeps this editing lane separate from expressions and block outputs.
                importedTarget = RimeEmoImportedClips.Import(project, importedDraft);
            }
            else RimeEmoImportedClips.SaveTo(project, importedDraft, importedTarget);
            EditorUtility.ClearDirty(importedDraft);

            hasUnsavedChanges = false; toolkitDirty = true;
            EditorGUIUtility.PingObject(importedTarget);
            return true;
        }

        private void EditImported(Action action)
        {
            if (!ImportedEditing) return;
            Undo.RecordObject(importedDraft, "RimeEmo: Edit Animation");
            action(); EditorUtility.SetDirty(importedDraft); hasUnsavedChanges = true;
            preview?.Invalidate(); toolkitDirty = true; RequestToolkitPreview();
        }

        private void DrawImportedCurves()
        {
            if (!ImportedEditing) return;
            var floats = AnimationUtility.GetCurveBindings(importedDraft);
            var objects = AnimationUtility.GetObjectReferenceCurveBindings(importedDraft);
            var bindings = floats.Concat(objects).ToArray();
            EditorGUILayout.HelpBox(RimeEmoLocalization.T("元のカーブを保持して編集します。カーブをクリックするとキーと補間を編集できます。プレビューは顔のBlendShapeのみです。"), MessageType.Info);
            if (bindings.Length == 0) { EditorGUILayout.LabelField(RimeEmoLocalization.T("編集できるカーブがありません。")); return; }
            importedCurveIndex = Mathf.Clamp(importedCurveIndex, 0, bindings.Length - 1);
            importedCurveIndex = EditorGUILayout.Popup(RimeEmoLocalization.T("プロパティ"), importedCurveIndex,
                bindings.Select(b => (string.IsNullOrEmpty(b.path) ? "(Root)" : b.path) + " / " + b.type.Name + " / " + b.propertyName).ToArray());
            var binding = bindings[importedCurveIndex];
            importedScroll = EditorGUILayout.BeginScrollView(importedScroll);
            if (importedCurveIndex < floats.Length)
            {
                var curve = AnimationUtility.GetEditorCurve(importedDraft, binding);
                EditorGUI.BeginChangeCheck();
                var changed = EditorGUILayout.CurveField(curve, GUILayout.Height(130));
                if (EditorGUI.EndChangeCheck()) EditImported(() => AnimationUtility.SetEditorCurve(importedDraft, binding, changed));
            }
            else
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(importedDraft, binding);
                for (int i = 0; i < keys.Length; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        float at = EditorGUILayout.FloatField(keys[i].time, GUILayout.Width(80));
                        var value = EditorGUILayout.ObjectField(keys[i].value, typeof(UnityEngine.Object), false);
                        if (EditorGUI.EndChangeCheck() && !float.IsNaN(at) && !float.IsInfinity(at))
                        {
                            keys[i] = new ObjectReferenceKeyframe { time = Mathf.Max(0, at), value = value };
                            var sorted = keys.OrderBy(k => k.time).ToArray();
                            EditImported(() => AnimationUtility.SetObjectReferenceCurve(importedDraft, binding, sorted));
                        }
                    }
                }
            }
            EditorGUILayout.LabelField(RimeEmoLocalization.T("Animation Eventと未編集のプロパティは、そのまま保存されます。"), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }

    }
}





