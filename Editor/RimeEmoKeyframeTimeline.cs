using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal sealed class RimeEmoKeyframeTimeline : VisualElement
    {
        internal AnimationClip Clip { get; set; }
        internal List<ShapeEntry> Shapes { get; set; } = new List<ShapeEntry>();
        internal int ActiveShape { get; set; } = -1;
        internal bool FrameSnap { get; set; } = true;
        internal float Time { get; private set; }
        internal bool IsInteracting => gesture != 0;
        internal HashSet<Vector2Int> SelectedKeys { get; } = new HashSet<Vector2Int>();
        internal event Action SelectionChanged;
        internal event Action<float> SeekRequested, MoveRequested;
        internal event Action<int, float> AddRequested;
        internal event Action DeleteRequested, CopyRequested, PasteRequested, DuplicateRequested, PlayToggleRequested;

        const float Gutter = 170, Ruler = 24, Row = 28;
        float pixels = 180, offset, dragDelta, grabTime, dragMin, dragMax, anchorTime;
        int gesture, pointer = -1;
        Vector2 pointerStart;
        bool moved, fitPending = true;
        AnimationClip cachedClip;
        AnimationCurve[] curves = Array.Empty<AnimationCurve>();
        readonly List<Label> labels = new List<Label>(), keys = new List<Label>(), ticks = new List<Label>();
        VisualElement playhead;
        Slider scroll;

        internal RimeEmoKeyframeTimeline()
        {
            AddToClassList("kf-timeline"); focusable = true;
            RegisterCallback<GeometryChangedEvent>(_ => { if (fitPending) Fit(); else UpdateVisuals(); });
            RegisterCallback<PointerDownEvent>(OnDown); RegisterCallback<PointerMoveEvent>(OnMove); RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCancelEvent>(_ => EndInteraction()); RegisterCallback<PointerCaptureOutEvent>(_ => EndInteraction());
            RegisterCallback<DetachFromPanelEvent>(_ => EndInteraction()); RegisterCallback<WheelEvent>(OnWheel); RegisterCallback<KeyDownEvent>(OnKey);
        }
        static VisualElement Box(string cls, float left, float top, float height)
        {
            var box = new VisualElement(); box.AddToClassList(cls); box.style.position = Position.Absolute;
            box.style.left = left; box.style.top = top; box.style.height = height; return box;
        }
        internal void Refresh()
        {
            EndInteraction(); Clear(); labels.Clear(); keys.Clear(); ticks.Clear();
            fitPending |= cachedClip != Clip; cachedClip = Clip;
            curves = Shapes.Select(s => Clip ? AnimationUtility.GetEditorCurve(Clip, RimeEmoClips.Binding(s)) : null).ToArray();
            SelectedKeys.RemoveWhere(id => !Valid(id));
            ActiveShape = Mathf.Clamp(ActiveShape, -1, Shapes.Count - 1);
            style.height = Mathf.Max(90, Ruler + Shapes.Count * Row + 18);
            var ruler = Box("kf-ruler", Gutter, 0, Ruler); ruler.style.right = 0; ruler.style.overflow = Overflow.Hidden; Add(ruler);
            for (int i = 0; i < 64; i++)
            {
                var tick = new Label(); tick.style.position = Position.Absolute; tick.style.width = 70; tick.style.height = Ruler;
                tick.pickingMode = PickingMode.Ignore; ruler.Add(tick); ticks.Add(tick);
            }
            for (int i = 0; i < Shapes.Count; i++)
            {
                var label = new Label(Shapes[i].shape) { tooltip = Shapes[i].part + " / " + Shapes[i].shape };
                label.AddToClassList("kf-label"); label.style.position = Position.Absolute; label.style.left = 0;
                label.style.top = Ruler + i * Row; label.style.width = Gutter; label.style.height = Row; Add(label); labels.Add(label);
                var lane = Box("kf-row", Gutter, Ruler + i * Row, Row); lane.style.right = 0; lane.style.overflow = Overflow.Hidden; Add(lane);
                if (curves[i] == null) continue;
                for (int k = 0; k < curves[i].length; k++)
                {
                    var key = new Label("◆"); key.AddToClassList("kf-key"); key.userData = new Vector2Int(i, k);
                    key.style.position = Position.Absolute; key.style.width = 14; key.style.height = 20; key.style.top = 4;
                    key.style.marginLeft = key.style.marginRight = 0;
                    key.tooltip = curves[i][k].time.ToString("0.###") + "s : " + curves[i][k].value.ToString("0.###");
                    lane.Add(key); keys.Add(key);
                }
            }
            var overlay = Box("kf-overlay", Gutter, 0, Mathf.Max(Ruler, Shapes.Count * Row + Ruler));
            overlay.style.right = 0; overlay.style.overflow = Overflow.Hidden; overlay.pickingMode = PickingMode.Ignore; Add(overlay);
            playhead = Box("kf-playhead", 0, 0, Mathf.Max(Ruler, Shapes.Count * Row + Ruler));
            playhead.style.width = 2; playhead.pickingMode = PickingMode.Ignore; overlay.Add(playhead);
            scroll = new Slider(0, 1) { name = "kf-scroll" }; scroll.AddToClassList("kf-scroll");
            scroll.style.position = Position.Absolute; scroll.style.left = Gutter; scroll.style.right = 0; scroll.style.bottom = 0; scroll.style.height = 18;
            scroll.RegisterValueChangedCallback(e => { offset = e.newValue; UpdateVisuals(); });
            scroll.RegisterCallback<PointerDownEvent>(e => e.StopPropagation()); Add(scroll);
            UpdateVisuals();
        }
        bool Valid(Vector2Int id) => id.x >= 0 && id.x < curves.Length && curves[id.x] != null && id.y >= 0 && id.y < curves[id.x].length;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static float Clean(float t) => Finite(t) ? Mathf.Clamp(t, 0, 600) : 0;
        float Snap(float t) => Clean(FrameSnap ? Mathf.Round(t * 60) / 60 : t);
        float X(float t) => 10 + (t - offset) * pixels;
        float MouseTime(float x) => offset + (x - Gutter - 10) / pixels;
        internal void SetTime(float value) { Time = Clean(value); if (playhead != null) playhead.transform.position = new Vector3(X(Time), 0, 0); }
        internal void Fit()
        {
            if (!Finite(contentRect.width) || contentRect.width <= Gutter + 30) { fitPending = true; return; }
            pixels = Mathf.Clamp((contentRect.width - Gutter - 24) / Mathf.Max(.5f, Clip ? Clip.length : 0, Time), .25f, 2400);
            offset = 0; fitPending = false; UpdateVisuals();
        }
        void UpdateVisuals()
        {
            if (playhead == null) return;
            for (int i = 0; i < labels.Count; i++) labels[i].EnableInClassList("is-selected", i == ActiveShape);
            foreach (var key in keys)
            {
                var id = (Vector2Int)key.userData; bool selected = SelectedKeys.Contains(id);
                key.transform.position = new Vector3(X(curves[id.x][id.y].time + (selected ? dragDelta : 0)) - 7, 0, 0);
                key.EnableInClassList("is-selected", selected);
            }
            SetTime(Time);
            float raw = 65 / pixels, power = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(raw))), scaled = raw / power;
            float step = (scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10) * power;
            float first = Mathf.Floor(offset / step) * step;
            for (int i = 0; i < ticks.Count; i++)
            {
                float at = first + i * step; ticks[i].transform.position = new Vector3(X(at), 0, 0);
                ticks[i].text = at.ToString("0.###") + "s";
                ticks[i].style.display = X(at) <= contentRect.width - Gutter ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (scroll != null)
            {
                float visible = Mathf.Max(1, contentRect.width - Gutter) / pixels;
                scroll.highValue = Mathf.Max(.01f, offset, Mathf.Max(Clip ? Clip.length : 0, Time) - visible * .5f);
                scroll.SetValueWithoutNotify(offset);
            }
        }
        void Begin(int mode, PointerDownEvent e, Vector2 p)
        {
            gesture = mode; pointer = e.pointerId; pointerStart = p; moved = false; dragDelta = 0;
            this.CapturePointer(pointer);
        }
        void Seek(float at) { SetTime(Snap(at)); SeekRequested?.Invoke(Time); }
        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 || IsInteracting || e.target == scroll) return;
            var p = this.WorldToLocal(e.position); if (p.y >= contentRect.height - 18) return; Focus();
            if (p.y < Ruler) { if (p.x >= Gutter) { Seek(MouseTime(p.x)); Begin(1, e, p); } e.StopPropagation(); return; }
            int row = Mathf.FloorToInt((p.y - Ruler) / Row); if (row < 0 || row >= Shapes.Count) return;
            ActiveShape = row;
            if (p.x < Gutter) { SelectedKeys.Clear(); SelectionChanged?.Invoke(); UpdateVisuals(); e.StopPropagation(); return; }
            int hit = -1;
            if (curves[row] != null) for (int k = 0; k < curves[row].length; k++)
                if (Mathf.Abs(X(curves[row][k].time) - (p.x - Gutter)) <= 7) { hit = k; break; }
            if (hit < 0)
            {
                if (!e.ctrlKey && !e.commandKey) SelectedKeys.Clear();
                SelectionChanged?.Invoke(); UpdateVisuals();
                if (e.clickCount == 2) AddRequested?.Invoke(row, Snap(MouseTime(p.x)));
                else { Seek(MouseTime(p.x)); Begin(1, e, p); }
                e.StopPropagation(); return;
            }
            var id = new Vector2Int(row, hit); bool toggle = e.ctrlKey || e.commandKey;
            if (toggle && SelectedKeys.Contains(id))
            {
                SelectedKeys.Remove(id); SelectionChanged?.Invoke(); UpdateVisuals(); e.StopPropagation(); return;
            }
            if (!toggle && !SelectedKeys.Contains(id)) SelectedKeys.Clear();
            SelectedKeys.Add(id);
            grabTime = MouseTime(p.x); anchorTime = curves[row][hit].time;
            var selectedTimes = SelectedKeys.Where(Valid).Select(k => curves[k.x][k.y].time).ToArray();
            dragMin = selectedTimes.Min(); dragMax = selectedTimes.Max();
            Begin(2, e, p); SelectionChanged?.Invoke(); UpdateVisuals(); e.StopPropagation();
        }
        void DragTo(Vector2 p)
        {
            if (gesture == 1) { Seek(MouseTime(p.x)); return; }
            float target = Snap(anchorTime + MouseTime(p.x) - grabTime);
            dragDelta = Mathf.Clamp(target - anchorTime, -dragMin, 600 - dragMax); UpdateVisuals();
        }
        void OnMove(PointerMoveEvent e)
        {
            if (!IsInteracting || e.pointerId != pointer) return;
            var p = this.WorldToLocal(e.position); if ((p - pointerStart).sqrMagnitude > 1) moved = true;
            if (moved) DragTo(p); e.StopPropagation();
        }
        void OnUp(PointerUpEvent e)
        {
            if (!IsInteracting || e.pointerId != pointer) return;
            if (moved) DragTo(this.WorldToLocal(e.position));
            bool commit = moved && gesture == 2 && Mathf.Abs(dragDelta) > .000001f; float delta = dragDelta;
            EndInteraction(); if (commit) MoveRequested?.Invoke(delta); e.StopPropagation();
        }
        internal void EndInteraction()
        {
            if (!IsInteracting) return;
            int old = pointer; gesture = 0; pointer = -1; moved = false; dragDelta = 0;
            if (old >= 0 && this.HasPointerCapture(old)) this.ReleasePointer(old);
            UpdateVisuals(); SelectionChanged?.Invoke();
        }
        void OnWheel(WheelEvent e)
        {
            if (IsInteracting) return;
            if (e.shiftKey) offset = Mathf.Clamp(offset + e.delta.y * 25 / pixels, 0, 600);
            else
            {
                var p = this.WorldToLocal(e.mousePosition); if (p.x < Gutter) return; // Let the surrounding list scroll.
                float anchor = MouseTime(p.x); pixels = Mathf.Clamp(pixels * Mathf.Exp(-e.delta.y * .08f), .25f, 2400);
                offset = Mathf.Clamp(anchor - (p.x - Gutter - 10) / pixels, 0, 600);
            }
            fitPending = false; UpdateVisuals(); e.StopPropagation();
        }
        void OnKey(KeyDownEvent e)
        {
            if (e.target is TextField) return;
            bool command = e.ctrlKey || e.commandKey;
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) DeleteRequested?.Invoke();
            else if (command && e.keyCode == KeyCode.C) CopyRequested?.Invoke();
            else if (command && e.keyCode == KeyCode.V) PasteRequested?.Invoke();
            else if (command && e.keyCode == KeyCode.D) DuplicateRequested?.Invoke();
            else if (command && e.keyCode == KeyCode.A) { SelectedKeys.Clear(); for (int r = 0; r < curves.Length; r++) if (curves[r] != null) for (int k = 0; k < curves[r].length; k++) SelectedKeys.Add(new Vector2Int(r, k)); SelectionChanged?.Invoke(); UpdateVisuals(); }
            else if (e.keyCode == KeyCode.Escape) { EndInteraction(); SelectedKeys.Clear(); SelectionChanged?.Invoke(); UpdateVisuals(); }
            else if (e.keyCode == KeyCode.Space) PlayToggleRequested?.Invoke();
            else return;
            e.StopPropagation();
        }
    }
}
