using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // Top-bar Apply and the Gesture rail badge: tells whether the avatar is behind the current edits.
    public sealed partial class RimeEmoWindow
    {
        private RimeEmoBuildState.State applyState;
        private bool applyStateDirty = true;
        private double nextApplyStateCheck;

        private void BindApplyState()
        {
            var badge = new VisualElement { name = "gesture-badge", pickingMode = PickingMode.Ignore };
            badge.AddToClassList("re-rail-badge");
            UI<Button>("gesture-settings").Add(badge);
            Button("apply", ApplyFromTopBar);
            RefreshApplyState();
        }

        private void InvalidateApplyState() => applyStateDirty = true;

        // Edits arrive in bursts (slider drags, undo), so the fingerprint is recomputed at most a few times per second.
        private void TickApplyState(double now)
        {
            if (!applyStateDirty || now < nextApplyStateCheck) return;
            applyStateDirty = false; nextApplyStateCheck = now + 0.25;
            var next = RimeEmoBuildState.Describe(project, avatar);
            if (next == applyState) return;
            applyState = next;
            RefreshApplyState();
        }

        private void RefreshApplyState()
        {
            if (toolkitRoot == null) return;
            bool stale = applyState == RimeEmoBuildState.State.Stale;
            bool applied = applyState == RimeEmoBuildState.State.UpToDate;
            var button = UI<Button>("apply");
            button.style.display = applyState == RimeEmoBuildState.State.Unavailable ? DisplayStyle.None : DisplayStyle.Flex;
            button.EnableInClassList("re-button--filled", stale);
            button.EnableInClassList("re-button--tonal", applyState == RimeEmoBuildState.State.NotApplied);
            button.EnableInClassList("re-apply--done", applied);
            // Re-applying identical content would only add another build folder.
            button.SetEnabled(!applied);
            SetButtonCaption(button, RimeEmoLocalization.T(stale ? "変更を適用" : applied ? "適用済み" : "アバターに適用"));
            button.tooltip = RimeEmoLocalization.T(stale ? "前回の適用後に変更があります。FXを再生成してアバターへ適用します。"
                : applied ? "アバターには最新の内容が適用されています。"
                : "Gestureの割り当てからFXを生成し、アバターへ適用します。");
            UI<VisualElement>("gesture-badge").style.display = stale ? DisplayStyle.Flex : DisplayStyle.None;
            // One filled button at a time: while the avatar is behind, applying is the next step (it also saves), so Save steps back to tonal.
            var save = UI<Button>("save");
            save.EnableInClassList("re-button--filled", !stale);
            save.EnableInClassList("re-button--tonal", stale);
        }

        private void ApplyFromTopBar()
        {
            if (!project || !avatar) return;
            EndStaticGesture(); retainedTimeline?.EndInteraction();
            // The build reads saved motions; an unsaved draft would silently be left out.
            // An empty draft (a fresh "new motion" nobody has filled) has nothing to lose, so it never asks.
            bool draftHasContent = ImportedEditing || (motion && motion.tracks.Any(t => t != null && t.states.Count > 0));
            if (draftHasContent && !FinishDraft()) return;
            var errors = RimeEmoApply.Validate(project, avatar);
            if (errors.Count != 0)
            {
                // The Gesture page lists every error next to the settings that cause it.
                ShowPage(1);
                ShowNotification(new GUIContent(RimeEmoLocalization.T("適用できません。Gestureページの内容を確認してください。")));
                return;
            }
            RimeEmoApply.Apply(project, avatar, false);
            applyStateDirty = true; nextApplyStateCheck = 0;
            gesturePanel?.MarkDirty();
            ShowNotification(new GUIContent(RimeEmoLocalization.T("アバターに適用しました。Sceneを保存してください。")));
        }
    }
}
