using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal static class RimeEmoKeyframeUIChecks
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags).Invoke(obj, args);
        static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Flags).GetValue(obj);
        static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("Keyframe UI: " + message); }
        static void Near(float a, float b, string message) => Check(Mathf.Abs(a - b) < .001f, message + " (" + a + " / " + b + ")");
        static void Layout(RimeEmoWindow window)
        {
            var root = window.rootVisualElement;
            root.panel.visualTree.style.width = 1000; root.panel.visualTree.style.height = 760;
            root.style.width = 1000; root.style.height = 760;
            root.panel.GetType().GetMethod("ValidateLayout", Flags | BindingFlags.Public).Invoke(root.panel, null);
        }
        internal static void Run()
        {
            string folder = "Assets/RimeEmoKeyframeUIChecks-" + Guid.NewGuid().ToString("N");
            RimeEmoWindow window = null; AnimationClip material = null, compiled = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring(7));
                var project = ScriptableObject.CreateInstance<RimeEmoProject>(); AssetDatabase.CreateAsset(project, folder + "/Project.asset");
                var shape = new ShapeEntry { path = "Face", shape = "Smile", part = FacePart.Mouth };
                var blink = new ShapeEntry { path = "Face", shape = "Blink", part = FacePart.LeftEye };
                project.shapes.Add(shape); project.shapes.Add(blink); project.faceMeshSelected = true; project.faceMeshPath = "Face";
                var binding = RimeEmoClips.Binding(shape);
                material = new AnimationClip(); AnimationUtility.SetEditorCurve(material, binding, AnimationCurve.Constant(0, 1, 25));
                project.clips.Add(new ClipEntry { clip = material });
                window = ScriptableObject.CreateInstance<RimeEmoWindow>(); Set(window, "project", project); Set(window, "mode", 1);
                window.Show(); window.position = new Rect(0, 0, 1000, 760); window.CreateGUI();
                var root = window.rootVisualElement; var queue = Get<RimeEmoMotion>(window, "motion");
                queue.tracks.First(t => t.part == FacePart.Mouth).states.Add(new MotionState { clip = material, hold = 1 });
                Set(window, "time", .4f); EditorUtility.SetDirty(queue);
                Call(window, "SetAnimationEditor", 1); Call(window, "RefreshToolkit"); Layout(window);
                var raw = Get<AnimationClip>(window, "importedDraft");
                Check(raw && raw != material && ReferenceEquals(queue, Get<RimeEmoMotion>(window, "motion")), "switch lost queue draft");
                Check(root.Q("block-editor").resolvedStyle.display == DisplayStyle.None && root.Q("imported-curves").resolvedStyle.display == DisplayStyle.Flex, "wrong editor shown");
                Check(root.Q("kf-tracks").worldBound.height > 70 && root.Q("kf-inspector").worldBound.width > 150, "key editor has no usable space");
                Check(root.Q("motion-controls").worldBound.yMax <= root.worldBound.yMax + 1, "key editor exceeds minimum window height");
                root.Q<DropdownField>("kf-add-shape").value = "Smile";
                root.Q<FloatField>("time").value = .2f;
                root.Q<FloatField>("kf-value").value = 70;
                Check(AnimationUtility.GetEditorCurve(raw, binding) == null, "value preview wrote a key before Add");
                Undo.IncrementCurrentGroup(); Call(window, "AddCurrentKeyframe");
                Near(AnimationUtility.GetEditorCurve(raw, binding).Evaluate(.2f), 70, "add did not use preview value");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Call(window, "RefreshToolkit");
                Check(AnimationUtility.GetEditorCurve(raw, binding) == null, "key add undo failed");
                root.Q<FloatField>("kf-value").value = 70; Call(window, "AddCurrentKeyframe");
                root.Q<FloatField>("time").value = .8f; root.Q<Toggle>("kf-auto-key").value = true;
                Layout(window); var slider = root.Q<Slider>("kf-value-slider");
                using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = slider.worldBound.center })) { e.target = slider; slider.SendEvent(e); }
                slider.value = 20; slider.value = 40;
                Check(AnimationUtility.GetEditorCurve(raw, binding).length == 1, "auto-key drag wrote intermediate key");
                using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = slider.worldBound.center })) { e.target = slider; slider.SendEvent(e); }
                Check(AnimationUtility.GetEditorCurve(raw, binding).length == 2, "auto-key release failed");
                Near(AnimationUtility.GetEditorCurve(raw, binding).Evaluate(.8f), 40, "auto-key release value");
                string rawJson = EditorJsonUtility.ToJson(raw);
                Call(window, "SetAnimationEditor", 0); Call(window, "RefreshToolkit");
                Near(Get<float>(window, "time"), .4f, "queue cursor not restored");
                Check(ReferenceEquals(queue, Get<RimeEmoMotion>(window, "motion")), "return to queue replaced draft");
                Call(window, "SetAnimationEditor", 1); Call(window, "RefreshToolkit");
                Check(Get<AnimationClip>(window, "importedDraft") == raw && EditorJsonUtility.ToJson(raw) == rawJson, "return to keyframe lost draft");
                Near(Get<float>(window, "time"), .8f, "keyframe cursor not restored");

                var timeline = Get<RimeEmoKeyframeTimeline>(window, "keyframeTimeline");
                timeline.SelectedKeys.Clear(); timeline.SelectedKeys.UnionWith(new[] { new Vector2Int(0, 0), new Vector2Int(0, 1) });
                Call(window, "CopyKeyframeSelection");
                root.Q<TextField>("kf-search").value = "Blink"; Set(window, "time", 1.2f); Call(window, "PasteKeyframeSelection");
                Check(AnimationUtility.GetEditorCurve(raw, binding).length == 4, "filter discarded copied Shape Keys");
                root.Q<TextField>("kf-search").value = "";
                timeline.SelectedKeys.Clear(); timeline.SelectedKeys.Add(new Vector2Int(0, 0)); timeline.SelectedKeys.Add(new Vector2Int(0, 1));
                Call(window, "MoveKeyframeSelection", .1f); Near(Get<float>(window, "time"), 1.2f, "moving keys changed playhead");
                var moved = AnimationUtility.GetEditorCurve(raw, binding);
                Near(moved[1].time - moved[0].time, .6f, "moving selection changed key spacing");
                Call(window, "DeleteKeyframeSelection"); Check(AnimationUtility.GetEditorCurve(raw, binding).length == 2, "delete selection failed");

                // Save both drafts, then verify that mode changes never overwrite either output.
                var queueOutput = RimeEmoDrafts.SaveTo(queue, folder + "/Queue.anim"); EditorUtility.ClearDirty(queue);
                Check((bool)Call(window, "SaveKeyframeToPath", folder + "/Keys.anim"), "keyframe save failed");
                var saved = Get<AnimationClip>(window, "importedTarget");
                Call(window, "OpenImportedClip", saved); Call(window, "RefreshToolkit");
                Check(Get<bool>(window, "keyframeMode") && Get<AnimationClip>(window, "importedTarget") == saved, "saved output did not reopen in keyframe mode");
                raw = Get<AnimationClip>(window, "importedDraft");
                RimeEmoKeyframes.SetKey(raw, shape, 0, 91, Interpolation.Linear); EditorUtility.SetDirty(raw);
                queue.loop = true; EditorUtility.SetDirty(queue);
                Check((bool)Call(window, "SaveAllAnimationDrafts"), "save all left dirty draft");
                Near(AnimationUtility.GetEditorCurve(saved, binding).Evaluate(0), 91, "inactive keyframe draft not saved");
                Check(AnimationUtility.GetAnimationClipSettings(queueOutput).loopTime, "inactive queue draft not saved");
                string queueJson = EditorJsonUtility.ToJson(queue);
                compiled = RimeEmoCompiler.Compile(queue);
                Call(window, "CopyQueueToKeyframes"); Call(window, "RefreshToolkit");
                var duplicate = Get<AnimationClip>(window, "importedDraft");
                Check(Get<AnimationClip>(window, "importedTarget") == null && EditorUtility.IsDirty(duplicate), "copy reused source output");
                Check(EditorJsonUtility.ToJson(queue) == queueJson, "copy changed source queue");
                for (int i = 0; i <= 60; i++) Near(AnimationUtility.GetEditorCurve(duplicate, binding).Evaluate(i / 60f), AnimationUtility.GetEditorCurve(compiled, binding).Evaluate(i / 60f), "copy changed animation");
                Near(AnimationUtility.GetEditorCurve(saved, binding).Evaluate(0), 91, "copy changed previous keyframe asset");
                TestTimelineDrag(window);
            }
            finally
            {
                if (window) { window.DiscardChanges(); window.Close(); }
                if (material) UnityEngine.Object.DestroyImmediate(material);
                if (compiled) UnityEngine.Object.DestroyImmediate(compiled);
                AssetDatabase.DeleteAsset(folder);
            }
        }
        static void TestTimelineDrag(RimeEmoWindow window)
        {
            var raw = Get<AnimationClip>(window, "importedDraft"); var project = Get<RimeEmoProject>(window, "project");
            var binding = RimeEmoClips.Binding(project.shapes[0]);
            AnimationUtility.SetEditorCurve(raw, binding, AnimationCurve.Linear(.2f, 0, .6f, 100));
            Call(window, "RefreshToolkit"); Layout(window);
            var timeline = Get<RimeEmoKeyframeTimeline>(window, "keyframeTimeline"); timeline.Fit(); Layout(window);
            timeline.SelectedKeys.Add(new Vector2Int(0, 0)); timeline.SelectedKeys.Add(new Vector2Int(0, 1));
            var keys = timeline.Query<Label>(className: "kf-key").ToList();
            var start = keys[0].worldBound.center; var end = start + new Vector2(30, 0);
            float before = AnimationUtility.GetEditorCurve(raw, binding)[0].time;
            using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = start })) { e.target = timeline; timeline.SendEvent(e); }
            Check(timeline.IsInteracting && timeline.SelectedKeys.Count == 2, "drag lost multi-selection");
            using (var e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = end })) { e.target = timeline; timeline.SendEvent(e); }
            Near(AnimationUtility.GetEditorCurve(raw, binding)[0].time, before, "drag wrote before release");
            using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = end })) { e.target = timeline; timeline.SendEvent(e); }
            var after = AnimationUtility.GetEditorCurve(raw, binding);
            Check(after[0].time > before && !timeline.IsInteracting && timeline.SelectedKeys.Count == 2, "drag failed to commit");
            Near(after[1].time - after[0].time, .4f, "drag changed group spacing");
        }
    }
}
