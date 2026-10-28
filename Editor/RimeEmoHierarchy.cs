using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    /// <summary>
    /// Adds a small, discoverable entry point to the avatar root in the Hierarchy.
    /// The descriptor check is deliberately name based so RimeEmo remains usable
    /// when the VRChat SDK is not installed in the current project.
    /// </summary>
    [InitializeOnLoad]
    internal static class RimeEmoHierarchy
    {
        // Measured from the font, so the label is never clipped (a fixed 62px cut it to "RimeEmc").
        private static float buttonWidth;
        private static readonly GUIContent ButtonContent = new GUIContent("RimeEmo", "Open RimeEmo for this avatar");
        // Every visible row is drawn on each repaint; walking up a deep bone chain each time was wasted work.
        private static readonly Dictionary<int, bool> isRootRow = new Dictionary<int, bool>();

        static RimeEmoHierarchy()
        {
            EditorApplication.hierarchyWindowItemOnGUI += DrawItem;
            EditorApplication.hierarchyChanged += isRootRow.Clear;
        }

        private static void DrawItem(int instanceId, Rect selectionRect)
        {
            if (!isRootRow.TryGetValue(instanceId, out bool root))
            {
                var row = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                // Only draw once, on the resolved avatar root rather than every child row.
                isRootRow[instanceId] = root = row && ResolveAvatarRoot(row) == row;
            }
            if (!root) return;
            var avatar = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (!avatar) return;

            if (buttonWidth <= 0) buttonWidth = Mathf.Ceil(EditorStyles.miniButton.CalcSize(ButtonContent).x) + 4f;
            var buttonRect = new Rect(
                selectionRect.xMax - buttonWidth - 4f,
                selectionRect.y,
                buttonWidth,
                selectionRect.height);
            if (GUI.Button(buttonRect, ButtonContent, EditorStyles.miniButton))
                RimeEmoWindow.OpenForAvatar(avatar);
        }

        internal static GameObject ResolveAvatarRoot(GameObject item)
        {
            if (!item || EditorUtility.IsPersistent(item) || !item.scene.IsValid()) return null;
            GameObject animatorRoot = null;
            for (var current = item.transform; current; current = current.parent)
            {
                var candidate = current.gameObject;
                if (IsAvatarRoot(candidate)) return candidate;
                if (candidate.GetComponent<Animator>()) animatorRoot = candidate;
            }
            return animatorRoot;
        }

        private static bool IsAvatarRoot(GameObject candidate)
        {
            if (!candidate || EditorUtility.IsPersistent(candidate) || !candidate.scene.IsValid()) return false;

            foreach (var component in candidate.GetComponents<Component>())
            {
                if (component && component.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor")
                    return true;
            }

            // Settings are required to be a direct child of the avatar root.
            foreach (var child in candidate.transform)
                if (((Transform)child).GetComponent<RimeEmoAvatarSettings>()) return true;

            // Keep the button useful before SDK setup by accepting a root Animator.
            return false;
        }
    }
}
