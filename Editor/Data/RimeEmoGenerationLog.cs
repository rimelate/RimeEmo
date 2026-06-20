using System;
using System.Collections.Generic;
using UnityEngine;

namespace RimeEmo.Editor
{
    public sealed class RimeEmoGenerationLog : ScriptableObject
    {
        public string avatarName;
        public string sourceControllerPath;
        public string generatedControllerPath;
        public string generatedGestureControllerPath;
        public string generatedAtIso8601;
        public List<RimeEmoClipGenerationLog> clips = new List<RimeEmoClipGenerationLog>();
        public List<string> warnings = new List<string>();
    }

    [Serializable]
    public sealed class RimeEmoClipGenerationLog
    {
        public string sourceClipPath;
        public string generatedClipPath;
        public string clipName;
        public List<RimeEmoCurveGenerationLog> curves = new List<RimeEmoCurveGenerationLog>();
    }

    [Serializable]
    public sealed class RimeEmoCurveGenerationLog
    {
        public string bindingPath;
        public string propertyName;
        public string mode;
        public float delta;
        public string message;
    }
}

