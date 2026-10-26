using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // User Shape Key groups: wildcard matching, hand-picked overrides, the asset and the editor filter.
    internal static class RimeEmoShapeGroupChecks
    {
        internal static void Run()
        {
            TestMatching();
            TestMembership();
            TestPatterns();
            TestAssetAndFilter();
            TestSharing();
        }

        static void TestMatching()
        {
            Check(RimeEmoShapeGroups.Matches("Eyelid_*", "eyelid_close_R"), "prefix pattern ignored case");
            Check(RimeEmoShapeGroups.Matches("*Tare*", "Eyelid_Tare_L"), "contains pattern missed");
            Check(RimeEmoShapeGroups.Matches("*_R", "Eyelid_R") && !RimeEmoShapeGroups.Matches("*_R", "Eyelid_L"), "suffix pattern wrong");
            Check(RimeEmoShapeGroups.Matches("a.b*", "a.bc") && !RimeEmoShapeGroups.Matches("a.b*", "axbc"), "pattern did not escape regex characters");
            Check(!RimeEmoShapeGroups.Matches("", "Smile"), "empty pattern matched");
        }

        static void TestMembership()
        {
            var set = ScriptableObject.CreateInstance<RimeEmoShapeGroupSet>();
            try
            {
                var group = new ShapeGroup { name = "Tare" }; group.patterns.Add("*Tare*"); set.groups.Add(group);
                RimeEmoShapeGroups.Remove(set, group, "Tare_L");
                Check(!RimeEmoShapeGroups.Contains(group, "Tare_L") && group.removed.Contains("Tare_L"), "removing a pattern match was not recorded");
                RimeEmoShapeGroups.Add(set, group, "Tare_L");
                Check(RimeEmoShapeGroups.Contains(group, "Tare_L") && !group.removed.Contains("Tare_L") && !group.added.Contains("Tare_L"), "re-adding a pattern match left overrides");
                RimeEmoShapeGroups.Add(set, group, "Smile");
                Check(RimeEmoShapeGroups.Contains(group, "Smile") && group.added.Contains("Smile"), "hand-picked shape was not added");
                RimeEmoShapeGroups.Remove(set, group, "Smile");
                Check(!RimeEmoShapeGroups.Contains(group, "Smile") && !group.added.Contains("Smile") && !group.removed.Contains("Smile"), "removing a hand-picked shape left an override");
                RimeEmoShapeGroups.Rename(set, group, "Tare");
                Check(group.name == "Tare", "renaming to the same name changed it");
                set.groups.Add(new ShapeGroup { name = "Other" });
                RimeEmoShapeGroups.Rename(set, set.groups[1], "Tare");
                Check(set.groups[1].name == "Tare 2", "duplicate group name was not made unique");
                RimeEmoShapeGroups.Move(set, 1, -1);
                Check(set.groups[0].name == "Tare 2", "group was not moved");
                RimeEmoShapeGroups.Delete(set, 0);
                Check(set.groups.Count == 1 && set.groups[0] == group, "group was not deleted");
            }
            finally { Undo.ClearUndo(set); UnityEngine.Object.DestroyImmediate(set); }
        }

        static void TestPatterns()
        {
            var set = ScriptableObject.CreateInstance<RimeEmoShapeGroupSet>();
            int raised = 0;
            void Count(RimeEmoShapeGroupSet edited) { if (edited == set) raised++; }
            RimeEmoShapeGroups.Changed += Count;
            try
            {
                var group = new ShapeGroup { name = "Eye" }; set.groups.Add(group);
                RimeEmoShapeGroups.AddPattern(set, group, "");
                RimeEmoShapeGroups.SetPattern(set, group, 0, "  Eye*  ");
                Check(group.patterns.Count == 1 && group.patterns[0] == "Eye*" && RimeEmoShapeGroups.Contains(group, "EyeLook_Up"), "pattern was not added and trimmed");
                RimeEmoShapeGroups.RemovePattern(set, group, 0);
                Check(group.patterns.Count == 0 && !RimeEmoShapeGroups.Contains(group, "EyeLook_Up"), "pattern was not removed");
                group.added.Add("Smile"); group.added.Add("OtherAvatarOnly"); group.removed.Add("GoneToo");
                var mesh = new System.Collections.Generic.HashSet<string> { "Smile", "Blink" };
                Check(RimeEmoShapeGroups.MissingNames(group, mesh).Count == 2, "missing names were not found");
                RimeEmoShapeGroups.PruneMissing(set, group, mesh);
                Check(group.added.Count == 1 && group.added[0] == "Smile" && group.removed.Count == 0, "missing names were not pruned");
                RimeEmoShapeGroups.SetInfo(set, " Base ", " Author ");
                Check(set.baseModel == "Base" && set.author == "Author", "set info was not saved");
                Check(raised == 5, "edits did not notify listeners: " + raised);
            }
            finally { RimeEmoShapeGroups.Changed -= Count; Undo.ClearUndo(set); UnityEngine.Object.DestroyImmediate(set); }
        }

        static void TestSharing()
        {
            string folder = "Assets/RimeEmoGroupShare-" + Guid.NewGuid().ToString("N");
            RimeEmoClassificationWindow window = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                AssetDatabase.CreateFolder(folder, "A"); AssetDatabase.CreateFolder(folder, "B");
                RimeEmoProject Make(string sub)
                {
                    var p = ScriptableObject.CreateInstance<RimeEmoProject>();
                    foreach (string name in new[] { "Smile", "Blink", "Blush" }) p.shapes.Add(new ShapeEntry { path = sub == "A" ? "Body" : "Face/Body", shape = name });
                    AssetDatabase.CreateAsset(p, folder + "/" + sub + "/Project.asset");
                    return p;
                }
                var a = Make("A"); var b = Make("B");
                RimeEmoShapeGroups.Create(a, "Bl", "Bl*", null);
                RimeEmoShapeGroups.Add(a.shapeGroups, a.shapeGroups.groups[0], "Smile");
                var names = new System.Collections.Generic.HashSet<string> { "Smile", "Blink", "Blush" };
                Check(Mathf.Approximately(RimeEmoShapeGroups.Score(a.shapeGroups, names), 1), "a fitting set did not score fully");
                Check(RimeEmoShapeGroups.Score(a.shapeGroups, new System.Collections.Generic.HashSet<string> { "Other" }) == 0, "an unrelated mesh scored");
                Check(RimeEmoShapeGroups.FindMatch(b, out float score) && score >= 0.6f, "no set was suggested for a matching avatar");
                b.shapeGroups = a.shapeGroups; EditorUtility.SetDirty(b); RimeEmoShapeGroups.InvalidateUsers();
                Check(RimeEmoShapeGroups.Users(a.shapeGroups).Count == 2, "sharing avatars were not counted");
                var copy = RimeEmoShapeGroups.Duplicate(b, a.shapeGroups);
                Check(copy && copy != a.shapeGroups && AssetDatabase.GetAssetPath(copy) == folder + "/B/ShapeGroups.asset"
                    && copy.groups.Count == 1 && copy.groups[0].patterns[0] == "Bl*", "duplicate was not a separate copy next to the settings");

                RimeEmoClassificationWindow.OpenGroups(a, 0);
                window = EditorWindow.GetWindow<RimeEmoClassificationWindow>();
                var root = window.rootVisualElement;
                Check(root.Q<ListView>("group-list").itemsSource.Count == 1, "group list did not bind");
                Check(root.Q<ListView>("group-shapes").itemsSource.Count == 3, "group checklist did not list the face shapes");
                root.Q<Toggle>("group-members-only").value = true;
                Check(root.Q<ListView>("group-shapes").itemsSource.Count == 3, "members filter dropped members");
                RimeEmoShapeGroups.Remove(a.shapeGroups, a.shapeGroups.groups[0], "Blush");
                Check(root.Q<ListView>("group-shapes").itemsSource.Count == 2, "editor did not follow a group edit");
                Check(root.Q<Label>("group-summary").text.Contains("2"), "summary did not count members");
            }
            finally
            {
                if (window) window.Close();
                RimeEmoShapeGroups.InvalidateUsers();
                AssetDatabase.DeleteAsset(folder);
            }
        }

        static void TestAssetAndFilter()
        {
            string folder = "Assets/RimeEmoGroupCheck-" + Guid.NewGuid().ToString("N");
            var project = ScriptableObject.CreateInstance<RimeEmoProject>();
            var clip = new AnimationClip();
            RimeEmoWindow window = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                AssetDatabase.CreateFolder(folder, "Animations");
                AssetDatabase.CreateAsset(project, folder + "/Project.asset");
                AssetDatabase.CreateAsset(clip, folder + "/Animations/Test.anim");
                foreach (string name in new[] { "Smile", "Blink", "Blush" }) project.shapes.Add(new ShapeEntry { path = "Body", shape = name });
                project.clips.Add(new ClipEntry { clip = clip });
                Check(RimeEmoShapeGroups.Groups(project).Count == 0 && !project.shapeGroups, "a project without groups created an asset");
                int index = RimeEmoShapeGroups.Create(project, "Bl", "Bl*", null);
                Check(project.shapeGroups && AssetDatabase.GetAssetPath(project.shapeGroups) == folder + "/ShapeGroups.asset", "group asset was not created next to the settings");
                Check(index == 0 && project.shapeGroups.groups[0].patterns[0] == "Bl*", "group was not created from the pattern");

                window = ScriptableObject.CreateInstance<RimeEmoWindow>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(RimeEmoWindow);
                type.GetField("project", flags).SetValue(window, project);
                type.GetField("selected", flags).SetValue(window, clip);
                window.Show(); window.CreateGUI();
                var root = window.rootVisualElement;
                var list = root.Q<ListView>("shape-list");
                Check(list.itemsSource.Count == 3, "all shapes were not listed");
                Check(root.Q<Button>("shape-group-tab-0") != null && root.Q<Button>("shape-group-add") != null, "group chips were not built");
                type.GetMethod("SelectShapeGroup", flags).Invoke(window, new object[] { 0 });
                Check(list.itemsSource.Count == 2 && root.Q<Button>("shape-group-tab-0").ClassListContains("is-active")
                    && !root.Q<Button>("shape-part-tab-0").ClassListContains("is-active"), "group chip did not filter");
                type.GetMethod("SelectShapePart", flags).Invoke(window, new object[] { 0 });
                Check(list.itemsSource.Count == 3 && (int)type.GetField("shapeGroup", flags).GetValue(window) == -1, "part chip did not clear the group");
                RimeEmoShapeGroups.Delete(project.shapeGroups, 0);
                type.GetField("shapeGroup", flags).SetValue(window, 0);
                type.GetMethod("ShapeGroupsEdited", flags).Invoke(window, null);
                Check(list.itemsSource.Count == 3 && root.Q<Button>("shape-group-tab-0") == null, "a deleted group still filtered");
            }
            finally
            {
                if (window) { window.DiscardChanges(); window.Close(); }
                if (project && project.shapeGroups) Undo.ClearUndo(project.shapeGroups);
                Undo.ClearUndo(clip); Undo.ClearUndo(project);
                AssetDatabase.DeleteAsset(folder);
                if (clip) UnityEngine.Object.DestroyImmediate(clip);
                if (project) UnityEngine.Object.DestroyImmediate(project);
            }
        }

        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Shape groups: " + message); }
    }
}
