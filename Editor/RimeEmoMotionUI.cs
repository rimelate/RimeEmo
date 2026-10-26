using UnityEngine;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        private int activeTrack;
        private int activeState;
        private bool transitionSelected;
        private static string[] PartLabels => new[] { RimeEmoLocalization.T("左目"), RimeEmoLocalization.T("右目"), RimeEmoLocalization.T("眉"), RimeEmoLocalization.T("口"), RimeEmoLocalization.T("その他") };

        private bool TryGetState(out PartTrack track, out MotionState state)
        {
            track = null; state = null;
            if (!motion || motion.project != project || activeTrack < 0 || activeTrack >= motion.tracks.Count) return false;
            track = motion.tracks[activeTrack];
            if (activeState < 0 || activeState >= track.states.Count) return false;
            state = track.states[activeState];
            return true;
        }

        // Shelf items keep their managed copy; only clips from outside the project are imported.
        private AnimationClip ManagedMaterial(AnimationClip clip)
        {
            if (!project || !clip) return null;
            bool known = project.clips.Exists(c => c.clip == clip) || project.animations.Exists(c => c.clip == clip) || RimeEmoDynamicClips.Find(project, clip);
            return known ? clip : RimeEmoAssets.Import(project, clip);
        }
    }
}
