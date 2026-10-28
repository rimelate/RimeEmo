using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        internal const string GuidePreference = "RimeEmo.Guide.Seen.v1";
        [SerializeField] private bool guideOpen;
        [SerializeField] private int guideStep;
        private bool guideInitialized;
        private VisualElement guideCard, guideTarget;

        private static readonly string[] GuideTargets =
        {
            "language", "avatar", "static-mode", "dynamic-mode", "capture-frame", "save", "gesture-settings"
        };
        private static readonly string[] GuideTitles =
        {
            "表示する言語を選ぶ", "編集するアバターを選ぶ", "表情を作る", "動きを作る", "動きから表情を取り出す", "作ったものを保存する", "Gestureに割り当てる"
        };
        private static readonly string[] GuideText =
        {
            "Language / 言語は常に右上にあります。日本語とEnglishを切り替えられます。このガイドも選んだ言語に切り替わります。",
            "上のAvatar欄にSceneのアバターを指定します。HierarchyのRimeEmoボタンから開くと自動で指定されます。初めてのアバターは、左下の設定で新規セットアップします。",
            "新しい表情を作るか、素材を追加して選び、Shape Keyを調整します。その他メニューには複製や書き出しがあります。プレビューで仕上がりを確認できます。",
            "新しい動きはタイムライン上部から作成。既存の動きは左の素材から開きます。キューを選び、再生位置で分割。端でトリミング、右の詳細で調整・複製・削除します。",
            "時刻を合わせ、編集中の表情をプレビューで確認して、その下のこの時刻の表情を取り出すを押します。表情の編集に切り替わります。元の動きと再生位置は保持します。",
            "右上の保存で編集内容を保存します。動きを別のファイルに保存する場合は、名前欄の横の別名で保存を使います。",
            "左のGestureで左右の手に表情や動きを割り当て、再生成して適用を押します。適用後に表情や動きを直したときは、上部の変更を適用で反映できます。適用後はSceneを保存してください。このガイドは右上の？からいつでも開けます。"
        };

        private void BindGuide()
        {
            UI<Button>("show-guide").clicked += () => { guideStep = 0; guideOpen = true; RenderGuide(); };
            if (!guideInitialized)
            {
                guideInitialized = true;
                if (!Application.isBatchMode) StartInitialGuide();
            }
            // Tooltips explain the less frequent actions without adding permanent text to the workspace.
            Explain("save", "現在のモードの編集内容を保存します。");
            Explain("gesture-settings", "手のGestureへの割り当てと、アバターへの適用を行います。");
            Explain("new-motion", "空のタイムラインから新しい動きを作ります。");
            Explain("save-as", "編集中の動きを別名で保存します。");
            Explain("material-more", "選択した表情の複製、書き出しなどを行います。");
            Explain("material-edit", "選んだ素材の種類に合わせて編集欄を開きます。別の動きを開く場合は未保存の変更を確認します。");
            Explain("setup", "このアバター用の設定と編集素材を新しく作ります。");
            Explain("part-settings", "Shape Keyがどの顔パーツに属するかを指定します。");
            Explain("reload-preview", "Sceneのアバターをプレビューへ読み直します。");
            Explain("fit", "タイムライン全体が収まるように表示します。");
            Explain("add", "選択したパーツと現在時刻に表情キーを追加します。");
            RenderGuide();
        }

        private void Explain(string id, string japanese) => UI<Button>(id).tooltip = RimeEmoLocalization.T(japanese);

        private void StartInitialGuide()
        {
            if (EditorPrefs.GetBool(GuidePreference, false)) return;
            guideOpen = true; guideStep = 0;
            EditorPrefs.SetBool(GuidePreference, true);
        }

        private void RenderGuide()
        {
            guideTarget?.RemoveFromClassList("re-guide-target");
            guideCard?.RemoveFromHierarchy();
            guideTarget = guideCard = null;
            if (!guideOpen) return;
            guideStep = Mathf.Clamp(guideStep, 0, GuideTargets.Length - 1);
            UpdateGuideHighlight();
            guideCard = new VisualElement { name = "guide-card" };
            guideCard.AddToClassList("re-guide-card");
            guideCard.Add(new Label((guideStep + 1) + " / " + GuideTargets.Length + "  " + RimeEmoLocalization.T(GuideTitles[guideStep])) { name = "guide-title" });
            var body = new Label(RimeEmoLocalization.T(GuideText[guideStep])); body.AddToClassList("re-guide-text"); guideCard.Add(body);
            var controls = new VisualElement(); controls.AddToClassList("re-toolbar");
            var back = new Button(() => { guideStep--; RenderGuide(); }) { text = RimeEmoLocalization.T("戻る") }; back.SetEnabled(guideStep > 0);
            var next = new Button(() => { if (guideStep == GuideTargets.Length - 1) CloseGuide(); else { guideStep++; RenderGuide(); } })
                { text = RimeEmoLocalization.T(guideStep == GuideTargets.Length - 1 ? "完了" : "次へ") };
            var close = new Button(CloseGuide) { text = RimeEmoLocalization.T("閉じる") };
            foreach (var button in new[] { back, next, close }) { button.AddToClassList("re-button"); controls.Add(button); }
            guideCard.Add(controls);
            // Inside the .re-app element: the theme's colour variables are defined there, and outside it the card
            // had no background and the preview showed through.
            (UI<VisualElement>("dynamic-root") ?? toolkitRoot).Add(guideCard);
        }

        private void UpdateGuideHighlight()
        {
            guideTarget?.RemoveFromClassList("re-guide-target");
            guideTarget = null;
            if (!guideOpen || toolkitRoot == null) return;
            string target = GuideTargets[Mathf.Clamp(guideStep, 0, GuideTargets.Length - 1)];
            // Capture is unavailable until a project and its motion timeline are displayed.
            if (target == "capture-frame" && (mode != 1 || page != 0 || !project || (!ImportedEditing && (!motion || motion.project != project))))
                target = "dynamic-mode";
            guideTarget = UI<VisualElement>(target);
            guideTarget?.AddToClassList("re-guide-target");
        }

        private void CloseGuide()
        {
            guideOpen = false;
            EditorPrefs.SetBool(GuidePreference, true);
            RenderGuide();
        }
    }
}

