using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RimeEmo.Editor
{
    // Default Face edits write the scene avatar's face mesh and the settings together, undo together,
    // and the scene face can be taken back as Default Face.
    internal static class RimeEmoDefaultFaceChecks
    {
        internal static void Run()
        {
            string folder = "Assets/RimeEmoDefaultFaceChecks-" + Guid.NewGuid().ToString("N");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            RimeEmoWindow window = null; Mesh mesh = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                var avatar = new GameObject("Default Face Avatar"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(avatar, scene);
                var body = new GameObject("Body"); body.transform.SetParent(avatar.transform, false);
                mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                mesh.AddBlendShapeFrame("Smile", 100, new[] { Vector3.zero, Vector3.zero, Vector3.zero }, null, null);
                mesh.AddBlendShapeFrame("Blink", 100, new[] { Vector3.zero, Vector3.zero, Vector3.zero }, null, null);
                var renderer = body.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh;
                var project = ScriptableObject.CreateInstance<RimeEmoProject>();
                project.faceMeshSelected = true; project.faceMeshPath = "Body";
                project.shapes.Add(new ShapeEntry { path = "Body", shape = "Smile" });
                project.shapes.Add(new ShapeEntry { path = "Body", shape = "Blink" });
                AssetDatabase.CreateAsset(project, folder + "/Project.asset");

                window = ScriptableObject.CreateInstance<RimeEmoWindow>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(RimeEmoWindow);
                type.GetField("project", flags).SetValue(window, project);
                type.GetField("avatar", flags).SetValue(window, avatar);

                // An edit reaches the scene mesh and the settings, and one Undo takes both back.
                Undo.IncrementCurrentGroup();
                type.GetMethod("SetDefaultFaceValues", flags).Invoke(window, new object[] { new[] { new KeyValuePair<ShapeEntry, float>(project.shapes[0], 40) } });
                Check(Mathf.Approximately(renderer.GetBlendShapeWeight(0), 40) && Mathf.Approximately(project.shapes[0].defaultValue, 40), "a Default Face edit did not reach both the scene and the settings");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(Mathf.Approximately(renderer.GetBlendShapeWeight(0), 0) && Mathf.Approximately(project.shapes[0].defaultValue, 0), "Undo did not take the Default Face edit back from both");

                // An animation or another tool changed the scene face: the difference is counted and can be taken in.
                renderer.SetBlendShapeWeight(1, 75);
                Check((int)type.GetMethod("SceneDifferences", flags).Invoke(window, null) == 1, "a scene face change was not noticed");
                type.GetMethod("CaptureSceneDefaultFace", flags).Invoke(window, null);
                Check(Mathf.Approximately(project.shapes[1].defaultValue, 75) && (int)type.GetMethod("SceneDifferences", flags).Invoke(window, null) == 0, "the scene face was not taken in as Default Face");
            }
            finally
            {
                if (window) UnityEngine.Object.DestroyImmediate(window);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Default Face: " + message);
        }
    }
}
