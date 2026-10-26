using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoAssignments
    {
        internal static List<GestureRule> Matches(RimeEmoProject project, int left, int right)
            => project.gestureRules.Where(r => (r.left < 0 || r.left == left) && (r.right < 0 || r.right == right)).ToList();

        internal static bool TryResolve(RimeEmoProject project, int left, int right, out GestureRule result)
        {
            result = null;
            var choice = project.gestureChoices.FirstOrDefault(c => c.left == left && c.right == right);
            if (choice != null)
            {
                if (!choice.useDefault) result = new GestureRule { left = left, right = right, clip = choice.clip, motion = choice.motion };
                return true;
            }
            var matches = Prioritize(project.gesturePriority, Matches(project, left, right), left, right);
            if (matches.Count > 1) return false;
            result = matches.FirstOrDefault(); return true;
        }

        // Both-hand rules beat one-hand rules. Then the priority hand wins unless it is Neutral (0) while the other hand
        // makes a gesture, so a resting priority hand never hides the other hand's expression.
        internal static List<GestureRule> Prioritize(GesturePriority priority, List<GestureRule> matches, int left, int right)
        {
            if (priority == GesturePriority.Manual || matches.Count < 2) return matches;
            var both = matches.Where(r => r.left >= 0 && r.right >= 0).ToList();
            if (both.Count > 0) return both;
            bool useLeft = priority == GesturePriority.Left ? left != 0 || right == 0 : !(right != 0 || left == 0);
            var preferred = matches.Where(r => useLeft ? r.left >= 0 : r.right >= 0).ToList();
            if (preferred.Count > 0) return preferred;
            var other = matches.Where(r => useLeft ? r.right >= 0 : r.left >= 0).ToList();
            return other.Count > 0 ? other : matches;
        }

        // How a combination is decided, for the combination table.
        internal enum Source { Default, Left, Right, Both, Choice, Unresolved }
        internal static Source Describe(RimeEmoProject project, int left, int right, out GestureRule result)
        {
            if (!TryResolve(project, left, right, out result)) return Source.Unresolved;
            if (project.gestureChoices.Any(c => c.left == left && c.right == right)) return Source.Choice;
            if (result == null) return Source.Default;
            if (result.left >= 0 && result.right >= 0) return Source.Both;
            return result.left >= 0 ? Source.Left : result.right >= 0 ? Source.Right : Source.Both;
        }
        internal static List<string> Validate(RimeEmoProject project)
        {
            var errors = new List<string>();
            if (!project) { errors.Add(RimeEmoLocalization.T("Projectを指定してください。")); return errors; }
            foreach (var rule in project.gestureRules)
            {
                if (rule.left < -1 || rule.left > 7 || rule.right < -1 || rule.right > 7) errors.Add(RimeEmoLocalization.T("Gesture条件が範囲外です。"));
                if (Missing(rule.clip, rule.motion)) { errors.Add(MissingMessage(rule.left, rule.right)); continue; }
                ValidateTarget(project, rule.clip, rule.motion, false, errors);
            }
            foreach (var choice in project.gestureChoices)
            {
                if (choice.left < 0 || choice.left > 7 || choice.right < 0 || choice.right > 7) errors.Add(RimeEmoLocalization.T("競合解決のGesture条件が範囲外です。"));
                if (!choice.useDefault && Missing(choice.clip, choice.motion)) { errors.Add(MissingMessage(choice.left, choice.right)); continue; }
                ValidateTarget(project, choice.clip, choice.motion, choice.useDefault, errors);
            }
            if (project.gestureChoices.GroupBy(c => new { c.left, c.right }).Any(g => g.Count() > 1)) errors.Add(RimeEmoLocalization.T("同じ組み合わせの競合解決が重複しています。"));
            for (int l = 0; l < 8; l++) for (int r = 0; r < 8; r++)
                if (!TryResolve(project, l, r, out _)) errors.Add(RimeEmoLocalization.T("競合: Left ") + RimeEmoGestures.Names[l] + " / Right " + RimeEmoGestures.Names[r]);
            return errors.Distinct().ToList();
        }
        // An assignment whose expression or motion asset was deleted (Unity keeps a "missing" reference that compares
        // equal to null). Said plainly, with the hand shapes, instead of "specify a Clip or a Motion".
        internal static bool Missing(AnimationClip clip, RimeEmoMotion motion)
            => (!ReferenceEquals(clip, null) && !clip && !motion) || (!ReferenceEquals(motion, null) && !motion && !clip);

        private static string MissingMessage(int left, int right)
        {
            string Hand(int index) => index < 0 ? "ANY" : RimeEmoGestures.Names[Mathf.Clamp(index, 0, RimeEmoGestures.Names.Length - 1)];
            return string.Format(RimeEmoLocalization.T("割り当てた表情・動きが見つかりません（削除された可能性があります）: L {0} / R {1}。Gestureページで割り当て直すか外してください。"), Hand(left), Hand(right));
        }

        private static void ValidateTarget(RimeEmoProject project, AnimationClip clip, RimeEmoMotion motion, bool useDefault, List<string> errors)
        {
            if (useDefault) return;
            if ((!clip && !motion) || (clip && motion)) errors.Add(RimeEmoLocalization.T("各割り当てにClipかMotionを1つ指定してください。"));
            if (clip && !project.clips.Any(c => c.clip == clip) && !project.animations.Any(c => c.clip == clip) && !RimeEmoDynamicClips.Find(project, clip)) errors.Add(RimeEmoLocalization.T("割り当てClipを素材一覧に取り込んでください: ") + clip.name);
            if (motion && (motion.project != project || !project.motions.Contains(motion))) errors.Add(RimeEmoLocalization.T("別ProjectのMotionは割り当てできません。"));
        }
    }
}
