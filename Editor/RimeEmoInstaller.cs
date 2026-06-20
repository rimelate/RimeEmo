using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RimeEmo.Editor
{
    public static class RimeEmoInstaller
    {
        public static bool IsModularAvatarAvailable()
        {
            return FindMergeAnimatorType() != null;
        }

        public static bool Install(GameObject avatarRoot, AnimatorController generatedController, int layerPriority, out string message, string layerTypeName = "FX")
        {
            message = string.Empty;
            if (avatarRoot == null || generatedController == null)
            {
                message = "Avatarまたは生成FX Controllerがありません。";
                return false;
            }

            Type mergeAnimatorType = FindMergeAnimatorType();
            if (mergeAnimatorType == null)
            {
                message = "Modular Avatar Merge Animator型が見つかりません。Modular Avatarを導入してください。";
                return false;
            }

            string objectName = layerTypeName == "FX" ? "RimeEmo" : "RimeEmo_" + layerTypeName;
            Transform child = avatarRoot.transform.Find(objectName);
            GameObject installerObject;
            if (child != null)
            {
                installerObject = child.gameObject;
            }
            else
            {
                installerObject = new GameObject(objectName);
                Undo.RegisterCreatedObjectUndo(installerObject, "Create RimeEmo Installer");
                installerObject.transform.SetParent(avatarRoot.transform, false);
            }

            Component mergeAnimator = installerObject.GetComponent(mergeAnimatorType);
            if (mergeAnimator == null)
            {
                mergeAnimator = Undo.AddComponent(installerObject, mergeAnimatorType);
            }

            Undo.RecordObject(mergeAnimator, "Configure RimeEmo Modular Avatar Merge Animator");
            var serialized = new SerializedObject(mergeAnimator);
            SetObjectReference(serialized, generatedController, "animator", "animatorController", "controller", "runtimeAnimatorController");
            SetEnum(serialized, layerTypeName, "layerType", "animLayerType", "playableLayer");
            SetEnum(serialized, "Replace", "mergeMode", "mergeAnimatorMode", "mode");
            SetEnum(serialized, "Absolute", "pathMode");
            SetBool(serialized, true, "matchAvatarWriteDefaults", "matchWriteDefaults");
            SetInt(serialized, layerPriority, "layerPriority", "priority");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mergeAnimator);

            message = "Modular Avatar Merge Animatorを設定しました。";
            return true;
        }

        private static Type FindMergeAnimatorType()
        {
            foreach (Type type in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (type.FullName == "nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator")
                {
                    return type;
                }
            }

            return null;
        }

        private static void SetObjectReference(SerializedObject serialized, UnityEngine.Object value, params string[] names)
        {
            foreach (string name in names)
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property != null && property.propertyType == SerializedPropertyType.ObjectReference)
                {
                    property.objectReferenceValue = value;
                    return;
                }
            }
        }

        private static void SetBool(SerializedObject serialized, bool value, params string[] names)
        {
            foreach (string name in names)
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property != null && property.propertyType == SerializedPropertyType.Boolean)
                {
                    property.boolValue = value;
                    return;
                }
            }
        }

        private static void SetInt(SerializedObject serialized, int value, params string[] names)
        {
            foreach (string name in names)
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property != null && property.propertyType == SerializedPropertyType.Integer)
                {
                    property.intValue = value;
                    return;
                }
            }
        }

        private static void SetEnum(SerializedObject serialized, string desiredName, params string[] names)
        {
            foreach (string name in names)
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property == null || property.propertyType != SerializedPropertyType.Enum)
                {
                    continue;
                }

                int index = FindEnumIndex(property, desiredName);
                if (index >= 0)
                {
                    property.enumValueIndex = index;
                    return;
                }
            }
        }

        private static int FindEnumIndex(SerializedProperty property, string desiredName)
        {
            string normalizedDesired = NormalizeEnumName(desiredName);
            for (int i = 0; i < property.enumNames.Length; i++)
            {
                string normalized = NormalizeEnumName(property.enumNames[i]);
                if (normalized.Contains(normalizedDesired) || normalizedDesired.Contains(normalized))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeEnumName(string value)
        {
            return value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}


