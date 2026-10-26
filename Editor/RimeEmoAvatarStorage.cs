using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoAvatarStorage
    {
        internal static RimeEmoAvatarSettings Find(GameObject avatar)
        {
            if (!avatar) return null;
            var settings = avatar.GetComponentsInChildren<RimeEmoAvatarSettings>(true)
                .Where(s => s.transform.parent == avatar.transform).ToArray();
            if (settings.Length > 1)
                throw new InvalidOperationException(RimeEmoLocalization.T("アバター直下にRimeEmoの設定が複数あります。使用する設定を1つにしてください。"));
            return settings.FirstOrDefault();
        }

        internal static RimeEmoProject Load(GameObject avatar)
        {
            var settings = Find(avatar);
            return settings ? settings.project as RimeEmoProject : null;
        }

        // The scene avatar whose settings child points to this project, in any open scene.
        internal static GameObject FindAvatar(RimeEmoProject project)
        {
            if (!project) return null;
            var settings = UnityEngine.Object.FindObjectsOfType<RimeEmoAvatarSettings>(true)
                .FirstOrDefault(s => s && s.project == project && s.transform.parent && s.gameObject.scene.IsValid());
            return settings ? settings.transform.parent.gameObject : null;
        }

        internal static void Attach(GameObject avatar, RimeEmoProject project)
        {
            if (!avatar || EditorUtility.IsPersistent(avatar) || !avatar.scene.IsValid())
                throw new InvalidOperationException(RimeEmoLocalization.T("Scene上のアバターを指定してください。"));
            if (!project || !EditorUtility.IsPersistent(project))
                throw new InvalidOperationException(RimeEmoLocalization.T("保存済みのRimeEmoProjectを指定してください。"));
            var settings = Find(avatar);
            if (settings && settings.project == project && settings.CompareTag("EditorOnly")) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("RimeEmo: Save Avatar Settings");
            try
            {
                if (!settings)
                {
                    var child = new GameObject(GameObjectUtility.GetUniqueNameForSibling(avatar.transform, "RimeEmo Settings"));
                    Undo.RegisterCreatedObjectUndo(child, "RimeEmo: Save Avatar Settings");
                    Undo.SetTransformParent(child.transform, avatar.transform, "RimeEmo: Save Avatar Settings");
                    child.transform.localPosition = Vector3.zero;
                    child.transform.localRotation = Quaternion.identity;
                    child.transform.localScale = Vector3.one;
                    settings = Undo.AddComponent<RimeEmoAvatarSettings>(child);
                }
                Undo.RecordObject(settings.gameObject, "RimeEmo: Save Avatar Settings");
                settings.gameObject.tag = "EditorOnly";
                Undo.RecordObject(settings, "RimeEmo: Save Avatar Settings");
                settings.project = project;
                PrefabUtility.RecordPrefabInstancePropertyModifications(settings.gameObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(settings);
                EditorUtility.SetDirty(settings);
                EditorSceneManager.MarkSceneDirty(avatar.scene);
                Undo.CollapseUndoOperations(group);
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }
    }

    [CustomEditor(typeof(RimeEmoAvatarSettings))]
    internal sealed class RimeEmoAvatarSettingsInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var settings = (RimeEmoAvatarSettings)target;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(RimeEmoLocalization.T("保存した設定"), settings.project, typeof(RimeEmoProject), false);
            EditorGUILayout.HelpBox(RimeEmoLocalization.T("RimeEmoのAvatar欄に親のアバターを指定すると、この設定を読み込みます。設定の関連付けはSceneまたはPrefabを保存してください。"), MessageType.Info);
            if (!settings.CompareTag("EditorOnly"))
                EditorGUILayout.HelpBox(RimeEmoLocalization.T("この設定用オブジェクトのタグをEditorOnlyにしてください。"), MessageType.Error);
        }
    }
}
