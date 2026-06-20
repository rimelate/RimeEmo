using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace RimeEmo.Editor
{
    public sealed class RimeEmoAnalysisResult
    {
        public AnimatorController fxController;
        public RuntimeAnimatorController gestureController;
        public List<RimeEmoExpressionSlot> slots = new List<RimeEmoExpressionSlot>();
        public List<RimeEmoExpressionSlot> gestureSlots = new List<RimeEmoExpressionSlot>();
        public List<string> warnings = new List<string>();
        public bool hasBlendTreeClip;
        public bool hasMixedWriteDefaults;
        public int ignoredNonFaceClipCount;
    }

    public static class RimeEmoAnalyzer
    {
        private static readonly string[] ExpressionWords =
        {
            "smile", "angry", "sad", "fun", "joy", "eye", "mouth", "face"
        };

        public static RimeEmoAnalysisResult Analyze(GameObject avatarRoot)
        {
            return Analyze(avatarRoot, null);
        }

        public static RimeEmoAnalysisResult Analyze(GameObject avatarRoot, SkinnedMeshRenderer targetFaceRenderer)
        {
            var result = new RimeEmoAnalysisResult();
            if (avatarRoot == null)
            {
                result.warnings.Add("Avatarが指定されていません。");
                return result;
            }

            VRCAvatarDescriptor descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                result.warnings.Add("VRCAvatarDescriptorが見つかりません。");
                return result;
            }

            RuntimeAnimatorController fx = GetPlayableController(descriptor, VRCAvatarDescriptor.AnimLayerType.FX);
            result.gestureController = GetPlayableController(descriptor, VRCAvatarDescriptor.AnimLayerType.Gesture);

            if (fx == null)
            {
                result.warnings.Add("FX Animatorがありません。");
                return result;
            }

            if (!(fx is AnimatorController fxController))
            {
                result.warnings.Add("FX AnimatorがAnimatorControllerではありません: " + fx.GetType().Name);
                return result;
            }

            result.fxController = fxController;
            string targetFacePath = targetFaceRenderer != null
                ? RimeEmoPathUtility.GetTransformPath(avatarRoot.transform, targetFaceRenderer.transform)
                : string.Empty;

            if (result.gestureController is AnimatorController gestureAnimatorController)
            {
                AnalyzeGestureReadOnly(gestureAnimatorController, result, targetFacePath);
            }

            if (RimeEmoAnimatorUtility.HasMixedWriteDefaults(fxController, out int wdOn, out int wdOff))
            {
                result.hasMixedWriteDefaults = true;
                result.warnings.Add("Write DefaultsがStateごとに混在しています。ON: " + wdOn + " / OFF: " + wdOff);
            }

            var clipUsage = new Dictionary<AnimationClip, int>();
            RimeEmoAnimatorUtility.WalkStates(fxController, (statePath, layer, childState) =>
            {
                AnimatorState state = childState.state;
                List<string> driverParameters = FindIncomingConditionParameters(fxController, state);
                bool gestureLike = HasHandGestureParameter(driverParameters);
                RimeEmoAnimatorUtility.WalkMotion(state.motion, false, (clip, fromBlendTree) =>
                {
                    if (clip == null)
                    {
                        return;
                    }

                    clipUsage.TryGetValue(clip, out int count);
                    clipUsage[clip] = count + 1;

                    RimeEmoExpressionSlot slot = BuildSlot(clip, state.motion, layer.name, statePath, fromBlendTree, true, gestureLike, driverParameters);
                    if (slot.blendShapeBindings.Count > 0)
                    {
                        if (ShouldUseSlotForTargetRenderer(slot, targetFacePath))
                        {
                            result.slots.Add(slot);
                        }
                        else
                        {
                            result.ignoredNonFaceClipCount++;
                        }
                    }

                    if (fromBlendTree)
                    {
                        result.hasBlendTreeClip = true;
                    }
                });
            });

            foreach (RimeEmoExpressionSlot slot in result.slots)
            {
                if (clipUsage.TryGetValue(slot.sourceClip, out int count) && count > 1)
                {
                    slot.warnings.Add("同じAnimationClipが複数Stateで共有されています。");
                }

                if (slot.isFromBlendTree)
                {
                    slot.warnings.Add("BlendTree内Clipです。生成コピー側で参照差し替えと差分適用を行います。");
                }

                if (RimeEmoPathUtility.IsPackageAsset(slot.sourceClip))
                {
                    slot.warnings.Add("元AnimationClipがPackages配下にあります。RimeEmoは生成コピーのみ編集します。");
                }
            }

            if (result.hasBlendTreeClip)
            {
                result.warnings.Add("BlendTree内Clipが検出されました。");
            }

            if (result.ignoredNonFaceClipCount > 0)
            {
                result.warnings.Add("選択中の顔メッシュ以外を動かすAnimationClipを除外しました: " + result.ignoredNonFaceClipCount);
            }

            result.slots = result.slots.OrderByDescending(s => s.confidence).ThenBy(s => s.displayName).ToList();
            result.gestureSlots = result.gestureSlots.OrderByDescending(s => s.confidence).ThenBy(s => s.displayName).ToList();
            return result;
        }

        private static void AnalyzeGestureReadOnly(AnimatorController gestureController, RimeEmoAnalysisResult result, string targetFacePath)
        {
            RimeEmoAnimatorUtility.WalkStates(gestureController, (statePath, layer, childState) =>
            {
                AnimatorState state = childState.state;
                List<string> driverParameters = FindStateConditionParameters(state);
                bool gestureLike = HasHandGestureParameter(driverParameters);
                RimeEmoAnimatorUtility.WalkMotion(state.motion, false, (clip, fromBlendTree) =>
                {
                    if (clip == null)
                    {
                        return;
                    }

                    RimeEmoExpressionSlot slot = BuildSlot(clip, state.motion, layer.name, statePath, fromBlendTree, false, gestureLike, driverParameters);
                    if (slot.blendShapeBindings.Count > 0)
                    {
                        if (ShouldUseSlotForTargetRenderer(slot, targetFacePath))
                        {
                            slot.warnings.Add("Gesture側の顔アニメーションです。生成コピー側で差分適用します。");
                            result.gestureSlots.Add(slot);
                        }
                        else
                        {
                            result.ignoredNonFaceClipCount++;
                        }
                    }
                });
            });
        }

        private static bool ShouldUseSlotForTargetRenderer(RimeEmoExpressionSlot slot, string targetFacePath)
        {
            if (slot == null || string.IsNullOrEmpty(targetFacePath))
            {
                return true;
            }

            return slot.blendShapeBindings.Any(binding => binding.rendererPath == targetFacePath);
        }

        private static List<string> FindIncomingConditionParameters(AnimatorController controller, AnimatorState targetState)
        {
            var parameters = new HashSet<string>();
            if (controller == null || targetState == null)
            {
                return parameters.ToList();
            }

            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                CollectIncomingConditionParameters(layer.stateMachine, targetState, parameters);
            }

            return parameters.ToList();
        }

        private static void CollectIncomingConditionParameters(AnimatorStateMachine stateMachine, AnimatorState targetState, HashSet<string> parameters)
        {
            if (stateMachine == null)
            {
                return;
            }

            foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions)
            {
                if (transition.destinationState == targetState)
                {
                    AddConditionParameters(transition.conditions, parameters);
                }
            }

            foreach (ChildAnimatorState state in stateMachine.states)
            {
                foreach (AnimatorStateTransition transition in state.state.transitions)
                {
                    if (transition.destinationState == targetState)
                    {
                        AddConditionParameters(transition.conditions, parameters);
                    }
                }
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                CollectIncomingConditionParameters(child.stateMachine, targetState, parameters);
            }
        }

        private static List<string> FindStateConditionParameters(AnimatorState state)
        {
            var parameters = new HashSet<string>();
            if (state == null)
            {
                return parameters.ToList();
            }

            foreach (AnimatorStateTransition transition in state.transitions)
            {
                AddConditionParameters(transition.conditions, parameters);
            }

            return parameters.ToList();
        }

        private static void AddConditionParameters(AnimatorCondition[] conditions, HashSet<string> parameters)
        {
            foreach (AnimatorCondition condition in conditions)
            {
                if (!string.IsNullOrEmpty(condition.parameter))
                {
                    parameters.Add(condition.parameter);
                }
            }
        }

        private static bool HasHandGestureParameter(IEnumerable<string> parameters)
        {
            foreach (string parameter in parameters)
            {
                if (parameter == "GestureLeft" || parameter == "GestureRight")
                {
                    return true;
                }
            }

            return false;
        }

        private static RuntimeAnimatorController GetPlayableController(VRCAvatarDescriptor descriptor, VRCAvatarDescriptor.AnimLayerType type)
        {
            foreach (VRCAvatarDescriptor.CustomAnimLayer layer in descriptor.baseAnimationLayers)
            {
                if (layer.type == type && !layer.isDefault)
                {
                    return layer.animatorController;
                }
            }

            return null;
        }

        private static RimeEmoExpressionSlot BuildSlot(AnimationClip clip, Motion sourceMotion, string layerName, string statePath, bool fromBlendTree, bool isFx, bool gestureLike, List<string> driverParameters)
        {
            var slot = new RimeEmoExpressionSlot
            {
                slotId = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)) + ":" + statePath,
                displayName = clip.name,
                animatorLayerName = layerName,
                statePath = statePath,
                sourceMotion = sourceMotion,
                sourceClip = clip,
                isFromBlendTree = fromBlendTree,
                isFromFx = isFx,
                hasGestureLikeCondition = gestureLike,
                isHandGestureDriven = gestureLike,
                driverParameters = driverParameters != null ? new List<string>(driverParameters) : new List<string>(),
                isGimmickDriven = driverParameters != null && driverParameters.Any(parameter => parameter != "GestureLeft" && parameter != "GestureRight")
            };

            slot.bindings.AddRange(AnimationUtility.GetCurveBindings(clip));
            foreach (EditorCurveBinding binding in slot.bindings)
            {
                if (RimeEmoClipUtility.IsBlendShapeBinding(binding))
                {
                    string blendShapeName = RimeEmoClipUtility.GetBlendShapeName(binding);
                    bool protect = RimeEmoClipUtility.IsProtectedCandidate(blendShapeName);
                    slot.blendShapeBindings.Add(new RimeEmoBlendShapeBindingInfo
                    {
                        binding = binding,
                        rendererPath = binding.path,
                        propertyName = binding.propertyName,
                        blendShapeName = blendShapeName,
                        isProtectedCandidate = protect
                    });

                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (IsDynamicCurve(curve))
                    {
                        slot.hasDynamicCurves = true;
                    }
                }
                else if (RimeEmoClipUtility.IsTransformBinding(binding))
                {
                    slot.transformBindingCount++;
                }
                else if (RimeEmoClipUtility.IsMaterialOrRendererBinding(binding))
                {
                    slot.materialBindingCount++;
                }
            }

            slot.confidence = CalculateConfidence(slot);
            return slot;
        }

        private static bool IsDynamicCurve(AnimationCurve curve)
        {
            if (curve == null || curve.keys == null || curve.keys.Length <= 1)
            {
                return false;
            }

            float firstValue = curve.keys[0].value;
            for (int i = 1; i < curve.keys.Length; i++)
            {
                if (!Mathf.Approximately(firstValue, curve.keys[i].value))
                {
                    return true;
                }
            }

            return false;
        }

        private static float CalculateConfidence(RimeEmoExpressionSlot slot)
        {
            float score = 0f;
            if (slot.blendShapeBindings.Count > 0)
            {
                score += 40f;
            }

            string text = (slot.displayName + " " + slot.statePath).ToLowerInvariant();
            foreach (string word in ExpressionWords)
            {
                if (text.Contains(word))
                {
                    score += 20f;
                    break;
                }
            }

            if (slot.isFromFx)
            {
                score += 20f;
            }

            if (slot.hasGestureLikeCondition)
            {
                score += 10f;
            }

            if (slot.isFromBlendTree)
            {
                score -= 20f;
            }

            return Mathf.Clamp(score, 0f, 100f);
        }
    }
}
