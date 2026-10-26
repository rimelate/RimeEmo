using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        private bool frameSnap = true;
        [SerializeField] private bool cueSnap = true;

        private float SnapTime(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp(frameSnap ? Mathf.Round(value * 60) / 60 : value, 0, 600);

        private void JumpKey(int direction)
        {
            var times = motion.tracks.SelectMany(RimeEmoTimeline.Times).Distinct().OrderBy(t => t).ToArray();
            if (direction < 0) time = times.Where(t => t < time - 0.0001f).DefaultIfEmpty(0).Last();
            else time = times.Where(t => t > time + 0.0001f).DefaultIfEmpty(RimeEmoCompiler.Duration(motion)).First();
            playing = false; showDefault = false; Repaint();
        }

        private void ShowAddKeyMenu(int trackIndex, float at)
        {
            if (!motion || trackIndex < 0 || trackIndex >= motion.tracks.Count) return;
            var menu = new GenericMenu();
            AddPasteItem(menu, trackIndex);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Default Face"), false, () => AddTimelineKey(trackIndex, at, null));
            foreach (var entry in project.clips.Where(c => c.clip && !RimeEmoImportedClips.IsAnimated(c.clip, project)))
            {
                var clip = entry.clip;
                menu.AddItem(new GUIContent(RimeEmoLocalization.MenuText(clip.name)), false, () => AddTimelineKey(trackIndex, at, clip));
            }
            menu.ShowAsContext();
        }

        // Pasting goes to the playhead, as in a video editor; from an empty lane it targets that lane.
        private void AddPasteItem(GenericMenu menu, int trackIndex = -1)
        {
            var label = new GUIContent(RimeEmoLocalization.T("再生位置に貼り付け") + "  Ctrl+V");
            if (copiedQueues.Count == 0 || copiedProject != project) { menu.AddDisabledItem(label); return; }
            menu.AddItem(label, false, () => Run(() => { if (trackIndex >= 0) activeTrack = trackIndex; PasteQueues(); }));
        }

        private void ShowTimelineMenu(int trackIndex, int stateIndex, float at)
        {
            if (stateIndex < 0) { ShowAddKeyMenu(trackIndex, at); return; }
            SelectQueue(trackIndex, stateIndex);
            var menu = new GenericMenu();
            if (SelectedQueues().Any(CanSplit)) menu.AddItem(new GUIContent(RimeEmoLocalization.T("再生位置で分割")), false, () => Run(SplitSelectedQueues));
            else menu.AddDisabledItem(new GUIContent(RimeEmoLocalization.T("再生位置で分割")));
            menu.AddItem(new GUIContent(RimeEmoLocalization.T("複製")), false, () => Run(DuplicateSelectedQueues));
            menu.AddItem(new GUIContent(RimeEmoLocalization.T("コピー") + "  Ctrl+C"), false, () => Run(() => CopySelectedQueues()));
            menu.AddItem(new GUIContent(RimeEmoLocalization.T("切り取り") + "  Ctrl+X"), false, () => Run(() => { if (CopySelectedQueues()) DeleteSelectedQueues(); }));
            AddPasteItem(menu);
            menu.AddItem(new GUIContent(RimeEmoLocalization.T("キーを削除")), false, () => Run(DeleteSelectedQueues));
            menu.ShowAsContext();
        }

        private void AddTimelineKey(int trackIndex, float at, AnimationClip clip)
        {
            if (!motion || trackIndex < 0 || trackIndex >= motion.tracks.Count) return;
            // A dynamic material becomes a moving cue on this part only; it never replaces the open draft.
            if (clip && RimeEmoImportedClips.IsAnimated(clip, project)) { AddTimelineMotion(trackIndex, at, clip); return; }
            clip = RimeEmoMotionMaterials.Snapshot(project, clip);
            activeTrack = trackIndex;
            EditMotion(() => activeState = RimeEmoTimeline.Insert(motion.tracks[trackIndex], SnapTime(at), clip));
            transitionSelected = false; SelectQueue(activeTrack, activeState); RequestToolkitPreview();
        }

        private void AddTimelineMotion(int trackIndex, float at, AnimationClip clip)
        {
            var part = motion.tracks[trackIndex].part;
            if (!RimeEmoClips.HasPart(project, clip, part))
            {
                ShowNotification(new GUIContent(string.Format(RimeEmoLocalization.T("この動きには{0}の変化が含まれていません。"), PartLabels[(int)part])));
                return;
            }
            var source = RimeEmoMotionMaterials.SnapshotSource(project, clip);
            activeTrack = trackIndex;
            EditMotion(() => activeState = RimeEmoTimeline.InsertMotion(motion.tracks[trackIndex], SnapTime(at), source));
            transitionSelected = false; SelectQueue(activeTrack, activeState); RequestToolkitPreview();
        }
    }
}
