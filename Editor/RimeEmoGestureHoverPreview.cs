using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    internal sealed class RimeEmoGestureHoverPreview
    {
        private const double HoverDelay = 0.2;
        private const double FrameInterval = 1.0 / 24;
        private const float ImageSize = 224;
        private readonly VisualElement root;
        private readonly VisualElement popup;
        private readonly Label title;
        private readonly IMGUIContainer image;
        private RimeEmoPreview preview;
        private RimeEmoProject activeProject;
        private GameObject activeAvatar;
        private VisualElement anchor;
        private AnimationClip clip;
        private RimeEmoMotion motion;
        private double showAt, startedAt, nextFrame;
        private float duration, sampleTime;
        private bool looping, visible;

        internal RimeEmoGestureHoverPreview(VisualElement root)
        {
            this.root = root;
            popup = new VisualElement { name = "gesture-hover-preview", pickingMode = PickingMode.Ignore };
            popup.AddToClassList("re-gesture-preview");
            title = new Label { pickingMode = PickingMode.Ignore };
            title.AddToClassList("re-gesture-preview-title");
            image = new IMGUIContainer(Draw) { pickingMode = PickingMode.Ignore, focusable = false };
            popup.Add(title);
            popup.Add(image);
            popup.style.display = DisplayStyle.None;
            root.Add(popup);
            root.RegisterCallback<PointerDownEvent>(_ => Hide(), TrickleDown.TrickleDown);
            root.RegisterCallback<DragUpdatedEvent>(_ => Hide(), TrickleDown.TrickleDown);
            root.RegisterCallback<WheelEvent>(_ => Hide(), TrickleDown.TrickleDown);
            root.RegisterCallback<DetachFromPanelEvent>(_ => Release());
            root.Q<ScrollView>("gesture-content").verticalScroller.valueChanged += _ => Hide();
            root.Q<ScrollView>("gesture-matrix-scroll").horizontalScroller.valueChanged += _ => Hide();
        }

        internal void Bind(VisualElement element, AnimationClip targetClip, RimeEmoMotion targetMotion, bool defaultFace = false)
        {
            if (targetMotion && targetMotion.animationClip)
            {
                targetClip = targetMotion.animationClip;
                targetMotion = null;
            }
            if (!targetClip && !targetMotion && !defaultFace) return;
            element.RegisterCallback<PointerEnterEvent>(e =>
            {
                if (e.pointerType != UnityEngine.UIElements.PointerType.mouse || e.pressedButtons != 0) return;
                Hide();
                anchor = element;
                clip = targetClip;
                motion = targetMotion;
                showAt = EditorApplication.timeSinceStartup + HoverDelay;
            });
            element.RegisterCallback<PointerLeaveEvent>(_ => { if (anchor == element) Hide(); });
            element.RegisterCallback<DetachFromPanelEvent>(_ => { if (anchor == element) Hide(); });
            element.RegisterCallback<TooltipEvent>(e =>
            {
                if (!visible || anchor != element) return;
                e.tooltip = "";
                e.StopImmediatePropagation();
            });
        }

        internal void Tick(RimeEmoProject project, GameObject avatar)
        {
            if (!project || !avatar || EditorApplication.isPlayingOrWillChangePlaymode || root.panel == null)
            {
                Release();
                return;
            }
            if (preview != null && (project != activeProject || avatar != activeAvatar)) Release();
            if (anchor == null) return;

            double now = EditorApplication.timeSinceStartup;
            if (!visible)
            {
                if (now < showAt) return;
                try { Show(project, avatar, now); }
                catch (Exception e) { Release(); Debug.LogException(e); }
                return;
            }
            if (duration <= 0 || now < nextFrame || (!looping && sampleTime >= duration)) return;
            nextFrame = now + FrameInterval;
            float elapsed = (float)(now - startedAt);
            sampleTime = looping ? elapsed % duration : Mathf.Min(elapsed, duration);
            image.MarkDirtyRepaint();
        }

        private void Show(RimeEmoProject project, GameObject avatar, double now)
        {
            var bounds = root.contentRect;
            float size = Mathf.Min(ImageSize, bounds.width - 28, bounds.height - 52);
            if (size < 64) { Hide(); return; }

            if (preview == null)
            {
                preview = new RimeEmoPreview();
                preview.SetAvatar(avatar);
                activeAvatar = avatar;
                activeProject = project;
            }
            preview.SetDefaults(project);
            duration = motion ? RimeEmoCompiler.Duration(motion)
                : clip && RimeEmoClipClassification.Classify(project, clip) == RimeEmoClipKind.Dynamic ? clip.length : 0;
            looping = motion ? motion.loop : clip && AnimationUtility.GetAnimationClipSettings(clip).loopTime;
            title.text = motion ? motion.name : clip ? clip.name : "Default Face";
            image.style.height = size;
            const float gap = 8;
            float width = size + 12;
            var target = root.WorldToLocal(anchor.worldBound);
            float left = target.xMax + gap;
            if (left + width > bounds.xMax - gap) left = target.xMin - gap - width;
            popup.style.left = Mathf.Clamp(left, bounds.xMin + gap, bounds.xMax - width - gap);
            popup.style.top = Mathf.Clamp(target.yMin, bounds.yMin + gap, bounds.yMax - size - 32 - gap);
            popup.style.width = width;
            popup.style.display = DisplayStyle.Flex;
            popup.BringToFront();
            visible = true;
            startedAt = now;
            nextFrame = now + FrameInterval;
            sampleTime = 0;
            image.MarkDirtyRepaint();
        }

        private void Draw()
        {
            if (!visible || preview == null || Event.current == null || Event.current.type != EventType.Repaint) return;
            Rect rect = image.contentRect;
            if (rect.width <= 0 || rect.height <= 0) return;
            try { preview.Draw(rect, clip, sampleTime, motion: motion); }
            catch (Exception e) { Release(); Debug.LogException(e); }
        }

        internal void Hide()
        {
            anchor = null;
            clip = null;
            motion = null;
            if (!visible) return;
            visible = false;
            popup.style.display = DisplayStyle.None;
        }

        internal void Release()
        {
            Hide();
            preview?.Dispose();
            preview = null;
            activeAvatar = null;
            activeProject = null;
        }
    }
}
