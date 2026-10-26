using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // Standalone host for the Gesture panel. The main window shows the same panel on its Gesture page.
    public sealed class RimeEmoGestureWindow : EditorWindow
    {
        [SerializeField] private RimeEmoProject project;
        [SerializeField] private GameObject avatar;
        private AnimationClip initialClip;
        private RimeEmoMotion initialMotion;
        private RimeEmoGesturePanel panel;

        public static void Open(RimeEmoProject project, GameObject avatar, AnimationClip clip = null, RimeEmoMotion motion = null)
        {
            var window = GetWindow<RimeEmoGestureWindow>("RimeEmo Gesture");
            window.project = project; window.avatar = avatar; window.initialClip = clip; window.initialMotion = motion;
            window.minSize = new Vector2(720, 540);
            if (window.panel != null) { window.panel.InitialClip = clip; window.panel.InitialMotion = motion; window.panel.MarkDirty(); }
            window.Show();
        }

        private void OnEnable() { Undo.undoRedoPerformed += RefreshLater; EditorApplication.projectChanged += RefreshLater; EditorApplication.hierarchyChanged += HierarchyChanged; RimeEmoLocalization.Changed += LanguageChanged; }
        private void OnDisable() { Undo.undoRedoPerformed -= RefreshLater; EditorApplication.projectChanged -= RefreshLater; EditorApplication.hierarchyChanged -= HierarchyChanged; RimeEmoLocalization.Changed -= LanguageChanged; }
        private void HierarchyChanged() { if (project && !avatar) { RestoreAvatar(); RefreshLater(); } }
        private void LanguageChanged() { CreateGUI(); }
        private void RefreshLater() => panel?.MarkDirty();
        private void Update() => panel?.Tick();

        // Avatar and settings always belong together: assignments are applied to the avatar shown here.
        private void SelectAvatar(GameObject value)
        {
            avatar = value;
            project = value ? RimeEmoAvatarStorage.Load(value) : null;
        }

        private void SelectProject(RimeEmoProject value)
        {
            var found = value ? RimeEmoAvatarStorage.FindAvatar(value) : null;
            if (value && !found)
                throw new System.InvalidOperationException(RimeEmoLocalization.T("このProjectのアバターが、開いているSceneに見つかりません。先に上のAvatar欄にアバターを指定してください。"));
            project = value; avatar = found;
        }

        // A restart or a scene reload drops the avatar reference but keeps the settings.
        private void RestoreAvatar()
        {
            if (!project || avatar) return;
            avatar = RimeEmoAvatarStorage.FindAvatar(project);
            if (!avatar) project = null;
        }

        public void CreateGUI()
        {
            RestoreAvatar();
            rootVisualElement.Clear();
            RimeEmoLocalization.MarkLanguage(rootVisualElement);
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this))).Replace('\\', '/') + "/UI/";
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "RimeEmoGestureWindow.uxml");
            if (!tree) { rootVisualElement.Add(new Label(RimeEmoLocalization.T("Gesture UXMLが見つかりません。"))); return; }
            foreach (var name in new[] { "RimeEmoTheme.uss", "RimeEmoCustom.uss" })
            { var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(folder + name); if (sheet) rootVisualElement.styleSheets.Add(sheet); }
            panel = new RimeEmoGesturePanel(() => project, SelectProject, () => avatar, SelectAvatar, false)
            { InitialClip = initialClip, InitialMotion = initialMotion };
            panel.Build(rootVisualElement, tree, folder);
        }
    }
}
