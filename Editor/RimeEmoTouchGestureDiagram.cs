using UnityEngine;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // VRChat's Touch gesture inputs shown on the Quest 3 Touch Plus controller.
    // Yellow highlights the contacted controls: thumb surface, index trigger and side grip.
    internal sealed class RimeEmoTouchGestureDiagram : VisualElement
    {
        internal enum Input { Off, Touch, Press }

        internal static readonly (Input thumb, Input trigger, Input grip)[] Inputs =
        {
            default, // Neutral has no pose in the Touch reference.
            (Input.Touch, Input.Press, Input.Press), // Fist
            (Input.Off, Input.Off, Input.Off),       // HandOpen
            (Input.Touch, Input.Off, Input.Press),  // FingerPoint
            (Input.Touch, Input.Off, Input.Off),    // Victory
            (Input.Touch, Input.Press, Input.Off),  // RockNRoll
            (Input.Off, Input.Off, Input.Press),    // HandGun
            (Input.Off, Input.Press, Input.Press)   // ThumbsUp
        };

        private static readonly Color Shell = new Color32(214, 214, 214, 255);
        private static readonly Color Edge = new Color32(112, 112, 112, 255);
        private static readonly Color Dark = new Color32(44, 44, 44, 255);
        private static readonly Color Inactive = new Color32(188, 188, 188, 255);
        private static readonly Color Contact = new Color32(255, 216, 88, 255);

        internal int Gesture { get; }
        internal bool LeftHand { get; }

        internal RimeEmoTouchGestureDiagram(int gesture, bool leftHand)
        {
            Gesture = gesture;
            LeftHand = leftHand;
            AddToClassList("re-touch-diagram");
            tooltip = RimeEmoGestures.Names[gesture] + "\n" + Describe(gesture);
            generateVisualContent += Draw;
        }

        internal static string Describe(int gesture)
        {
            if (gesture == 0) return RimeEmoLocalization.T("ジェスチャーの基本状態。");
            var (thumb, trigger, grip) = Inputs[gesture];
            return string.Format(RimeEmoLocalization.T("親指 {0}  /  トリガー {1}  /  グリップ {2}"),
                RimeEmoLocalization.T(thumb == Input.Touch ? "触れる" : "離す"),
                RimeEmoLocalization.T(trigger == Input.Press ? "押す" : "離す"),
                RimeEmoLocalization.T(grip == Input.Press ? "握る" : "緩める"));
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            var rect = contentRect;
            float scale = Mathf.Min(rect.width / 50, rect.height / 48);
            if (float.IsNaN(scale) || scale <= 0) return;
            var center = rect.center;
            // Preserve the original shell and button orientation for each hand.
            Vector2 Point(float x, float y) => center + new Vector2((LeftHand ? 25 - x : x - 25) * scale, (y - 24) * scale);
            var (thumb, trigger, grip) = Inputs[Gesture];

            // White body of the ringless Touch Plus, shown in three-quarter view.
            painter.BeginPath();
            painter.MoveTo(Point(7, 14));
            painter.BezierCurveTo(Point(8, 24), Point(16, 24), Point(19, 29));
            painter.BezierCurveTo(Point(21, 32), Point(23, 41), Point(25, 44));
            painter.BezierCurveTo(Point(29, 48), Point(40, 46), Point(40, 39));
            painter.BezierCurveTo(Point(38, 29), Point(33, 25), Point(38, 17));
            painter.ClosePath();
            FillOutline(painter, Shell, Edge, scale);

            // Trigger curves down from the faceplate rim, beside the neck of the handle.
            // In the left-hand view this is the right edge, as in the annotated reference.
            painter.BeginPath();
            painter.MoveTo(Point(10, 17));
            painter.BezierCurveTo(Point(8, 20), Point(8, 25), Point(9, 28));
            painter.BezierCurveTo(Point(10, 31), Point(15, 29), Point(19, 26));
            painter.LineTo(Point(20, 22));
            painter.ClosePath();
            FillOutline(painter, trigger == Input.Press ? Contact : Edge, Dark, scale);

            painter.BeginPath();
            painter.MoveTo(Point(12, 6));
            painter.BezierCurveTo(Point(20, 1), Point(34, 3), Point(38, 10));
            painter.BezierCurveTo(Point(43, 20), Point(32, 25), Point(21, 24));
            painter.BezierCurveTo(Point(9, 23), Point(2, 14), Point(12, 6));
            painter.ClosePath();
            FillOutline(painter, Dark, Edge, scale);
            Disc(painter, Point(17, 12), 6.2f * scale, Edge);
            Disc(painter, Point(17, 10.5f), 5.2f * scale, thumb == Input.Touch ? Contact : Dark);
            Disc(painter, Point(32, 12), 2.4f * scale, Inactive);
            Disc(painter, Point(28, 17), 2.5f * scale, Inactive);
            Disc(painter, Point(19, 19), 1.5f * scale, Inactive);

            // Side grip sits on the inner face of the handle.
            painter.BeginPath();
            painter.MoveTo(Point(21, 28));
            painter.BezierCurveTo(Point(26, 26), Point(28, 30), Point(30, 36));
            painter.BezierCurveTo(Point(30, 42), Point(25, 43), Point(23, 37));
            painter.ClosePath();
            FillOutline(painter, grip == Input.Press ? Contact : Edge, Dark, scale);
        }

        private static void FillOutline(Painter2D painter, Color fill, Color outline, float scale)
        {
            painter.fillColor = fill;
            painter.Fill(FillRule.OddEven);
            painter.strokeColor = outline;
            painter.lineWidth = scale;
            painter.Stroke();
        }

        private static void Disc(Painter2D painter, Vector2 center, float radius, Color fill)
        {
            painter.BeginPath();
            painter.Arc(center, radius, 0, 360);
            painter.ClosePath();
            painter.fillColor = fill;
            painter.Fill(FillRule.OddEven);
        }

    }
}
