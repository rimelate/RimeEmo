using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal sealed class RimeEmoTimelineElement : VisualElement
    {
        public RimeEmoMotion Motion { get; set; }
        public int ActiveTrack { get; set; }
        public int ActiveState { get; set; } = -1;
        public bool TransitionSelected { get; set; }
        public bool FrameSnap { get; set; } = true;
        // Magnet: cues snap to other cues' edges and the playhead (N toggles it, as in video editors).
        public bool CueSnap { get; set; } = true;
        public float Time { get; private set; }
        public bool IsInteracting => gesture != 0;
        public HashSet<Vector2Int> SelectedStates { get; } = new HashSet<Vector2Int>();
        public event Action SelectionChanged, Edited, InteractionEnded;
        public event Action SplitRequested, DuplicateRequested, DeleteRequested;
        public event Action PlayToggleRequested, CopyRequested, CutRequested, PasteRequested, SnapToggled;
        public event Action<int> JumpRequested;
        public event Action<float> NudgeRequested;
        public event Action<int, float> AddRequested;
        public event Action<int, int, float> ContextRequested;
        public event Action<int, float, AnimationClip> ClipDropped;
        private IEnumerable<RimeEmoAutoSplit.Interval> autoSplitPreview;
        VisualElement autoOverlay;
        public void SetAutoSplitPreview(IEnumerable<RimeEmoAutoSplit.Interval> intervals) { autoSplitPreview = intervals; UpdatePositions(); }

        const float Gutter = 78, RulerHeight = 25, RowHeight = 42;
        const float BandTop = 4, BandHeight = 16, TransitionTop = 22, KeepHandleTop = 17, KeepHandleHeight = 8;
        static string[] Parts => new[] { RimeEmoLocalization.T("左目"), RimeEmoLocalization.T("右目"), RimeEmoLocalization.T("眉"), RimeEmoLocalization.T("口"), RimeEmoLocalization.T("その他") };
        sealed class Row
        {
            internal Label label;
            internal readonly List<Label> spans = new List<Label>();
            internal readonly List<VisualElement> keys = new List<VisualElement>();
            internal readonly List<VisualElement> holdKeys = new List<VisualElement>();
            internal readonly List<Label> transitions = new List<Label>();
            internal readonly List<Label> keeps = new List<Label>();
            internal readonly List<VisualElement> keepKeys = new List<VisualElement>();
        }
        readonly List<Row> rows = new List<Row>();
        readonly List<Label> ticks = new List<Label>();
        VisualElement head, snapGuide, marquee;
        Slider scroll;
        Vector2 marqueeStart;
        HashSet<Vector2Int> marqueeBase;
        float pixelsPerSecond = 240, offset;
        int gesture, pointer = -1, undoGroup = -1, draggedTrack;
        float grabOffset, panOffset;
        Vector2 pointerStart;
        bool moved, changed, fitPending = true;
        RimeEmoTimeline.BlockDrag drag;
        RimeEmoMotion gestureMotion;

        public RimeEmoTimelineElement()
        {
            AddToClassList("re-timeline"); focusable = true;
            RegisterCallback<KeyDownEvent>(KeyDown);
            RegisterCallback<GeometryChangedEvent>(_ => { if (fitPending) Fit(); else UpdatePositions(); });
            RegisterCallback<PointerDownEvent>(PointerDown);
            RegisterCallback<PointerMoveEvent>(PointerMove);
            RegisterCallback<PointerUpEvent>(PointerUp);
            RegisterCallback<PointerCancelEvent>(_ => EndInteraction());
            RegisterCallback<PointerCaptureOutEvent>(_ => EndInteraction());
            RegisterCallback<DetachFromPanelEvent>(_ => EndInteraction());
            RegisterCallback<WheelEvent>(Wheel);
            RegisterCallback<DragUpdatedEvent>(e =>
            {
                if (TrackAt(this.WorldToLocal(e.mousePosition)) < 0 || !DragAndDrop.objectReferences.OfType<AnimationClip>().Any()) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.StopPropagation();
            });
            RegisterCallback<DragPerformEvent>(e =>
            {
                Vector2 p = this.WorldToLocal(e.mousePosition); int t = TrackAt(p);
                var clip = DragAndDrop.objectReferences.OfType<AnimationClip>().FirstOrDefault();
                if (t < 0 || !clip || p.x < Gutter) return;
                int key = HitKey(t, p.x);
                float at = key >= 0 ? RimeEmoTimeline.Times(Motion.tracks[t])[key] : Snap(MouseTime(p.x));
                DragAndDrop.AcceptDrag(); ClipDropped?.Invoke(t, at, clip); e.StopPropagation();
            });
        }

        public void Refresh()
        {
            EndInteraction(); Clear(); rows.Clear(); ticks.Clear();
            var ruler = Absolute("ruler", Gutter, 0); ruler.style.right = 0; ruler.style.height = RulerHeight; ruler.style.overflow = Overflow.Hidden; Add(ruler);
            for (int i = 0; i < 64; i++)
            {
                var tick = new Label(); tick.AddToClassList("timeline-tick"); tick.style.position = Position.Absolute;
                tick.style.top = 2; tick.style.width = 70; tick.style.height = 20; tick.pickingMode = PickingMode.Ignore;
                ruler.Add(tick); ticks.Add(tick);
            }
            if (Motion)
                for (int t = 0; t < Motion.tracks.Count; t++)
                {
                    var row = new Row(); rows.Add(row);
                    var container = Absolute("timeline-row", 0, RulerHeight + t * RowHeight); container.style.right = 0; container.style.height = RowHeight; Add(container);
                    row.label = new Label(Parts[(int)Motion.tracks[t].part]); row.label.AddToClassList("track-label");
                    row.label.style.position = Position.Absolute; row.label.style.width = Gutter; row.label.style.height = RowHeight; container.Add(row.label);
                    var lane = Absolute("track-lane", Gutter, 0); lane.style.right = 0; lane.style.height = RowHeight; lane.style.overflow = Overflow.Hidden; container.Add(lane);
                    foreach (var state in Motion.tracks[t].states)
                    {
                        var span = new Label(); span.AddToClassList("track-span"); span.style.position = Position.Absolute; span.style.left = 0; span.style.top = BandTop; span.style.height = RowHeight - BandTop * 2; lane.Add(span); row.spans.Add(span);
                        var transition = TimingBand("timeline-transition", TransitionTop); lane.Add(transition); row.transitions.Add(transition);
                        var keep = TimingBand("timeline-keep", BandTop); lane.Add(keep); row.keeps.Add(keep);
                    }
                    foreach (var state in Motion.tracks[t].states)
                    {
                        var key = Absolute("timeline-key", 0, BandTop); key.style.width = 4; key.style.height = RowHeight - BandTop * 2;
                         lane.Add(key); row.keys.Add(key);
                        var holdKey = Absolute("timeline-hold-key", 0, BandTop); holdKey.style.width = 4; holdKey.style.height = RowHeight - BandTop * 2;
                         lane.Add(holdKey); row.holdKeys.Add(holdKey);
                        var keepKey = Absolute("timeline-keep-key", 0, KeepHandleTop); keepKey.style.width = 6; keepKey.style.height = KeepHandleHeight;
                        lane.Add(keepKey); row.keepKeys.Add(keepKey);
                    }
                }
            var overlay = Absolute("timeline-overlay", Gutter, 0); overlay.style.right = 0; overlay.style.bottom = 18; overlay.style.overflow = Overflow.Hidden; overlay.pickingMode = PickingMode.Ignore; Add(overlay);
            autoOverlay = Absolute("auto-split-overlay", Gutter, RulerHeight); autoOverlay.style.right = 0; autoOverlay.style.bottom = 18; autoOverlay.style.overflow = Overflow.Hidden; autoOverlay.pickingMode = PickingMode.Ignore; Insert(IndexOf(overlay), autoOverlay);
            head = Absolute("playhead", 0, 0); head.style.width = 2; head.style.bottom = 0; head.pickingMode = PickingMode.Ignore; overlay.Add(head);
            snapGuide = Absolute("timeline-snap-guide", 0, 0); snapGuide.style.width = 1; snapGuide.style.bottom = 0; snapGuide.pickingMode = PickingMode.Ignore;
            snapGuide.style.display = DisplayStyle.None; overlay.Add(snapGuide);
            marquee = Absolute("timeline-marquee", 0, 0); marquee.pickingMode = PickingMode.Ignore; marquee.style.display = DisplayStyle.None; overlay.Add(marquee);
            scroll = new Slider(0, 1); scroll.name = "timeline-scroll"; scroll.style.position = Position.Absolute; scroll.style.left = Gutter; scroll.style.right = 0; scroll.style.bottom = 0; scroll.style.height = 16;
            scroll.RegisterValueChangedCallback(e => { offset = e.newValue; UpdatePositions(); });
            scroll.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            Add(scroll); UpdatePositions();
        }
        // Video-editor keys. The playhead keys stop playback, as the window does for any seek.
        void KeyDown(KeyDownEvent e)
        {
            if (e.target is TextField) return;
            bool command = e.ctrlKey || e.commandKey;
            float frame = 1f / 60 * (e.shiftKey ? 10 : 1);
            switch (e.keyCode)
            {
                case KeyCode.S when !command && !e.altKey: SplitRequested?.Invoke(); break;
                case KeyCode.D when command: DuplicateRequested?.Invoke(); break;
                case KeyCode.Delete: case KeyCode.Backspace: DeleteRequested?.Invoke(); break;
                case KeyCode.Space: PlayToggleRequested?.Invoke(); break;
                case KeyCode.C when command: CopyRequested?.Invoke(); break;
                case KeyCode.X when command: CutRequested?.Invoke(); break;
                case KeyCode.V when command: PasteRequested?.Invoke(); break;
                case KeyCode.A when command: SelectAll(); break;
                case KeyCode.Escape: ClearSelection(); break;
                // ←/→ move the playhead by a frame (Shift: 10); with Alt they move the selected cues instead.
                case KeyCode.LeftArrow: if (e.altKey) NudgeRequested?.Invoke(-frame); else Seek(Time - frame); break;
                case KeyCode.RightArrow: if (e.altKey) NudgeRequested?.Invoke(frame); else Seek(Time + frame); break;
                case KeyCode.UpArrow: JumpRequested?.Invoke(-1); break;
                case KeyCode.DownArrow: JumpRequested?.Invoke(1); break;
                case KeyCode.Home: Seek(0); break;
                case KeyCode.End: Seek(Motion ? RimeEmoCompiler.Duration(Motion) : 0); break;
                case KeyCode.N when !command: CueSnap = !CueSnap; SnapToggled?.Invoke(); break;
                case KeyCode.Backslash: Fit(); break;
                case KeyCode.Equals: case KeyCode.Plus: case KeyCode.KeypadPlus: ZoomAtPlayhead(1.25f); break;
                case KeyCode.Minus: case KeyCode.KeypadMinus: ZoomAtPlayhead(0.8f); break;
                default: return;
            }
            e.StopPropagation();
        }

        void Seek(float seconds)
        {
            Time = Clean(seconds); FollowTime(); UpdateHead();
            SelectionChanged?.Invoke();
        }

        void ZoomAtPlayhead(float factor)
        {
            float anchorX = X(Time);
            pixelsPerSecond = Mathf.Clamp(pixelsPerSecond * factor, 1, 2400);
            offset = Mathf.Max(0, Time - (anchorX - 10) / pixelsPerSecond); fitPending = false; UpdatePositions();
        }

        // Keeps the playhead in view: used during playback and keyboard seeking, not for ordinary refreshes,
        // so a view the user has scrolled away stays where it is.
        public void FollowTime()
        {
            float width = contentRect.width - Gutter - 20;
            if (!(width > 40) || head == null) return;
            float x = X(Time);
            if (x >= 0 && x <= width) return;
            offset = Mathf.Max(0, Time - (x > width ? width * 0.1f : width * 0.9f) / pixelsPerSecond);
            fitPending = false; UpdatePositions();
        }

        public void SelectAll()
        {
            if (!Motion) return;
            SelectedStates.Clear();
            for (int t = 0; t < Motion.tracks.Count; t++) for (int i = 0; i < Motion.tracks[t].states.Count; i++) SelectedStates.Add(new Vector2Int(t, i));
            TransitionSelected = false; SelectionChanged?.Invoke(); UpdatePositions();
        }

        public void ClearSelection()
        {
            SelectedStates.Clear(); ActiveState = -1; TransitionSelected = false;
            SelectionChanged?.Invoke(); UpdatePositions();
        }

        static VisualElement Absolute(string cls, float left, float top)
        {
            var e = new VisualElement(); e.AddToClassList(cls); e.style.position = Position.Absolute; e.style.left = left; e.style.top = top; return e;
        }
        static Label TimingBand(string cls, float top)
        {
            var label = new Label(); label.AddToClassList("timeline-timing-span"); label.AddToClassList(cls);
            label.style.position = Position.Absolute; label.style.left = 0; label.style.top = top; label.style.height = BandHeight;
            return label;
        }
        static void PositionBand(Label label, float start, float width, float seconds, string title, bool selected)
        {
            label.transform.position = new Vector3(start, 0, 0); label.style.width = Mathf.Max(0, width);
            label.style.display = seconds > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            string text = RimeEmoLocalization.T(title) + " " + seconds.ToString("0.##") + "s";
            label.text = width >= 40 ? text : "";
            label.tooltip = text + "\n" + RimeEmoLocalization.T("クリック: 保持・移行を編集");
            label.EnableInClassList("is-selected", selected);
        }
        float X(float seconds) => 10 + (seconds - offset) * pixelsPerSecond;
        float MouseTime(float x) => offset + (x - Gutter - 10) / pixelsPerSecond;
        static float Clean(float t) => float.IsNaN(t) || float.IsInfinity(t) ? 0 : Mathf.Clamp(t, 0, 600);
        float Snap(float t) => Clean(FrameSnap ? Mathf.Round(t * 60) / 60 : t);
        // Snaps to the nearest cue edge or the playhead and shows a guide line there, as video editors do.
        float SnapCue(float t, int skipTrack, int skipIndex)
        {
            float snapped = FindCueSnap(t, skipTrack, skipIndex, out bool guide);
            if (snapGuide != null)
            {
                snapGuide.style.display = guide ? DisplayStyle.Flex : DisplayStyle.None;
                if (guide) snapGuide.transform.position = new Vector3(X(snapped), 0, 0);
            }
            return snapped;
        }
        float FindCueSnap(float t, int skipTrack, int skipIndex, out bool guide)
        {
            guide = false;
            if (!CueSnap) return Snap(t);
            float best = float.PositiveInfinity, bestDistance = 7f / Mathf.Max(1, pixelsPerSecond);
            if (Mathf.Abs(Time - t) < bestDistance) { bestDistance = Mathf.Abs(Time - t); best = Time; }
            if (Motion) for (int tr = 0; tr < Motion.tracks.Count; tr++)
            {
                var ts = RimeEmoTimeline.Times(Motion.tracks[tr]);
                for (int i = 0; i < ts.Length; i++) if (tr != skipTrack || i != skipIndex)
                {
                    var state = Motion.tracks[tr].states[i]; float contentEnd = ts[i] + RimeEmoTimeline.ContentDuration(state);
                    float[] candidates = { ts[i], contentEnd, contentEnd + RimeEmoTimeline.HoldDuration(state) };
                    foreach (var c in candidates) { float d = Mathf.Abs(c - t); if (d < bestDistance) { bestDistance = d; best = c; } }
                }
            }
            guide = !float.IsPositiveInfinity(best);
            return guide ? best : Snap(t);
        }
        int TrackAt(Vector2 p)
        {
            int t = Mathf.FloorToInt((p.y - RulerHeight) / RowHeight);
            return Motion && t >= 0 && t < Motion.tracks.Count && p.y < contentRect.height - 18 ? t : -1;
        }
        int HitKey(int track, float x)
        {
            if (track < 0) return -1;
            var times = RimeEmoTimeline.Times(Motion.tracks[track]);
            for (int i = times.Length - 1; i >= 0; i--) if (x - Gutter >= X(times[i]) - 2 && x - Gutter <= Mathf.Max(X(times[i]) + 6, X(times[i] + RimeEmoTimeline.ContentDuration(Motion.tracks[track].states[i]))) + 2) return i;
            return -1;
        }
        int HitKeep(int track, Vector2 p)
        {
            float y = p.y - RulerHeight - track * RowHeight;
            if (track < 0 || y < KeepHandleTop - 1 || y > KeepHandleTop + KeepHandleHeight + 1) return -1;
            var times = RimeEmoTimeline.Times(Motion.tracks[track]);
            for (int i = times.Length - 1; i >= 0; i--)
            {
                var state = Motion.tracks[track].states[i];
                float end = times[i] + RimeEmoTimeline.ContentDuration(state) + RimeEmoTimeline.HoldDuration(state);
                if (Mathf.Abs(p.x - Gutter - X(end)) <= 7) return i;
            }
            return -1;
        }
        int HitTiming(int track, Vector2 p)
        {
            if (track < 0) return -1;
            float y = p.y - RulerHeight - track * RowHeight, x = p.x - Gutter;
            bool upper = y >= BandTop && y <= BandTop + BandHeight;
            bool lower = y >= TransitionTop && y <= TransitionTop + BandHeight;
            var times = RimeEmoTimeline.Times(Motion.tracks[track]);
            for (int i = times.Length - 1; i >= 0; i--)
            {
                var state = Motion.tracks[track].states[i];
                float contentEnd = times[i] + RimeEmoTimeline.ContentDuration(state), hold = RimeEmoTimeline.HoldDuration(state);
                if (upper && hold > 0 && x >= X(contentEnd) && x < X(contentEnd + hold)) return i;
                if (lower && i + 1 < times.Length && state.transition > 0 && x >= X(contentEnd + hold) && x < X(times[i + 1])) return i;
            }
            return -1;
        }

        public void UpdatePositions()
        {
            if (head == null) return;
            if (Motion)
                for (int t = 0; t < rows.Count && t < Motion.tracks.Count; t++)
                {
                    var row = rows[t]; var track = Motion.tracks[t]; var times = RimeEmoTimeline.Times(track);
                    row.label.EnableInClassList("is-selected", t == ActiveTrack);
                    for (int i = 0; i < times.Length && i < row.keys.Count; i++)
                    {
                        float x = X(times[i]); float next = X(i + 1 < times.Length ? times[i + 1] : RimeEmoCompiler.Duration(track));
                        // Translation avoids relaying out the rest of the editor during a drag.
                        row.keys[i].transform.position = new Vector3(x, 0, 0);
                        row.keys[i].EnableInClassList("is-selected", SelectedStates.Contains(new Vector2Int(t, i)) && !TransitionSelected);
                        row.keys[i].tooltip = RimeEmoLocalization.T("左端: 開始時刻を変更");
                        var state = track.states[i];
                        var span = row.spans[i]; span.transform.position = new Vector3(x, 0, 0); span.style.width = Mathf.Max(6, RimeEmoTimeline.ContentDuration(state) * pixelsPerSecond);
                        span.tooltip = (state.clip ? state.clip.name + "\n" : "") + RimeEmoLocalization.T(state.animated ? "左端: 開始 / 中央: 長さを保って移動 / 右端: 終了" : "ドラッグ: 表情キーを移動");
                        span.text = (track.states[i].animated ? RimeEmoLocalization.T("動き") + " · " : "") + (track.states[i].clip ? track.states[i].clip.name : "Default Face");
                        span.EnableInClassList("is-selected", SelectedStates.Contains(new Vector2Int(t, i)) && !TransitionSelected);
                        span.EnableInClassList("is-pose", !state.animated);
                        float contentEnd = X(times[i] + RimeEmoTimeline.ContentDuration(state));
                        float hold = contentEnd + RimeEmoTimeline.HoldDuration(state) * pixelsPerSecond;
                        row.holdKeys[i].style.display = state.animated ? DisplayStyle.Flex : DisplayStyle.None;
                        row.holdKeys[i].transform.position = new Vector3(Mathf.Max(x + 2, contentEnd - 4), 0, 0);
                        row.holdKeys[i].tooltip = RimeEmoLocalization.T("右端: 終了時刻を変更");
                        bool timingSelected = TransitionSelected && t == ActiveTrack && i == ActiveState;
                        PositionBand(row.keeps[i], contentEnd, hold - contentEnd, RimeEmoTimeline.HoldDuration(state), "保持", timingSelected);
                        row.keepKeys[i].transform.position = new Vector3(hold - 3, 0, 0);
                        row.keepKeys[i].tooltip = RimeEmoLocalization.T("ドラッグ: 保持と移行の配分を変更");
                        row.keepKeys[i].EnableInClassList("is-selected", t == ActiveTrack && i == ActiveState);
                        PositionBand(row.transitions[i], hold, next - hold, i + 1 < times.Length ? Mathf.Max(0, state.transition) : 0, "移行", timingSelected);
                    }
                }
            RenderAutoSplitPreview();
            UpdateRuler(); UpdateHead();
            if (scroll != null)
            {
                float visible = Mathf.Max(1, contentRect.width - Gutter) / pixelsPerSecond;
                scroll.highValue = Mathf.Max(offset, Motion ? RimeEmoCompiler.Duration(Motion) - visible * 0.5f : 0, 0.01f);
                scroll.SetValueWithoutNotify(offset);
            }
        }
        void RenderAutoSplitPreview()
        {
            if (autoOverlay == null) return;
            autoOverlay.Clear();
            if (Motion == null || autoSplitPreview == null) return;
            foreach (var interval in autoSplitPreview)
            {
                if (interval.track < 0 || interval.track >= Motion.tracks.Count) continue;
                var shade = Absolute("auto-split-interval", X(interval.start), interval.track * RowHeight + 3);
                shade.style.width = Mathf.Max(1, (interval.end - interval.start) * pixelsPerSecond); shade.style.height = RowHeight - 7; shade.pickingMode = PickingMode.Ignore; autoOverlay.Add(shade);
                foreach (var at in new[] { interval.start, interval.end })
                {
                    var edge = Absolute("auto-split-boundary", X(at), interval.track * RowHeight);
                    edge.style.width = 2; edge.style.height = RowHeight; edge.pickingMode = PickingMode.Ignore; autoOverlay.Add(edge);
                }
            }
        }
        void UpdateRuler()
        {
            float raw = 70 / pixelsPerSecond; float power = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(raw))); float scaled = raw / power;
            float step = (scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10) * power;
            float first = Mathf.Floor(offset / step) * step;
            for (int i = 0; i < ticks.Count; i++)
            {
                float at = first + i * step, x = X(at);
                ticks[i].style.display = x <= contentRect.width - Gutter ? DisplayStyle.Flex : DisplayStyle.None;
                ticks[i].transform.position = new Vector3(x, 0, 0); ticks[i].text = at.ToString("0.###") + "s";
            }
        }
        void UpdateHead() { if (head != null) head.transform.position = new Vector3(X(Time), 0, 0); }
        public void SetTime(float value) { Time = Clean(value); UpdateHead(); }
        public void Fit()
        {
            // Written as "not greater" so NaN counts as too narrow: before the first layout (e.g. rebuilt on a hidden page)
            // the width is NaN, which used to make pixelsPerSecond NaN and hide the ruler and cues until "Fit All".
            if (!Motion || !(contentRect.width > Gutter + 30)) { fitPending = true; return; }
            fitPending = false; pixelsPerSecond = Mathf.Clamp((contentRect.width - Gutter - 24) / Mathf.Max(0.5f, RimeEmoCompiler.Duration(Motion)), 1, 2400); offset = 0; UpdatePositions();
        }
        void Begin(int mode, PointerDownEvent e, Vector2 p)
        {
            gesture = mode; gestureMotion = Motion; pointer = e.pointerId; pointerStart = p; moved = changed = false;
            this.CapturePointer(pointer);
        }
        void PointerDown(PointerDownEvent e)
        {
            if (!Motion || IsInteracting) return;
            Vector2 p = this.WorldToLocal(e.position); if (p.y >= contentRect.height - 18) return;
            Focus(); int t = TrackAt(p), key = HitKey(t, p.x), timing = HitTiming(t, p);
            if (e.button == 1) { if (t >= 0) ContextRequested?.Invoke(t, key >= 0 ? key : timing, Snap(MouseTime(p.x))); e.StopPropagation(); return; }
            if (e.button == 2 || (e.button == 0 && e.altKey)) { panOffset = offset; Begin(3, e, p); e.StopPropagation(); return; }
            if (e.button != 0) return;
            if (p.x < Gutter) { if (t >= 0) { ActiveTrack = t; ActiveState = -1; TransitionSelected = false; SelectionChanged?.Invoke(); UpdatePositions(); } return; }
            int keepKey = HitKeep(t, p);
            if (e.clickCount == 2 && t >= 0 && key < 0 && keepKey < 0 && timing < 0) { AddRequested?.Invoke(t, Snap(MouseTime(p.x))); e.StopPropagation(); return; }
            if (keepKey >= 0)
            {
                ActiveTrack = draggedTrack = t; ActiveState = keepKey; TransitionSelected = true;
                var state = Motion.tracks[t].states[keepKey];
                grabOffset = MouseTime(p.x) - (RimeEmoTimeline.Times(Motion.tracks[t])[keepKey] + RimeEmoTimeline.ContentDuration(state) + RimeEmoTimeline.HoldDuration(state));
                Begin(4, e, p);
            }
            else if (key >= 0 && e.shiftKey && t == ActiveTrack && ActiveState >= 0 && ActiveState < Motion.tracks[t].states.Count && !TransitionSelected)
            {
                // Shift+click: every cue between the last clicked one and this one, as in a video editor.
                if (!(e.ctrlKey || e.commandKey)) SelectedStates.Clear();
                for (int i = Mathf.Min(ActiveState, key); i <= Mathf.Max(ActiveState, key); i++) SelectedStates.Add(new Vector2Int(t, i));
                ActiveState = key;
            }
            else if (key >= 0)
            {
                bool toggle = e.ctrlKey || e.commandKey;
                if (!toggle && !SelectedStates.Contains(new Vector2Int(t, key))) SelectedStates.Clear();
                var selected = new Vector2Int(t, key); if (toggle) { if (!SelectedStates.Add(selected)) SelectedStates.Remove(selected); }
                else SelectedStates.Add(selected);
                ActiveTrack = draggedTrack = t; ActiveState = key; TransitionSelected = false;
                float start = RimeEmoTimeline.Times(Motion.tracks[t])[key];
                float end = start + Mathf.Max(0, Motion.tracks[t].states[key].hold);
                float localX = p.x - Gutter, width = Mathf.Max(6, X(end) - X(start));
                float handle = Mathf.Min(7, width / 3);
                var edge = localX <= X(start) + handle ? RimeEmoTimeline.BlockEdge.Start :
                    localX >= X(start) + width - handle ? RimeEmoTimeline.BlockEdge.End : RimeEmoTimeline.BlockEdge.Move;
                if (!Motion.tracks[t].states[key].animated) edge = RimeEmoTimeline.BlockEdge.Move;
                grabOffset = MouseTime(p.x) - (edge == RimeEmoTimeline.BlockEdge.End ? end : start);
                if (!toggle || SelectedStates.Contains(selected)) { drag = new RimeEmoTimeline.BlockDrag(Motion.tracks[t], key, edge); Begin(2, e, p); }
            }
            else if (timing >= 0)
            {
                ActiveTrack = t; ActiveState = timing; TransitionSelected = true;
                SelectedStates.Clear(); SelectedStates.Add(new Vector2Int(t, timing));
                Begin(5, e, p);
            }
            else if (p.y < RulerHeight)
            {
                // The ruler moves the playhead; dragging it scrubs.
                Time = Clean(MouseTime(p.x));
                Begin(1, e, p);
            }
            else
            {
                // Empty lane: a click clears the selection, a drag draws a selection box (Ctrl/Shift add to it).
                bool additive = e.ctrlKey || e.commandKey || e.shiftKey;
                if (!additive) SelectedStates.Clear();
                marqueeBase = new HashSet<Vector2Int>(SelectedStates); marqueeStart = p;
                if (t >= 0) ActiveTrack = t;
                ActiveState = -1; TransitionSelected = false;
                Begin(6, e, p);
            }
            SelectionChanged?.Invoke(); UpdatePositions(); e.StopPropagation();
        }
        void PointerMove(PointerMoveEvent e)
        {
            if (!IsInteracting || e.pointerId != pointer) return;
            Vector2 p = this.WorldToLocal(e.position);
            if ((p - pointerStart).sqrMagnitude > 1) moved = true;
            if (moved) MovePointer(p);
            e.StopPropagation();
        }
        void MovePointer(Vector2 p)
        {
            if (gesture == 5) return; // Selecting a timing band does not move it or seek playback.
            if (gesture == 6) { UpdateMarquee(p); return; }
            if (gesture == 3) { offset = Mathf.Max(0, panOffset - (p.x - pointerStart.x) / pixelsPerSecond); UpdatePositions(); return; }
            if (gesture == 2 && drag != null)
            {
                float rawTarget = MouseTime(p.x) - grabOffset;
                float cueTarget = SnapCue(rawTarget, draggedTrack, ActiveState);
                float target = cueTarget;
                if (!drag.Apply(target, () =>
                {
                    if (!changed) { Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.RegisterCompleteObjectUndo(gestureMotion, "RimeEmo: Edit Expression Block"); }
                })) return;
                changed = true;
                Edited?.Invoke(); UpdatePositions();
            }
            else if (gesture == 4)
            {
                var track = gestureMotion.tracks[draggedTrack]; var state = track.states[ActiveState];
                var times = RimeEmoTimeline.Times(track); float contentEnd = times[ActiveState] + RimeEmoTimeline.ContentDuration(state);
                float target = Mathf.Clamp(SnapCue(MouseTime(p.x) - grabOffset, draggedTrack, ActiveState), contentEnd, ActiveState + 1 < times.Length ? times[ActiveState + 1] : 600);
                if (Mathf.Abs(target - contentEnd - RimeEmoTimeline.HoldDuration(state)) < .000001f) return;
                if (!changed) { Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.RegisterCompleteObjectUndo(gestureMotion, "RimeEmo: Edit Hold and Transition"); }
                RimeEmoTimeline.MoveHoldKey(track, ActiveState, target); changed = true;
                Edited?.Invoke(); UpdatePositions();
            }
            else { Time = Clean(MouseTime(p.x)); UpdateHead(); }
            SelectionChanged?.Invoke();
        }
        void PointerUp(PointerUpEvent e)
        {
            if (!IsInteracting || e.pointerId != pointer) return;
            if (moved) MovePointer(this.WorldToLocal(e.position));
            EndInteraction(); e.StopPropagation();
        }
        // Selects every cue whose extent (start to the end of its hold) meets the box.
        void UpdateMarquee(Vector2 p)
        {
            float left = Mathf.Min(marqueeStart.x, p.x), right = Mathf.Max(marqueeStart.x, p.x);
            float top = Mathf.Min(marqueeStart.y, p.y), bottom = Mathf.Max(marqueeStart.y, p.y);
            marquee.style.display = DisplayStyle.Flex;
            marquee.transform.position = new Vector3(left - Gutter, top, 0);
            marquee.style.width = right - left; marquee.style.height = bottom - top;
            SelectedStates.Clear(); SelectedStates.UnionWith(marqueeBase);
            for (int t = 0; t < Motion.tracks.Count; t++)
            {
                float rowTop = RulerHeight + t * RowHeight;
                if (bottom < rowTop + BandTop || top > rowTop + RowHeight - BandTop) continue;
                var track = Motion.tracks[t]; var times = RimeEmoTimeline.Times(track);
                for (int i = 0; i < times.Length; i++)
                {
                    var state = track.states[i];
                    float start = Gutter + X(times[i]);
                    float end = Gutter + Mathf.Max(X(times[i]) + 6, X(times[i] + RimeEmoTimeline.ContentDuration(state) + RimeEmoTimeline.HoldDuration(state)));
                    if (end >= left && start <= right) SelectedStates.Add(new Vector2Int(t, i));
                }
            }
            SelectionChanged?.Invoke(); UpdatePositions();
        }

        public void EndInteraction()
        {
            if (!IsInteracting) return;
            gesture = 0;
            if (marquee != null) marquee.style.display = DisplayStyle.None;
            if (snapGuide != null) snapGuide.style.display = DisplayStyle.None;
            marqueeBase = null;
            if (changed && gestureMotion) { EditorUtility.SetDirty(gestureMotion); if (undoGroup >= 0) Undo.CollapseUndoOperations(undoGroup); }
            undoGroup = -1; changed = false; drag = null; gestureMotion = null;
            int oldPointer = pointer; pointer = -1;
            if (oldPointer >= 0 && this.HasPointerCapture(oldPointer)) this.ReleasePointer(oldPointer);
            InteractionEnded?.Invoke();
        }
        void Wheel(WheelEvent e)
        {
            if (IsInteracting) return;
            // Shift+wheel and horizontal trackpad gestures scroll the time axis; a vertical wheel zooms.
            float pan = e.shiftKey ? e.delta.y : Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y) ? e.delta.x : 0;
            if (pan != 0)
            {
                offset = Mathf.Max(0, offset + pan * 20 / pixelsPerSecond); fitPending = false; UpdatePositions(); e.StopPropagation();
                return;
            }
            Vector2 p = this.WorldToLocal(e.mousePosition); float anchor = MouseTime(p.x);
            pixelsPerSecond = Mathf.Clamp(pixelsPerSecond * Mathf.Exp(-e.delta.y * 0.08f), 1, 2400);
            offset = Mathf.Max(0, anchor - (p.x - Gutter - 10) / pixelsPerSecond); fitPending = false; UpdatePositions(); e.StopPropagation();
        }
    }
}




