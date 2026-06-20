using System;
using System.Collections.Generic;
using UnityEngine;

namespace RimeEmo.Editor
{
    public enum RimeEmoApplyMode
    {
        AdditiveOffset,
        FillMissingOnly,
        Override,
        Protect
    }

    [Serializable]
    public sealed class RimeEmoApplyRule
    {
        public string pattern;
        public RimeEmoApplyMode mode;

        public RimeEmoApplyRule(string pattern, RimeEmoApplyMode mode)
        {
            this.pattern = pattern;
            this.mode = mode;
        }
    }

    public sealed class RimeEmoProfile : ScriptableObject
    {
        public string avatarName;
        public int modularAvatarLayerPriority;
        public RimeEmoApplyMode defaultApplyMode = RimeEmoApplyMode.AdditiveOffset;
        public List<RimeEmoApplyRule> rules = new List<RimeEmoApplyRule>();

        public static List<RimeEmoApplyRule> CreateDefaultRules()
        {
            return new List<RimeEmoApplyRule>
            {
                new RimeEmoApplyRule("Blink", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("blink", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("EyeBlink", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("eye_blink", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("vrc.v_", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("viseme", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("Viseme", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("Jaw", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("jaw", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("MTH_A", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("MTH_I", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("MTH_U", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("MTH_E", RimeEmoApplyMode.Protect),
                new RimeEmoApplyRule("MTH_O", RimeEmoApplyMode.Protect)
            };
        }
    }
}
