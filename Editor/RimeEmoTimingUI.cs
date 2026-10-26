using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        private void DrawQueueTimingInspector(VisualElement host, PartTrack track, MotionState state)
        {
            if (activeState < 0 || activeState >= track.states.Count) return;
            FloatField transition = null; DropdownField interpolation = null;
            var hold = new FloatField(RimeEmoLocalization.T("保持（秒）")) { name = "queue-hold", value = RimeEmoTimeline.HoldDuration(state), isDelayed = true, formatString = "0.###" };
            hold.RegisterValueChangedCallback(e =>
            {
                if (float.IsNaN(e.newValue) || float.IsInfinity(e.newValue)) { hold.SetValueWithoutNotify(RimeEmoTimeline.HoldDuration(state)); return; }
                EditMotion(() => RimeEmoTimeline.SetHoldDuration(track, activeState, Mathf.Max(0, e.newValue)));
                hold.SetValueWithoutNotify(RimeEmoTimeline.HoldDuration(state));
                transition?.SetValueWithoutNotify(Mathf.Max(0, state.transition));
                interpolation?.SetEnabled(state.transition > 0);
            }); host.Add(hold);
            if (activeState + 1 < track.states.Count)
            {
                transition = new FloatField(RimeEmoLocalization.T("移行（秒）")) { name = "queue-transition", value = Mathf.Max(0, state.transition), isDelayed = true, formatString = "0.###" };
                transition.RegisterValueChangedCallback(e =>
                {
                    if (float.IsNaN(e.newValue) || float.IsInfinity(e.newValue)) { transition.SetValueWithoutNotify(Mathf.Max(0, state.transition)); return; }
                    EditMotion(() => RimeEmoTimeline.SetTransitionDuration(track, activeState, Mathf.Max(0, e.newValue)));
                    transition.SetValueWithoutNotify(Mathf.Max(0, state.transition));
                    hold.SetValueWithoutNotify(RimeEmoTimeline.HoldDuration(state));
                    interpolation?.SetEnabled(state.transition > 0);
                }); host.Add(transition);
                interpolation = new DropdownField(RimeEmoLocalization.T("移行の補間"), new System.Collections.Generic.List<string> { RimeEmoLocalization.T("一定の速さ"), RimeEmoLocalization.T("なめらか") }, state.interpolation == Interpolation.Linear ? 0 : 1) { name = "queue-interpolation" };
                interpolation.SetEnabled(state.transition > 0);
                interpolation.RegisterValueChangedCallback(e => EditMotion(() => state.interpolation = interpolation.index == 0 ? Interpolation.Linear : Interpolation.EaseInOut)); host.Add(interpolation);
                host.Add(new Label(RimeEmoLocalization.T("合計時間は固定です。間隔を変えるには次のキーを移動します。")) { name = "queue-timing-hint" });
            }
        }
    }
}
