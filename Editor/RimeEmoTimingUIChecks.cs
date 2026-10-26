using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal static class RimeEmoTimingUIChecks
    {
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("TimingUI: " + message); }
        private static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);
        private static void Call(object o, string name) => o.GetType().GetMethod(name, Flags).Invoke(o, null);
        private static void Click(VisualElement element, Vector2 at)
        {
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = at }))
            { down.target = element; element.SendEvent(down); }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = at }))
            { up.target = element; element.SendEvent(up); }
        }

        internal static void Run()
        {
            RimeEmoProject project = null; RimeEmoMotion motion = null; AnimationClip source = null; RimeEmoWindow window = null;
            try
            {
                project = ScriptableObject.CreateInstance<RimeEmoProject>(); project.faceMeshSelected = true; project.faceMeshPath = "Face";
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "Smile", part = FacePart.Mouth });
                source = new AnimationClip { name = "Timing UI Source", frameRate = 60 };
                AnimationUtility.SetEditorCurve(source, RimeEmoClips.Binding(project.shapes[0]), AnimationCurve.Linear(0, 0, .5f, 80));
                motion = RimeEmoDrafts.Create(project); motion.sourceClip = source;
                var track = motion.tracks.Single(t => t.part == FacePart.Mouth);
                track.states.Add(new MotionState { clip = source, animated = true, hold = .5f, endHold = .7f, transition = .3f, sourceStart = 0, speed = 1 });
                track.states.Add(new MotionState { clip = source, animated = true, hold = .5f, sourceStart = .5f, speed = 1 });
                window = ScriptableObject.CreateInstance<RimeEmoWindow>(); Set(window, "project", project); Set(window, "motion", motion); Set(window, "mode", 1); Set(window, "activeTrack", motion.tracks.IndexOf(track)); Set(window, "activeState", 0); Set(window, "time", .2f);
                window.position = new Rect(0, 0, 1000, 760); window.Show(); window.CreateGUI();
                var root = window.rootVisualElement; Call(window, "RefreshToolkitInspector");
                var hold = root.Q<FloatField>("queue-hold"); var transition = root.Q<FloatField>("queue-transition");
                Check(hold != null && transition != null, "animated timing fields missing");
                Check(root.Q<VisualElement>(className: "timeline-keep") != null && root.Q<VisualElement>(className: "timeline-transition") != null && root.Q<VisualElement>(className: "timeline-keep-key") != null, "timeline timing elements missing");
                hold.value = .9f;
                track = motion.tracks[motion.tracks.IndexOf(track)]; var state = track.states[0];
                Check(Mathf.Abs(state.hold - .5f) < .0001f && Mathf.Abs(state.sourceStart) < .0001f && Mathf.Abs(state.endHold - .9f) < .0001f && Mathf.Abs(state.transition - .1f) < .0001f && Mathf.Abs(RimeEmoTimeline.Times(track)[1] - 1.5f) < .0001f, "hold field retimed source or next cue");
                Call(window, "RefreshToolkitInspector"); transition = root.Q<FloatField>("queue-transition"); transition.value = .3f;
                track = motion.tracks[motion.tracks.IndexOf(track)]; state = track.states[0];
                Check(Mathf.Abs(state.endHold - .7f) < .0001f && Mathf.Abs(state.transition - .3f) < .0001f && Mathf.Abs(RimeEmoTimeline.Times(track)[1] - 1.5f) < .0001f, "transition field did not restore end hold");
                Set(window, "activeState", 1); Call(window, "RefreshToolkitInspector"); Check(root.Q<FloatField>("queue-hold") != null && root.Q<FloatField>("queue-transition") == null, "last cue exposed invalid transition field");
                Check(Mathf.Abs((float)Get(window, "time") - .2f) < .0001f, "timing edit changed UI time");
                Set(window, "activeState", 0); Call(window, "RefreshToolkit");
                root.panel.visualTree.style.width = 1000; root.panel.visualTree.style.height = 760;
                root.style.width = 1000; root.style.height = 760;
                var layout = root.panel.GetType().GetMethod("ValidateLayout", Flags | BindingFlags.Public);
                layout.Invoke(root.panel, null);
                var timeline = root.Q<RimeEmoTimelineElement>(); timeline.FrameSnap = false; timeline.Fit(); layout.Invoke(root.panel, null);
                foreach (var control in new VisualElement[] { root.Q<FloatField>("queue-hold"), root.Q<FloatField>("queue-transition"), root.Q<DropdownField>("queue-interpolation") })
                    Check(control.worldBound.width > 20 && control.worldBound.xMin >= root.worldBound.xMin && control.worldBound.xMax <= root.worldBound.xMax + 1
                        && control.worldBound.yMax <= root.worldBound.yMax + 1, "timing control clipped at minimum window size");
                var keep = root.Q<VisualElement>(className: "timeline-keep"); var blend = root.Q<VisualElement>(className: "timeline-transition");
                float scale = (float)Get(timeline, "pixelsPerSecond");
                Check(Mathf.Abs(keep.resolvedStyle.width - .7f * scale) < 1 && Mathf.Abs(blend.resolvedStyle.width - .3f * scale) < 1, "hold/transition band widths");
                var sourceBand = root.Q<Label>(className: "track-span");
                Check(keep.worldBound.height >= 14 && blend.worldBound.height >= 14 && keep.worldBound.yMax < blend.worldBound.yMin,
                    "hold and transition do not occupy separate half rows");
                Check(sourceBand.worldBound.yMin <= keep.worldBound.yMin + 1 && sourceBand.worldBound.yMax >= blend.worldBound.yMax - 1,
                    "source band does not span both halves");
                Check(Mathf.Abs(keep.worldBound.xMin - sourceBand.worldBound.xMax) < 1 && Mathf.Abs(blend.worldBound.xMin - keep.worldBound.xMax) < 1,
                    "timing bands do not follow the source and each other on the time axis");
                Check(((Label)keep).text.Contains("0.7") && ((Label)blend).text.Contains("0.3"), "timing bands lack duration labels");
                string beforeSelection = EditorJsonUtility.ToJson(motion);
                timeline.SelectedStates.Clear(); timeline.SelectedStates.Add(new Vector2Int(motion.tracks.IndexOf(track), 1));
                foreach (var band in new[] { keep, blend })
                {
                    Click(band, band.worldBound.center);
                    Check(timeline.ActiveState == 0 && timeline.TransitionSelected && keep.ClassListContains("is-selected") && blend.ClassListContains("is-selected"),
                        "timing band does not select timing details");
                    Check(timeline.SelectedStates.SetEquals(new[] { new Vector2Int(motion.tracks.IndexOf(track), 0) }), "timing band retained another cue's selection");
                    Check(Mathf.Abs(timeline.Time - .2f) < .0001f && EditorJsonUtility.ToJson(motion) == beforeSelection,
                        "timing band selection changed playback or animation");
                }
                Click(timeline, new Vector2(blend.worldBound.center.x, keep.worldBound.center.y));
                Check(timeline.ActiveState == -1 && !timeline.TransitionSelected, "blank opposite half selected a timing band");
                root.Q<FloatField>("time").value = .2f;
                Click(keep, keep.worldBound.center);
                RimeEmoTimeline.SetHoldDuration(track, 0, 0); timeline.UpdatePositions(); layout.Invoke(root.panel, null);
                Check(keep.resolvedStyle.display == DisplayStyle.None && blend.resolvedStyle.display == DisplayStyle.Flex, "zero hold left a phantom band");
                RimeEmoTimeline.SetTransitionDuration(track, 0, 0); timeline.UpdatePositions(); layout.Invoke(root.panel, null);
                Check(blend.resolvedStyle.display == DisplayStyle.None && keep.resolvedStyle.display == DisplayStyle.Flex, "zero transition left a phantom band");
                RimeEmoTimeline.SetHoldDuration(track, 0, .7f); timeline.UpdatePositions(); layout.Invoke(root.panel, null);
                var handle = root.Q<VisualElement>(className: "timeline-keep-key");
                Vector2 begin = handle.worldBound.center, end = begin + Vector2.right * (.2f * scale);
                using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = begin }))
                { down.target = handle; handle.SendEvent(down); }
                Check((int)Get(timeline, "gesture") == 4, "hold handle hit source trim or seek");
                using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = end }))
                { move.target = timeline; timeline.SendEvent(move); }
                using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = end }))
                { up.target = timeline; timeline.SendEvent(up); }
                int part = motion.tracks.FindIndex(t => t.part == FacePart.Mouth);
                state = motion.tracks[part].states[0];
                Check(Mathf.Abs(state.endHold - .9f) < .0001f && Mathf.Abs(state.hold - .5f) < .0001f
                    && Mathf.Abs(RimeEmoTimeline.Times(motion.tracks[part])[1] - 1.5f) < .0001f, "hold drag changed source or next cue");
                Check(Mathf.Abs(timeline.Time - .2f) < .0001f && Mathf.Abs((float)Get(window, "time") - .2f) < .0001f, "hold drag sought playback");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(Mathf.Abs(motion.tracks[part].states[0].endHold - .7f) < .0001f, "hold drag undo failed");
                Undo.PerformRedo(); Check(Mathf.Abs(motion.tracks[part].states[0].endHold - .9f) < .0001f, "hold drag redo failed");
            }
            finally { if (window) { window.DiscardChanges(); window.Close(); } else if (motion) UnityEngine.Object.DestroyImmediate(motion); if (source) UnityEngine.Object.DestroyImmediate(source); if (project) UnityEngine.Object.DestroyImmediate(project); }
        }
        private static object Get(object o, string name) => o.GetType().GetField(name, Flags).GetValue(o);
    }
}
