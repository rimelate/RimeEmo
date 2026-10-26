using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // Regression checks for imported animation blocks in the part queue.
    internal static class RimeEmoQueueChecks
    {
        internal static void Run()
        {
            string root = "Assets/RimeEmoQueueChecks-" + Guid.NewGuid().ToString("N");
            RimeEmoProject project = null; AnimationClip source = null; RimeEmoMotion motion = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", root.Substring(7));
                project = ScriptableObject.CreateInstance<RimeEmoProject>();
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "Mouth", part = FacePart.Mouth });
                project.shapes.Add(new ShapeEntry { path = "Face", shape = "Eye", part = FacePart.LeftEye });
                project.faceMeshSelected = true; project.faceMeshPath = "Face";
                AssetDatabase.CreateAsset(project, root + "/Project.asset");
                source = new AnimationClip { name = "QueueSource", frameRate = 30 };
                var mouth = RimeEmoClips.Binding(project.shapes[0]);
                var eye = RimeEmoClips.Binding(project.shapes[1]);
                var mk = new Keyframe(0, 0, 1, 2, .2f, .4f) { weightedMode = WeightedMode.Both };
                AnimationUtility.SetEditorCurve(source, mouth, new AnimationCurve(mk, new Keyframe(1, 1, 2, 3)));
                AnimationUtility.SetEditorCurve(source, eye, AnimationCurve.Linear(.15f, 0, .85f, 1));
                var transform = EditorCurveBinding.FloatCurve("Face", typeof(Transform), "m_LocalPosition.x");
                AnimationUtility.SetEditorCurve(source, transform, AnimationCurve.Linear(0, 0, 1, 2));
                var mesh = new Texture2D(1, 1);
                var obj = EditorCurveBinding.PPtrCurve("Face", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]");
                AnimationUtility.SetObjectReferenceCurve(source, obj, new[] { new ObjectReferenceKeyframe { time = 0, value = mesh } });
                AnimationUtility.SetAnimationEvents(source, new[] { new AnimationEvent { time = .5f, functionName = "Ping" } });
                AssetDatabase.CreateAsset(mesh, root + "/Material.asset"); AssetDatabase.CreateAsset(source, root + "/Source.anim");
                motion = RimeEmoQueueImport.Create(project, source);
                Check(motion.sourceClip && motion.sourceClip != source, "queue import did not isolate source clip");
                var mt = motion.tracks.Single(t => t.part == FacePart.Mouth);
                var et = motion.tracks.Single(t => t.part == FacePart.LeftEye);
                Check(mt.states.Count == 1 && mt.states[0].animated && mt.states[0].sourceStart == 0 && Mathf.Approximately(mt.states[0].speed, 1), "mouth state metadata");
                Check(et.states.Count == 1 && et.states[0].animated, "eye state metadata");
                var baseline = RimeEmoCompiler.Compile(motion);
                CheckCurveEqual(source, baseline, mouth, "baseline mouth"); CheckCurveEqual(source, baseline, eye, "baseline eye");
                var mapped = new System.Collections.Generic.List<Keyframe>();
                RimeEmoQueueCurves.AddMapped(mapped, AnimationUtility.GetEditorCurve(source, mouth), .2f, .66f, .13f, 2);
                var mappedCurve = new AnimationCurve(mapped.ToArray());
                for (int i = 0; i <= 100; i++) { float t = .2f + .66f * i / 100f; Check(Mathf.Abs(mappedCurve.Evaluate(t) - AnimationUtility.GetEditorCurve(source, mouth).Evaluate(.13f + (t - .2f) * 2)) < .002f, "mapped Bezier sample mismatch"); }
                Check(AnimationUtility.GetEditorCurve(baseline, transform) != null && AnimationUtility.GetObjectReferenceCurve(baseline, obj).Length == 1, "non-face curves were lost");
                Check(baseline.events.Length == 1 && baseline.events[0].functionName == "Ping", "events were lost");
                var right = RimeEmoTimeline.Split(mt, 0, .37f);
                Check(right == 1 && Mathf.Approximately(mt.states[1].sourceStart, .37f), "animated split mapping");
                var splitClip = RimeEmoCompiler.Compile(motion);
                CheckCurveEqual(source, splitClip, mouth, "split mouth");
                var originalKeys = AnimationUtility.GetEditorCurve(source, mouth).keys;
                var splitKeys = AnimationUtility.GetEditorCurve(splitClip, mouth).keys;
                Check(originalKeys.Length == splitKeys.Length && splitKeys[0].weightedMode == originalKeys[0].weightedMode && splitKeys[0].outWeight == originalKeys[0].outWeight, "split changed weighted keys");
                UnityEngine.Object.DestroyImmediate(splitClip);
                RimeEmoTimeline.BlockDrag center = new RimeEmoTimeline.BlockDrag(et, 0, RimeEmoTimeline.BlockEdge.Move); center.Apply(.25f);
                Check(Mathf.Approximately(et.startTime, .25f) && Mathf.Approximately(et.states[0].sourceStart, 0), "center drag changed source mapping or failed to move");
                var duplicate = RimeEmoTimeline.Duplicate(et, 0);
                Check(duplicate == 1 && et.states[duplicate] != et.states[0] && et.states[duplicate].animated, "duplicate did not deep copy state");
                RimeEmoTimeline.BlockDrag drag = new RimeEmoTimeline.BlockDrag(mt, 0, RimeEmoTimeline.BlockEdge.Start);
                Undo.IncrementCurrentGroup();
                drag.Apply(.1f, () => Undo.RegisterCompleteObjectUndo(motion, "Trim queue test"));
                Check(mt.states[0].sourceStart > 0 && RimeEmoTimeline.Times(mt)[0] == .1f, "animated edge trim mapping");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                mt = motion.tracks.Single(t => t.part == FacePart.Mouth);
                Check(mt.startTime == 0 && mt.states[0].sourceStart == 0, "trim undo failed to restore source mapping");
                drag = new RimeEmoTimeline.BlockDrag(mt, 0, RimeEmoTimeline.BlockEdge.Start); drag.Apply(.1f);
                var trimmedClip = RimeEmoCompiler.Compile(motion);
                for (int i = 0; i <= 100; i++)
                {
                    float t = .1f + .9f * i / 100;
                    Check(Mathf.Abs(AnimationUtility.GetEditorCurve(trimmedClip, mouth).Evaluate(t) - AnimationUtility.GetEditorCurve(source, mouth).Evaluate(t)) < .002f, "left trim changed surviving motion");
                }
                UnityEngine.Object.DestroyImmediate(trimmedClip);
                CheckLeadingHold(project, mouth);
                var output = RimeEmoDrafts.SaveTo(motion, root + "/Output.anim");
                var saved = project.motions.Last(); var reopened = RimeEmoDrafts.Copy(saved);
                Check(reopened.sourceClip == saved.sourceClip && reopened.tracks[0].states.Any(s => s.animated), "saved queue metadata did not reload");
                Check(AnimationUtility.GetObjectReferenceCurve(output, obj).Length == 1 && output.events.Length == 1, "save lost source data");
                Check(AnimationUtility.GetEditorCurve(source, mouth).keys[0].outTangent == 2, "source was modified");
                UnityEngine.Object.DestroyImmediate(baseline); UnityEngine.Object.DestroyImmediate(reopened);
                CheckUI(project, source, mouth);
            }
            finally { if (motion) { Undo.ClearUndo(motion); UnityEngine.Object.DestroyImmediate(motion); } AssetDatabase.DeleteAsset(root); }
        }
        private static void CheckLeadingHold(RimeEmoProject project, EditorCurveBinding binding)
        {
            foreach (bool weighted in new[] { false, true })
            {
                var source = new AnimationClip { name = "LeadingHoldSource", frameRate = 60 };
                RimeEmoMotion motion = null; AnimationClip output = null;
                try
                {
                    var middle = new Keyframe(.5f, 50, 100, 100);
                    if (weighted)
                    {
                        middle.inTangent = 140; middle.outTangent = 70;
                        middle.inWeight = .2f; middle.outWeight = .45f; middle.weightedMode = WeightedMode.Both;
                    }
                    AnimationUtility.SetEditorCurve(source, binding, new AnimationCurve(new Keyframe(0, 0, 0, 100), middle, new Keyframe(1, 100, 100, 0)));
                    motion = RimeEmoQueueImport.Create(project, source);
                    var track = motion.tracks.Single(t => t.part == FacePart.Mouth);
                    new RimeEmoTimeline.BlockDrag(track, 0, RimeEmoTimeline.BlockEdge.Start).Apply(.75f);
                    output = RimeEmoCompiler.Compile(motion);
                    var original = AnimationUtility.GetEditorCurve(source, binding);
                    var actual = AnimationUtility.GetEditorCurve(output, binding);
                    for (int i = 0; i <= 50; i++)
                        Check(Mathf.Abs(actual.Evaluate(.75f * i / 50) - original.Evaluate(.75f)) < .002f, "animated first cue trim interpolated through leading hold");
                    for (int i = 0; i <= 50; i++)
                    {
                        float time = .75f + .25f * i / 50;
                        Check(Mathf.Abs(actual.Evaluate(time) - original.Evaluate(time)) < .002f, "leading hold changed surviving source motion");
                    }
                }
                finally
                {
                    if (output) UnityEngine.Object.DestroyImmediate(output);
                    if (motion) { Undo.ClearUndo(motion); UnityEngine.Object.DestroyImmediate(motion); }
                    UnityEngine.Object.DestroyImmediate(source);
                }
            }
        }
        private static void CheckUI(RimeEmoProject project, AnimationClip source, EditorCurveBinding binding)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(RimeEmoWindow); var window = ScriptableObject.CreateInstance<RimeEmoWindow>();
            try
            {
                type.GetField("project", flags).SetValue(window, project); window.Show(); window.CreateGUI();
                type.GetMethod("OpenImportedClip", flags).Invoke(window, new object[] { source });
                type.GetMethod("RefreshToolkit", flags).Invoke(window, null);
                var root = window.rootVisualElement; var timeline = root.Q<RimeEmoTimelineElement>();
                Check(!window.hasUnsavedChanges && !EditorUtility.IsDirty(timeline.Motion), "opening an existing animation requested saving");
                type.GetMethod("Changed", flags).Invoke(window, null);
                Check(!window.hasUnsavedChanges && (bool)type.GetMethod("FinishDraft", flags).Invoke(window, null), "refreshing an untouched animation requested saving");
                var initial = timeline.Motion;
                type.GetMethod("EditMotion", flags).Invoke(window, new object[] { (Action)(() => initial.loop = initial.loop) });
                Check(!window.hasUnsavedChanges, "unchanged edit requested saving");
                window.position = new Rect(0, 0, 1000, 760);
                root.panel.visualTree.style.width = 1000; root.panel.visualTree.style.height = 760;
                root.style.width = 1000; root.style.height = 760;
                root.panel.GetType().GetMethod("ValidateLayout", flags | System.Reflection.BindingFlags.Public).Invoke(root.panel, null);
                Check(root.Q<VisualElement>("block-editor").style.display.value == DisplayStyle.Flex && root.Q<VisualElement>("imported-curves").style.display.value == DisplayStyle.None, "import did not open part queues");
                var editing = timeline.Motion; int mouth = editing.tracks.FindIndex(t => t.part == FacePart.Mouth), eye = editing.tracks.FindIndex(t => t.part == FacePart.LeftEye);
                timeline.SelectedStates.Add(new Vector2Int(mouth, 0)); timeline.SelectedStates.Add(new Vector2Int(eye, 0));
                root.Q<FloatField>("time").value = .37f;
                Check(!window.hasUnsavedChanges, "selection or seeking requested saving");
                Check(root.Q<Button>("split-queue").enabledSelf, "split button not enabled for a selected cue");
                timeline.Focus();
                using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.S })) { e.target = timeline; timeline.SendEvent(e); }
                Check(editing.tracks[mouth].states.Count == 2 && editing.tracks[eye].states.Count == 2, "S did not split selected parts");
                Check(window.hasUnsavedChanges, "real queue edit did not request saving");
                Check(Mathf.Approximately(timeline.Time, .37f), "split moved the playhead");
                var result = RimeEmoCompiler.Compile(editing); CheckCurveEqual(source, result, binding, "UI split"); UnityEngine.Object.DestroyImmediate(result);
                type.GetMethod("RefreshToolkit", flags).Invoke(window, null);
                type.GetMethod("SwitchMode", flags).Invoke(window, new object[] { 0 });
                type.GetMethod("RefreshToolkit", flags).Invoke(window, null);
                type.GetMethod("SwitchMode", flags).Invoke(window, new object[] { 1 });
                type.GetMethod("RefreshToolkit", flags).Invoke(window, null);
                Check(timeline.Motion == editing && window.hasUnsavedChanges && Mathf.Approximately(timeline.Time, .37f), "mode switch discarded queue edits");
                CheckEditorKeys(window, timeline, editing, mouth);
            }
            finally { window.DiscardChanges(); window.Close(); }
        }
        // Video-editor keys on the focused timeline.
        static void CheckEditorKeys(RimeEmoWindow window, RimeEmoTimelineElement timeline, RimeEmoMotion editing, int mouth)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(RimeEmoWindow);
            void Key(KeyCode code, EventModifiers modifiers = EventModifiers.None)
            {
                timeline.Focus();
                using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = code, modifiers = modifiers })) { e.target = timeline; timeline.SendEvent(e); }
            }
            float Time() => (float)type.GetField("time", flags).GetValue(window);
            Key(KeyCode.RightArrow);
            Check(Mathf.Abs(timeline.Time - (.37f + 1f / 60)) < .0001f && Mathf.Abs(Time() - timeline.Time) < .0001f, "→ did not step the playhead by a frame");
            Key(KeyCode.LeftArrow, EventModifiers.Shift);
            Check(Mathf.Abs(timeline.Time - (.37f + 1f / 60 - 10f / 60)) < .0001f, "Shift+← did not step 10 frames");
            Key(KeyCode.Home);
            Check(timeline.Time == 0, "Home did not go to the start");
            Key(KeyCode.Space);
            Check((bool)type.GetField("playing", flags).GetValue(window), "Space did not start playback");
            Key(KeyCode.Space);
            Check(!(bool)type.GetField("playing", flags).GetValue(window), "Space did not pause playback");
            int total = editing.tracks.Sum(t => t.states.Count);
            Key(KeyCode.A, EventModifiers.Control);
            Check(timeline.SelectedStates.Count == total, "Ctrl+A did not select every cue");
            Key(KeyCode.Escape);
            Check(timeline.SelectedStates.Count == 0, "Esc did not clear the selection");
            Key(KeyCode.N);
            Check(!timeline.CueSnap && !window.rootVisualElement.Q<Toggle>("magnet").value, "N did not turn snapping off");
            Key(KeyCode.N);
            Check(timeline.CueSnap, "N did not turn snapping back on");
            // Copy a cue and paste it at the playhead on its track.
            timeline.SelectedStates.Add(new Vector2Int(mouth, 0)); timeline.ActiveTrack = mouth;
            type.GetField("activeTrack", flags).SetValue(window, mouth);
            Key(KeyCode.C, EventModifiers.Control);
            int before = editing.tracks[mouth].states.Count;
            window.rootVisualElement.Q<FloatField>("time").value = .9f;
            Key(KeyCode.V, EventModifiers.Control);
            var times = RimeEmoTimeline.Times(editing.tracks[mouth]);
            Check(editing.tracks[mouth].states.Count == before + 1 && times.Any(t => Mathf.Abs(t - .9f) < .0001f), "Ctrl+V did not paste the cue at the playhead");
            int pasted = Array.FindIndex(times, t => Mathf.Abs(t - .9f) < .0001f);
            Check(timeline.SelectedStates.Count == 1 && timeline.SelectedStates.Contains(new Vector2Int(mouth, pasted)), "the pasted cue was not selected");
            // Alt+→ moves the selected cue by a frame.
            Key(KeyCode.RightArrow, EventModifiers.Alt);
            var moved = RimeEmoTimeline.Times(editing.tracks[mouth]);
            Check(moved.Any(t => Mathf.Abs(t - (.9f + 1f / 60)) < .0001f) && !moved.Any(t => Mathf.Abs(t - .9f) < .0001f), "Alt+→ did not move the cue by a frame");
        }
        static void CheckCurveEqual(AnimationClip a, AnimationClip b, EditorCurveBinding binding, string label)
        { var x = AnimationUtility.GetEditorCurve(a, binding); var y = AnimationUtility.GetEditorCurve(b, binding); Check(x != null && y != null, label + " curve missing"); for (int i = 0; i < 9; i++) { float t = i / 8f; Check(Mathf.Abs(x.Evaluate(t) - y.Evaluate(t)) < .0001f, label + " value mismatch"); } }
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("Queue: " + message); }
    }
}
