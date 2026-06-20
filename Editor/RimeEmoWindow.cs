using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace RimeEmo.Editor
{
    public sealed class RimeEmoWindow : EditorWindow
    {
        private const string UxmlPath = "Assets/rimerime/RimeEmo/Editor/RimeEmoWindow.uxml";
        private const string UssPath = "Assets/rimerime/RimeEmo/Editor/RimeEmoWindow.uss";

        private GameObject avatarRoot;
        private RimeEmoAnalysisResult analysis;
        private RimeEmoDefaultSnapshot snapshot;
        private RimeEmoGenerationLog generationLog;
        private readonly List<RimeEmoApplyRule> rules = new List<RimeEmoApplyRule>();
        private RimeEmoApplyMode defaultApplyMode = RimeEmoApplyMode.AdditiveOffset;
        private const int ModularAvatarLayerPriority = 0;
        private List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
        private readonly List<string> rendererLabels = new List<string>();
        private int selectedRendererIndex;
        private readonly List<string> windowMessages = new List<string>();
        private readonly List<RimeEmoExpressionSlot> displayedSlots = new List<RimeEmoExpressionSlot>();
        private readonly List<RimeEmoBlendShapeSnapshotEntry> displayedSnapshotEntries = new List<RimeEmoBlendShapeSnapshotEntry>();
        private readonly List<RimeEmoCurveGenerationLog> displayedLogEntries = new List<RimeEmoCurveGenerationLog>();

        private ObjectField avatarField;
        private Button useSelectionButton;
        private Button analyzeButton;
        private Button snapshotButton;
        private Button generateButton;
        private Label controllerSummaryLabel;
        private Label slotSummaryLabel;
        private Label rendererSummaryLabel;
        private Label snapshotSummaryLabel;
        private Label generateHintLabel;
        private Label logSummaryLabel;
        private VisualElement messagesContainer;
        private VisualElement analysisWarningsContainer;
        private VisualElement rendererDropdownContainer;
        private DropdownField rendererDropdown;
        private ListView slotList;
        private ListView snapshotList;
        private ListView logList;

        [MenuItem("Tools/RimeEmo/Open RimeEmo Window")]
        public static void Open()
        {
            GetWindow<RimeEmoWindow>("RimeEmo");
        }

        private void OnEnable()
        {
            if (rules.Count == 0)
            {
                rules.AddRange(RimeEmoProfile.CreateDefaultRules());
            }

            TryAutoAssignAvatar();
            RefreshRenderers();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (tree != null)
            {
                tree.CloneTree(rootVisualElement);
            }
            else
            {
                rootVisualElement.Add(new Label("RimeEmoWindow.uxml が見つかりません: " + UxmlPath));
            }

            UnityEngine.UIElements.StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.StyleSheet>(UssPath);
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            BindElements();
            BuildDynamicFields();
            ConfigureLists();
            RegisterCallbacks();
            RefreshAll();
        }

        private void BindElements()
        {
            useSelectionButton = rootVisualElement.Q<Button>("useSelectionButton");
            analyzeButton = rootVisualElement.Q<Button>("analyzeButton");
            snapshotButton = rootVisualElement.Q<Button>("snapshotButton");
            generateButton = rootVisualElement.Q<Button>("generateButton");
            controllerSummaryLabel = rootVisualElement.Q<Label>("controllerSummaryLabel");
            slotSummaryLabel = rootVisualElement.Q<Label>("slotSummaryLabel");
            rendererSummaryLabel = rootVisualElement.Q<Label>("rendererSummaryLabel");
            snapshotSummaryLabel = rootVisualElement.Q<Label>("snapshotSummaryLabel");
            generateHintLabel = rootVisualElement.Q<Label>("generateHintLabel");
            logSummaryLabel = rootVisualElement.Q<Label>("logSummaryLabel");
            messagesContainer = rootVisualElement.Q<VisualElement>("messagesContainer");
            analysisWarningsContainer = rootVisualElement.Q<VisualElement>("analysisWarningsContainer");
            rendererDropdownContainer = rootVisualElement.Q<VisualElement>("rendererDropdownContainer");
            slotList = rootVisualElement.Q<ListView>("slotList");
            snapshotList = rootVisualElement.Q<ListView>("snapshotList");
            logList = rootVisualElement.Q<ListView>("logList");
        }

        private void BuildDynamicFields()
        {
            VisualElement avatarContainer = rootVisualElement.Q<VisualElement>("avatarFieldContainer");
            avatarField = new ObjectField("アバター") { objectType = typeof(GameObject), allowSceneObjects = true, value = avatarRoot };
            avatarContainer.Add(avatarField);

            rendererDropdown = new DropdownField("顔メッシュ");
            rendererDropdownContainer.Add(rendererDropdown);
        }

        private void ConfigureLists()
        {
            ConfigureSlotList();
            ConfigureSnapshotList();
            ConfigureLogList();
        }

        private void ConfigureSlotList()
        {
            if (slotList == null) { return; }
            slotList.itemsSource = displayedSlots;
            slotList.fixedItemHeight = 76;
            slotList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            slotList.makeItem = () =>
            {
                var row = new VisualElement();
                row.AddToClassList("slot-row");
                row.Add(new Label { name = "title" });
                row.Q<Label>("title").AddToClassList("row-title");
                row.Add(new Label { name = "subtitle" });
                row.Q<Label>("subtitle").AddToClassList("row-subtitle");
                row.Add(new Label { name = "metrics" });
                row.Q<Label>("metrics").AddToClassList("row-subtitle");
                row.Add(new Label { name = "warning" });
                row.Q<Label>("warning").AddToClassList("row-subtitle");
                return row;
            };
            slotList.bindItem = (element, index) =>
            {
                RimeEmoExpressionSlot slot = displayedSlots[index];
                element.Q<Label>("title").text = GetAnimationFileName(slot);
                element.Q<Label>("subtitle").text = slot.isFromFx ? "FX側" : "Gesture側";
                element.Q<Label>("metrics").text = BuildAnimationKindLabel(slot);
                element.Q<Label>("warning").text = slot.warnings.Count > 0 ? "注意: " + ToUserWarning(slot.warnings[0]) : string.Empty;
            };
        }

        private static string GetAnimationFileName(RimeEmoExpressionSlot slot)
        {
            if (slot == null || slot.sourceClip == null)
            {
                return "(不明なアニメーション)";
            }

            string path = AssetDatabase.GetAssetPath(slot.sourceClip);
            if (string.IsNullOrEmpty(path))
            {
                return slot.sourceClip.name;
            }

            return System.IO.Path.GetFileName(path) + " / " + slot.sourceClip.name;
        }

        private static string BuildAnimationKindLabel(RimeEmoExpressionSlot slot)
        {
            var labels = new List<string>();
            if (slot.isHandGestureDriven)
            {
                labels.Add("ハンドジェスチャー");
            }

            if (slot.isGimmickDriven)
            {
                labels.Add("ギミック・条件");
            }

            if (slot.hasDynamicCurves)
            {
                labels.Add("動きあり");
            }
            else
            {
                labels.Add("固定表情");
            }

            if (slot.isFromBlendTree)
            {
                labels.Add("BlendTree内");
            }

            return string.Join(" / ", labels);
        }
        private static string ToUserWarning(string warning)
        {
            if (warning.Contains("BlendTree"))
            {
                return "特殊な作りの表情です。反映結果を確認してください。";
            }

            if (warning.Contains("Packages"))
            {
                return "元データがPackage内にあります。コピーだけを変更します。";
            }

            if (warning.Contains("shared") || warning.Contains("共有"))
            {
                return "複数の表情で同じアニメーションが使われています。";
            }

            if (warning.Contains("Gesture"))
            {
                return "Gesture側で見つかった顔アニメーションです。";
            }

            return warning;
        }
        private void ConfigureSnapshotList()
        {
            snapshotList.itemsSource = displayedSnapshotEntries;
            snapshotList.fixedItemHeight = 42;
            snapshotList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            snapshotList.makeItem = () =>
            {
                var row = new VisualElement();
                row.AddToClassList("snapshot-row");
                row.Add(new Label { name = "title" });
                row.Q<Label>("title").AddToClassList("row-title");
                row.Add(new Label { name = "value" });
                row.Q<Label>("value").AddToClassList("row-subtitle");
                return row;
            };
            snapshotList.bindItem = (element, index) =>
            {
                RimeEmoBlendShapeSnapshotEntry entry = displayedSnapshotEntries[index];
                element.Q<Label>("title").text = entry.blendShapeName;
                element.Q<Label>("value").text = "番号 " + entry.blendShapeIndex + " / 値 " + entry.value.ToString("0.###");
            };
        }

        private void ConfigureLogList()
        {
            logList.itemsSource = displayedLogEntries;
            logList.fixedItemHeight = 54;
            logList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            logList.makeItem = () =>
            {
                var row = new VisualElement();
                row.AddToClassList("log-row");
                row.Add(new Label { name = "title" });
                row.Q<Label>("title").AddToClassList("row-title");
                row.Add(new Label { name = "message" });
                row.Q<Label>("message").AddToClassList("row-subtitle");
                return row;
            };
            logList.bindItem = (element, index) =>
            {
                RimeEmoCurveGenerationLog entry = displayedLogEntries[index];
                element.Q<Label>("title").text = entry.propertyName + "  [" + entry.mode + "]";
                element.Q<Label>("message").text = entry.bindingPath + " / " + entry.message;
            };
        }

        private void RegisterCallbacks()
        {
            avatarField.RegisterValueChangedCallback(evt =>
            {
                avatarRoot = NormalizeAvatarRoot(evt.newValue as GameObject);
                avatarField.SetValueWithoutNotify(avatarRoot);
                analysis = null;
                snapshot = null;
                generationLog = null;
                RefreshRenderers();
                RefreshAll();
            });

            useSelectionButton.clicked += () =>
            {
                TryAutoAssignAvatar();
                avatarField.SetValueWithoutNotify(avatarRoot);
                analysis = null;
                snapshot = null;
                generationLog = null;
                RefreshRenderers();
                RefreshAll();
            };

            analyzeButton.clicked += () =>
            {
                windowMessages.Clear();
                analysis = RimeEmoAnalyzer.Analyze(avatarRoot, GetSelectedRenderer());
                generationLog = null;
                RefreshAll();
            };

            rendererDropdown.RegisterValueChangedCallback(evt =>
            {
                int index = rendererLabels.IndexOf(evt.newValue);
                if (index >= 0)
                {
                    selectedRendererIndex = index;
                    RefreshRendererSummary();
                }
            });

            snapshotButton.clicked += () =>
            {
                if (selectedRendererIndex >= 0 && selectedRendererIndex < renderers.Count)
                {
                    SaveSnapshot(renderers[selectedRendererIndex]);
                    RefreshAll();
                }
            };

            generateButton.clicked += () =>
            {
                if (CanGenerate() && EditorUtility.DisplayDialog(
                        "RimeEmo 作成",
                        "Assets/RimeEmoGenerated 以下に作成します。同名の生成物は上書きされます。元のAnimatorControllerとAnimationClipは編集しません。",
                        "生成",
                        "キャンセル"))
                {
                    Generate();
                    RefreshAll();
                }
            };
        }

        private void RefreshAll()
        {
            RefreshMessages();
            RefreshAnalyzeSummary();
            RefreshSlots();
            RefreshRendererDropdown();
            RefreshSnapshot();
            RefreshGenerateState();
            RefreshLog();
        }

        private void RefreshMessages()
        {
            messagesContainer.Clear();
            if (avatarRoot != null && avatarRoot.GetComponent<VRCAvatarDescriptor>() == null)
            {
                AddMessage(messagesContainer, "選択されたオブジェクトにVRChatアバター設定がありません。", "message-warning");
            }

            if (!RimeEmoInstaller.IsModularAvatarAvailable())
            {
                AddMessage(messagesContainer, "Modular Avatarが見つかりません。作成はできますが、アバターへの自動設定は失敗します。", "message-error");
            }

            foreach (string message in windowMessages)
            {
                AddMessage(messagesContainer, message, message.StartsWith("Error:") ? "message-error" : "message-info");
            }
        }

        private static void AddMessage(VisualElement container, string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            container.Add(label);
        }

        private void RefreshAnalyzeSummary()
        {
            analysisWarningsContainer.Clear();
            analyzeButton.SetEnabled(avatarRoot != null);
            if (analysis == null)
            {
                controllerSummaryLabel.text = "未解析";
                return;
            }

            controllerSummaryLabel.text = BuildApproachSummary();
            foreach (string warning in analysis.warnings)
            {
                AddMessage(analysisWarningsContainer, warning, "message-warning");
            }
        }

        private void RefreshSlots()
        {
            displayedSlots.Clear();
            if (analysis != null)
            {
                displayedSlots.AddRange(analysis.slots);
                displayedSlots.AddRange(analysis.gestureSlots);
            }
            slotSummaryLabel.text = BuildApproachSummary();
            if (slotList != null) { slotList.RefreshItems(); }
        }

        private string BuildApproachSummary()
        {
            if (analysis == null)
            {
                return "まだ解析していません";
            }

            int handGestureCount = analysis.slots.Count(slot => slot.isHandGestureDriven);
            int gimmickCount = analysis.slots.Count(slot => slot.isGimmickDriven);
            int dynamicCount = analysis.slots.Count(slot => slot.hasDynamicCurves);

            return "FX側: " + analysis.slots.Count
                + " / Gesture側: " + analysis.gestureSlots.Count
                + " / ハンドジェスチャー: " + handGestureCount
                + " / ギミック・条件: " + gimmickCount
                + " / 動きのある表情: " + dynamicCount;
        }
        private SkinnedMeshRenderer GetSelectedRenderer()
        {
            if (selectedRendererIndex < 0 || selectedRendererIndex >= renderers.Count)
            {
                return null;
            }

            return renderers[selectedRendererIndex];
        }

        private void RefreshRendererDropdown()
        {
            rendererDropdown.choices = rendererLabels;
            if (rendererLabels.Count > 0)
            {
                selectedRendererIndex = Mathf.Clamp(selectedRendererIndex, 0, rendererLabels.Count - 1);
                rendererDropdown.SetValueWithoutNotify(rendererLabels[selectedRendererIndex]);
            }
            else
            {
                rendererDropdown.SetValueWithoutNotify(string.Empty);
            }

            RefreshRendererSummary();
        }

        private void RefreshRendererSummary()
        {
            if (renderers.Count == 0)
            {
                rendererSummaryLabel.text = "対象Rendererが見つかりません。";
                snapshotButton.SetEnabled(false);
                return;
            }

            SkinnedMeshRenderer renderer = renderers[Mathf.Clamp(selectedRendererIndex, 0, renderers.Count - 1)];
            Mesh mesh = renderer.sharedMesh;
            rendererSummaryLabel.text = "メッシュ: " + (mesh != null ? mesh.name : "なし") + " / 顔の項目: " + (mesh != null ? mesh.blendShapeCount.ToString() : "0");
            snapshotButton.SetEnabled(mesh != null);
        }

        private void RefreshSnapshot()
        {
            displayedSnapshotEntries.Clear();
            if (snapshot != null)
            {
                displayedSnapshotEntries.AddRange(snapshot.entries);
                snapshotSummaryLabel.text = "デフォルト顔: " + snapshot.name + " / 対象: " + snapshot.rendererPath + " / 項目: " + snapshot.entries.Count;
                int mismatch = CountSnapshotMismatches();
                if (mismatch > 0)
                {
                    snapshotSummaryLabel.text += " / 不一致: " + mismatch;
                }
            }
            else
            {
                snapshotSummaryLabel.text = "デフォルト顔はまだ保存されていません";
            }

            snapshotList.RefreshItems();
        }

        private void RefreshGenerateState()
        {
            bool canGenerate = CanGenerate();
            generateButton.SetEnabled(canGenerate);
            generateHintLabel.text = canGenerate
                ? "作成できます。元アセットは直接編集しません。"
                : "アバター、対象になる表情、デフォルト顔が必要です。";
        }

        private void RefreshLog()
        {
            displayedLogEntries.Clear();
            if (generationLog != null)
            {
                foreach (RimeEmoClipGenerationLog clip in generationLog.clips)
                {
                    displayedLogEntries.AddRange(clip.curves);
                }

                logSummaryLabel.text = "作成したAnimator: " + generationLog.generatedControllerPath + " / 変更: " + displayedLogEntries.Count;
            }
            else
            {
                logSummaryLabel.text = "まだ作成していません。";
            }

            logList.RefreshItems();
        }

        private bool CanGenerate()
        {
            return avatarRoot != null && analysis != null && analysis.fxController != null && (analysis.slots.Count + analysis.gestureSlots.Count) > 0 && snapshot != null;
        }

        private void Generate()
        {
            windowMessages.Clear();
            var options = new RimeEmoGenerateOptions
            {
                avatarRoot = avatarRoot,
                sourceFxController = analysis.fxController,
                sourceGestureController = analysis.gestureController as AnimatorController,
                slots = analysis.slots,
                gestureSlots = analysis.gestureSlots,
                snapshot = snapshot,
                rules = new List<RimeEmoApplyRule>(rules),
                defaultApplyMode = defaultApplyMode,
                modularAvatarLayerPriority = ModularAvatarLayerPriority,
                overwrite = true
            };

            RimeEmoGenerateResult result = RimeEmoGenerator.Generate(options);
            foreach (string error in result.errors)
            {
                windowMessages.Add("Error: " + error);
            }

            foreach (string warning in result.warnings)
            {
                windowMessages.Add("Warning: " + warning);
            }

            generationLog = result.log;
            if (result.generatedController != null)
            {
                bool installed = RimeEmoInstaller.Install(avatarRoot, result.generatedController, ModularAvatarLayerPriority, out string message, "FX");
                windowMessages.Add(message);
                if (!installed && generationLog != null)
                {
                    generationLog.warnings.Add(message);
                }
            }

            if (result.generatedGestureController != null)
            {
                bool installed = RimeEmoInstaller.Install(avatarRoot, result.generatedGestureController, ModularAvatarLayerPriority, out string message, "Gesture");
                windowMessages.Add(message);
                if (!installed && generationLog != null)
                {
                    generationLog.warnings.Add(message);
                }
            }

            if (generationLog != null)
            {
                EditorUtility.SetDirty(generationLog);
                AssetDatabase.SaveAssets();
            }
        }

        private void SaveSnapshot(SkinnedMeshRenderer renderer)
        {
            windowMessages.Clear();
            if (avatarRoot == null || renderer == null || renderer.sharedMesh == null)
            {
                windowMessages.Add("Error: アバターまたは顔メッシュがありません。");
                return;
            }

            string avatarName = RimeEmoPathUtility.SafeFileName(avatarRoot.name);
            RimeEmoPathUtility.EnsureAvatarGeneratedFolders(avatarName, out _, out _, out string profileFolder, out _);
            string snapshotPath = profileFolder + "/" + avatarName + "_DefaultSnapshot.asset";
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(snapshotPath)))
            {
                if (!EditorUtility.DisplayDialog("デフォルト顔の上書き", "保存済みのデフォルト顔を上書きします。", "上書き", "キャンセル"))
                {
                    return;
                }

                AssetDatabase.DeleteAsset(snapshotPath);
            }

            var newSnapshot = ScriptableObject.CreateInstance<RimeEmoDefaultSnapshot>();
            newSnapshot.avatarName = avatarRoot.name;
            newSnapshot.rendererPath = RimeEmoPathUtility.GetTransformPath(avatarRoot.transform, renderer.transform);
            newSnapshot.meshName = renderer.sharedMesh.name;
            newSnapshot.savedAtIso8601 = DateTime.Now.ToString("o");

            Mesh mesh = renderer.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendShapeName = mesh.GetBlendShapeName(i);
                float value = renderer.GetBlendShapeWeight(i);
                newSnapshot.entries.Add(new RimeEmoBlendShapeSnapshotEntry
                {
                    rendererPath = newSnapshot.rendererPath,
                    meshName = mesh.name,
                    blendShapeName = blendShapeName,
                    blendShapeIndex = i,
                    value = value,
                    key = RimeEmoDefaultSnapshot.BuildKey(newSnapshot.rendererPath, blendShapeName)
                });
            }

            AssetDatabase.CreateAsset(newSnapshot, snapshotPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            snapshot = newSnapshot;
            windowMessages.Add("デフォルト顔を保存しました: " + snapshotPath);
        }

        private void RefreshRenderers()
        {
            renderers.Clear();
            rendererLabels.Clear();
            selectedRendererIndex = 0;
            if (avatarRoot == null)
            {
                return;
            }

            renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer.sharedMesh != null && renderer.sharedMesh.blendShapeCount > 0)
                .OrderByDescending(ScoreRenderer)
                .ToList();

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                string path = RimeEmoPathUtility.GetTransformPath(avatarRoot.transform, renderer.transform);
                string label = string.IsNullOrEmpty(path) ? renderer.name : path;
                rendererLabels.Add(label + " (" + renderer.sharedMesh.blendShapeCount + ")");
            }
        }

        private static int ScoreRenderer(SkinnedMeshRenderer renderer)
        {
            int score = renderer.sharedMesh != null ? renderer.sharedMesh.blendShapeCount : 0;
            string text = (renderer.name + " " + (renderer.sharedMesh != null ? renderer.sharedMesh.name : string.Empty)).ToLowerInvariant();
            if (text.Contains("face") || text.Contains("head") || text.Contains("body"))
            {
                score += 1000;
            }

            return score;
        }

        private int CountSnapshotMismatches()
        {
            if (analysis == null || snapshot == null)
            {
                return 0;
            }

            var snapshotKeys = new HashSet<string>(snapshot.entries.Select(entry => entry.key));
            int mismatch = 0;
            foreach (RimeEmoExpressionSlot slot in analysis.slots)
            {
                foreach (RimeEmoBlendShapeBindingInfo binding in slot.blendShapeBindings)
                {
                    string key = RimeEmoDefaultSnapshot.BuildKey(binding.rendererPath, binding.blendShapeName);
                    if (!snapshotKeys.Contains(key))
                    {
                        mismatch++;
                    }
                }
            }

            return mismatch;
        }

        private void TryAutoAssignAvatar()
        {
            avatarRoot = NormalizeAvatarRoot(Selection.activeGameObject);
        }

        private static GameObject NormalizeAvatarRoot(GameObject selected)
        {
            if (selected == null)
            {
                return null;
            }

            VRCAvatarDescriptor descriptor = selected.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                descriptor = selected.GetComponentInParent<VRCAvatarDescriptor>();
            }

            if (descriptor == null)
            {
                descriptor = selected.GetComponentInChildren<VRCAvatarDescriptor>(true);
            }

            return descriptor != null ? descriptor.gameObject : selected;
        }
    }
}


















