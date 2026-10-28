using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        private Vector2Int[] SelectedQueues()
        {
            if (!motion) return new Vector2Int[0];
            return retainedTimeline.SelectedStates.Where(p => p.x >= 0 && p.x < motion.tracks.Count && p.y >= 0 && p.y < motion.tracks[p.x].states.Count).ToArray();
        }
        private bool CanSplit(Vector2Int p)
        {
            var track = motion.tracks[p.x]; float start = RimeEmoTimeline.Times(track)[p.y];
            return time > start + .00001f && time < start + track.states[p.y].hold - .00001f;
        }
        private void RefreshQueueButtons()
        {
            if (toolkitRoot == null || retainedTimeline == null) return;
            RefreshAutoSplitContext();
            var selectedQueues = SelectedQueues();
            UI<Button>("split-queue")?.SetEnabled(selectedQueues.Any(CanSplit));
            UI<Button>("duplicate-queue")?.SetEnabled(selectedQueues.Length > 0);
            UI<Button>("delete-queue")?.SetEnabled(selectedQueues.Length > 0);
            UI<Label>("queue-selection").text = selectedQueues.Length > 0
                ? string.Format(RimeEmoLocalization.T("選択: {0} キュー"), selectedQueues.Length)
                : RimeEmoLocalization.T("キューを選択");
            bool hasTrack = motion && activeTrack >= 0 && activeTrack < motion.tracks.Count;
            UI<Button>("add").SetEnabled(hasTrack);
            SetButtonCaption(UI<Button>("add"), hasTrack
                ? string.Format(RimeEmoLocalization.T("{0}にキーを追加"), PartLabels[(int)motion.tracks[activeTrack].part])
                : RimeEmoLocalization.T("＋ キー"));
            if (UI<Button>("auto-split") != null) UI<Button>("auto-split").SetEnabled(selectedQueues.Any(p => motion.tracks[p.x].states[p.y].animated));
        }
        private void SplitSelectedQueues()
        {
            retainedTimeline.EndInteraction();
            var selection = SelectedQueues().Where(CanSplit).OrderByDescending(p => p.y).ToArray();
            if (selection.Length == 0) return;
            EditMotion(() =>
            {
                foreach (var p in selection) RimeEmoTimeline.Split(motion.tracks[p.x], p.y, time);
            });
            retainedTimeline.SelectedStates.Clear();
            foreach (var p in selection) retainedTimeline.SelectedStates.Add(new Vector2Int(p.x, p.y + 1));
            activeTrack = selection[0].x; activeState = selection[0].y + 1; transitionSelected = false;
            playing = false; showDefault = false; RequestToolkitPreview();
        }
        private void DuplicateSelectedQueues()
        {
            retainedTimeline.EndInteraction(); var selection = SelectedQueues().OrderBy(p => p.x).ThenBy(p => p.y).ToArray();
            if (selection.Length == 0) return;
            retainedTimeline.SelectedStates.Clear();
            EditMotion(() =>
            {
                foreach (var p in selection)
                {
                    int index = RimeEmoTimeline.Duplicate(motion.tracks[p.x], p.y);
                    retainedTimeline.SelectedStates.Add(new Vector2Int(p.x, index)); activeTrack = p.x; activeState = index;
                }
            });
            transitionSelected = false; playing = false; RequestToolkitPreview();
        }
        private void DeleteSelectedQueues()
        {
            retainedTimeline.EndInteraction(); var selection = SelectedQueues().OrderByDescending(p => p.y).ToArray();
            if (selection.Length == 0) return;
            EditMotion(() => { foreach (var p in selection) RimeEmoTimeline.Remove(motion.tracks[p.x], p.y); });
            retainedTimeline.SelectedStates.Clear(); activeState = -1; transitionSelected = false;
            playing = false; RequestToolkitPreview();
        }
        // Cues copied with Ctrl+C: each keeps its track and its offset from the earliest copied cue.
        private sealed class CopiedQueue { internal int track; internal FacePart part; internal float offset; internal MotionState state; }
        private static List<CopiedQueue> copiedQueues = new List<CopiedQueue>();
        // Cues carry copies of one avatar's materials; they are pasted only into the same avatar settings.
        private static RimeEmoProject copiedProject;

        private bool CopySelectedQueues()
        {
            var selection = SelectedQueues();
            if (selection.Length == 0) return false;
            float first = selection.Min(p => RimeEmoTimeline.Times(motion.tracks[p.x])[p.y]);
            copiedQueues = selection.Select(p => new CopiedQueue
            {
                track = p.x, part = motion.tracks[p.x].part,
                offset = RimeEmoTimeline.Times(motion.tracks[p.x])[p.y] - first,
                state = RimeEmoTimeline.CopyState(motion.tracks[p.x].states[p.y]),
            }).ToList();
            copiedProject = project;
            ShowNotification(new GUIContent(string.Format(RimeEmoLocalization.T("{0} キューをコピーしました"), copiedQueues.Count)));
            return true;
        }

        // Pastes at the playhead. Cues copied from one track go to the active track; cues from several
        // tracks go back to the tracks of the same parts, keeping their spacing.
        private void PasteQueues()
        {
            if (!motion || copiedQueues.Count == 0) return;
            if (copiedProject != project)
            {
                ShowNotification(new GUIContent(RimeEmoLocalization.T("別のアバターの設定でコピーしたキューは貼り付けできません。")));
                return;
            }
            retainedTimeline.EndInteraction();
            bool oneTrack = copiedQueues.Select(c => c.track).Distinct().Count() == 1;
            var placed = new List<KeyValuePair<int, MotionState>>();
            int skipped = 0;
            EditMotion(() =>
            {
                foreach (var item in copiedQueues.OrderBy(c => c.offset))
                {
                    int track = oneTrack && activeTrack >= 0 && activeTrack < motion.tracks.Count ? activeTrack : motion.tracks.FindIndex(t => t.part == item.part);
                    if (track < 0) { skipped++; continue; }
                    var state = RimeEmoTimeline.CopyState(item.state);
                    // A copied cue whose material was deleted since (e.g. by cleanup) would silently become Default Face.
                    if (!ReferenceEquals(state.clip, null) && !state.clip) { skipped++; continue; }
                    // A moving cue only plays its own part; pasted onto a part it does not move, it would do nothing.
                    if (state.animated && !RimeEmoClips.HasPart(project, state.clip, motion.tracks[track].part)) { skipped++; continue; }
                    RimeEmoTimeline.InsertState(motion.tracks[track], SnapTime(time + item.offset), state);
                    placed.Add(new KeyValuePair<int, MotionState>(track, state));
                }
            });
            retainedTimeline.SelectedStates.Clear();
            foreach (var pair in placed)
            {
                int index = motion.tracks[pair.Key].states.IndexOf(pair.Value);
                if (index < 0) continue;
                retainedTimeline.SelectedStates.Add(new Vector2Int(pair.Key, index)); activeTrack = pair.Key; activeState = index;
            }
            if (skipped > 0) ShowNotification(new GUIContent(string.Format(RimeEmoLocalization.T("{0} キューは貼り付けられませんでした（この部位に動きがないか、素材が削除されています）。"), skipped)));
            transitionSelected = false; playing = false; showDefault = false; RequestToolkitPreview();
        }

        // Alt+←/→: moves the selected cues by a frame (Shift: 10 frames). A cue that would land on another stays.
        private void NudgeSelectedQueues(float delta)
        {
            var selection = SelectedQueues();
            if (selection.Length == 0) return;
            retainedTimeline.EndInteraction();
            var moved = selection.Select(p => new KeyValuePair<int, MotionState>(p.x, motion.tracks[p.x].states[p.y])).ToList();
            EditMotion(() =>
            {
                foreach (var group in moved.GroupBy(p => p.Key))
                {
                    var track = motion.tracks[group.Key];
                    // The leading cue moves first, so cues moved together never land on each other.
                    var order = group.OrderBy(p => track.states.IndexOf(p.Value) * (delta > 0 ? -1 : 1));
                    foreach (var pair in order)
                    {
                        int index = track.states.IndexOf(pair.Value);
                        RimeEmoTimeline.Move(track, index, SnapTime(RimeEmoTimeline.Times(track)[index] + delta));
                    }
                }
            });
            retainedTimeline.SelectedStates.Clear();
            foreach (var pair in moved)
            {
                int index = motion.tracks[pair.Key].states.IndexOf(pair.Value);
                if (index >= 0) retainedTimeline.SelectedStates.Add(new Vector2Int(pair.Key, index));
                if (pair.Key == activeTrack && index >= 0) activeState = index;
            }
            playing = false; RequestToolkitPreview();
        }

        private void SelectQueue(int track, int index)
        {
            var p = new Vector2Int(track, index);
            if (!retainedTimeline.SelectedStates.Contains(p)) { retainedTimeline.SelectedStates.Clear(); retainedTimeline.SelectedStates.Add(p); }
            activeTrack = track; activeState = index; transitionSelected = false;
            retainedTimeline.ActiveTrack = track; retainedTimeline.ActiveState = index; retainedTimeline.TransitionSelected = false;
            retainedTimeline.UpdatePositions(); RefreshQueueButtons();
        }
        private void DrawQueueSourceInspector(VisualElement host, PartTrack track, MotionState state)
        {
            if (!state.animated) return;
            host.Add(new Label(RimeEmoLocalization.T("元の区間: ") + state.sourceStart.ToString("0.###") + " – " + (state.sourceStart + state.hold * state.speed).ToString("0.###") + " s"));
            var speed = new FloatField(RimeEmoLocalization.T("速度")) { value = state.speed, isDelayed = true, formatString = "0.###", name = "queue-speed" };
            speed.RegisterValueChangedCallback(e =>
            {
                if (float.IsNaN(e.newValue) || float.IsInfinity(e.newValue) || e.newValue <= 0) { speed.SetValueWithoutNotify(state.speed); return; }
                float amount = state.hold * state.speed;
                float room = activeState + 1 < track.states.Count ? state.hold + state.transition
                    : 600 - RimeEmoTimeline.Times(track)[activeState] - state.endHold;
                float nextSpeed = Mathf.Max(.05f, e.newValue, amount / Mathf.Max(.00001f, room));
                EditMotion(() => { float previous = state.hold; state.speed = nextSpeed; state.hold = amount / nextSpeed;
                    if (activeState + 1 < track.states.Count) state.transition += previous - state.hold; });
            });
            host.Add(speed);
            host.Add(new Label(RimeEmoLocalization.T("端のドラッグで元の区間をトリミングします。")));
        }
    }
}
