using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RimeEmo.Editor
{
    internal static class RimeEmoNativeMenu
    {
        private const int ControlsPerMenu = 8;
        // SDK AV3 demo assets match the SDK inspector's default parameters and empty custom menu.
        internal static Object DefaultMenu => FindAsset("024fb8ef5b3988c46b446863c92f4522");
        internal static Object DefaultParameters => FindAsset("03a6d797deb62f0429471c4e17ea99a7");
        private static Type MenuType => Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu");
        private static Type ParametersType => Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters");

        private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);

        private static Object FindAsset(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);
        }

        private static IEnumerable<(string name, string type, int cost)> Required()
        {
            yield return (RimeEmoFaceMenu.LockParameter, "Bool", 1);
            yield return (RimeEmoFaceMenu.FaceParameter, "Int", 8);
        }

        internal static void Validate(Object menu, Object parameters, List<string> errors)
        {
            var parameterType = ParametersType;
            if (MenuType == null || parameterType == null)
            {
                errors.Add(RimeEmoLocalization.T("Avatar DescriptorのExpressions設定に対応していません。"));
                return;
            }
            if (menu)
                using (var serialized = new SerializedObject(menu))
                {
                    if (serialized.FindProperty("controls").arraySize >= ControlsPerMenu)
                        errors.Add(RimeEmoLocalization.T("Expressions Menuのルートに空きがありません。項目を1つ移動してから適用してください。"));
                }
            int addedCost = parameters ? 0 : 3 * 8 + Required().Sum(parameter => parameter.cost);
            if (parameters)
                using (var serialized = new SerializedObject(parameters))
                {
                    var list = serialized.FindProperty("parameters");
                    foreach (var required in Required())
                    {
                        var matching = Matching(list, required.name).ToArray();
                        if (matching.Length == 0) addedCost += required.cost;
                        else if (matching.Length != 1 || TypeName(matching[0].FindPropertyRelative("valueType")) != required.type)
                            errors.Add(RimeEmoLocalization.T("Expressions Parametersのパラメーター型が一致しません: ") + required.name);
                    }
                }
            int cost = parameters ? (int)parameterType.GetMethod("CalcTotalCost").Invoke(parameters, null) : 0;
            int maxCost = (int)parameterType.GetField("MAX_PARAMETER_COST").GetValue(null);
            if (cost + addedCost > maxCost)
                errors.Add(RimeEmoLocalization.T("Expressions Parametersの同期容量が足りません。"));
        }

        private static IEnumerable<SerializedProperty> Matching(SerializedProperty list, string name)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("name").stringValue == name) yield return element;
            }
        }

        private static string TypeName(SerializedProperty property) => property.enumValueIndex < 0 ? "" : property.enumNames[property.enumValueIndex];

        internal static void Build(RimeEmoProject project, string folder, Object baseMenu, Object baseParameters,
            List<string> created, out Object menu, out Object parameters)
        {
            var menuType = MenuType;
            ScriptableObject Create(Type type, Object basis, string name)
            {
                bool copy = basis;
                var instance = copy ? (ScriptableObject)Object.Instantiate(basis) : ScriptableObject.CreateInstance(type);
                instance.name = name;
                instance.hideFlags = HideFlags.None;
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset");
                AssetDatabase.CreateAsset(instance, path);
                created.Add(path);
                return instance;
            }
            menu = Create(menuType, baseMenu, "RimeEmo Expressions Menu");
            parameters = Create(ParametersType, baseParameters, "RimeEmo Expression Parameters");

            using (var serialized = new SerializedObject(parameters))
            {
                var list = serialized.FindProperty("parameters");
                if (!baseParameters)
                {
                    list.ClearArray();
                    AddParameter(list, "VRCEmote", "Int", true);
                    AddParameter(list, "VRCFaceBlendH", "Float", true);
                    AddParameter(list, "VRCFaceBlendV", "Float", true);
                }
                foreach (var required in Required())
                {
                    if (Matching(list, required.name).Any()) continue;
                    AddParameter(list, required.name, required.type, false);
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var clips = RimeEmoFaceMenu.MenuClips(project);
            var features = Create(menuType, null, "RimeEmo Face Menu");
            var choices = clips.Count > 0 ? Create(menuType, null, "RimeEmo Faces") : null;
            EditControls(menu, controls =>
            {
                if (!baseMenu) controls.ClearArray();
                AddControl(controls, RimeEmoLocalization.T("表情"), features);
            });
            EditControls(features, controls =>
            {
                AddControl(controls, RimeEmoLocalization.T("表情固定"), parameter: RimeEmoFaceMenu.LockParameter, value: 1);
                if (choices) AddControl(controls, RimeEmoLocalization.T("表情を選ぶ"), choices);
            });

            for (int offset = 0; offset < clips.Count;)
            {
                var next = clips.Count - offset > ControlsPerMenu ? Create(menuType, null, "RimeEmo Faces") : null;
                int count = Mathf.Min(clips.Count - offset, next ? ControlsPerMenu - 1 : ControlsPerMenu);
                EditControls(choices, controls =>
                {
                    for (int i = 0; i < count; i++)
                        AddControl(controls, clips[offset + i].name, parameter: RimeEmoFaceMenu.FaceParameter, value: offset + i + 1);
                    if (next) AddControl(controls, RimeEmoLocalization.T("次の表情"), next);
                });
                offset += count;
                choices = next;
            }
        }

        private static void EditControls(Object menu, Action<SerializedProperty> edit)
        {
            using (var serialized = new SerializedObject(menu))
            {
                edit(serialized.FindProperty("controls"));
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static SerializedProperty Append(SerializedProperty list)
        {
            int index = list.arraySize;
            list.arraySize++;
            return list.GetArrayElementAtIndex(index);
        }

        private static void AddParameter(SerializedProperty list, string name, string type, bool saved)
        {
            var parameter = Append(list);
            parameter.FindPropertyRelative("name").stringValue = name;
            SetEnum(parameter.FindPropertyRelative("valueType"), type);
            parameter.FindPropertyRelative("defaultValue").floatValue = 0;
            parameter.FindPropertyRelative("saved").boolValue = saved;
            parameter.FindPropertyRelative("networkSynced").boolValue = true;
        }

        private static void AddControl(SerializedProperty controls, string name, Object submenu = null, string parameter = "", int value = 0)
        {
            var control = Append(controls);
            SetEnum(control.FindPropertyRelative("type"), submenu ? "SubMenu" : "Toggle");
            control.FindPropertyRelative("name").stringValue = name;
            control.FindPropertyRelative("icon").objectReferenceValue = null;
            control.FindPropertyRelative("subMenu").objectReferenceValue = submenu;
            control.FindPropertyRelative("parameter").FindPropertyRelative("name").stringValue = parameter;
            control.FindPropertyRelative("value").floatValue = value;
            control.FindPropertyRelative("subParameters").ClearArray();
            control.FindPropertyRelative("labels").ClearArray();
        }

        private static void SetEnum(SerializedProperty property, string value) => property.enumValueIndex = Array.IndexOf(property.enumNames, value);
    }
}
