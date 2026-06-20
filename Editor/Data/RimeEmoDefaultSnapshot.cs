using System;
using System.Collections.Generic;
using UnityEngine;

namespace RimeEmo.Editor
{
    public sealed class RimeEmoDefaultSnapshot : ScriptableObject
    {
        public string avatarName;
        public string rendererPath;
        public string meshName;
        public string savedAtIso8601;
        public List<RimeEmoBlendShapeSnapshotEntry> entries = new List<RimeEmoBlendShapeSnapshotEntry>();

        public bool TryGetValue(string rendererPath, string blendShapeName, out float value)
        {
            string key = BuildKey(rendererPath, blendShapeName);
            foreach (RimeEmoBlendShapeSnapshotEntry entry in entries)
            {
                if (entry.key == key)
                {
                    value = entry.value;
                    return true;
                }
            }

            value = 0f;
            return false;
        }

        public static string BuildKey(string rendererPath, string blendShapeName)
        {
            return rendererPath + "/blendShape." + blendShapeName;
        }
    }

    [Serializable]
    public sealed class RimeEmoBlendShapeSnapshotEntry
    {
        public string key;
        public string rendererPath;
        public string meshName;
        public string blendShapeName;
        public int blendShapeIndex;
        public float value;
    }
}
