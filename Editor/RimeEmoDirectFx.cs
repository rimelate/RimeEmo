using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RimeEmo.Editor
{
    internal static class RimeEmoDirectFx
    {
        internal const string UndoLabel = "RimeEmo: Apply FX";
        private static readonly Dictionary<string, (DateTime lastWrite, long length, string hash)> ContentHashes =
            new Dictionary<string, (DateTime, long, string)>();

        internal static RimeEmoDirectFxLink Find(RimeEmoProject project, GameObject avatar) =>
            project && avatar ? Links(avatar).FirstOrDefault(link => link.project == project) : null;

        private static IEnumerable<RimeEmoDirectFxLink> Links(GameObject avatar) => avatar
            .GetComponentsInChildren<RimeEmoDirectFxLink>(true).Where(link => link.transform.parent == avatar.transform);

        internal static Component Descriptor(GameObject avatar) => avatar ? avatar.GetComponents<Component>()
            .FirstOrDefault(c => c && c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") : null;

        internal static AnimatorController EffectiveBaseFx(RimeEmoProject project, GameObject avatar)
        {
            if (!project || project.fxApplyMode == FxApplyMode.CreateFx) return null;
            if (project.fxApplyMode == FxApplyMode.ModularAvatarAppend) return project.sourceFX as AnimatorController;
            if (project.overwriteFX) return project.overwriteFX as AnimatorController;
            var descriptor = Descriptor(avatar);
            if (!descriptor) return null;
            using (var target = new Target(descriptor))
                return target.BaseFx(project, Find(project, avatar)) as AnimatorController;
        }

        // Compare the actual descriptor fields to the scene record before claiming ownership.
        private sealed class Target : IDisposable
        {
            internal readonly SerializedObject serialized;
            internal readonly SerializedProperty customLayers, customExpressions, menu, parameters;
            private readonly SerializedProperty layers;
            private SerializedProperty fx;
            internal SerializedProperty Controller => fx?.FindPropertyRelative("animatorController");
            internal SerializedProperty DefaultFx => fx?.FindPropertyRelative("isDefault");
            internal SerializedProperty Mask => fx?.FindPropertyRelative("mask");
            internal bool HasFx => customLayers != null && Controller != null && DefaultFx != null && Mask != null;
            internal bool CanInitialize => customLayers != null && layers != null && layers.arraySize == 0;
            internal bool HasMenu => customExpressions != null && menu != null && parameters != null;

            internal Target(Component descriptor)
            {
                serialized = new SerializedObject(descriptor);
                customLayers = serialized.FindProperty("customizeAnimationLayers");
                customExpressions = serialized.FindProperty("customExpressions");
                menu = serialized.FindProperty("expressionsMenu");
                parameters = serialized.FindProperty("expressionParameters");
                layers = serialized.FindProperty("baseAnimationLayers");
                for (int i = 0; layers != null && i < layers.arraySize; i++)
                {
                    var layer = layers.GetArrayElementAtIndex(i);
                    var type = layer.FindPropertyRelative("type");
                    if (type == null || type.enumValueIndex < 0 || type.enumNames[type.enumValueIndex] != "FX") continue;
                    fx = layer;
                    break;
                }
            }

            internal void InitializeFx(GameObject avatar)
            {
                if (HasFx) return;
                var animator = avatar.GetComponent<Animator>();
                foreach (string type in !animator || animator.isHuman
                    ? new[] { "Base", "Additive", "Gesture", "Action", "FX" } : new[] { "Base", "Action", "FX" })
                    fx = AddDefaultLayer(layers, type);
                var special = serialized.FindProperty("specialAnimationLayers");
                if (special != null && special.arraySize == 0)
                    foreach (string type in new[] { "Sitting", "TPose", "IKPose" }) AddDefaultLayer(special, type);
            }

            private static SerializedProperty AddDefaultLayer(SerializedProperty list, string name)
            {
                int index = list.arraySize;
                list.arraySize++;
                var layer = list.GetArrayElementAtIndex(index);
                var type = layer.FindPropertyRelative("type");
                type.enumValueIndex = Array.IndexOf(type.enumNames, name);
                layer.FindPropertyRelative("isDefault").boolValue = true;
                return layer;
            }

            internal bool OwnsFx(RimeEmoDirectFxLink link) => link && link.appliedFx && HasFx && Controller.objectReferenceValue == link.appliedFx;
            internal bool OwnsMenu(RimeEmoDirectFxLink link) => link && link.appliedMenu && HasMenu && menu.objectReferenceValue == link.appliedMenu;
            internal bool OwnsParameters(RimeEmoDirectFxLink link) => link && link.appliedParameters && HasMenu && parameters.objectReferenceValue == link.appliedParameters;

            internal RuntimeAnimatorController BaseFx(RimeEmoProject project, RimeEmoDirectFxLink link)
            {
                if (project.fxApplyMode != FxApplyMode.OverwriteFx) return null;
                if (project.overwriteFX) return project.overwriteFX;
                if (OwnsFx(link)) return link.previousCustomLayers && !link.previousDefaultFx ? link.previousFx : null;
                return HasFx && customLayers.boolValue && !DefaultFx.boolValue ? Controller.objectReferenceValue as RuntimeAnimatorController : null;
            }

            internal Object BaseMenu(RimeEmoDirectFxLink link)
            {
                bool own = OwnsMenu(link);
                bool custom = own ? link.previousCustomExpressions : customExpressions?.boolValue == true;
                var basis = custom ? (own ? link.previousMenu : menu.objectReferenceValue) : null;
                return basis ? basis : RimeEmoNativeMenu.DefaultMenu;
            }

            internal Object BaseParameters(RimeEmoDirectFxLink link)
            {
                bool own = OwnsParameters(link);
                bool custom = own ? link.previousCustomExpressions : customExpressions?.boolValue == true;
                var basis = custom ? (own ? link.previousParameters : parameters.objectReferenceValue) : null;
                return basis ? basis : RimeEmoNativeMenu.DefaultParameters;
            }

            internal bool OtherCustomLayers()
            {
                foreach (string path in new[] { "baseAnimationLayers", "specialAnimationLayers" })
                {
                    var layers = serialized.FindProperty(path);
                    for (int i = 0; layers != null && i < layers.arraySize; i++)
                    {
                        var layer = layers.GetArrayElementAtIndex(i);
                        if (layer.propertyPath == fx.propertyPath) continue;
                        if (layer.FindPropertyRelative("isDefault")?.boolValue == false && layer.FindPropertyRelative("animatorController")?.objectReferenceValue)
                            return true;
                    }
                }
                return false;
            }

            public void Dispose() => serialized.Dispose();
        }

        internal static void Validate(RimeEmoProject project, GameObject avatar, Component descriptor, List<string> errors)
        {
            if (!descriptor) return;
            if (Links(avatar).Any(link => link.project && link.project != project))
                errors.Add(RimeEmoLocalization.T("別のRimeEmo設定の直接適用を解除してから適用してください。"));
            using (var target = new Target(descriptor))
            {
                if (!target.HasFx && !target.CanInitialize)
                {
                    errors.Add(RimeEmoLocalization.T("Avatar DescriptorのPlayable Layersを開いてFXスロットを初期化してください。"));
                    return;
                }
                var link = Find(project, avatar);
                var basis = target.BaseFx(project, link);
                if (GeneratedAssetsEdited(target, link))
                    errors.Add(RimeEmoLocalization.T("適用中の生成アセットが編集されています。編集内容を元のアセットに移すか、適用を解除してからやり直してください。"));
                if (project.fxApplyMode == FxApplyMode.OverwriteFx)
                {
                    string path = AssetDatabase.GetAssetPath(basis);
                    if (!(basis is AnimatorController) || AssetDatabase.LoadMainAssetAtPath(path) != basis || !path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                        errors.Add(RimeEmoLocalization.T("上書きには保存済みのAnimator Controllerを指定してください。"));
                    else if (path.StartsWith(RimeEmoAssets.GeneratedRoot + "/", StringComparison.Ordinal))
                        errors.Add(RimeEmoLocalization.T("上書き元にはRimeEmoの生成FX以外を指定してください。"));
                    else
                        foreach (var required in RequiredParameters(project))
                            if (((AnimatorController)basis).parameters.Any(p => p.name == required.name && p.type != required.type))
                                errors.Add(RimeEmoLocalization.T("FXのパラメーター型が一致しません: ") + required.name);
                }
                if (project.expressionMenu && target.HasMenu)
                    RimeEmoNativeMenu.Validate(target.BaseMenu(link), target.BaseParameters(link), errors);
                else if (project.expressionMenu)
                    errors.Add(RimeEmoLocalization.T("Avatar DescriptorのExpressions設定に対応していません。"));
            }
        }

        private static IEnumerable<(string name, AnimatorControllerParameterType type)> RequiredParameters(RimeEmoProject project)
        {
            yield return ("GestureLeft", AnimatorControllerParameterType.Int);
            yield return ("GestureRight", AnimatorControllerParameterType.Int);
            if (!project.expressionMenu) yield break;
            yield return (RimeEmoFaceMenu.LockParameter, AnimatorControllerParameterType.Bool);
            yield return (RimeEmoFaceMenu.FaceParameter, AnimatorControllerParameterType.Int);
        }

        internal static bool IsCurrent(RimeEmoProject project, GameObject avatar, RimeEmoDirectFxLink link, string buildFingerprint)
        {
            var descriptor = Descriptor(avatar);
            if (!descriptor || (int)project.fxApplyMode != link.mode) return false;
            using (var target = new Target(descriptor))
                return target.OwnsFx(link) && target.customLayers.boolValue && !target.DefaultFx.boolValue
                    && target.Mask.objectReferenceValue == link.appliedMask && !GeneratedAssetsEdited(target, link)
                    && (!project.expressionMenu || (target.OwnsMenu(link) && target.OwnsParameters(link) && target.customExpressions.boolValue))
                    && buildFingerprint.Length > 0 && link.fingerprint == Fingerprint(project, target, link, buildFingerprint);
        }

        private static string Fingerprint(RimeEmoProject project, Target target, RimeEmoDirectFxLink link, string buildFingerprint)
        {
            string menu = project.expressionMenu ? AssetFingerprint(target.BaseMenu(link)) + AssetFingerprint(target.BaseParameters(link)) + RimeEmoLocalization.English : "";
            return Hash128.Compute(buildFingerprint + AssetFingerprint(target.BaseFx(project, link)) + menu).ToString();
        }

        private static string AssetFingerprint(Object value)
        {
            string path = AssetDatabase.GetAssetPath(value);
            if (string.IsNullOrEmpty(path)) return "";
            string dirty = string.Join(",", AssetDatabase.LoadAllAssetsAtPath(path).Where(EditorUtility.IsDirty)
                .Select(asset => EditorUtility.GetDirtyCount(asset)));
            return AssetDatabase.AssetPathToGUID(path) + AssetDatabase.GetAssetDependencyHash(path) + dirty;
        }

        private static string AssetContents(Object value)
        {
            string path = AssetDatabase.GetAssetPath(value);
            if (string.IsNullOrEmpty(path)) return "";
            var file = new FileInfo(path);
            if (!file.Exists) return "";
            if (ContentHashes.TryGetValue(path, out var cached) && cached.lastWrite == file.LastWriteTimeUtc && cached.length == file.Length)
                return cached.hash;
            // Dependency hashes include referenced source clips; a change there must not count as a manual edit to this copy.
            using (var digest = SHA256.Create())
            using (var stream = file.OpenRead())
            {
                string hash = Convert.ToBase64String(digest.ComputeHash(stream));
                ContentHashes[path] = (file.LastWriteTimeUtc, file.Length, hash);
                return hash;
            }
        }

        private static bool Edited(Object asset, string recorded) =>
            AssetContents(asset) != recorded || AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(asset)).Any(EditorUtility.IsDirty);

        private static bool GeneratedAssetsEdited(Target target, RimeEmoDirectFxLink link) =>
            (target.OwnsFx(link) && Edited(link.appliedFx, link.fxHash))
            || (target.OwnsMenu(link) && (Edited(link.appliedMenu, link.menuHash)
                || link.submenus.Where((asset, index) => Edited(asset, link.submenuHashes[index])).Any()))
            || (target.OwnsParameters(link) && Edited(link.appliedParameters, link.parametersHash));

        internal static void Apply(RimeEmoProject project, GameObject avatar, AnimatorController generated, string buildFingerprint)
        {
            var descriptor = Descriptor(avatar);
            var old = Find(project, avatar);
            var created = new List<string>();
            try
            {
                using (var target = new Target(descriptor))
                {
                    var basis = target.BaseFx(project, old);
                    var previousMenu = target.BaseMenu(old);
                    var previousParameters = target.BaseParameters(old);
                    string folder = RimeEmoBuild.OutputFolder(project) + "/Direct FX";
                    RimeEmoAssets.Folder(folder);
                    var controller = CopyController(generated, basis as AnimatorController, folder, created);
                    Object menu = null, parameters = null;
                    if (project.expressionMenu)
                        RimeEmoNativeMenu.Build(project, folder, previousMenu, previousParameters, created, out menu, out parameters);
                    AssetDatabase.SaveAssets();

                    Restore(project, avatar);
                    target.serialized.Update();
                    target.InitializeFx(avatar);
                    var link = Links(avatar).FirstOrDefault(candidate => !candidate.project && candidate.CompareTag("EditorOnly"));
                    if (!link)
                    {
                        var child = new GameObject(GameObjectUtility.GetUniqueNameForSibling(avatar.transform, "RimeEmo Direct FX"));
                        child.tag = "EditorOnly";
                        Undo.RegisterCreatedObjectUndo(child, UndoLabel);
                        Undo.SetTransformParent(child.transform, avatar.transform, UndoLabel);
                        link = Undo.AddComponent<RimeEmoDirectFxLink>(child);
                    }
                    Undo.RecordObject(link, UndoLabel);
                    link.project = project;
                    link.mode = (int)project.fxApplyMode;
                    link.baseFx = basis;
                    link.previousFx = target.Controller.objectReferenceValue as RuntimeAnimatorController;
                    link.previousDefaultFx = target.DefaultFx.boolValue;
                    link.previousCustomLayers = target.customLayers.boolValue;
                    link.previousMask = target.Mask.objectReferenceValue;
                    link.appliedFx = controller;
                    link.appliedMask = basis ? link.previousMask : null;
                    target.Controller.objectReferenceValue = controller;
                    target.Mask.objectReferenceValue = link.appliedMask;
                    target.customLayers.boolValue = true;
                    target.DefaultFx.boolValue = false;
                    if (project.expressionMenu)
                    {
                        link.previousCustomExpressions = target.customExpressions.boolValue;
                        link.previousMenu = target.menu.objectReferenceValue;
                        link.previousParameters = target.parameters.objectReferenceValue;
                        link.appliedMenu = menu;
                        link.appliedParameters = parameters;
                        target.menu.objectReferenceValue = menu;
                        target.parameters.objectReferenceValue = parameters;
                        target.customExpressions.boolValue = true;
                    }
                    target.serialized.ApplyModifiedProperties();
                    link.fingerprint = Fingerprint(project, target, link, buildFingerprint);
                    link.fxHash = AssetContents(controller);
                    link.menuHash = AssetContents(menu);
                    link.parametersHash = AssetContents(parameters);
                    link.submenus = created.Skip(1).Select(AssetDatabase.LoadMainAssetAtPath)
                        .Where(asset => asset != menu && asset != parameters).ToArray();
                    link.submenuHashes = link.submenus.Select(AssetContents).ToArray();
                    EditorUtility.SetDirty(link);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(link);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);
                }
            }
            catch
            {
                foreach (string path in created.AsEnumerable().Reverse()) AssetDatabase.DeleteAsset(path);
                throw;
            }
        }

        private static AnimatorController CopyController(AnimatorController generated, AnimatorController basis, string folder, List<string> created)
        {
            var original = basis ? basis : generated;
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/RimeEmo Direct FX.controller");
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), path))
                throw new InvalidOperationException(RimeEmoLocalization.T("FXをコピーできませんでした。"));
            created.Add(path);
            var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!basis) return copy;

            // Clone the entire generated graph into the destination before fixing its local references.
            // Unity animator graph nodes use native references that Object.Instantiate cannot clone safely.
            var clones = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(generated))
                .Where(source => source && source != generated).ToDictionary(source => source, CloneGeneratedSubasset);
            foreach (var pair in clones)
            {
                pair.Value.name = pair.Key.name;
                pair.Value.hideFlags = pair.Key.hideFlags;
                AssetDatabase.AddObjectToAsset(pair.Value, copy);
            }
            foreach (Object clone in clones.Values)
                using (var serialized = new SerializedObject(clone))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue
                            && clones.TryGetValue(property.objectReferenceValue, out var reference))
                            property.objectReferenceValue = reference;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            var layer = generated.layers[0];
            layer.name = copy.MakeUniqueLayerName(layer.name);
            layer.stateMachine = (AnimatorStateMachine)clones[layer.stateMachine];
            var writeDefaults = copy.layers.Where(existing => existing.blendingMode != AnimatorLayerBlendingMode.Additive)
                .SelectMany(existing => RimeEmoBuild.States(existing.stateMachine)).Select(state => state.writeDefaultValues).Distinct().ToArray();
            if (writeDefaults.Length == 1)
                foreach (var state in RimeEmoBuild.States(layer.stateMachine)) state.writeDefaultValues = writeDefaults[0];
            copy.AddLayer(layer);
            foreach (var parameter in generated.parameters)
                if (!copy.parameters.Any(p => p.name == parameter.name)) copy.AddParameter(parameter);
            EditorUtility.SetDirty(copy);
            return copy;
        }

        private static Object CloneGeneratedSubasset(Object source)
        {
            Object clone;
            switch (source)
            {
                case AnimatorStateMachine _: clone = new AnimatorStateMachine(); break;
                case AnimatorState _: clone = new AnimatorState(); break;
                case AnimatorStateTransition _: clone = new AnimatorStateTransition(); break;
                case AnimationClip _: clone = new AnimationClip(); break;
                case StateMachineBehaviour _: clone = ScriptableObject.CreateInstance(source.GetType()); break;
                default: throw new InvalidOperationException("Unsupported generated FX asset: " + source.GetType().Name);
            }
            EditorUtility.CopySerialized(source, clone);
            return clone;
        }

        internal static void Restore(RimeEmoProject project, GameObject avatar)
        {
            var link = Find(project, avatar);
            if (!link) return;
            var descriptor = Descriptor(avatar);
            if (descriptor)
                using (var target = new Target(descriptor))
                {
                    if (target.OwnsFx(link))
                    {
                        target.Controller.objectReferenceValue = link.previousFx;
                        if (target.Mask.objectReferenceValue == link.appliedMask) target.Mask.objectReferenceValue = link.previousMask;
                        if (!target.DefaultFx.boolValue) target.DefaultFx.boolValue = link.previousDefaultFx;
                        if (target.customLayers.boolValue && !target.OtherCustomLayers()) target.customLayers.boolValue = link.previousCustomLayers;
                    }
                    bool menu = target.OwnsMenu(link), parameters = target.OwnsParameters(link);
                    if (menu) target.menu.objectReferenceValue = link.previousMenu;
                    if (parameters) target.parameters.objectReferenceValue = link.previousParameters;
                    if (menu && parameters && target.customExpressions.boolValue)
                        target.customExpressions.boolValue = link.previousCustomExpressions;
                    target.serialized.ApplyModifiedProperties();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);
                }
            if (PrefabUtility.IsPartOfNonAssetPrefabInstance(link) && !PrefabUtility.IsAddedComponentOverride(link)
                && !PrefabUtility.IsAddedGameObjectOverride(link.gameObject))
            {
                // An inherited component cannot be destroyed from this instance.
                Undo.RecordObject(link, UndoLabel);
                link.project = null;
                link.previousFx = link.appliedFx = link.baseFx = null;
                link.previousMask = link.appliedMask = null;
                link.previousMenu = link.previousParameters = link.appliedMenu = link.appliedParameters = null;
                link.previousCustomLayers = link.previousDefaultFx = link.previousCustomExpressions = false;
                link.mode = 0;
                link.fingerprint = link.fxHash = link.menuHash = link.parametersHash = "";
                link.submenus = Array.Empty<Object>();
                link.submenuHashes = Array.Empty<string>();
                PrefabUtility.RecordPrefabInstancePropertyModifications(link);
                return;
            }
            // A user can add children or components to the record object; remove only the owned component in that case.
            if (link.CompareTag("EditorOnly") && link.transform.childCount == 0 && link.GetComponents<Component>().Length == 2)
                Undo.DestroyObjectImmediate(link.gameObject);
            else
                Undo.DestroyObjectImmediate(link);
        }
    }
}
