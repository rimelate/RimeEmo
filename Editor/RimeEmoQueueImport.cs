using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoQueueImport
    {
        internal static RimeEmoMotion Create(RimeEmoProject project, AnimationClip source)
        {
            var draft = RimeEmoDrafts.Create(project);
            // Reopening the same animation reuses its frozen copy instead of adding "Name 1", "Name 2", ...
            var copy = RimeEmoMotionMaterials.SnapshotSource(project, source);
            draft.sourceClip = copy; draft.name = source.name;
            draft.loop = AnimationUtility.GetAnimationClipSettings(copy).loopTime;
            foreach (var track in draft.tracks)
            {
                if (!RimeEmoClips.HasPart(project, copy, track.part)) continue;
                track.states.Add(new MotionState { clip = copy, animated = true, sourceStart = 0, speed = 1,
                    hold = Mathf.Max(1f / 60, copy.length), transition = 0 });
            }
            // Establish editing boundaries on first import without changing the source animation.
            var selection = Enumerable.Range(0, draft.tracks.Count).Where(t => draft.tracks[t].states.Count > 0)
                .Select(t => new Vector2Int(t, 0));
            var intervals = RimeEmoAutoSplit.Analyze(draft, selection,
                RimeEmoAutoSplit.DefaultMinimumDuration, RimeEmoAutoSplit.DefaultTolerance);
            RimeEmoAutoSplit.Apply(draft, intervals);
            EditorUtility.ClearDirty(draft);
            return draft;
        }
    }
}
