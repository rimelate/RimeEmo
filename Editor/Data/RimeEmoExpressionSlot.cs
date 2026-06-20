using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    [Serializable]
    public sealed class RimeEmoExpressionSlot
    {
        public string slotId;
        public string displayName;
        public string animatorLayerName;
        public string statePath;
        public Motion sourceMotion;
        public AnimationClip sourceClip;
        public List<EditorCurveBinding> bindings = new List<EditorCurveBinding>();
        public List<RimeEmoBlendShapeBindingInfo> blendShapeBindings = new List<RimeEmoBlendShapeBindingInfo>();
        public int transformBindingCount;
        public int materialBindingCount;
        public bool isFromBlendTree;
        public bool isFromFx;
        public bool hasGestureLikeCondition;
        public bool isHandGestureDriven;
        public bool isGimmickDriven;
        public bool hasDynamicCurves;
        public List<string> driverParameters = new List<string>();
        public float confidence;
        public List<string> warnings = new List<string>();
    }

    [Serializable]
    public sealed class RimeEmoBlendShapeBindingInfo
    {
        public EditorCurveBinding binding;
        public string rendererPath;
        public string propertyName;
        public string blendShapeName;
        public bool isProtectedCandidate;
    }
}


