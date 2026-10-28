using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RimeEmo.Editor
{
    // The face lock and expression list of the Expressions Menu, and pausing VRChat's blinking and lip sync per
    // expression. VRChat and Modular Avatar types are reached by name, as elsewhere, so RimeEmo compiles without them.
    internal static class RimeEmoFaceMenu
    {
        internal const string LockParameter = "RimeEmo/Lock";
        // 0 follows the hands; 1–255 show the menu's expression of that number.
        internal const string FaceParameter = "RimeEmo/Face";
        internal const int MaxExpressions = 255;

        internal static Type TrackingControlType => Find("VRC.SDK3.Avatars.Components.VRCAnimatorTrackingControl");
        internal static Type ParametersType => Find("nadena.dev.modular_avatar.core.ModularAvatarParameters");
        internal static Type InstallerType => Find("nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller");
        internal static Type MenuItemType => Find("nadena.dev.modular_avatar.core.ModularAvatarMenuItem");
        private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

        internal static TrackingSetting Tracking(RimeEmoProject project, AnimationClip clip)
            => project && clip ? project.tracking.FirstOrDefault(t => t != null && t.clip == clip) : null;

        internal static void SetTracking(RimeEmoProject project, AnimationClip clip, bool stopBlink, bool stopLipSync)
        {
            if (!project || !clip) return;
            Undo.RecordObject(project, "RimeEmo: Blink and Lip Sync");
            // Entries of deleted clips go too.
            project.tracking.RemoveAll(t => t == null || !t.clip || t.clip == clip);
            if (stopBlink || stopLipSync) project.tracking.Add(new TrackingSetting { clip = clip, stopBlink = stopBlink, stopLipSync = stopLipSync });
            EditorUtility.SetDirty(project);
        }

        // The material clip an assignment or menu entry stands for; a motion is keyed by its saved clip.
        internal static AnimationClip MaterialClip(Object key) => key is RimeEmoMotion motion ? motion.animationClip : key as AnimationClip;

        // Every expression among the materials, in the order of the materials list: expressions, imported motions, then
        // saved motions. Setup also imports the source FX's lip sync, talk patterns and AFK clips; they are not faces to
        // choose, and listed, their Shape Keys would be written in every state and stop the avatar's own lip sync
        // (MAYO's MAYO_Lip_Sync, SiuSiu's talk patterns). A clip copied from the source FX is listed only when the FX
        // shows it for a hand shape or it is assigned here.
        internal static List<AnimationClip> MenuClips(RimeEmoProject project)
        {
            if (!project || !project.expressionMenu) return new List<AnimationClip>();
            var fxClips = new HashSet<AnimationClip>(project.sourceFX ? project.sourceFX.animationClips.Where(c => c) : Enumerable.Empty<AnimationClip>());
            var handClips = RimeEmoBuild.SourceGestureClips(project);
            var assigned = new HashSet<AnimationClip>(project.gestureRules.Where(r => r != null).Select(r => r.motion ? r.motion.animationClip : r.clip)
                .Concat(project.gestureChoices.Where(c => c != null).Select(c => c.motion ? c.motion.animationClip : c.clip)).Where(c => c));
            bool Expression(ClipEntry entry) => entry != null && (!entry.source || !fxClips.Contains(entry.source) || handClips.Contains(entry.source) || assigned.Contains(entry.clip));
            return project.clips.Concat(project.animations).Where(Expression).Select(c => c.clip)
                .Concat(project.motions.Where(m => m).Select(m => m.animationClip))
                .Where(c => c && RimeEmoClipClassification.Classify(project, c) != RimeEmoClipKind.Excluded)
                .Distinct().Take(MaxExpressions).ToList();
        }

        // Whether VRChat itself blinks and lip-syncs this avatar; only then can an expression pause it. An avatar without
        // eyelids set blinks inside its own clips, and "Viseme Parameter Only" moves the mouth from its FX (SiuSiu does
        // both); a tracking pause reaches neither.
        internal static void TrackingSupport(GameObject avatar, out bool blink, out bool lipSync)
        {
            blink = lipSync = true;
            var descriptor = avatar ? avatar.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") : null;
            if (!descriptor) return;
            using (var serialized = new SerializedObject(descriptor))
            {
                string Value(string path)
                {
                    var property = serialized.FindProperty(path);
                    return property != null && property.propertyType == SerializedPropertyType.Enum && property.enumValueIndex >= 0 ? property.enumNames[property.enumValueIndex] : null;
                }
                var eyeLook = serialized.FindProperty("enableEyeLook");
                blink = (eyeLook == null || eyeLook.boolValue) && Value("customEyeLookSettings.eyelidType") != "None";
                lipSync = Value("lipSync") != "VisemeParameterOnly";
            }
        }

        internal static bool UsesTracking(RimeEmoProject project) => project && project.tracking.Any(t => t != null && t.clip && (t.stopBlink || t.stopLipSync));

        // "Animation" pauses VRChat's blinking (eyes) or lip sync (mouth) while the state plays; "Tracking" gives it back.
        // null leaves a part alone, so the avatar's own FX keeps control of what no expression pauses.
        internal static void AddTrackingControl(AnimatorState state, bool? pauseEyes, bool? pauseMouth)
        {
            if (pauseEyes == null && pauseMouth == null) return;
            var behaviour = state.AddStateMachineBehaviour(TrackingControlType);
            void Set(string field, bool? pause)
            {
                var info = behaviour.GetType().GetField(field);
                info.SetValue(behaviour, Enum.Parse(info.FieldType, pause == null ? "NoChange" : pause.Value ? "Animation" : "Tracking"));
            }
            Set("trackingEyes", pauseEyes); Set("trackingMouth", pauseMouth);
            EditorUtility.SetDirty(behaviour);
        }

        // The FX itself needs the SDK only for the pause behaviours.
        internal static List<string> ValidateBuild(RimeEmoProject project)
        {
            var errors = new List<string>();
            if (UsesTracking(project) && TrackingControlType == null) errors.Add(RimeEmoLocalization.T("まばたき・口パクを止めるにはVRChat SDKが必要です。"));
            return errors;
        }

        // The menu is made of Modular Avatar components on the applied object.
        internal static List<string> ValidateApply(RimeEmoProject project)
        {
            var errors = new List<string>();
            if (project && project.expressionMenu && (ParametersType == null || InstallerType == null || MenuItemType == null))
                errors.Add(RimeEmoLocalization.T("Expressions Menuへの追加には、Modular AvatarのMenu InstallerとMenu Itemが必要です。"));
            return errors;
        }

        // The menu lives on the applied object. MA Parameters declares the two parameters (not saved, so each avatar
        // load starts from the hands); MA Menu Installer and a Menu Item make a submenu in the avatar's menu, holding
        // the face lock and a submenu with one toggle per expression (Modular Avatar pages long lists).
        // Rebuilt on every apply; with the menu turned off, its components and children are removed.
        internal static void Apply(GameObject target, RimeEmoProject project)
        {
            foreach (var child in target.transform.Cast<Transform>().ToList()) Undo.DestroyObjectImmediate(child.gameObject);
            foreach (var type in new[] { MenuItemType, InstallerType, ParametersType })
            {
                var existing = type != null ? target.GetComponent(type) : null;
                if (existing) Undo.DestroyObjectImmediate(existing);
            }
            if (!project.expressionMenu) return;
            using (var serialized = new SerializedObject(Undo.AddComponent(target, ParametersType)))
            {
                var list = serialized.FindProperty("parameters"); list.arraySize = 2;
                Parameter(list.GetArrayElementAtIndex(0), LockParameter, "Bool");
                Parameter(list.GetArrayElementAtIndex(1), FaceParameter, "Int");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Undo.AddComponent(target, InstallerType);
            Item(target, RimeEmoLocalization.T("表情"), true, null, 0);
            Item(Child(target, RimeEmoLocalization.T("表情固定")), RimeEmoLocalization.T("表情固定"), false, LockParameter, 1);
            var choose = Child(target, RimeEmoLocalization.T("表情を選ぶ"));
            Item(choose, RimeEmoLocalization.T("表情を選ぶ"), true, null, 0);
            var clips = MenuClips(project);
            for (int i = 0; i < clips.Count; i++) Item(Child(choose, clips[i].name), clips[i].name, false, FaceParameter, i + 1);
        }

        // The applied object holds only what RimeEmo puts there: the merge, the menu components and menu item children.
        internal static bool OnlyOwnParts(GameObject target, Component merge)
        {
            bool IsType(Component c, Type type) => c && type != null && type.IsInstanceOfType(c);
            bool MenuOnly(Transform t) => t.Cast<Transform>().All(child =>
                child.GetComponents<Component>().All(c => c is Transform || IsType(c, MenuItemType)) && MenuOnly(child));
            return target.GetComponents<Component>().All(c => c is Transform || c == merge
                    || IsType(c, ParametersType) || IsType(c, InstallerType) || IsType(c, MenuItemType))
                && MenuOnly(target.transform);
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(child, "RimeEmo: Apply FX");
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static void Item(GameObject target, string label, bool submenu, string parameter, int value)
        {
            using (var serialized = new SerializedObject(Undo.AddComponent(target, MenuItemType)))
            {
                // ControlType values are VRChat's own: Toggle 102, SubMenu 103.
                serialized.FindProperty("Control.type").intValue = submenu ? 103 : 102;
                serialized.FindProperty("Control.name").stringValue = label;
                serialized.FindProperty("Control.parameter.name").stringValue = parameter ?? "";
                serialized.FindProperty("Control.value").floatValue = value;
                SetEnum(serialized.FindProperty("MenuSource"), "Children");
                serialized.FindProperty("label").stringValue = label;
                serialized.FindProperty("isSaved").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void Parameter(SerializedProperty config, string name, string type)
        {
            config.FindPropertyRelative("nameOrPrefix").stringValue = name;
            config.FindPropertyRelative("remapTo").stringValue = "";
            config.FindPropertyRelative("internalParameter").boolValue = false;
            config.FindPropertyRelative("isPrefix").boolValue = false;
            SetEnum(config.FindPropertyRelative("syncType"), type);
            config.FindPropertyRelative("localOnly").boolValue = false;
            config.FindPropertyRelative("defaultValue").floatValue = 0;
            config.FindPropertyRelative("saved").boolValue = false;
        }

        private static void SetEnum(SerializedProperty property, string name)
        {
            int index = property == null ? -1 : Array.IndexOf(property.enumNames, name);
            if (index < 0) throw new InvalidOperationException(RimeEmoLocalization.T("Modular Avatarの設定が対応していません: ") + (property?.name ?? name));
            property.enumValueIndex = index;
        }
    }
}
