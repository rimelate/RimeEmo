using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoApply
    {
        internal static Type MergeType => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator")).FirstOrDefault(t => t != null);
        private static string ChildName(RimeEmoProject project) => "RimeEmo-" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(project));
        internal const int LayerPriority = 100;
        internal static GameObject FindApplied(RimeEmoProject project, GameObject avatar)
        {
            if (!project || !avatar) return null;
            var child = avatar.transform.Find(ChildName(project)); return child ? child.gameObject : null;
        }
        // The controller the avatar actually uses; it may be older than project.generatedFX.
        internal static RuntimeAnimatorController AppliedController(RimeEmoProject project, GameObject avatar)
        {
            var child = FindApplied(project, avatar);
            var type = child ? MergeType : null;
            var component = type != null ? child.GetComponent(type) : null;
            return component ? Read(component, "animator") as RuntimeAnimatorController : null;
        }
        // Older versions replaced the whole FX; such a component is updated to Append on the next apply.
        internal static bool AppliedAsAppend(RimeEmoProject project, GameObject avatar)
        {
            var child = FindApplied(project, avatar);
            var type = child ? MergeType : null;
            var component = type != null ? child.GetComponent(type) : null;
            return component && Read(component, "mergeAnimatorMode")?.ToString() == "Append";
        }
        internal static List<string> Validate(RimeEmoProject project, GameObject avatar)
        {
            var errors = RimeEmoBuild.Validate(project);
            if (!avatar || EditorUtility.IsPersistent(avatar)) { errors.Add(RimeEmoLocalization.T("Scene上のアバターを指定してください。")); return errors; }
            var descriptor = avatar.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (!descriptor) errors.Add(RimeEmoLocalization.T("VRCAvatarDescriptorが見つかりません。"));
            if (MergeType == null) { errors.Add(RimeEmoLocalization.T("Modular AvatarのMA Merge Animatorが必要です。")); return errors; }
            foreach (string field in new[] { "animator", "deleteAttachedAnimator", "matchAvatarWriteDefaults", "layerPriority" })
                if (MergeType.GetField(field) == null) errors.Add(RimeEmoLocalization.T("Modular Avatarの設定が対応していません: ") + field);
            foreach (var setting in new[] { new[] { "layerType", "FX" }, new[] { "pathMode", "Absolute" }, new[] { "mergeAnimatorMode", "Append" } })
            {
                var field = MergeType.GetField(setting[0]);
                if (field == null || !field.FieldType.IsEnum || !Enum.GetNames(field.FieldType).Contains(setting[1]))
                    errors.Add(RimeEmoLocalization.T("Modular Avatarの設定が対応していません: ") + setting[0]);
            }
            if (!project) return errors;
            errors.AddRange(RimeEmoFaceMenu.ValidateApply(project));
            var own = FindApplied(project, avatar);
            if (own && !IsOwned(project, own)) errors.Add(RimeEmoLocalization.T("適用先と同名のオブジェクトがあります。自動で変更できません。"));
            foreach (var component in avatar.GetComponentsInChildren(MergeType, true).Cast<Component>())
            {
                if (own && component.gameObject == own) continue;
                if (Read(component, "layerType")?.ToString() != "FX") continue;
                var controller = Read(component, "animator") as RuntimeAnimatorController;
                if (!controller) continue;
                string prefix = "";
                if (Read(component, "pathMode")?.ToString() != "Absolute")
                {
                    var reference = Read(component, "relativePathRoot");
                    var get = reference?.GetType().GetMethod("Get", new[] { typeof(Component) });
                    var root = get?.Invoke(reference, new object[] { avatar.transform }) as GameObject;
                    prefix = AnimationUtility.CalculateTransformPath(root ? root.transform : component.transform, avatar.transform);
                    if (prefix.Length > 0) prefix += "/";
                }
                string face = RimeEmoFace.Path(project);
                if (controller.animationClips.Any(c => c && AnimationUtility.GetCurveBindings(c).Any(b => RimeEmoClips.IsShape(b) && prefix + b.path == face)))
                    errors.Add(RimeEmoLocalization.T("別のMA FXが顔のBlendShapeを操作しています: ") + component.name);
                if (controller is UnityEditor.Animations.AnimatorController animatorController && animatorController.parameters.Any(p =>
                    (p.name == "GestureLeft" || p.name == "GestureRight") && p.type != AnimatorControllerParameterType.Int))
                    errors.Add(RimeEmoLocalization.T("別のMA FXがGestureパラメーターをInt以外で定義しています: ") + component.name);
            }
            foreach (var group in RimeEmoFace.Shapes(project).GroupBy(s => s.path))
            {
                var t = string.IsNullOrEmpty(group.Key) ? avatar.transform : avatar.transform.Find(group.Key);
                var renderer = t ? t.GetComponent<SkinnedMeshRenderer>() : null;
                if (!renderer || !renderer.sharedMesh) { errors.Add(RimeEmoLocalization.T("顔メッシュが見つかりません: ") + group.Key); continue; }
                if (group.Any(s => renderer.sharedMesh.GetBlendShapeIndex(s.shape) < 0)) errors.Add(RimeEmoLocalization.T("保存済みShape Keyと顔メッシュが一致しません。"));
            }
            return errors.Distinct().ToList();
        }
        private static object Read(Component component, string field) => component.GetType().GetField(field)?.GetValue(component);
        private static bool IsOwned(RimeEmoProject project, GameObject child)
        {
            var component = child.GetComponent(MergeType);
            var controller = component ? Read(component, "animator") as RuntimeAnimatorController : null;
            if (!controller || string.IsNullOrEmpty(project.generatedFolder)) return false;
            string parent = System.IO.Path.GetDirectoryName(project.generatedFolder).Replace('\\', '/') + "/";
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            // Current output lives under GeneratedRoot; older numbered Build folders sit under the settings' folder.
            bool generated = controllerPath.StartsWith(RimeEmoAssets.GeneratedRoot + "/", StringComparison.Ordinal) || controllerPath.StartsWith(parent, StringComparison.Ordinal);
            return generated && RimeEmoFaceMenu.OnlyOwnParts(child, component);
        }
        // selectResult: the Gesture page shows the new object; quick re-applies from the top bar keep the user's selection.
        internal static void Apply(RimeEmoProject project, GameObject avatar, bool selectResult = true)
        {
            var errors = Validate(project, avatar);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            var controller = RimeEmoBuild.Build(project, avatar);
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("RimeEmo: Apply FX");
            try
            {
            var child = FindApplied(project, avatar);
            if (!child)
            {
                child = new GameObject(ChildName(project)); Undo.RegisterCreatedObjectUndo(child, "RimeEmo: Apply FX");
                Undo.SetTransformParent(child.transform, avatar.transform, "RimeEmo: Apply FX");
                child.transform.localPosition = Vector3.zero; child.transform.localRotation = Quaternion.identity; child.transform.localScale = Vector3.one;
            }
            // Unity's overloaded null must be checked explicitly; "??" can pass a destroyed or placeholder component.
            var component = child.GetComponent(MergeType);
            // Older versions replaced the whole FX; a new or migrated component gets RimeEmo's defaults once,
            // later applies keep what the user set on it (priority, Write Defaults).
            bool initialize = !component || Read(component, "mergeAnimatorMode")?.ToString() != "Append";
            if (!component) component = Undo.AddComponent(child, MergeType);
            Undo.RecordObject(component, "RimeEmo: Apply FX");
            using (var serialized = new SerializedObject(component))
            {
                serialized.FindProperty("animator").objectReferenceValue = controller;
                SetEnum(serialized, "layerType", "FX"); SetEnum(serialized, "pathMode", "Absolute"); SetEnum(serialized, "mergeAnimatorMode", "Append");
                serialized.FindProperty("deleteAttachedAnimator").boolValue = false;
                if (initialize)
                {
                    // Above the avatar's own FX layers (priority 0), so the face set here wins.
                    serialized.FindProperty("layerPriority").intValue = LayerPriority;
                    serialized.FindProperty("matchAvatarWriteDefaults").boolValue = true;
                }
                serialized.ApplyModifiedProperties();
            }
            RimeEmoFaceMenu.Apply(child, project);
            Undo.CollapseUndoOperations(group); EditorSceneManager.MarkSceneDirty(avatar.scene); if (selectResult) Selection.activeGameObject = child;
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }
        private static void SetEnum(SerializedObject serialized, string property, string name)
        {
            var p = serialized.FindProperty(property); int index = Array.IndexOf(p.enumNames, name);
            if (index < 0) throw new InvalidOperationException(RimeEmoLocalization.T("Modular Avatarの設定が対応していません: ") + property);
            p.enumValueIndex = index;
        }
        internal static void Remove(RimeEmoProject project, GameObject avatar)
        {
            var child = FindApplied(project, avatar);
            if (!child) return;
            if (MergeType == null || !IsOwned(project, child)) throw new InvalidOperationException(RimeEmoLocalization.T("適用オブジェクトが変更されているため、自動解除できません。"));
            Undo.DestroyObjectImmediate(child); EditorSceneManager.MarkSceneDirty(avatar.scene);
        }
    }
}
