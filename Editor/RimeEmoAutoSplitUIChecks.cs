using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal static class RimeEmoAutoSplitUIChecks
    {
        static BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("AutoSplitUI: " + message); }
        static object Get(object o, string n) => o.GetType().GetField(n, Flags).GetValue(o);
        static void Set(object o, string n, object v) => o.GetType().GetField(n, Flags).SetValue(o, v);
        static void Call(object o, string n) => o.GetType().GetMethod(n, Flags).Invoke(o, null);

        internal static void Run()
        {
            RimeEmoProject project = null; RimeEmoMotion motion = null; AnimationClip source = null; RimeEmoWindow window = null;
            try
            {
                project = ScriptableObject.CreateInstance<RimeEmoProject>(); project.faceMeshSelected = true; project.faceMeshPath = "Face";
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "SmileMouth", part = FacePart.Mouth });
                source = new AnimationClip { name = "AutoSplitUI", frameRate = 60 };
                AnimationUtility.SetEditorCurve(source, RimeEmoClips.Binding(project.shapes[0]), new AnimationCurve(new Keyframe(0, 0), new Keyframe(.4f, 100), new Keyframe(.8f, 100), new Keyframe(1.2f, 0)));
                motion = RimeEmoDrafts.Create(project); motion.sourceClip = source;
                var track = motion.tracks.Single(t => t.part == FacePart.Mouth);
                track.states.Add(new MotionState { clip = source, animated = true, hold = 1.2f, transition = 0 });
                window = ScriptableObject.CreateInstance<RimeEmoWindow>(); Set(window, "project", project); Set(window, "motion", motion); Set(window, "mode", 1); Set(window, "time", .6f);
                window.position = new Rect(0, 0, 1000, 760); window.Show(); window.CreateGUI();
                var timeline = window.rootVisualElement.Q<RimeEmoTimelineElement>();
                int trackIndex = motion.tracks.IndexOf(track);
                timeline.SelectedStates.Add(new Vector2Int(trackIndex, 0));
                Call(window, "RefreshQueueButtons"); var root = window.rootVisualElement; var auto = root.Q<Button>("auto-split"); Check(auto != null && auto.enabledSelf, "auto-split button disabled");
                Call(window, "OpenAutoSplit"); var panel = root.Q<VisualElement>("auto-split-panel"); Check(panel != null && root.Query<VisualElement>(className: "auto-split-boundary").ToList().Count == 2, "preview boundaries missing");
                Check(track.states.Count == 1 && !window.hasUnsavedChanges && Mathf.Approximately((float)Get(window, "time"), .6f), "preview changed motion or time");
                var min = root.Q<FloatField>("auto-split-minimum"); var tol = root.Q<FloatField>("auto-split-tolerance"); Check(min != null && tol != null, "numeric fields missing");
                root.panel.visualTree.style.width = 1000; root.panel.visualTree.style.height = 760;
                root.style.width = 1000; root.style.height = 760;
                root.panel.GetType().GetMethod("ValidateLayout", Flags | BindingFlags.Public).Invoke(root.panel, null);
                foreach (var control in new VisualElement[] { min, tol, root.Q<Button>("auto-split-apply"), root.Q<Button>("auto-split-cancel") })
                    Check(control.worldBound.width > 20 && control.worldBound.xMin >= root.worldBound.xMin && control.worldBound.xMax <= root.worldBound.xMax + 1
                        && control.worldBound.yMax <= root.worldBound.yMax + 1, "auto-split control clipped at minimum window size");
                min.value = float.NaN; tol.value = float.PositiveInfinity; Check(!float.IsNaN(min.value) && !float.IsInfinity(tol.value), "nonfinite values accepted");
                min.value = .5f; Check(!root.Q<Button>("auto-split-apply").enabledSelf, "apply enabled below threshold"); min.value = .2f;
                Call(window, "CancelAutoSplit"); Check(root.Query<VisualElement>(className: "auto-split-boundary").ToList().Count == 0 && track.states.Count == 1, "cancel changed state");
                Call(window, "OpenAutoSplit"); Call(window, "ApplyAutoSplit"); Check(track.states.Count == 2 && Mathf.Approximately((float)Get(window, "time"), .6f), "apply result or time");
                Check(window.hasUnsavedChanges, "apply did not mark draft dirty");
                Undo.PerformUndo(); Check(motion.tracks[trackIndex].states.Count == 1, "undo failed"); Undo.PerformRedo(); Check(motion.tracks[trackIndex].states.Count == 2, "redo failed");
                Undo.PerformUndo(); window.CreateGUI();
                timeline = root.Q<RimeEmoTimelineElement>(); timeline.SelectedStates.Add(new Vector2Int(trackIndex, 0));
                Call(window, "OpenAutoSplit"); Check(root.Q<VisualElement>("auto-split-panel") != panel && root.Query<VisualElement>(className: "auto-split-boundary").ToList().Count == 2, "rebuild cannot reopen fresh panel");
                timeline.SelectedStates.Clear(); Call(window, "RefreshQueueButtons");
                Check(root.Query<VisualElement>(className: "auto-split-boundary").ToList().Count == 0, "selection change retained stale preview");
                timeline.SelectedStates.Add(new Vector2Int(trackIndex, 0)); Call(window, "OpenAutoSplit");
                Set(window, "mode", 0); Call(window, "RefreshToolkit"); Check(root.Query<VisualElement>(className: "auto-split-boundary").ToList().Count == 0, "mode switch retained preview");
            }
            finally { if (window) { window.DiscardChanges(); window.Close(); } else if (motion) UnityEngine.Object.DestroyImmediate(motion); if (source) UnityEngine.Object.DestroyImmediate(source); if (project) UnityEngine.Object.DestroyImmediate(project); }
        }
    }
}
