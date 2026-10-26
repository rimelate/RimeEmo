using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    public sealed partial class RimeEmoWindow
    {
        [SerializeField] private float materialPanelRatio = 2f / 3;
        [SerializeField] private float timelinePanelRatio = .68f;
        // Height of the bottom card; expressions and motions keep their own.
        // 0 = not dragged yet: the expression card then takes a share of the page, so Shape Keys get most of a small
        // window. (Renamed from staticPanelHeight so the old fixed 300px saved in window layouts is not kept.)
        [SerializeField] private float expressionPanelHeight;
        [SerializeField] private float timelinePanelHeight = TimelinePanelDefault;
        private const float StaticPanelShare = .58f, StaticPanelMinimum = 160;
        // All five part tracks stay visible; extra height goes to the key details beside them.
        private const float TimelinePanelDefault = 240, TimelinePanelMinimum = 240;
        // Keeps the material card's search, two rows and its add row, and about 100px of preview; at 150 they were clipped.
        private const float WorkspaceMinimum = 220;

        private void BindPanelWidths()
        {
            BindPanelDivider(UI<VisualElement>("editor"), UI<VisualElement>("preview-panel"),
                400, 280, 2f / 3, () => materialPanelRatio, value => materialPanelRatio = value);
            BindPanelDivider(UI<VisualElement>("timeline-host"), UI<VisualElement>("queue-details"),
                450, 260, .68f, () => timelinePanelRatio, value => timelinePanelRatio = value);
        }

        private static void BindPanelDivider(VisualElement left, VisualElement right,
            float leftMinimum, float rightMinimum, float initial, Func<float> read, Action<float> write)
        {
            var parent = left.parent;
            var handle = new VisualElement { name = left.name + "-divider", focusable = true,
                tooltip = RimeEmoLocalization.T("ドラッグで幅を調整・ダブルクリックで元に戻す") };
            handle.AddToClassList("re-panel-divider");
            parent.Insert(parent.IndexOf(right), handle);
            left.style.minWidth = leftMinimum; right.style.minWidth = rightMinimum;
            left.style.width = right.style.width = StyleKeyword.Auto;
            left.style.flexBasis = right.style.flexBasis = 0;
            left.style.flexShrink = right.style.flexShrink = 1;

            float Available() => parent.contentRect.width - 8
                - left.resolvedStyle.marginLeft - left.resolvedStyle.marginRight
                - right.resolvedStyle.marginLeft - right.resolvedStyle.marginRight;
            void Apply()
            {
                float width = Available();
                if (float.IsNaN(width) || width < leftMinimum + rightMinimum) return;
                float ratio = Mathf.Clamp(read(), leftMinimum / width, 1 - rightMinimum / width);
                // Use explicit bases: flex-grow distributes only space left after the minimum widths.
                left.style.flexBasis = width * ratio;
                right.style.flexBasis = width * (1 - ratio);
                left.style.flexGrow = right.style.flexGrow = 0;
            }
            int pointer = -1;
            float startX = 0, startWidth = 0;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                handle.Focus();
                if (e.clickCount == 2) { write(initial); Apply(); e.StopPropagation(); return; }
                pointer = e.pointerId; startX = e.position.x; startWidth = left.layout.width;
                handle.CapturePointer(pointer); handle.AddToClassList("is-dragging"); e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (pointer != e.pointerId || !handle.HasPointerCapture(pointer)) return;
                float width = Available();
                if (width >= leftMinimum + rightMinimum)
                {
                    write(Mathf.Clamp(startWidth + e.position.x - startX, leftMinimum, width - rightMinimum) / width);
                    Apply();
                }
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (pointer != e.pointerId) return;
                handle.ReleasePointer(pointer); pointer = -1; handle.RemoveFromClassList("is-dragging"); e.StopPropagation();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => { pointer = -1; handle.RemoveFromClassList("is-dragging"); });
            handle.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.LeftArrow && e.keyCode != KeyCode.RightArrow) return;
                float width = Available();
                if (width >= leftMinimum + rightMinimum)
                {
                    write(Mathf.Clamp((left.layout.width + (e.keyCode == KeyCode.LeftArrow ? -10 : 10)) / width,
                        leftMinimum / width, 1 - rightMinimum / width));
                    Apply();
                }
                e.StopPropagation();
            });
            parent.RegisterCallback<GeometryChangedEvent>(_ => Apply());
            left.style.flexGrow = read(); right.style.flexGrow = 1 - read();
        }

        // Divider between the workspace (materials and preview) and the bottom card.
        private void BindPanelHeights()
        {
            var page = UI<VisualElement>("editor-page");
            var workspace = page.Q<VisualElement>(className: "re-workspace");
            var handle = new VisualElement { name = "workspace-divider", focusable = true,
                tooltip = RimeEmoLocalization.T("ドラッグで高さを調整・ダブルクリックで元に戻す") };
            handle.AddToClassList("re-panel-divider"); handle.AddToClassList("re-panel-divider--horizontal");
            page.Insert(page.IndexOf(workspace) + 1, handle);

            bool IsMotion() => mode == 1;
            VisualElement Card() => UI<VisualElement>(IsMotion() ? "motion-controls" : "static-controls");
            VisualElement Target() => UI<VisualElement>(IsMotion() ? "block-editor" : "static-controls");
            float Minimum() => IsMotion() ? TimelinePanelMinimum : StaticPanelMinimum;
            float Read() => IsMotion() ? timelinePanelHeight : expressionPanelHeight > 0 ? expressionPanelHeight : page.contentRect.height * StaticPanelShare;
            void Write(float value) { if (IsMotion()) timelinePanelHeight = value; else expressionPanelHeight = value; }
            // Room the workspace can still give up; negative while the page overflows (small window, auto-split sheet).
            float Spare()
            {
                float used = 0;
                foreach (var child in page.Children())
                    if (child.resolvedStyle.display == DisplayStyle.Flex)
                        used += child.layout.height + child.resolvedStyle.marginTop + child.resolvedStyle.marginBottom;
                return workspace.layout.height - WorkspaceMinimum - Mathf.Max(0, used - page.contentRect.height);
            }
            float Maximum() => Mathf.Max(Minimum(), Target().layout.height + Spare());
            void Apply()
            {
                bool shown = Card().resolvedStyle.display == DisplayStyle.Flex;
                handle.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (!shown || float.IsNaN(page.contentRect.height) || float.IsNaN(Target().layout.height)) return;
                // The stored height is kept, so the panel grows back when the window does.
                float height = Mathf.Clamp(Read(), Minimum(), Maximum());
                if (Mathf.Abs(Target().resolvedStyle.height - height) > 0.5f) Target().style.height = height;
            }
            void Resize(float height) { Write(Mathf.Clamp(height, Minimum(), Maximum())); Apply(); }

            int pointer = -1;
            float startY = 0, startHeight = 0;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                handle.Focus();
                if (e.clickCount == 2) { Write(IsMotion() ? TimelinePanelDefault : 0); Apply(); e.StopPropagation(); return; }
                pointer = e.pointerId; startY = e.position.y; startHeight = Target().layout.height;
                handle.CapturePointer(pointer); handle.AddToClassList("is-dragging"); e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (pointer != e.pointerId || !handle.HasPointerCapture(pointer)) return;
                // Dragging down hands height from the bottom card to the workspace.
                Resize(startHeight - (e.position.y - startY));
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (pointer != e.pointerId) return;
                handle.ReleasePointer(pointer); pointer = -1; handle.RemoveFromClassList("is-dragging"); e.StopPropagation();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => { pointer = -1; handle.RemoveFromClassList("is-dragging"); });
            handle.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.UpArrow && e.keyCode != KeyCode.DownArrow) return;
                Resize(Target().layout.height + (e.keyCode == KeyCode.UpArrow ? 10 : -10));
                e.StopPropagation();
            });
            page.RegisterCallback<GeometryChangedEvent>(_ => Apply());
            // The cards are hidden while the window is built; showing one does not resize the page, so it also re-applies.
            UI<VisualElement>("static-controls").RegisterCallback<GeometryChangedEvent>(_ => Apply());
            UI<VisualElement>("motion-controls").RegisterCallback<GeometryChangedEvent>(_ => Apply());
        }
    }
}
