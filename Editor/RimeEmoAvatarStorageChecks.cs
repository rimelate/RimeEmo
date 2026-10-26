using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoAvatarStorageChecks
    {
        internal static void Run()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path))
                {
                    Debug.LogWarning("RimeEmo: Sceneが未保存のため設定の保存・復元チェックを省略します。Sceneを保存して再実行してください。");
                    return;
                }
            string folder = "Assets/RimeEmoStorageChecks-" + Guid.NewGuid().ToString("N");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            RimeEmoWindow window = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring(7));
                var project = ScriptableObject.CreateInstance<RimeEmoProject>();
                project.gestureBlendSeconds = 0.37f;
                AssetDatabase.CreateAsset(project, folder + "/Project.asset");
                var other = ScriptableObject.CreateInstance<RimeEmoProject>();
                AssetDatabase.CreateAsset(other, folder + "/Other.asset");
                var avatar = new GameObject("Avatar");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(avatar, scene);
                RimeEmoAvatarStorage.Attach(avatar, project);
                RimeEmoAvatarStorage.Attach(avatar, project);
                Check(avatar.transform.childCount == 1, "reattaching duplicated settings");
                Check(!avatar.CompareTag("EditorOnly"), "avatar root was excluded from build");
                Check(RimeEmoAvatarStorage.Find(avatar).CompareTag("EditorOnly"), "settings were not excluded from build");
                RimeEmoAvatarStorage.Attach(avatar, other);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Check(RimeEmoAvatarStorage.Load(avatar) == project, "Undo did not restore settings");
                Undo.PerformRedo();
                Check(RimeEmoAvatarStorage.Load(avatar) == other, "Redo did not restore settings");
                RimeEmoAvatarStorage.Attach(avatar, project);
                AssetDatabase.SaveAssets();
                Check(EditorSceneManager.SaveScene(scene, folder + "/Avatar.unity"), "scene could not be saved");
                EditorSceneManager.CloseScene(scene, true);
                scene = EditorSceneManager.OpenScene(folder + "/Avatar.unity", OpenSceneMode.Additive);
                avatar = scene.GetRootGameObjects()[0];
                Check(RimeEmoAvatarStorage.Load(avatar) == project, "scene reload lost settings reference");
                Check(Mathf.Approximately(RimeEmoAvatarStorage.Load(avatar).gestureBlendSeconds, 0.37f), "settings values did not survive reload");
                var prefab = PrefabUtility.SaveAsPrefabAsset(avatar, folder + "/Avatar.prefab");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Check(RimeEmoAvatarStorage.Load(instance) == project, "prefab instance lost settings reference");
                RimeEmoAvatarStorage.Attach(instance, other);
                Check(RimeEmoAvatarStorage.Load(avatar) == project, "prefab override changed original avatar reference");
                Check(EditorSceneManager.SaveScene(scene), "prefab override scene could not be saved");
                EditorSceneManager.CloseScene(scene, true);
                scene = EditorSceneManager.OpenScene(folder + "/Avatar.unity", OpenSceneMode.Additive);
                var roots = scene.GetRootGameObjects();
                avatar = Array.Find(roots, r => !PrefabUtility.IsPartOfPrefabInstance(r));
                instance = Array.Find(roots, r => PrefabUtility.IsPartOfPrefabInstance(r));
                Check(RimeEmoAvatarStorage.Load(instance) == other, "prefab override lost on scene reload");

                window = ScriptableObject.CreateInstance<RimeEmoWindow>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var select = typeof(RimeEmoWindow).GetMethod("SelectAvatar", flags);
                var field = typeof(RimeEmoWindow).GetField("project", flags);
                select.Invoke(window, new object[] { avatar });
                Check((RimeEmoProject)field.GetValue(window) == project, "avatar assignment did not load project");
                select.Invoke(window, new object[] { instance });
                Check((RimeEmoProject)field.GetValue(window) == other, "avatar switch retained previous project");
                var empty = new GameObject("Unconfigured");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(empty, scene);
                select.Invoke(window, new object[] { empty });
                Check(field.GetValue(window) == null, "unconfigured avatar inherited previous project");
                typeof(RimeEmoWindow).GetMethod("SelectProject", flags).Invoke(window, new object[] { project });
                Check(RimeEmoAvatarStorage.Load(empty) == project, "legacy project was not attached to selected avatar");
                select.Invoke(window, new object[] { null });
                Check(field.GetValue(window) == null, "clearing avatar retained project");
                // After a restart or a scene reload the window keeps its settings but loses the avatar reference.
                var avatarField = typeof(RimeEmoWindow).GetField("avatar", flags);
                var refresh = typeof(RimeEmoWindow).GetMethod("RefreshAvatar", flags);
                field.SetValue(window, project); avatarField.SetValue(window, null);
                refresh.Invoke(window, null);
                var restored = (GameObject)avatarField.GetValue(window);
                Check(restored && RimeEmoAvatarStorage.Load(restored) == project && (RimeEmoProject)field.GetValue(window) == project, "a lost avatar was not found again from its settings");
                var orphan = ScriptableObject.CreateInstance<RimeEmoProject>(); AssetDatabase.CreateAsset(orphan, folder + "/Orphan.asset");
                field.SetValue(window, orphan); avatarField.SetValue(window, null);
                refresh.Invoke(window, null);
                Check(field.GetValue(window) == null, "settings whose avatar is not in any open scene stayed loaded under an empty Avatar field");
            }
            finally
            {
                if (window) UnityEngine.Object.DestroyImmediate(window);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Avatar storage: " + message);
        }
    }
}
