using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow : EditorWindow
    {
        [SerializeField] private GameObject avatar;
        [SerializeField] private RimeEmoProject project;
        [SerializeField] private AnimationClip selected;
        [SerializeField] private RimeEmoMotion motion;
        [SerializeField] private int mode;
        private RuntimeAnimatorController fx;
        private RimeEmoPreview preview;
        private AnimationClip defaults;
        private string search = "";
        private int category;
        private bool showDefault, playing;
        private float time;
        private double lastTick;
        private static string[] Parts => new[] { RimeEmoLocalization.T("すべて"), RimeEmoLocalization.T("左目"), RimeEmoLocalization.T("右目"), RimeEmoLocalization.T("眉"), RimeEmoLocalization.T("口"), RimeEmoLocalization.T("その他") };

        [MenuItem("Tools/RimeEmo/Open")]
        public static void Open() => GetWindow<RimeEmoWindow>("RimeEmo");

        public static void OpenForAvatar(GameObject target)
        {
            var window = GetWindow<RimeEmoWindow>("RimeEmo");
            window.Run(() => window.SelectAvatar(target));
            window.Show(); window.Focus();
        }

        private void OnEnable()
        {
            minSize = new Vector2(1000, 760);
            saveChangesMessage = RimeEmoLocalization.T("編集中のアニメーションを保存しますか？");
            preview = new RimeEmoPreview();
            RimeEmoLocalization.Changed += LanguageChanged;
            RimeEmoUpdates.Changed += RefreshUpdates;
            RefreshAvatar();
            Undo.undoRedoPerformed += Changed;
            RimeEmoClassificationWindow.ClassificationChanged += ClassificationEdited;
            RimeEmoShapeGroups.Changed += GroupSetEdited;
            EditorApplication.update += Tick;
            EditorApplication.projectChanged += Changed;
            AnimationUtility.onCurveWasModified += CurveModified;
            EditorApplication.hierarchyChanged += InvalidateApplyState;
            EditorApplication.hierarchyChanged += AvatarMayHaveChanged;
        }

        private void OnDisable()
        {
            CancelAutoSplit();
            EndStaticGesture();
            RimeEmoLocalization.Changed -= LanguageChanged;
            RimeEmoUpdates.Changed -= RefreshUpdates;
            retainedTimeline?.EndInteraction();
            Undo.undoRedoPerformed -= Changed;
            RimeEmoClassificationWindow.ClassificationChanged -= ClassificationEdited;
            RimeEmoShapeGroups.Changed -= GroupSetEdited;
            EditorApplication.update -= Tick;
            EditorApplication.projectChanged -= Changed;
            AnimationUtility.onCurveWasModified -= CurveModified;
            EditorApplication.hierarchyChanged -= InvalidateApplyState;
            EditorApplication.hierarchyChanged -= AvatarMayHaveChanged;
            preview?.Dispose(); preview = null;
            DestroyTemporary();
        }

        private void DestroyTemporary()
        {
            if (defaults) DestroyImmediate(defaults);
            defaults = null;
        }

        private void LanguageChanged()
        {
            saveChangesMessage = RimeEmoLocalization.T("編集中のアニメーションを保存しますか？");
            if (toolkitRoot == null) return;
            CreateGUI();
        }

        private void RefreshAvatar()
        {
            RestoreAvatar();
            if (project) foreach (var savedMotion in project.motions.Where(m => m)) RimeEmoMotionMaterials.Freeze(savedMotion);
            RimeEmoClips.RepairAutomaticClassification(project);
            RimeEmoFace.Resolve(project, avatar);
            preview?.SetAvatar(avatar);
            fx = project ? project.sourceFX : RimeEmoAssets.FindFX(avatar);
            Changed();
        }

        private bool SelectAvatar(GameObject next)
        {
            // Both "null" while settings are still loaded means the avatar went missing: clear them too.
            if (next == avatar && !(next == null && project)) return true;
            if (next && (EditorUtility.IsPersistent(next) || !next.scene.IsValid()))
                throw new InvalidOperationException(RimeEmoLocalization.T("Scene上のアバタールートを指定してください。"));
            var saved = RimeEmoAvatarStorage.Load(next);
            if (!FinishDraft()) return false;
            avatar = next;
            SetProject(saved);
            return true;
        }

        private bool SelectProject(RimeEmoProject next)
        {
            if (next == project) return true;
            if (!FinishDraft()) return false;
            var targetAvatar = avatar;
            // Settings belong to one scene avatar. Find it in the open scenes (older projects also by their saved id);
            // without it the materials would show under an empty Avatar field, so linking is refused.
            if (!targetAvatar && next) targetAvatar = FindAvatarOf(next);
            if (next && !targetAvatar)
                throw new InvalidOperationException(RimeEmoLocalization.T("このProjectのアバターが、開いているSceneに見つかりません。先に上のAvatar欄にアバターを指定してください。"));
            if (next && targetAvatar) RimeEmoAvatarStorage.Attach(targetAvatar, next);
            avatar = targetAvatar;
            SetProject(next);
            return true;
        }

        private static GameObject FindAvatarOf(RimeEmoProject settings)
        {
            var found = RimeEmoAvatarStorage.FindAvatar(settings);
            // TryParse throws on null; settings made without a scene avatar (and the checks' temporary ones) have no id.
            if (!found && !string.IsNullOrEmpty(settings.avatarId) && GlobalObjectId.TryParse(settings.avatarId, out var id)) found = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
            return found && found.scene.IsValid() && !EditorUtility.IsPersistent(found) ? found : null;
        }

        // The window keeps its settings across restarts and scene changes, but not its reference to the scene avatar.
        // Find the avatar again from the settings; with no avatar, show nothing rather than one avatar's materials
        // under an empty Avatar field.
        private void RestoreAvatar()
        {
            if (!project || avatar) return;
            var found = FindAvatarOf(project);
            if (found) { avatar = found; return; }
            if (hasUnsavedChanges) return; // An unsaved motion is kept; the notice asks for the avatar.
            SetProject(null);
        }

        // Scene opened or closed, or the avatar deleted.
        private void AvatarMayHaveChanged()
        {
            if (!project || avatar) return;
            RefreshAvatar(); toolkitDirty = true;
        }

        private void SetProject(RimeEmoProject next)
        {
            PreviewMaterial(null);
            motionMaterial = null;
            // Choices made for the previous avatar's settings mean nothing for the next one; a group index would
            // silently select another avatar's group.
            shapeGroup = -1;
            defaultFaceSelected = false;
            if (next != project)
            {
                // Searches and the Default Face view were for the previous avatar; kept, they made the next avatar's
                // lists look empty or its preview look wrong.
                materialSearch = ""; search = ""; showDefault = false;
                if (toolkitRoot != null) { UI<UnityEngine.UIElements.TextField>("material-search")?.SetValueWithoutNotify(""); UI<UnityEngine.UIElements.TextField>("shape-search")?.SetValueWithoutNotify(""); }
            }
            RimeEmoClassificationWindow.FollowProject(project, next);
            project = next;
            selected = project ? project.clips.FirstOrDefault(c => c.clip && RimeEmoClipClassification.Classify(project, c.clip) == RimeEmoClipKind.Static)?.clip : null;
            ReplaceDraft(project ? RimeEmoDrafts.Create(project) : null);
            RefreshAvatar();
        }

        private void Changed()
        {
            CancelAutoSplit();
            EndStaticGesture(false);
            if (importedDraft && EditorUtility.IsDirty(importedDraft)) hasUnsavedChanges = true;
            if (motion && !EditorUtility.IsPersistent(motion) && EditorUtility.IsDirty(motion)) hasUnsavedChanges = true;
            retainedTimeline?.EndInteraction();
            toolkitDirty = true;
            applyStateDirty = true;
            DestroyTemporary();
            rowsDirty = true;
            // Only meshes with Shape Keys are in project.shapes. Most Shape Keys first: the face mesh (hundreds) comes before
            // clothing and accessories with a handful each.
            var faceGroups = project ? project.shapes.Where(s => s != null).GroupBy(s => s.path).OrderByDescending(g => g.Count()).ToArray() : Array.Empty<IGrouping<string, ShapeEntry>>();
            facePaths = faceGroups.Select(g => g.Key).ToArray();
            faceShapeCounts = faceGroups.ToDictionary(g => g.Key, g => g.Count());
            if (project) defaults = RimeEmoClips.DefaultClip(project);
            preview?.SetDefaults(project);
            preview?.Invalidate();
            gesturePanel?.MarkDirty();
            Repaint();
        }

        private void ClassificationEdited(RimeEmoProject edited) { if (edited == project) Changed(); }

        private void CurveModified(AnimationClip clip, EditorCurveBinding binding, AnimationUtility.CurveModifiedType type)
        {
            applyStateDirty = true;
            if (autoSplitOpen && autoSplitSelection.Any(p => motion && p.x < motion.tracks.Count && p.y < motion.tracks[p.x].states.Count && motion.tracks[p.x].states[p.y].clip == clip)) CancelAutoSplit();
            if (importedDraft && clip == importedDraft) { hasUnsavedChanges = true; preview?.Invalidate(); return; }
            if (!project || !project.clips.Any(c => c.clip == clip)) return;
            if (clip == selected)
            {
                var row = shapeRows.FirstOrDefault(r => r.binding.Equals(binding));
                var curve = binding.type == null ? null : AnimationUtility.GetEditorCurve(clip, binding);
                // Fast path for slider edits; adding or removing a curve changes the state counts.
                if (row != null && row.included && curve != null)
                {
                    row.value = curve.Evaluate(0);
                    row.constant = RimeEmoClips.IsConstant(curve, row.value);
                }
                else rowsDirty = true;
            }
            preview?.InvalidateCurve(clip, binding);
            RequestToolkitPreview();
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (playing && mode == 1 && (ImportedEditing || (motion && motion.project == project)))
            {
                float duration = EditingDuration;
                time += (float)(now - lastTick);
                if (time > duration)
                {
                    if (EditingLoop) time %= duration;
                    else { time = duration; playing = false; }
                }
                SyncToolkitTime(); RequestToolkitPreview();
                retainedTimeline?.FollowTime(); // The view pages along with playback, as in a video editor.
            }
            lastTick = now;
            TickToolkit(now);
            TickApplyState(now);
        }

        // Gesture assignments and the scene face are edited in other windows; refresh the shelf labels and the
        // Default Face scene notice on return.
        private void OnFocus()
        {
            if (toolkitRoot == null || !project) return;
            RefreshMaterials();
            if (EditingDefaultFace) RefreshDefaultFace();
        }

        private void OnLostFocus() { EndStaticGesture(); PreviewMaterial(null); retainedTimeline?.EndInteraction(); }

        private sealed class ShapeRow
        {
            internal ShapeEntry shape;
            internal EditorCurveBinding binding;
            internal float value;
            internal bool constant;
            // The clip has a curve for this shape; otherwise the face uses Default Face.
            internal bool included;
            internal bool Changed => included && (!constant || Mathf.Abs(value - shape.defaultValue) >= 0.0001f);
        }
        private readonly List<ShapeRow> shapeRows = new List<ShapeRow>();
        private AnimationClip rowsClip;
        private bool rowsDirty = true;
        private string[] facePaths = Array.Empty<string>();
        private Dictionary<string, int> faceShapeCounts = new Dictionary<string, int>();

        private void ClipEdited()
        {
            preview?.Invalidate();
            Repaint();
        }

        private static float SafeTime(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp(value, 0, 600);

        private void EditMotion(Action edit)
        {
            string before = EditorJsonUtility.ToJson(motion);
            Undo.RecordObject(motion, "RimeEmo: Edit Motion");
            edit();
            if (EditorJsonUtility.ToJson(motion) == before) return;
            CancelAutoSplit();
            hasUnsavedChanges = true;
            preview?.InvalidateMotion();
            toolkitDirty = true;
            EditorUtility.SetDirty(motion); Repaint();
        }

        private void Run(Action action)
        {
            try { action(); }
            catch (Exception ex) { Debug.LogException(ex); EditorUtility.DisplayDialog("RimeEmo", ex.Message, "OK"); }
        }
    }
}
