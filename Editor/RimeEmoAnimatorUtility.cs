using System;
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace RimeEmo.Editor
{
    public static class RimeEmoAnimatorUtility
    {
        public static void WalkStates(AnimatorController controller, Action<string, AnimatorControllerLayer, ChildAnimatorState> visitor)
        {
            if (controller == null || visitor == null)
            {
                return;
            }

            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                WalkStateMachine(layer.name, layer, layer.stateMachine, visitor);
            }
        }

        private static void WalkStateMachine(string path, AnimatorControllerLayer layer, AnimatorStateMachine stateMachine, Action<string, AnimatorControllerLayer, ChildAnimatorState> visitor)
        {
            if (stateMachine == null)
            {
                return;
            }

            foreach (ChildAnimatorState state in stateMachine.states)
            {
                string statePath = string.IsNullOrEmpty(path) ? state.state.name : path + "/" + state.state.name;
                visitor(statePath, layer, state);
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                string childPath = string.IsNullOrEmpty(path) ? child.stateMachine.name : path + "/" + child.stateMachine.name;
                WalkStateMachine(childPath, layer, child.stateMachine, visitor);
            }
        }

        public static void WalkMotion(Motion motion, bool fromBlendTree, Action<AnimationClip, bool> visitor)
        {
            if (motion == null || visitor == null)
            {
                return;
            }

            if (motion is AnimationClip clip)
            {
                visitor(clip, fromBlendTree);
                return;
            }

            if (motion is BlendTree blendTree)
            {
                foreach (ChildMotion child in blendTree.children)
                {
                    WalkMotion(child.motion, true, visitor);
                }
            }
        }

        public static void ReplaceMotionReferences(AnimatorController controller, IReadOnlyDictionary<AnimationClip, AnimationClip> replacements)
        {
            if (controller == null || replacements == null || replacements.Count == 0)
            {
                return;
            }

            WalkStates(controller, (statePath, layer, childState) =>
            {
                AnimatorState state = childState.state;
                state.motion = ReplaceMotion(state.motion, replacements);
            });
        }

        private static Motion ReplaceMotion(Motion motion, IReadOnlyDictionary<AnimationClip, AnimationClip> replacements)
        {
            if (motion == null)
            {
                return null;
            }

            if (motion is AnimationClip clip && replacements.TryGetValue(clip, out AnimationClip replacement))
            {
                return replacement;
            }

            if (motion is BlendTree blendTree)
            {
                ChildMotion[] children = blendTree.children;
                bool changed = false;
                for (int i = 0; i < children.Length; i++)
                {
                    Motion replaced = ReplaceMotion(children[i].motion, replacements);
                    if (replaced != children[i].motion)
                    {
                        children[i].motion = replaced;
                        changed = true;
                    }
                }

                if (changed)
                {
                    blendTree.children = children;
                }
            }

            return motion;
        }

        public static bool HasMixedWriteDefaults(AnimatorController controller, out int onCount, out int offCount)
        {
            int writeDefaultsOn = 0;
            int writeDefaultsOff = 0;

            WalkStates(controller, (statePath, layer, childState) =>
            {
                if (childState.state.writeDefaultValues)
                {
                    writeDefaultsOn++;
                }
                else
                {
                    writeDefaultsOff++;
                }
            });

            onCount = writeDefaultsOn;
            offCount = writeDefaultsOff;
            return onCount > 0 && offCount > 0;
        }
    }
}

