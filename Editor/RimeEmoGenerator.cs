using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RimeEmo.Editor
{
    public sealed class RimeEmoGenerateOptions
    {
        public GameObject avatarRoot;
        public AnimatorController sourceFxController;
        public AnimatorController sourceGestureController;
        public List<RimeEmoExpressionSlot> slots = new List<RimeEmoExpressionSlot>();
        public List<RimeEmoExpressionSlot> gestureSlots = new List<RimeEmoExpressionSlot>();
        public RimeEmoDefaultSnapshot snapshot;
        public List<RimeEmoApplyRule> rules = new List<RimeEmoApplyRule>();
        public RimeEmoApplyMode defaultApplyMode = RimeEmoApplyMode.AdditiveOffset;
        public int modularAvatarLayerPriority;
        public bool overwrite;
    }

    public sealed class RimeEmoGenerateResult
    {
        public AnimatorController generatedController;
        public AnimatorController generatedGestureController;
        public RimeEmoGenerationLog log;
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
    }

    public static class RimeEmoGenerator
    {
        public static RimeEmoGenerateResult Generate(RimeEmoGenerateOptions options)
        {
            var result = new RimeEmoGenerateResult();
            if (options == null || options.avatarRoot == null || options.sourceFxController == null || options.snapshot == null)
            {
                result.errors.Add("Avatar、FX Controller、Snapshotのいずれかがありません。");
                return result;
            }

            string avatarName = RimeEmoPathUtility.SafeFileName(options.avatarRoot.name);
            RimeEmoPathUtility.EnsureAvatarGeneratedFolders(avatarName, out string controllerFolder, out string clipFolder, out string profileFolder, out string logFolder);

            var log = ScriptableObject.CreateInstance<RimeEmoGenerationLog>();
            log.avatarName = options.avatarRoot.name;
            log.sourceControllerPath = AssetDatabase.GetAssetPath(options.sourceFxController);
            log.generatedAtIso8601 = DateTime.Now.ToString("o");
            result.log = log;

            result.generatedController = GenerateControllerForLayer(
                "FX",
                avatarName,
                options.sourceFxController,
                options.slots,
                controllerFolder,
                clipFolder,
                options,
                result,
                log);

            if (result.errors.Count > 0)
            {
                return result;
            }

            log.generatedControllerPath = result.generatedController != null ? AssetDatabase.GetAssetPath(result.generatedController) : string.Empty;

            if (options.sourceGestureController != null && options.gestureSlots != null && options.gestureSlots.Count > 0)
            {
                result.generatedGestureController = GenerateControllerForLayer(
                    "Gesture",
                    avatarName,
                    options.sourceGestureController,
                    options.gestureSlots,
                    controllerFolder,
                    clipFolder,
                    options,
                    result,
                    log);

                if (result.generatedGestureController != null)
                {
                    log.generatedGestureControllerPath = AssetDatabase.GetAssetPath(result.generatedGestureController);
                }
            }

            if (result.errors.Count > 0)
            {
                return result;
            }

            string profilePath = profileFolder + "/" + avatarName + "_RimeEmoProfile.asset";
            PrepareExistingAsset(profilePath, options.overwrite, result);
            if (result.errors.Count > 0)
            {
                return result;
            }

            var profile = ScriptableObject.CreateInstance<RimeEmoProfile>();
            profile.avatarName = options.avatarRoot.name;
            profile.modularAvatarLayerPriority = options.modularAvatarLayerPriority;
            profile.defaultApplyMode = options.defaultApplyMode;
            profile.rules = new List<RimeEmoApplyRule>(options.rules);
            AssetDatabase.CreateAsset(profile, profilePath);

            string logPath = logFolder + "/" + avatarName + "_GenerationLog.asset";
            PrepareExistingAsset(logPath, options.overwrite, result);
            if (result.errors.Count > 0)
            {
                return result;
            }

            AssetDatabase.CreateAsset(log, logPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return result;
        }

        private static AnimatorController GenerateControllerForLayer(
            string layerType,
            string avatarName,
            AnimatorController sourceController,
            List<RimeEmoExpressionSlot> slots,
            string controllerFolder,
            string clipFolder,
            RimeEmoGenerateOptions options,
            RimeEmoGenerateResult result,
            RimeEmoGenerationLog log)
        {
            if (sourceController == null || slots == null || slots.Count == 0)
            {
                return null;
            }

            string controllerPath = controllerFolder + "/" + avatarName + "_RimeEmo_" + layerType + ".controller";
            PrepareExistingAsset(controllerPath, options.overwrite, result);
            if (result.errors.Count > 0)
            {
                return null;
            }

            string sourceControllerPath = AssetDatabase.GetAssetPath(sourceController);
            if (!AssetDatabase.CopyAsset(sourceControllerPath, controllerPath))
            {
                result.errors.Add(layerType + " AnimatorControllerのコピーに失敗しました: " + sourceControllerPath);
                return null;
            }

            AssetDatabase.ImportAsset(controllerPath);
            AnimatorController generatedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (generatedController == null)
            {
                result.errors.Add(layerType + " 生成AnimatorControllerの読み込みに失敗しました: " + controllerPath);
                return null;
            }

            var uniqueClips = slots
                .Where(slot => slot != null && slot.sourceClip != null)
                .Select(slot => slot.sourceClip)
                .Distinct()
                .ToList();

            var replacements = new Dictionary<AnimationClip, AnimationClip>();
            foreach (AnimationClip sourceClip in uniqueClips)
            {
                string generatedClipPath = MakeUniqueClipPath(clipFolder, sourceClip.name, layerType);
                PrepareExistingAsset(generatedClipPath, options.overwrite, result);
                if (result.errors.Count > 0)
                {
                    return null;
                }

                string sourceClipPath = AssetDatabase.GetAssetPath(sourceClip);
                AnimationClip generatedClip;
                if (sourceClipPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                {
                    if (!AssetDatabase.CopyAsset(sourceClipPath, generatedClipPath))
                    {
                        result.errors.Add(layerType + " AnimationClipのコピーに失敗しました: " + sourceClipPath);
                        return null;
                    }

                    AssetDatabase.ImportAsset(generatedClipPath);
                    generatedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(generatedClipPath);
                }
                else
                {
                    generatedClip = new AnimationClip();
                    EditorUtility.CopySerialized(sourceClip, generatedClip);
                    generatedClip.name = sourceClip.name + "_RimeEmo_" + layerType;
                    AssetDatabase.CreateAsset(generatedClip, generatedClipPath);
                    result.warnings.Add(layerType + ": 元Clipが単独の.animではないため、CopySerializedで生成.animを作成しました: " + sourceClipPath);
                }

                if (generatedClip == null)
                {
                    result.errors.Add(layerType + " 生成AnimationClipの読み込みに失敗しました: " + generatedClipPath);
                    return null;
                }

                replacements[sourceClip] = generatedClip;
                RimeEmoClipGenerationLog clipLog = ApplySnapshotToClip(generatedClip, sourceClipPath, generatedClipPath, options);
                clipLog.clipName = layerType + ": " + clipLog.clipName;
                log.clips.Add(clipLog);
            }

            RimeEmoAnimatorUtility.ReplaceMotionReferences(generatedController, replacements);
            EditorUtility.SetDirty(generatedController);
            return generatedController;
        }

        private static string MakeUniqueClipPath(string clipFolder, string sourceClipName, string layerType)
        {
            string fileName = RimeEmoPathUtility.SafeFileName(sourceClipName) + "_RimeEmo_" + layerType + ".anim";
            return clipFolder + "/" + fileName;
        }

        private static RimeEmoClipGenerationLog ApplySnapshotToClip(AnimationClip clip, string sourceClipPath, string generatedClipPath, RimeEmoGenerateOptions options)
        {
            var clipLog = new RimeEmoClipGenerationLog
            {
                sourceClipPath = sourceClipPath,
                generatedClipPath = generatedClipPath,
                clipName = clip.name
            };

            Undo.RecordObject(clip, "Apply RimeEmo Default Face");
            EditorCurveBinding[] existingBindings = AnimationUtility.GetCurveBindings(clip);
            var existingByKey = new Dictionary<string, EditorCurveBinding>();
            foreach (EditorCurveBinding binding in existingBindings)
            {
                if (RimeEmoClipUtility.IsBlendShapeBinding(binding))
                {
                    existingByKey[binding.path + "|" + binding.propertyName] = binding;
                }
            }

            foreach (RimeEmoBlendShapeSnapshotEntry entry in options.snapshot.entries)
            {
                if (entry == null)
                {
                    continue;
                }

                string propertyName = RimeEmoClipUtility.BlendShapePrefix + entry.blendShapeName;
                string key = entry.rendererPath + "|" + propertyName;
                bool hasCurve = existingByKey.TryGetValue(key, out EditorCurveBinding binding);
                if (!hasCurve)
                {
                    binding = RimeEmoClipUtility.MakeBlendShapeBinding(entry.rendererPath, entry.blendShapeName);
                }

                float delta = Mathf.Clamp(entry.value, 0f, 100f);
                RimeEmoApplyMode mode = RimeEmoClipUtility.ResolveMode(entry.blendShapeName, options.rules, options.defaultApplyMode);
                var curveLog = new RimeEmoCurveGenerationLog
                {
                    bindingPath = entry.rendererPath,
                    propertyName = propertyName,
                    mode = mode.ToString(),
                    delta = delta
                };

                if (mode == RimeEmoApplyMode.Protect)
                {
                    curveLog.message = "Protected";
                    clipLog.curves.Add(curveLog);
                    continue;
                }

                if (!hasCurve && Mathf.Approximately(delta, 0f) && mode != RimeEmoApplyMode.Override)
                {
                    curveLog.message = "差分0の未存在カーブは追加しませんでした";
                    clipLog.curves.Add(curveLog);
                    continue;
                }

                AnimationCurve existingCurve = hasCurve ? AnimationUtility.GetEditorCurve(clip, binding) : null;
                AnimationCurve newCurve = null;
                switch (mode)
                {
                    case RimeEmoApplyMode.AdditiveOffset:
                        newCurve = hasCurve ? RimeEmoClipUtility.OffsetCurveValues(existingCurve, delta) : RimeEmoClipUtility.CreateConstantCurve(delta);
                        curveLog.message = hasCurve ? "AdditiveOffset +" + delta : "AdditiveOffset add constant +" + delta;
                        break;
                    case RimeEmoApplyMode.FillMissingOnly:
                        if (!hasCurve)
                        {
                            newCurve = RimeEmoClipUtility.CreateConstantCurve(delta);
                            curveLog.message = "FillMissingOnly add constant +" + delta;
                        }
                        else
                        {
                            curveLog.message = "FillMissingOnly: 既存カーブのためスキップ";
                        }
                        break;
                    case RimeEmoApplyMode.Override:
                        newCurve = RimeEmoClipUtility.CreateConstantCurve(delta);
                        curveLog.message = "Override " + delta;
                        break;
                }

                if (newCurve != null)
                {
                    AnimationUtility.SetEditorCurve(clip, binding, newCurve);
                }

                clipLog.curves.Add(curveLog);
            }

            EditorUtility.SetDirty(clip);
            return clipLog;
        }

        private static void PrepareExistingAsset(string path, bool overwrite, RimeEmoGenerateResult result)
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
            {
                return;
            }

            if (!overwrite)
            {
                result.errors.Add("生成物が既に存在します。上書きを許可してください: " + path);
                return;
            }

            if (!AssetDatabase.DeleteAsset(path))
            {
                result.errors.Add("既存生成物の削除に失敗しました: " + path);
            }
        }
    }
}
