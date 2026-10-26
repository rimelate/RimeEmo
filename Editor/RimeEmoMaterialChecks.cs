using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal static class RimeEmoMaterialChecks
    {
        internal static void Run()
        {
            TestOtherAvatarClips();
            string root = "Assets/RimeEmoMaterialChecks-" + Guid.NewGuid().ToString("N");
            RimeEmoWindow window = null;
            var source = new AnimationClip(); var unrelated = new AnimationClip(); var dynamic = new AnimationClip();
            try
            {
                AssetDatabase.CreateFolder("Assets", root.Substring(7));
                var project = ScriptableObject.CreateInstance<RimeEmoProject>();
                project.faceMeshSelected = true; project.faceMeshPath = "Body";
                project.shapes.Add(new ShapeEntry { path = "Body", shape = "Smile", part = FacePart.Mouth });
                project.shapes.Add(new ShapeEntry { path = "Body", shape = "Blink", part = FacePart.LeftEye });
                AssetDatabase.CreateAsset(project, root + "/Project.asset");
                var body = RimeEmoClips.Binding(project.shapes[0]);
                var clothing = EditorCurveBinding.FloatCurve("Clothing", typeof(SkinnedMeshRenderer), "blendShape.Wide");
                AnimationUtility.SetEditorCurve(unrelated, clothing, AnimationCurve.Linear(0, 0, 1, 100));
                Check(RimeEmoClipClassification.Classify(project, unrelated) == RimeEmoClipKind.Excluded, "non-Body clip accepted");
                Check(!RimeEmoAssets.Import(project, unrelated) && project.clips.Count == 0 && project.animations.Count == 0, "excluded clip created an asset entry");
                AnimationUtility.SetEditorCurve(source, body, new AnimationCurve(new Keyframe(0, 30)));
                Check(RimeEmoClipClassification.Classify(project, source) == RimeEmoClipKind.Static, "single key not static");
                AnimationUtility.SetEditorCurve(source, body, AnimationCurve.Constant(0, 3, 30));
                AnimationUtility.SetEditorCurve(source, clothing, AnimationCurve.Linear(0, 0, 3, 100));
                AnimationUtility.SetAnimationEvents(source, new[] { new AnimationEvent { time = 1, functionName = "Ping" } });
                Check(RimeEmoClipClassification.Classify(project, source) == RimeEmoClipKind.Static, "other mesh or events affected classification");
                source.name = "Static fixture"; var stat = RimeEmoAssets.Import(project, source);
                AnimationUtility.SetEditorCurve(dynamic, body, new AnimationCurve(new Keyframe(0, 20, 0, 20), new Keyframe(1, 20, -20, 0)));
                Check(RimeEmoClipClassification.Classify(project, dynamic) == RimeEmoClipKind.Dynamic, "equal endpoints hid interpolation changes");
                dynamic.name = "Dynamic fixture"; var dyn = RimeEmoAssets.Import(project, dynamic);
                Check(project.clips.Any(c => c.clip == stat) && project.animations.Any(c => c.clip == dyn), "wrong import registry");
                // An old ignored registration must also stay out of the visible shelf.
                project.clips.Add(new ClipEntry { clip = unrelated });
                var type = typeof(RimeEmoWindow); const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                window = ScriptableObject.CreateInstance<RimeEmoWindow>();
                type.GetField("project", flags).SetValue(window, project); type.GetField("selected", flags).SetValue(window, stat);
                window.Show(); window.CreateGUI(); var ui = window.rootVisualElement;
                window.position = new Rect(0, 0, 1000, 760); ui.style.width = 1000; ui.style.height = 760;
                ui.panel.visualTree.style.width = 1000; ui.panel.visualTree.style.height = 760;
                ui.panel.GetType().GetMethod("ValidateLayout", flags | BindingFlags.Public).Invoke(ui.panel, null);
                var list = ui.Q<ListView>("material-list");
                // The expression page lists the Default Face row first, then the clips.
                var shelf = list.itemsSource.Cast<ClipEntry>().ToList();
                Check(shelf.Count == 3 && !shelf[0].clip && shelf.Skip(1).All(c => c.clip),
                    "shelf did not show Default Face and both kinds, or retained excluded clips (" + shelf.Count + " rows)");
                var labels = ui.Query<Label>("clip-kind").ToList();
                Check(labels.Any(l => l.text == RimeEmoLocalization.T("静的")) && labels.Any(l => l.text == RimeEmoLocalization.T("動的")), "kind badges missing");
                Check(ui.Q<Slider>("static-time").style.display.value == DisplayStyle.None, "unrelated motion exposed a static time slider");
                Check(ui.Q<DropdownField>("shape-part") == null && ui.Q<VisualElement>("shape-part-tabs").childCount == 6, "part dropdown was not replaced");
                type.GetMethod("SelectShapePart", flags).Invoke(window, new object[] { 4 });
                var shapeList = ui.Q<ListView>("shape-list");
                bool Included(object row) => (bool)row.GetType().GetField("included", flags).GetValue(row);
                Check(ui.Q<Button>("shape-part-tab-4").ClassListContains("is-active") && shapeList.itemsSource.Count == 1 && Included(shapeList.itemsSource[0]), "mouth tab did not filter");
                type.GetMethod("SelectShapePart", flags).Invoke(window, new object[] { 1 });
                Check(shapeList.itemsSource.Count == 1 && !Included(shapeList.itemsSource[0]), "eye tab did not filter");
                type.GetMethod("OpenImportedClip", flags).Invoke(window, new object[] { dyn });
                type.GetMethod("RefreshToolkit", flags).Invoke(window, null);
                Check((int)type.GetField("mode", flags).GetValue(window) == 1 && list.itemsSource.Count == 2, "dynamic editor did not keep mixed shelf");
                Check(!window.hasUnsavedChanges, "opening a material requested saving");
                var queueDraft = (RimeEmoMotion)type.GetField("motion", flags).GetValue(window);
                type.GetMethod("EditMotion", flags).Invoke(window, new object[] { (Action)(() => queueDraft.loop = !queueDraft.loop) });
                // Reclassify an old dynamic registration without modifying its original asset.
                AnimationUtility.SetEditorCurve(dyn, body, AnimationCurve.Constant(0, 1, 20));
                type.GetMethod("OpenStaticMaterial", flags).Invoke(window, new object[] { dyn });
                var edited = (AnimationClip)type.GetField("selected", flags).GetValue(window);
                Check(edited != dyn && RimeEmoAssets.Owns(project, edited) && window.hasUnsavedChanges, "static conversion touched motion output or discarded draft");
            }
            finally
            {
                if (window) { window.DiscardChanges(); window.Close(); }
                UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(unrelated); UnityEngine.Object.DestroyImmediate(dynamic);
                AssetDatabase.DeleteAsset(root);
            }
        }
        // MAYO and Manuka in one project: both faces are "Body", but their Shape Key names differ.
        private static void TestOtherAvatarClips()
        {
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var mine = new AnimationClip(); var other = new AnimationClip(); var sameBase = new AnimationClip();
            try
            {
                project.faceMeshSelected = true; project.faceMeshPath = "Body";
                foreach (string name in new[] { "Smile", "Blink", "Angry", "Sad", "vrc.v_aa" }) project.shapes.Add(new ShapeEntry { path = "Body", shape = name });
                EditorCurveBinding Key(string name) => EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + name);
                AnimationUtility.SetEditorCurve(mine, Key("Smile"), AnimationCurve.Constant(0, 1, 100));
                foreach (string name in new[] { "Manuka_Smile", "Manuka_Eye", "vrc.v_aa" }) AnimationUtility.SetEditorCurve(other, Key(name), AnimationCurve.Constant(0, 1, 100));
                // An edited avatar of the same base may miss a key and still belongs (5 of 6 keys match).
                foreach (string name in new[] { "Smile", "Blink", "Angry", "Sad", "vrc.v_aa", "RemovedInEdit" }) AnimationUtility.SetEditorCurve(sameBase, Key(name), AnimationCurve.Constant(0, 1, 50));
                Check(RimeEmoClipClassification.CanImport(project, mine), "this avatar's expression was rejected");
                Check(!RimeEmoClipClassification.CanImport(project, other) && RimeEmoClipClassification.Rejection(project, other) != null, "another avatar's expression was accepted");
                Check(RimeEmoClipClassification.CanImport(project, sameBase), "an expression of the same base with one unknown key was rejected");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mine); UnityEngine.Object.DestroyImmediate(other); UnityEngine.Object.DestroyImmediate(sameBase);
                UnityEngine.Object.DestroyImmediate(project);
            }
        }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Materials: " + message); }
    }
}
