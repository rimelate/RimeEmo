using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    // Membership and editing of user Shape Key groups. Every edit is undoable on the group asset.
    internal static class RimeEmoShapeGroups
    {
        private static readonly Dictionary<string, Regex> Patterns = new Dictionary<string, Regex>();

        internal static IReadOnlyList<ShapeGroup> Groups(RimeEmoProject project)
            => project && project.shapeGroups ? project.shapeGroups.groups : (IReadOnlyList<ShapeGroup>)Array.Empty<ShapeGroup>();

        internal static bool Matches(string pattern, string name)
        {
            if (string.IsNullOrEmpty(pattern) || name == null) return false;
            if (!Patterns.TryGetValue(pattern, out var regex))
                Patterns[pattern] = regex = new Regex("^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return regex.IsMatch(name);
        }

        // (a pattern matches or hand-picked) and not excluded.
        internal static bool Contains(ShapeGroup group, string name)
            => group != null && !group.removed.Contains(name) && (group.added.Contains(name) || group.patterns.Any(p => Matches(p, name)));

        // The asset is created with the first group, next to the avatar settings.
        internal static RimeEmoShapeGroupSet Ensure(RimeEmoProject project)
        {
            if (project.shapeGroups) return project.shapeGroups;
            var set = ScriptableObject.CreateInstance<RimeEmoShapeGroupSet>();
            AssetDatabase.CreateAsset(set, AssetDatabase.GenerateUniqueAssetPath(RimeEmoAssets.Root(project) + "/ShapeGroups.asset"));
            project.shapeGroups = set; EditorUtility.SetDirty(project);
            return set;
        }

        internal static int Create(RimeEmoProject project, string name, string pattern, string shape)
        {
            var set = Ensure(project);
            Edit(set, "RimeEmo: Create Shape Group", () =>
            {
                var group = new ShapeGroup { name = UniqueName(set, name) };
                if (!string.IsNullOrEmpty(pattern)) group.patterns.Add(pattern);
                if (!string.IsNullOrEmpty(shape)) group.added.Add(shape);
                set.groups.Add(group);
            });
            return set.groups.Count - 1;
        }

        internal static void Add(RimeEmoShapeGroupSet set, ShapeGroup group, string shape) => Edit(set, "RimeEmo: Add to Shape Group", () =>
        {
            group.removed.Remove(shape);
            if (!Contains(group, shape)) group.added.Add(shape);
        });

        // A name still matched by a pattern is recorded as excluded, so it stays out.
        internal static void Remove(RimeEmoShapeGroupSet set, ShapeGroup group, string shape) => Edit(set, "RimeEmo: Remove from Shape Group", () =>
        {
            group.added.Remove(shape);
            if (Contains(group, shape)) group.removed.Add(shape);
        });

        internal static void Rename(RimeEmoShapeGroupSet set, ShapeGroup group, string name) => Edit(set, "RimeEmo: Rename Shape Group", () =>
        {
            if (group.name != name) group.name = UniqueName(set, name);
        });

        internal static void Move(RimeEmoShapeGroupSet set, int index, int offset) => Edit(set, "RimeEmo: Move Shape Group", () =>
        {
            int next = index + offset;
            if (index < 0 || index >= set.groups.Count || next < 0 || next >= set.groups.Count) return;
            var group = set.groups[index]; set.groups.RemoveAt(index); set.groups.Insert(next, group);
        });

        internal static void Delete(RimeEmoShapeGroupSet set, int index) => Edit(set, "RimeEmo: Delete Shape Group", () =>
        {
            if (index >= 0 && index < set.groups.Count) set.groups.RemoveAt(index);
        });

        private static string UniqueName(RimeEmoShapeGroupSet set, string name)
        {
            string trimmed = string.IsNullOrWhiteSpace(name) ? RimeEmoLocalization.T("新しいグループ") : name.Trim();
            string result = trimmed;
            for (int i = 2; set.groups.Any(g => g.name == result); i++) result = trimmed + " " + i;
            return result;
        }

        private static void Edit(RimeEmoShapeGroupSet set, string label, Action action)
        {
            Undo.RecordObject(set, label);
            action();
            EditorUtility.SetDirty(set);
            Changed?.Invoke(set);
        }

        // Raised after every edit, so the editor window and the expression editor stay in step.
        internal static event Action<RimeEmoShapeGroupSet> Changed;

        internal static void AddPattern(RimeEmoShapeGroupSet set, ShapeGroup group, string pattern) => Edit(set, "RimeEmo: Add Group Pattern", () => group.patterns.Add(pattern ?? ""));

        internal static void SetPattern(RimeEmoShapeGroupSet set, ShapeGroup group, int index, string pattern) => Edit(set, "RimeEmo: Edit Group Pattern", () =>
        {
            if (index >= 0 && index < group.patterns.Count) group.patterns[index] = (pattern ?? "").Trim();
        });

        internal static void RemovePattern(RimeEmoShapeGroupSet set, ShapeGroup group, int index) => Edit(set, "RimeEmo: Remove Group Pattern", () =>
        {
            if (index >= 0 && index < group.patterns.Count) group.patterns.RemoveAt(index);
        });

        internal static void SetInfo(RimeEmoShapeGroupSet set, string baseModel, string author) => Edit(set, "RimeEmo: Edit Shape Group Info", () =>
        {
            set.baseModel = (baseModel ?? "").Trim(); set.author = (author ?? "").Trim();
        });

        // Hand-picked or excluded names that this face mesh does not have.
        internal static List<string> MissingNames(ShapeGroup group, ICollection<string> meshNames)
            => group.added.Concat(group.removed).Where(n => !meshNames.Contains(n)).Distinct().ToList();

        internal static void PruneMissing(RimeEmoShapeGroupSet set, ShapeGroup group, ICollection<string> meshNames) => Edit(set, "RimeEmo: Remove Missing Shape Names", () =>
        {
            group.added.RemoveAll(n => !meshNames.Contains(n));
            group.removed.RemoveAll(n => !meshNames.Contains(n));
        });

        // ---------- Sharing between avatar settings ----------

        private static readonly Dictionary<RimeEmoShapeGroupSet, List<RimeEmoProject>> users = new Dictionary<RimeEmoShapeGroupSet, List<RimeEmoProject>>();

        [InitializeOnLoadMethod]
        private static void WatchProject() => EditorApplication.projectChanged += InvalidateUsers;

        internal static void InvalidateUsers() => users.Clear();

        // Every saved avatar setting that points to this set.
        internal static List<RimeEmoProject> Users(RimeEmoShapeGroupSet set)
        {
            if (!set) return new List<RimeEmoProject>();
            if (users.TryGetValue(set, out var cached)) return cached;
            var result = AssetDatabase.FindAssets("t:" + nameof(RimeEmoProject))
                .Select(guid => AssetDatabase.LoadAssetAtPath<RimeEmoProject>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(p => p && p.shapeGroups == set).ToList();
            users[set] = result;
            return result;
        }

        // A copy next to these avatar settings; the caller links it.
        internal static RimeEmoShapeGroupSet Duplicate(RimeEmoProject project, RimeEmoShapeGroupSet source)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(RimeEmoAssets.Root(project) + "/ShapeGroups.asset");
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path)) throw new InvalidOperationException(RimeEmoLocalization.T("グループを複製できませんでした。"));
            InvalidateUsers();
            return AssetDatabase.LoadAssetAtPath<RimeEmoShapeGroupSet>(path);
        }

        // Share of the set's names and patterns that exist on this face mesh (0..1).
        internal static float Score(RimeEmoShapeGroupSet set, ICollection<string> meshNames)
        {
            if (!set || meshNames.Count == 0) return 0;
            var names = set.groups.SelectMany(g => g.added.Concat(g.removed)).Distinct().ToList();
            var patterns = set.groups.SelectMany(g => g.patterns).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
            int total = names.Count + patterns.Count;
            if (total == 0) return 0;
            int hits = names.Count(meshNames.Contains) + patterns.Count(p => meshNames.Any(n => Matches(p, n)));
            return (float)hits / total;
        }

        // The existing set that best fits this avatar's Shape Key names, e.g. one made for another avatar of the same base model.
        internal static RimeEmoShapeGroupSet FindMatch(RimeEmoProject project, out float score, float minimum = 0.6f)
        {
            score = 0;
            if (!project) return null;
            var meshNames = new HashSet<string>(RimeEmoFace.Shapes(project).Select(s => s.shape));
            RimeEmoShapeGroupSet best = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(RimeEmoShapeGroupSet)))
            {
                var set = AssetDatabase.LoadAssetAtPath<RimeEmoShapeGroupSet>(AssetDatabase.GUIDToAssetPath(guid));
                if (!set || set == project.shapeGroups || set.groups.Count == 0) continue;
                float value = Score(set, meshNames);
                if (value < minimum) continue;
                if (!best || value > score || (Mathf.Approximately(value, score) && set.groups.Count > best.groups.Count)) { best = set; score = value; }
            }
            return best;
        }
    }
}
