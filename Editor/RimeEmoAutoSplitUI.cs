using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        private VisualElement autoSplitPanel;
        private Label autoSplitStatus;
        private Button autoSplitApply;
        private float autoSplitMinimum = RimeEmoAutoSplit.DefaultMinimumDuration, autoSplitTolerance = RimeEmoAutoSplit.DefaultTolerance;
        private bool autoSplitOpen;
        private RimeEmoMotion autoSplitMotion;
        private Vector2Int[] autoSplitSelection = Array.Empty<Vector2Int>();
        private List<RimeEmoAutoSplit.Interval> autoSplitIntervals;

        private Vector2Int[] AutoSplitSelection() => SelectedQueues()
            .Where(p => motion.tracks[p.x].states[p.y].animated && motion.tracks[p.x].states[p.y].clip)
            .OrderBy(p => p.x).ThenBy(p => p.y).ToArray();

        private void EnsureAutoSplitPanel()
        {
            if (autoSplitPanel != null && autoSplitPanel.parent != null) return;
            autoSplitPanel = new VisualElement { name = "auto-split-panel" };
            autoSplitPanel.AddToClassList("re-auto-split");
            var hint = new Label(RimeEmoLocalization.T("停止区間を確認してから適用します。元のカーブは変更しません。"));
            hint.AddToClassList("re-hint"); autoSplitPanel.Add(hint);
            var controls = new VisualElement(); controls.AddToClassList("re-toolbar"); controls.AddToClassList("re-wrap");
            autoSplitPanel.Add(controls);
            var minimum = new FloatField(RimeEmoLocalization.T("最小停止（秒）")) { name = "auto-split-minimum", value = autoSplitMinimum, isDelayed = true };
            var tolerance = new FloatField(RimeEmoLocalization.T("許容変化量")) { name = "auto-split-tolerance", value = autoSplitTolerance, isDelayed = true,
                tooltip = RimeEmoLocalization.T("0で完全静止。正の値で微小な変化も対象にします。") };
            controls.Add(minimum); controls.Add(tolerance);
            autoSplitApply = new Button(() => Run(ApplyAutoSplit)) { name = "auto-split-apply", text = RimeEmoLocalization.T("適用") };
            autoSplitApply.AddToClassList("re-button"); autoSplitApply.AddToClassList("re-button--primary"); controls.Add(autoSplitApply);
            var cancel = new Button(CancelAutoSplit) { name = "auto-split-cancel", text = RimeEmoLocalization.T("キャンセル") };
            cancel.AddToClassList("re-button"); controls.Add(cancel);
            autoSplitStatus = new Label { name = "auto-split-status" }; autoSplitStatus.AddToClassList("re-hint"); autoSplitPanel.Add(autoSplitStatus);
            minimum.RegisterValueChangedCallback(e =>
            {
                if (!float.IsNaN(e.newValue) && !float.IsInfinity(e.newValue)) autoSplitMinimum = Mathf.Clamp(e.newValue, .001f, 600);
                minimum.SetValueWithoutNotify(autoSplitMinimum); Run(RecomputeAutoSplit);
            });
            tolerance.RegisterValueChangedCallback(e =>
            {
                if (!float.IsNaN(e.newValue) && !float.IsInfinity(e.newValue)) autoSplitTolerance = Mathf.Max(0, e.newValue);
                tolerance.SetValueWithoutNotify(autoSplitTolerance); Run(RecomputeAutoSplit);
            });
            var host = UI<VisualElement>("motion-controls");
            host.Insert(host.IndexOf(UI<VisualElement>("block-editor")), autoSplitPanel);
            autoSplitPanel.style.display = DisplayStyle.None;
        }
        private void OpenAutoSplit()
        {
            if (mode != 1 || !motion || AutoSplitSelection().Length == 0) return;
            retainedTimeline.EndInteraction(); playing = false;
            EnsureAutoSplitPanel(); autoSplitOpen = true; autoSplitMotion = motion; autoSplitSelection = AutoSplitSelection();
            autoSplitPanel.style.display = DisplayStyle.Flex; RecomputeAutoSplit(); SyncToolkitTime();
        }
        private void CancelAutoSplit()
        {
            bool hadPreview = autoSplitOpen || autoSplitIntervals != null;
            autoSplitOpen = false; autoSplitMotion = null; autoSplitIntervals = null; autoSplitSelection = Array.Empty<Vector2Int>();
            if (autoSplitPanel != null) autoSplitPanel.style.display = DisplayStyle.None;
            if (hadPreview) retainedTimeline?.SetAutoSplitPreview(null);
        }
        private void RefreshAutoSplitContext()
        {
            if (autoSplitOpen && (mode != 1 || motion != autoSplitMotion || !AutoSplitSelection().SequenceEqual(autoSplitSelection))) CancelAutoSplit();
        }
        private void RecomputeAutoSplit()
        {
            RefreshAutoSplitContext(); if (!autoSplitOpen) return;
            autoSplitIntervals = RimeEmoAutoSplit.Analyze(motion, autoSplitSelection, autoSplitMinimum, autoSplitTolerance);
            int cuts = RimeEmoAutoSplit.CutCount(motion, autoSplitIntervals);
            int gaps = RimeEmoAutoSplit.GapCount(motion, autoSplitIntervals);
            autoSplitStatus.text = cuts == 0 && gaps == 0 ? RimeEmoLocalization.T("候補がありません。停止時間や許容変化量を調整してください。")
                : string.Format(RimeEmoLocalization.T("候補: {0} 区間 / 分割点: {1} / 空白化: {2}"), autoSplitIntervals.Count, cuts, gaps);
            autoSplitApply.SetEnabled(cuts > 0 || gaps > 0); retainedTimeline.SetAutoSplitPreview(autoSplitIntervals);
        }
        private void ApplyAutoSplit()
        {
            retainedTimeline.EndInteraction(); RecomputeAutoSplit();
            if (!autoSplitOpen || (RimeEmoAutoSplit.CutCount(motion, autoSplitIntervals) == 0 && RimeEmoAutoSplit.GapCount(motion, autoSplitIntervals) == 0)) return;
            var intervals = autoSplitIntervals;
            var original = autoSplitSelection.Select(p => new RimeEmoAutoSplit.Interval { track = p.x, state = p.y,
                start = RimeEmoTimeline.Times(motion.tracks[p.x])[p.y],
                end = RimeEmoTimeline.Times(motion.tracks[p.x])[p.y] + motion.tracks[p.x].states[p.y].hold }).ToArray();
            playing = false; Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("RimeEmo: Auto Split");
            try { EditMotion(() => RimeEmoAutoSplit.Apply(motion, intervals)); Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); }
            finally { Undo.IncrementCurrentGroup(); }
            CancelAutoSplit(); retainedTimeline.SelectedStates.Clear();
            foreach (var range in original)
            {
                var track = motion.tracks[range.track]; var starts = RimeEmoTimeline.Times(track);
                for (int i = 0; i < starts.Length; i++)
                    if (starts[i] >= range.start - .00001f && starts[i] + track.states[i].hold <= range.end + .00001f)
                        retainedTimeline.SelectedStates.Add(new Vector2Int(range.track, i));
            }
            activeTrack = original[0].track; activeState = -1;
            if (retainedTimeline.SelectedStates.Count > 0)
            {
                var first = retainedTimeline.SelectedStates.OrderBy(p => p.x).ThenBy(p => p.y).First();
                activeTrack = first.x; activeState = first.y;
            }
            transitionSelected = false;
            toolkitDirty = true; RefreshToolkit(); RequestToolkitPreview();
        }
    }
}
