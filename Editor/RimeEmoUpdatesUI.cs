using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        private void BindUpdates()
        {
            UI<Toggle>("updates-automatic").SetValueWithoutNotify(RimeEmoUpdates.Automatic);
            UI<Toggle>("updates-automatic").RegisterValueChangedCallback(e => RimeEmoUpdates.Automatic = e.newValue);
            Button("updates-check", () => RimeEmoUpdates.Check(true));
            Button("updates-release", RimeEmoUpdates.OpenRelease);
            Button("updates-vcc", RimeEmoUpdates.OpenVcc);
            Button("updates-dismiss", RimeEmoUpdates.Dismiss);
            Button("updates-copy-url", () => UnityEditor.EditorGUIUtility.systemCopyBuffer = RimeEmoUpdates.ListingUrl);
            RefreshUpdates();
            // This schedule pauses when the UI is detached, including when the window is closed.
            toolkitRoot.schedule.Execute(() => RimeEmoUpdates.Check()).Every(60000);
            RimeEmoUpdates.Check();
        }

        private void RefreshUpdates()
        {
            if (toolkitRoot == null || UI<Label>("updates-version") == null) return;
            string version = RimeEmoUpdates.CurrentVersion;
            UI<Label>("updates-version").text = "RimeEmo " + (string.IsNullOrEmpty(version) ? "?" : version);
            UI<Toggle>("updates-automatic").SetValueWithoutNotify(RimeEmoUpdates.Automatic);
            UI<VisualElement>("updates-banner").style.display = RimeEmoUpdates.ShowBanner ? DisplayStyle.Flex : DisplayStyle.None;
            if (RimeEmoUpdates.HasUpdate)
                UI<Label>("updates-message").text = string.Format(RimeEmoLocalization.T("RimeEmo {0} が公開されています（現在 {1}）。"), RimeEmoUpdates.Latest.version, version);
            string status;
            switch (RimeEmoUpdates.State)
            {
                case RimeEmoUpdates.CheckState.Checking: status = "更新を確認中…"; break;
                case RimeEmoUpdates.CheckState.Failed: status = "更新を確認できませんでした。通信状態を確認して、再試行してください。"; break;
                case RimeEmoUpdates.CheckState.UnknownVersion: status = "インストール済みのバージョンを取得できません。"; break;
                default:
                    status = RimeEmoUpdates.HasUpdate ? "新しいバージョンがあります。VCC / ALCOMで更新してください。"
                        : RimeEmoUpdates.Latest != null ? "利用できる更新はありません。" : "まだ更新を確認していません。";
                    break;
            }
            UI<Label>("updates-status").text = RimeEmoLocalization.T(status);
            UI<Button>("updates-check").SetEnabled(RimeEmoUpdates.State != RimeEmoUpdates.CheckState.Checking);
        }
    }
}
