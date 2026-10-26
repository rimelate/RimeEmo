using System;
using System.Collections.Generic;
using UnityEngine;

namespace RimeEmo.Editor
{
    public enum FacePart { LeftEye, RightEye, Brow, Mouth, Other }
    public enum Interpolation { Linear, EaseInOut, Step }
    // Which hand wins when both hands make different assigned gestures. Manual keeps every overlap unresolved until chosen.
    public enum GesturePriority { Manual, Left, Right }

    [Serializable]
    public sealed class ShapeEntry
    {
        public string path;
        public string shape;
        public float defaultValue;
        public FacePart part;
        public bool classificationOverridden;
    }

    [Serializable]
    public sealed class ClipEntry
    {
        public AnimationClip clip;
        public AnimationClip source;
        public string gesture = "Unknown";
        // Made with "New Expression": it starts with no curves (unset Shape Keys use Default Face), so it is an
        // expression even before any face curve exists. Imported clips without face curves stay excluded.
        public bool created;
    }

    [Serializable]
    public sealed class MotionState
    {
        public AnimationClip clip;
        public bool animated;
        public float sourceStart;
        public float speed = 1f;
        public float hold = 0.1f;
        // Extra hold after the source span. Existing static poses keep their legacy hold field.
        public float endHold;
        public float transition = 0.1f;
        public Interpolation interpolation = Interpolation.EaseInOut;
        [HideInInspector] public bool hasGraphPosition;
        [HideInInspector] public Vector2 graphPosition;
    }

    [Serializable]
    public sealed class PartTrack
    {
        public FacePart part;
        public float startTime;
        public List<MotionState> states = new List<MotionState>();
    }

    // Referenced by the avatar's EditorOnly settings component.
    public sealed class RimeEmoProject : ScriptableObject
    {
        public string avatarId;
        public string avatarName;
        public RuntimeAnimatorController sourceFX;
        public bool faceMeshSelected;
        public string faceMeshPath;
        public List<ShapeEntry> shapes = new List<ShapeEntry>();
        public List<ClipEntry> clips = new List<ClipEntry>();
        public List<ClipEntry> animations = new List<ClipEntry>();
        public List<RimeEmoMotion> motions = new List<RimeEmoMotion>();
        public List<GestureRule> gestureRules = new List<GestureRule>();
        public List<GestureChoice> gestureChoices = new List<GestureChoice>();
        public float gestureBlendSeconds = 0.1f;
        // Also write the face Shape Keys the source FX's own hand-gesture layers animate, silencing the old expressions.
        // Off by default: RimeEmo writes only the Shape Keys its expressions use and leaves the rest to the avatar's FX.
        public bool overrideSourceGestureFaces;
        public GesturePriority gesturePriority;
        public RuntimeAnimatorController generatedFX;
        public string generatedFolder;
        // Inputs of the last build, compared with the current ones to tell whether the avatar is behind.
        public string generatedFingerprint;
        // Created with the first group; may be shared with other avatar settings.
        public RimeEmoShapeGroupSet shapeGroups;
    }

    [Serializable]
    public sealed class GestureRule
    {
        public int left = -1, right = -1; // -1 = ANY
        public AnimationClip clip;
        public RimeEmoMotion motion;
    }

    [Serializable]
    public sealed class GestureChoice
    {
        public int left, right;
        public AnimationClip clip;
        public RimeEmoMotion motion;
        public bool useDefault;
    }
}
