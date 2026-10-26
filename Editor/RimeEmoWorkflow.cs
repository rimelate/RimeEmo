using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        private float dynamicCursor, staticCursor;

        private void SwitchMode(int next)
        {
            if (page != 0) { page = 0; toolkitDirty = true; }
            if (mode == next) return;
            EndStaticGesture();
            retainedTimeline?.EndInteraction();
            PreviewMaterial(null);
            pointerMaterialRow = null;
            if (mode == 1) dynamicCursor = time; else staticCursor = time;
            mode = next;
            time = mode == 1 ? dynamicCursor : staticCursor;
            playing = false; showDefault = false; toolkitDirty = true;
            RequestToolkitPreview();
        }

        private static void SetButtonCaption(Button button, string text)
        {
            var caption = button.Q<Label>("action-caption");
            if (caption != null) caption.text = text; else button.text = text;
        }

        private void OpenMaterialActions()
        {
            if (!project || !selected) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(RimeEmoLocalization.T("表情を複製")), false, () => Run(() => { selected = RimeEmoAssets.Import(project, selected); RefreshMaterials(); }));
            menu.AddItem(new GUIContent(RimeEmoLocalization.T("Clipを書き出す")), false, () => Run(() => RimeEmoAssets.Export(project, selected, false)));
            if (mode == 0 && CanEditStatic())
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(RimeEmoLocalization.T("Defaultと同じ値を削除")), false, () => Run(() => { RimeEmoClips.RemoveDefaults(selected, project); Changed(); }));
                menu.AddItem(new GUIContent(RimeEmoLocalization.T("この表情から動的アニメーションを作成")), false, () => Run(() =>
                {
                    if (!FinishDraft()) return;
                    var draft = RimeEmoDrafts.Create(project); draft.name = selected.name + " Animation";
                    foreach (var track in draft.tracks)
                        if (RimeEmoClips.HasPart(project, selected, track.part))
                            track.states.Add(new MotionState { clip = selected });
                    RimeEmoMotionMaterials.Freeze(draft);
                    ReplaceDraft(draft); dynamicCursor = 0; SwitchMode(1);
                    hasUnsavedChanges = true; EditorUtility.SetDirty(draft);
                }));
                // Destructive, so last and apart from the rest.
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(RimeEmoLocalization.T("表情を削除…")), false, () => Run(DeleteSelectedExpression));
            }
            menu.ShowAsContext();
        }

        // Moves the selected expression to the trash (recoverable from there). Hands assigned to it fall back to
        // Default Face; motions keep working because they use frozen copies in MotionSources.
        private void DeleteSelectedExpression()
        {
            if (!CanEditStatic()) return;
            var clip = selected;
            int rules = project.gestureRules.Count(r => r != null && r.clip == clip);
            int choices = project.gestureChoices.Count(c => c != null && c.clip == clip);
            string message = string.Format(RimeEmoLocalization.T("表情「{0}」を削除しますか？Clipはゴミ箱へ移動します。"), clip.name);
            if (rules + choices > 0)
                message += "\n\n" + string.Format(RimeEmoLocalization.T("Gestureの割り当て{0}件で使われています。削除すると、その手の形はDefault Faceになります。"), rules + choices);
            if (!EditorUtility.DisplayDialog("RimeEmo", message, RimeEmoLocalization.T("削除"), RimeEmoLocalization.T("キャンセル"))) return;
            EndStaticGesture(false);
            int index = Mathf.Max(0, materialItems.FindIndex(c => c.clip == clip));
            Undo.RecordObject(project, "RimeEmo: Delete Expression");
            project.clips.RemoveAll(c => c != null && c.clip == clip);
            project.gestureRules.RemoveAll(r => r != null && r.clip == clip);
            project.gestureChoices.RemoveAll(c => c != null && c.clip == clip);
            EditorUtility.SetDirty(project);
            if (!AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(clip)))
                throw new System.InvalidOperationException(RimeEmoLocalization.T("Clipをゴミ箱へ移動できませんでした。"));
            AssetDatabase.SaveAssets();
            // The nearest expression takes its place (the next one, else the one above), so the editor stays on an
            // expression and repeated deletes stay on the same spot. Motions are skipped.
            RefreshMaterials();
            bool IsExpression(int i) => RimeEmoClipClassification.Classify(project, materialItems[i].clip) == RimeEmoClipKind.Static;
            int pick = Enumerable.Range(index, Mathf.Max(0, materialItems.Count - index)).Concat(Enumerable.Range(0, Mathf.Clamp(index, 0, materialItems.Count)).Reverse())
                .Where(IsExpression).DefaultIfEmpty(-1).First();
            selected = pick < 0 ? null : materialItems[pick].clip;
            rowsDirty = true; RefreshMaterials(); Changed();
        }

        private void CaptureCurrentFrame()
        {
            if (mode != 1 || (!ImportedEditing && (!motion || motion.project != project))) return;
            retainedTimeline?.EndInteraction();
            playing = false;
            float at = Mathf.Clamp(time, 0, EditingDuration);
            selected = ImportedEditing ? RimeEmoImportedClips.CaptureFrame(project, importedDraft, at) : RimeEmoAssets.CaptureFrame(motion, at);
            materialSearch = "";
            UI<TextField>("material-search").SetValueWithoutNotify("");
            SwitchMode(0); time = staticCursor = 0;
            rowsDirty = true; RefreshMaterials();
            ShowNotification(new GUIContent(RimeEmoLocalization.T("現在の表情を作成しました。動的な下書きは保持しています。")));
        }
    }
}
