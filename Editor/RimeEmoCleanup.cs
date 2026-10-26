using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RimeEmo.Editor
{
    // Moves data no saved motion or scene uses to the OS trash. Everything still referenced is kept.
    internal static class RimeEmoCleanup
    {
        private const string LegacyGeneratedRoot = "Assets/rimerime/RimeEmo/Generated";

        internal static void Run(RimeEmoProject project, RimeEmoMotion openDraft)
        {
            if (!project || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(project))) return;
            List<string> sources, builds;
            try
            {
                sources = UnusedSources(project, openDraft);
                builds = UnusedBuilds(project);
            }
            finally { EditorUtility.ClearProgressBar(); }
            if (sources.Count == 0 && builds.Count == 0)
            {
                EditorUtility.DisplayDialog("RimeEmo", RimeEmoLocalization.T("整理できる未使用データはありません。"), "OK");
                return;
            }
            string message = string.Format(RimeEmoLocalization.T("動き用の表情コピー {0} 件と、古いFX生成物 {1} 件をゴミ箱へ移動します。使用中の動きと、Scene・Prefabから参照中のFXは残します。"), sources.Count, builds.Count);
            if (!EditorUtility.DisplayDialog("RimeEmo", message, RimeEmoLocalization.T("ゴミ箱へ移動"), RimeEmoLocalization.T("キャンセル"))) return;
            var failed = sources.Concat(builds).Where(path => !AssetDatabase.MoveAssetToTrash(path)).ToArray();
            AssetDatabase.Refresh();
            if (failed.Length > 0)
                EditorUtility.DisplayDialog("RimeEmo", RimeEmoLocalization.T("移動できなかったデータがあります。") + "\n" + string.Join("\n", failed.Take(10)), "OK");
        }

        // Frozen material copies that no saved motion, open draft or assignment refers to.
        internal static List<string> UnusedSources(RimeEmoProject project, RimeEmoMotion openDraft)
        {
            var result = new List<string>();
            string folder = RimeEmoMotionMaterials.Folder(project);
            if (!AssetDatabase.IsValidFolder(folder)) return result;
            var used = new HashSet<AnimationClip>();
            foreach (var motion in project.motions.Concat(new[] { openDraft }).Where(m => m))
            {
                if (motion.sourceClip) used.Add(motion.sourceClip);
                if (motion.animationClip) used.Add(motion.animationClip);
                foreach (var state in motion.tracks.Where(t => t != null).SelectMany(t => t.states).Where(s => s != null && s.clip)) used.Add(state.clip);
            }
            foreach (var entry in project.clips.Concat(project.animations).Where(c => c != null)) { if (entry.clip) used.Add(entry.clip); if (entry.source) used.Add(entry.source); }
            foreach (var rule in project.gestureRules.Where(r => r != null && r.clip)) used.Add(rule.clip);
            foreach (var choice in project.gestureChoices.Where(c => c != null && c.clip)) used.Add(choice.clip);
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip && !used.Contains(clip)) result.Add(path);
            }
            return result;
        }

        // Older FX builds of this project that are neither the latest build nor referenced by any scene or prefab.
        internal static List<string> UnusedBuilds(RimeEmoProject project)
        {
            var result = new List<string>();
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(project));
            if (string.IsNullOrEmpty(guid)) return result;
            string name = RimeEmoAssets.SafeName(project.avatarName) + "-" + guid.Substring(0, 8);
            var parents = new[] { RimeEmoAssets.GeneratedRoot + "/" + name, LegacyGeneratedRoot + "/" + name }
                .Concat(string.IsNullOrEmpty(project.generatedFolder) ? Array.Empty<string>() : new[] { Path.GetDirectoryName(project.generatedFolder).Replace('\\', '/') })
                .Distinct().Where(folder => AssetDatabase.IsValidFolder(folder)).ToArray();
            var candidates = parents.SelectMany(parent => AssetDatabase.GetSubFolders(parent))
                .Where(folder => Path.GetFileName(folder).StartsWith("Build", StringComparison.Ordinal) && folder != project.generatedFolder).Distinct().ToList();
            if (candidates.Count == 0) return result;
            var referenced = ReferencedControllers();
            foreach (string folder in candidates)
            {
                bool inUse = AssetDatabase.FindAssets("t:AnimatorController", new[] { folder })
                    .Select(g => AssetDatabase.GUIDToAssetPath(g)).Any(path => referenced.Contains(path));
                if (!inUse) result.Add(folder);
            }
            return result;
        }

        private static HashSet<string> ReferencedControllers()
        {
            var result = new HashSet<string>();
            // Open scenes may hold unsaved references, so read them from memory as well as from files.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var dependency in EditorUtility.CollectDependencies(scene.GetRootGameObjects()))
                    if (dependency is RuntimeAnimatorController) result.Add(AssetDatabase.GetAssetPath(dependency));
            }
            var files = AssetDatabase.FindAssets("t:Scene").Concat(AssetDatabase.FindAssets("t:Prefab")).Distinct()
                .Select(g => AssetDatabase.GUIDToAssetPath(g)).Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).ToArray();
            for (int i = 0; i < files.Length; i++)
            {
                if (i % 50 == 0) EditorUtility.DisplayProgressBar("RimeEmo", RimeEmoLocalization.T("参照を確認しています…"), (float)i / files.Length);
                foreach (string dependency in AssetDatabase.GetDependencies(files[i], false))
                    if (dependency.EndsWith(".controller", StringComparison.OrdinalIgnoreCase)) result.Add(dependency);
            }
            return result;
        }
    }
}
