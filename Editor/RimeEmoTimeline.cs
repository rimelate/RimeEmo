using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RimeEmo.Editor
{
    // Source playback, held end pose, then transition to the next cue.
    internal static class RimeEmoTimeline
    {
        private const float Epsilon = 0.00001f;
        // One 60 fps frame between key starts keeps every key reachable on the timeline.
        private const float MinGap = 1f / 60;
        internal static float ContentDuration(MotionState state) => state.animated ? Mathf.Max(0, state.hold) : 0;
        internal static float HoldDuration(MotionState state) => Mathf.Max(0, state.endHold) + (state.animated ? 0 : Mathf.Max(0, state.hold));
        private static void SetKeep(MotionState state, float duration)
        {
            if (state.animated) state.endHold = duration;
            else { state.hold = duration; state.endHold = 0; }
        }
        internal static void SetHoldDuration(PartTrack track, int index, float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || index < 0 || index >= track.states.Count) return;
            var state = track.states[index]; bool next = index + 1 < track.states.Count;
            float gap = next ? HoldDuration(state) + Mathf.Max(0, state.transition)
                : Mathf.Max(0, 600 - Times(track)[index] - ContentDuration(state));
            float keep = Mathf.Clamp(duration, 0, gap);
            SetKeep(state, keep);
            if (next) state.transition = gap - keep;
        }
        internal static void SetTransitionDuration(PartTrack track, int index, float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || index < 0 || index + 1 >= track.states.Count) return;
            var state = track.states[index]; float gap = HoldDuration(state) + Mathf.Max(0, state.transition);
            state.transition = Mathf.Clamp(duration, 0, gap); SetKeep(state, gap - state.transition);
        }
        internal static void NormalizeKeeps(RimeEmoMotion motion)
        {
            if (!motion) return;
            foreach (var track in motion.tracks)
                for (int i = 0; i + 1 < track.states.Count; i++)
                {
                    var state = track.states[i];
                    if (state.interpolation == Interpolation.Step && state.transition > 0)
                    {
                        SetKeep(state, HoldDuration(state) + state.transition);
                        state.transition = 0; state.interpolation = Interpolation.EaseInOut;
                    }
                }
        }
        internal static float[] Times(PartTrack track)
        {
            var times = new float[track.states.Count];
            float time = Mathf.Max(0, track.startTime);
            for (int i = 0; i < times.Length; i++)
            {
                times[i] = time;
                time += Mathf.Max(0, track.states[i].hold);
                time += Mathf.Max(0, track.states[i].endHold);
                if (i + 1 < times.Length) time += Mathf.Max(0, track.states[i].transition);
            }
            return times;
        }

        private static float Clean(float time) => float.IsNaN(time) || float.IsInfinity(time) ? 0 : Mathf.Clamp(time, 0, 600);

        internal enum BlockEdge { Start, Move, End }
        internal sealed class BlockDrag
        {
            private readonly PartTrack track;
            private readonly int index;
            private readonly BlockEdge edge;
            private readonly float[] starts, ends;
            private readonly float[] keeps;
            private float lastStart, lastEnd;
            private readonly float sourceStart, speed, clipLength;
            internal BlockDrag(PartTrack track, int index, BlockEdge edge)
            {
                this.track = track; this.index = index; this.edge = edge;
                starts = Times(track); ends = starts.Select((t, i) => t + ContentDuration(track.states[i])).ToArray();
                keeps = track.states.Select(HoldDuration).ToArray();
                lastStart = starts[index]; lastEnd = ends[index];
                var state = track.states[index]; sourceStart = state.sourceStart; speed = Mathf.Max(0.000001f, state.speed); clipLength = state.clip ? state.clip.length : 0;
            }
            internal bool Apply(float target, Action beforeChange = null)
            {
                float left = index > 0 ? ends[index - 1] : 0;
                float right = index + 1 < starts.Length ? starts[index + 1] : Mathf.Max(600, ends[index]);
                float start = starts[index], end = ends[index], length = end - start;
                var state = track.states[index];
                float candidateSource = state.sourceStart;
                if (!state.animated && edge == BlockEdge.End)
                {
                    float keep = Mathf.Clamp(Clean(target) - start, 0, right - start);
                    if (Mathf.Abs(HoldDuration(state) - keep) < Epsilon) return false;
                    beforeChange?.Invoke(); SetHoldDuration(track, index, keep); return true;
                }
                // A cue may touch a neighbour's end, but never share its start time: stacked keys hide each other.
                float minStart0 = index > 0 ? Mathf.Max(left, starts[index - 1] + MinGap) : 0;
                float maxStart = index + 1 < starts.Length ? starts[index + 1] - MinGap : float.PositiveInfinity;
                if (edge == BlockEdge.Start && state.animated)
                {
                    float minStart = minStart0;
                    if (state.animated) minStart = Mathf.Max(minStart, starts[index] - sourceStart / speed);
                    start = Mathf.Clamp(Clean(target), minStart, state.animated ? end - Mathf.Min(length, .00001f) : end);
                    if (state.animated)
                    {
                        candidateSource = Mathf.Max(0, sourceStart + (start - starts[index]) * speed);
                    }
                }
                else if (edge == BlockEdge.End)
                {
                    end = Mathf.Clamp(Clean(target), state.animated ? start + Mathf.Min(length, .00001f) : start, right);
                    if (state.animated && clipLength > 0) end = Mathf.Min(end, start + Mathf.Max(0, (clipLength - sourceStart) / speed));
                }
                else
                {
                    float high = Mathf.Min(right - length, maxStart);
                    start = Mathf.Clamp(Clean(target), minStart0, Mathf.Max(minStart0, high)); end = start + length;
                }
                if (Mathf.Abs(start - lastStart) < Epsilon && Mathf.Abs(end - lastEnd) < Epsilon) return false;
                beforeChange?.Invoke(); lastStart = start; lastEnd = end;
                if (state.animated && edge == BlockEdge.Start) state.sourceStart = candidateSource;
                track.startTime = index == 0 ? start : starts[0];
                for (int i = 0; i < starts.Length; i++)
                {
                    float a = i == index ? start : starts[i], b = i == index ? end : ends[i];
                    if (track.states[i].animated) track.states[i].hold = Mathf.Max(0, b - a);
                    if (i + 1 < starts.Length)
                    {
                        float gap = Mathf.Max(0, (i + 1 == index ? start : starts[i + 1]) - b);
                        float keep = Mathf.Min(keeps[i], gap);
                        SetKeep(track.states[i], keep);
                        track.states[i].transition = gap - keep;
                    }
                    else SetKeep(track.states[i], Mathf.Min(keeps[i], Mathf.Max(0, 600 - b)));
                }
                return true;
            }
        }

        internal static float MoveHoldKey(PartTrack track, int index, float at)
        {
            var times = Times(track);
            float end = index + 1 < times.Length ? times[index + 1] : 600;
            float contentEnd = times[index] + ContentDuration(track.states[index]);
            at = Mathf.Clamp(Clean(at), contentEnd, Mathf.Max(contentEnd, end));
            SetHoldDuration(track, index, at - contentEnd);
            return at;
        }

        // A pointer drag is one edit from the original timings, not a chain of
        // edits from progressively shortened holds or extended track endings.
        internal sealed class Drag
        {
            private readonly PartTrack track;
            private readonly MotionState[] states;
            private readonly float[] holds, endHolds, transitions, times;
            private readonly float start;
            private readonly int originalIndex;
            private float lastTarget;

            internal Drag(PartTrack track, int index)
            {
                this.track = track; originalIndex = index;
                states = track.states.ToArray();
                holds = states.Select(s => s.hold).ToArray();
                endHolds = states.Select(s => s.endHold).ToArray();
                transitions = states.Select(s => s.transition).ToArray();
                times = Times(track); start = track.startTime; lastTarget = times[index];
            }

            internal bool CanMove(float time)
            {
                time = Clean(time);
                return Mathf.Abs(time - lastTarget) > 0.000001f &&
                    !times.Where((_, i) => i != originalIndex).Any(t => Mathf.Abs(t - time) < Epsilon);
            }

            internal int MoveTo(float time)
            {
                if (!CanMove(time)) return track.states.IndexOf(states[originalIndex]);
                track.startTime = start;
                track.states = states.ToList();
                for (int i = 0; i < states.Length; i++)
                { states[i].hold = holds[i]; states[i].endHold = endHolds[i]; states[i].transition = transitions[i]; }
                lastTarget = Clean(time);
                return Move(track, originalIndex, lastTarget);
            }
        }

        internal static int Move(PartTrack track, int index, float time)
        {
            if (index < 0 || index >= track.states.Count) return -1;
            time = Clean(time);
            var times = Times(track);
            if (times.Where((t, i) => i != index).Any(t => Mathf.Abs(t - time) < Epsilon)) return index;
            float end = RimeEmoCompiler.Duration(track);
            var state = track.states[index];
            var ordered = track.states.Select((s, i) => new KeyValuePair<float, MotionState>(i == index ? time : times[i], s)).OrderBy(p => p.Key).ToArray();
            Rewrite(track, ordered, Mathf.Max(end, time));
            return track.states.IndexOf(state);
        }

        internal static int Insert(PartTrack track, float time, AnimationClip clip)
        {
            time = Clean(time);
            var times = Times(track);
            for (int i = 0; i < times.Length; i++)
                if (Mathf.Abs(times[i] - time) < Epsilon) { track.states[i].clip = clip; track.states[i].hold += track.states[i].endHold; track.states[i].endHold = 0; track.states[i].animated = false; track.states[i].sourceStart = 0; track.states[i].speed = 1; return i; }
            float end = Mathf.Max(RimeEmoCompiler.Duration(track), time + 0.1f);
            var state = new MotionState { clip = clip, hold = 0.1f, transition = 0 };
            var ordered = track.states.Select((s, i) => new KeyValuePair<float, MotionState>(times[i], s))
                .Concat(new[] { new KeyValuePair<float, MotionState>(time, state) }).OrderBy(p => p.Key).ToArray();
            int index = Array.FindIndex(ordered, pair => pair.Value == state);
            // Keep the existing block's hold; Rewrite trims it only if insertion overlaps.
            Rewrite(track, ordered, end);
            return index;
        }

        // Places a moving cue that plays the clip from its start. A cue already at that time is replaced.
        internal static int InsertMotion(PartTrack track, float time, AnimationClip clip)
        {
            time = Clean(time);
            float length = Mathf.Max(1f / 60, clip ? clip.length : 0);
            var times = Times(track);
            int replaced = Array.FindIndex(times, t => Mathf.Abs(t - time) < Epsilon);
            var state = new MotionState { clip = clip, animated = true, sourceStart = 0, speed = 1, hold = length, transition = 0 };
            var ordered = track.states.Select((s, i) => new KeyValuePair<float, MotionState>(times[i], s)).Where((_, i) => i != replaced)
                .Concat(new[] { new KeyValuePair<float, MotionState>(time, state) }).OrderBy(p => p.Key).ToArray();
            int index = Array.FindIndex(ordered, pair => pair.Value == state);
            Rewrite(track, ordered, Mathf.Max(RimeEmoCompiler.Duration(track), time + length));
            // As the final cue, play the source once and hold its last pose for any remaining track length.
            if (index == track.states.Count - 1 && state.hold > length) { state.endHold = state.hold - length; state.hold = length; }
            return index;
        }

        // Pastes a cue at the given time, replacing a cue that starts there. Later cues keep their times: a cue
        // pasted over another overwrites it from that point, like an overwrite edit in a video editor.
        internal static int InsertState(PartTrack track, float time, MotionState state)
        {
            time = Clean(time);
            var times = Times(track);
            int replaced = Array.FindIndex(times, t => Mathf.Abs(t - time) < Epsilon);
            float length = ContentDuration(state) + HoldDuration(state);
            var ordered = track.states.Select((s, i) => new KeyValuePair<float, MotionState>(times[i], s)).Where((_, i) => i != replaced)
                .Concat(new[] { new KeyValuePair<float, MotionState>(time, state) }).OrderBy(p => p.Key).ToArray();
            int index = Array.FindIndex(ordered, pair => pair.Value == state);
            Rewrite(track, ordered, Mathf.Max(RimeEmoCompiler.Duration(track), time + Mathf.Max(1f / 60, length)));
            return index;
        }

        // A cue's own settings, without its place in the track (transition is recomputed where it lands).
        internal static MotionState CopyState(MotionState s) => new MotionState
        {
            clip = s.clip, animated = s.animated, sourceStart = s.sourceStart, speed = s.speed,
            hold = s.hold, endHold = s.endHold, transition = 0, interpolation = s.interpolation,
        };

        internal static void Remove(PartTrack track, int index)
        {
            if (index < 0 || index >= track.states.Count) return;
            var times = Times(track);
            track.states.RemoveAt(index);
            if (track.states.Count == 0) { track.startTime = 0; return; }
            track.startTime = index == 0 ? times[1] : times[0];
            if (index > 0)
            {
                var previous = track.states[index - 1];
                // Preserve the previous block's end, including when deleting the final block.
                // Only the gap to a surviving successor changes.
                previous.transition = index < track.states.Count
                    ? Mathf.Max(0, times[index + 1] - times[index - 1] - Mathf.Max(0, previous.hold) - Mathf.Max(0, previous.endHold))
                    : 0;
            }
        }

        internal static int Split(PartTrack track, int index, float absoluteTime)
        {
            if (track == null || index < 0 || index >= track.states.Count) return -1;
            var times = Times(track); var s = track.states[index]; float at = Mathf.Clamp(Clean(absoluteTime), times[index], times[index] + Mathf.Max(0, s.hold));
            float cut = at - times[index]; if (cut <= Epsilon || cut >= s.hold - Epsilon) return -1;
            var right = new MotionState { clip = s.clip, hold = s.hold - cut, endHold = s.endHold, transition = s.transition, interpolation = s.interpolation, animated = s.animated, sourceStart = s.sourceStart + cut * s.speed, speed = s.speed };
            s.hold = cut; s.endHold = 0; s.transition = 0;
            track.states.Insert(index + 1, right); return index + 1;
        }

        internal static int Duplicate(PartTrack track, int index)
        {
            if (track == null || index < 0 || index >= track.states.Count) return -1;
            var s = track.states[index]; var copy = new MotionState { clip = s.clip, hold = s.hold, endHold = s.endHold, transition = 0, interpolation = s.interpolation, animated = s.animated, sourceStart = s.sourceStart, speed = s.speed };
            if (track.states.Count > 0) track.states[track.states.Count - 1].transition = 0;
            track.states.Add(copy); return track.states.Count - 1;
        }

        private static void Rewrite(PartTrack track, KeyValuePair<float, MotionState>[] keys, float end)
        {
            track.states = keys.Select(p => p.Value).ToList();
            track.startTime = keys.Length > 0 ? keys[0].Key : 0;
            for (int i = 0; i < keys.Length; i++)
            {
                var state = keys[i].Value;
                float span = Mathf.Max(0, (i + 1 < keys.Length ? keys[i + 1].Key : end) - keys[i].Key);
                if (i + 1 == keys.Length)
                {
                    state.endHold = Mathf.Clamp(state.endHold, 0, span);
                    state.hold = Mathf.Max(0, span - state.endHold);
                }
                else
                {
                    state.hold = Mathf.Clamp(state.hold, 0, span);
                    state.endHold = Mathf.Clamp(state.endHold, 0, span - state.hold);
                    state.transition = span - state.hold - state.endHold;
                }
            }
        }
    }
}

