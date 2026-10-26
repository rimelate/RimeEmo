using System.Collections.Generic;
using UnityEngine;

namespace RimeEmo.Editor
{
    public sealed class RimeEmoMotion : ScriptableObject
    {
        public RimeEmoProject project;
        public AnimationClip animationClip;
        public AnimationClip sourceClip;
        [HideInInspector] public List<string> generatedShapePaths = new List<string>();
        [HideInInspector] public List<string> generatedShapeProperties = new List<string>();
        public bool loop;
        public List<PartTrack> tracks = new List<PartTrack>();
    }
}
