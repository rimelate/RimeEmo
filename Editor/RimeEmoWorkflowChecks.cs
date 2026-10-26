using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal static class RimeEmoWorkflowChecks
    {
        internal static void Run()
        {
            string root = "Assets/RimeEmoWorkflowChecks-" + Guid.NewGuid().ToString("N");
            var from = new AnimationClip(); var to = new AnimationClip();
            RimeEmoMotion draft = null; RimeEmoMotion generated = null;
            RimeEmoWindow window = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", root.Substring(7));
                var project = ScriptableObject.CreateInstance<RimeEmoProject>();
                var mouth = new ShapeEntry { path = "Face", shape = "Smile", part = FacePart.Mouth, defaultValue = 10 };
                var eye = new ShapeEntry { path = "Face", shape = "Blink", part = FacePart.LeftEye, defaultValue = 17 };
                project.shapes.Add(mouth); project.shapes.Add(eye);
                project.faceMeshSelected = true; project.faceMeshPath = "Face";
                AssetDatabase.CreateAsset(project, root + "/Project.asset");
                RimeEmoAssets.Folder(root + "/Animations");
                RimeEmoAssets.Folder(root + "/Motions");
                AnimationUtility.SetEditorCurve(from, RimeEmoClips.Binding(mouth), AnimationCurve.Constant(0, 1, 0));
                var unrelated = EditorCurveBinding.FloatCurve("Other", typeof(SkinnedMeshRenderer), "blendShape.NotFace");
                AnimationUtility.SetEditorCurve(from, unrelated, AnimationCurve.Constant(0, 1, 100));
                AnimationUtility.SetEditorCurve(to, RimeEmoClips.Binding(mouth), AnimationCurve.Constant(0, 1, 100));
                Check(RimeEmoClips.HasPart(project, from, FacePart.Mouth), "mouth binding was not detected");
                Check(!RimeEmoClips.HasPart(project, from, FacePart.LeftEye), "phantom eye part detected");
                generated = RimeEmoAssets.NewMotion(project, from);
                Check(generated.tracks.Single(t => t.part == FacePart.Mouth).states.Count == 1, "NewMotion omitted mouth cue");
                Check(generated.tracks.Where(t => t.part != FacePart.Mouth).All(t => t.states.Count == 0), "NewMotion created phantom cues");
                project.motions.Remove(generated);
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(generated)); generated = null;
                draft = RimeEmoDrafts.Create(project);
                var track = draft.tracks.Find(t => t.part == FacePart.Mouth);
                track.states.Add(new MotionState { clip = from, hold = 0.5f, transition = 0.5f, interpolation = Interpolation.Linear });
                track.states.Add(new MotionState { clip = to, hold = 0.5f });
                // Use an unsaved draft: extraction must include edits absent from any saved output.
                foreach (var interpolation in new[] { Interpolation.Linear, Interpolation.EaseInOut, Interpolation.Step })
                {
                    track.states[0].interpolation = interpolation;
                    var result = RimeEmoAssets.CaptureFrame(draft, 0.75f);
                    Check(Mathf.Approximately(RimeEmoClips.Value(result, mouth), interpolation == Interpolation.Step ? 0 : 50), "interpolated frame was not sampled");
                    Check(Mathf.Approximately(RimeEmoClips.Value(result, eye), 17), "unassigned part lost Default Face");
                    foreach (var b in AnimationUtility.GetCurveBindings(result))
                    {
                        var c = AnimationUtility.GetEditorCurve(result, b);
                        Check(RimeEmoClips.IsConstant(c, c.Evaluate(0)), "extraction retained temporal curves");
                    }
                    Check(!AnimationUtility.GetAnimationClipSettings(result).loopTime, "snapshot unexpectedly loops");
                    Check(RimeEmoAssets.Owns(project, result), "snapshot was not registered as editable material");
                }
                Check(!draft.animationClip && project.motions.Count == 0, "capture saved or replaced the dynamic draft");
                window = ScriptableObject.CreateInstance<RimeEmoWindow>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(RimeEmoWindow);
                type.GetField("project", flags).SetValue(window, project);
                type.GetField("motion", flags).SetValue(window, draft);
                type.GetField("mode", flags).SetValue(window, 1);
                type.GetField("time", flags).SetValue(window, 0.75f);
                EditorUtility.SetDirty(draft);
                type.GetMethod("Changed", flags).Invoke(window, null);
                window.CreateGUI();
                type.GetMethod("CaptureCurrentFrame", flags).Invoke(window, null);
                Check((int)type.GetField("mode", flags).GetValue(window) == 0, "capture did not open static editing");
                Check(window.hasUnsavedChanges && (RimeEmoMotion)type.GetField("motion", flags).GetValue(window) == draft, "capture discarded pending dynamic edits");
                type.GetField("time", flags).SetValue(window, 0.01f);
                type.GetMethod("SwitchMode", flags).Invoke(window, new object[] { 1 });
                Check(Mathf.Approximately((float)type.GetField("time", flags).GetValue(window), 0.75f), "static editing overwrote dynamic cursor");
                type.GetMethod("RefreshToolkit", flags).Invoke(window, null);
                Check(window.rootVisualElement.Q<Button>("export") == null, "duplicate dynamic save button returned");
                Check(window.rootVisualElement.Q<Button>("material-default").style.display.value == DisplayStyle.None, "static creation action is shown in dynamic mode");
            }
            finally
            {
                if (window) { window.DiscardChanges(); UnityEngine.Object.DestroyImmediate(window); }
                if (draft) UnityEngine.Object.DestroyImmediate(draft);
                UnityEngine.Object.DestroyImmediate(from); UnityEngine.Object.DestroyImmediate(to);
                AssetDatabase.DeleteAsset(root);
            }
        }
        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Workflow: " + message);
        }
    }
}
