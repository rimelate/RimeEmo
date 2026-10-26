using System;
using System.Collections.Generic;
using UnityEngine;

namespace RimeEmo.Editor
{
    // A user-defined set of Shape Keys. Keys are stored by name, so a set also works on other avatars
    // built from the same base model even when the face mesh path differs.
    [Serializable]
    public sealed class ShapeGroup
    {
        public string name;
        // Wildcard patterns: "Eyelid_*", "*Tare*", "*_R". Case is ignored.
        public List<string> patterns = new List<string>();
        // Hand-picked names, and names excluded even when a pattern matches.
        public List<string> added = new List<string>();
        public List<string> removed = new List<string>();
    }

    // Saved as its own asset so several avatar settings can point to the same groups.
    public sealed class RimeEmoShapeGroupSet : ScriptableObject
    {
        // Shown when the set is suggested for another avatar or shared as a file.
        public string baseModel;
        public string author;
        public List<ShapeGroup> groups = new List<ShapeGroup>();
    }
}
