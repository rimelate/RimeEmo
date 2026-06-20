using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    public static class RimeEmoClipUtility
    {
        public const string BlendShapePrefix = "blendShape.";

        public static bool IsBlendShapeBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith(BlendShapePrefix);
        }

        public static bool IsTransformBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(Transform);
        }

        public static bool IsMaterialOrRendererBinding(EditorCurveBinding binding)
        {
            return typeof(Renderer).IsAssignableFrom(binding.type) || binding.propertyName.StartsWith("material.");
        }

        public static string GetBlendShapeName(EditorCurveBinding binding)
        {
            return binding.propertyName.StartsWith(BlendShapePrefix)
                ? binding.propertyName.Substring(BlendShapePrefix.Length)
                : binding.propertyName;
        }

        public static AnimationCurve CreateConstantCurve(float value)
        {
            value = Mathf.Clamp(value, 0f, 100f);
            return new AnimationCurve(new Keyframe(0f, value));
        }

        public static AnimationCurve OffsetCurveValues(AnimationCurve source, float delta)
        {
            if (source == null)
            {
                return CreateConstantCurve(delta);
            }

            Keyframe[] keys = source.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = keys[i];
                key.value = Mathf.Clamp(key.value + delta, 0f, 100f);
                keys[i] = key;
            }

            var curve = new AnimationCurve(keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
            return curve;
        }

        public static RimeEmoApplyMode ResolveMode(string blendShapeName, IList<RimeEmoApplyRule> rules, RimeEmoApplyMode fallback)
        {
            if (rules != null)
            {
                foreach (RimeEmoApplyRule rule in rules)
                {
                    if (!string.IsNullOrEmpty(rule.pattern) && blendShapeName.Contains(rule.pattern))
                    {
                        return rule.mode;
                    }
                }
            }

            return fallback;
        }

        public static bool IsProtectedCandidate(string blendShapeName)
        {
            foreach (RimeEmoApplyRule rule in RimeEmoProfile.CreateDefaultRules())
            {
                if (!string.IsNullOrEmpty(rule.pattern) && blendShapeName.Contains(rule.pattern))
                {
                    return true;
                }
            }

            return false;
        }

        public static EditorCurveBinding MakeBlendShapeBinding(string path, string blendShapeName)
        {
            return new EditorCurveBinding
            {
                path = path,
                type = typeof(SkinnedMeshRenderer),
                propertyName = BlendShapePrefix + blendShapeName
            };
        }
    }
}
