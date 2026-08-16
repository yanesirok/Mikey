using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Profile
{
    /// <summary>
    /// Self-contained five-axis capability radar for Profile's hero element: nested
    /// guide-grid pentagons, axis lines, a soft layered red glow, and the data
    /// polygon, all drawn with <see cref="Painter2D"/> inside
    /// <c>generateVisualContent</c> (no shader/third-party chart package). Vertex
    /// geometry is delegated to <see cref="ProfileRadarMath"/> so it stays directly
    /// unit-testable. The five vertex/value labels are real <see cref="Label"/>
    /// children (crisp text, not drawn into the mesh), repositioned alongside the
    /// polygon. <see cref="Progress"/> drives the 0 (collapsed at center) -> 1
    /// (full target values) entrance animation; <see cref="ProfileController"/> owns
    /// the coroutine that animates it, so this class itself never runs a per-frame
    /// Update loop and only repaints when something actually changes.
    /// </summary>
    public sealed class ProfileRadarChart : VisualElement
    {
        private const int GridRingCount = 4;
        private const float RadiusFillFraction = 0.72f;
        private const float LabelOutset = 34f;

        private static readonly Color GridColor = new Color(0.75f, 0.72f, 0.65f, 0.12f);
        private static readonly Color OuterGridColor = new Color(0.75f, 0.72f, 0.65f, 0.22f);
        private static readonly Color AxisColor = new Color(0.75f, 0.72f, 0.65f, 0.12f);
        private static readonly Color GlowOuter = new Color(0.78f, 0.16f, 0.09f, 0.05f);
        private static readonly Color GlowInner = new Color(0.78f, 0.16f, 0.09f, 0.08f);
        private static readonly Color DataFill = new Color(0.78f, 0.16f, 0.09f, 0.32f);
        private static readonly Color DataStroke = new Color(0.90f, 0.30f, 0.20f, 0.95f);

        private float[] _targetValues = new float[ProfileRadarMath.AxisCount];
        private float _maxValue = 100f;
        private float _progress;

        private readonly Label[] _axisLabels = new Label[ProfileRadarMath.AxisCount];
        private readonly Label[] _valueLabels = new Label[ProfileRadarMath.AxisCount];

        public ProfileRadarChart()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                RepositionLabels();
                MarkDirtyRepaint();
            });

            for (int i = 0; i < ProfileRadarMath.AxisCount; i++)
            {
                var axisLabel = new Label(ProfileRadarMath.AxisLabels[i]) { pickingMode = PickingMode.Ignore };
                axisLabel.AddToClassList("profile-radar-label");
                axisLabel.style.opacity = 0f;
                Add(axisLabel);
                _axisLabels[i] = axisLabel;

                var valueLabel = new Label { pickingMode = PickingMode.Ignore };
                valueLabel.AddToClassList("profile-radar-label");
                valueLabel.AddToClassList("profile-radar-label--value");
                valueLabel.style.opacity = 0f;
                Add(valueLabel);
                _valueLabels[i] = valueLabel;
            }
        }

        /// <summary>Target capability values in <see cref="ProfileRadarMath.AxisLabels"/> order.</summary>
        public void SetValues(float[] values, float maxValue)
        {
            if (values == null || values.Length != ProfileRadarMath.AxisCount)
                throw new ArgumentException($"Expected {ProfileRadarMath.AxisCount} values.", nameof(values));

            _targetValues = values;
            _maxValue = maxValue;
            for (int i = 0; i < ProfileRadarMath.AxisCount; i++)
                _valueLabels[i].text = Mathf.RoundToInt(values[i]).ToString();

            RepositionLabels();
            MarkDirtyRepaint();
        }

        /// <summary>0 = collapsed at center (entrance start), 1 = settled at full target values.</summary>
        public float Progress
        {
            get => _progress;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(clamped, _progress))
                    return;
                _progress = clamped;

                for (int i = 0; i < ProfileRadarMath.AxisCount; i++)
                {
                    _axisLabels[i].style.opacity = _progress;
                    _valueLabels[i].style.opacity = _progress;
                }

                RepositionLabels();
                MarkDirtyRepaint();
            }
        }

        private float Radius => Mathf.Min(contentRect.width, contentRect.height) * 0.5f * RadiusFillFraction;

        private Vector2 Center => new Vector2(contentRect.width * 0.5f, contentRect.height * 0.5f);

        private void RepositionLabels()
        {
            if (contentRect.width <= 0f || contentRect.height <= 0f)
                return;

            float radius = Radius;
            Vector2 center = Center;

            for (int i = 0; i < ProfileRadarMath.AxisCount; i++)
            {
                Vector2 anchor = ProfileRadarMath.LabelAnchor(i, radius, center, LabelOutset);
                PlaceLabel(_axisLabels[i], anchor, -10f);
                PlaceLabel(_valueLabels[i], anchor, 12f);
            }
        }

        private static void PlaceLabel(Label label, Vector2 anchor, float verticalOffset)
        {
            label.style.left = anchor.x;
            label.style.top = anchor.y + verticalOffset;
            label.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (contentRect.width <= 0f || contentRect.height <= 0f)
                return;

            float radius = Radius;
            Vector2 center = Center;
            Painter2D painter = mgc.painter2D;

            for (int ring = 1; ring <= GridRingCount; ring++)
            {
                float fraction = ring / (float)GridRingCount;
                Color color = ring == GridRingCount ? OuterGridColor : GridColor;
                DrawPolygonOutline(painter, ProfileRadarMath.GridRing(fraction, radius, center), color, 1.5f);
            }

            for (int i = 0; i < ProfileRadarMath.AxisCount; i++)
            {
                Vector2 outer = center + ProfileRadarMath.AxisDirection(i) * radius;
                painter.strokeColor = AxisColor;
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.MoveTo(center);
                painter.LineTo(outer);
                painter.Stroke();
            }

            if (_progress <= 0f)
                return;

            Vector2[] data = ProfileRadarMath.Polygon(_targetValues, _maxValue, radius * _progress, center);

            // Restrained glow: a couple of larger, very-low-alpha copies of the same
            // polygon behind the real one — no shader/blur pass needed.
            DrawPolygonFill(painter, ScalePolygon(data, center, 1.3f), GlowOuter);
            DrawPolygonFill(painter, ScalePolygon(data, center, 1.15f), GlowInner);

            DrawPolygonFill(painter, data, DataFill);
            DrawPolygonOutline(painter, data, DataStroke, 2.5f);
        }

        private static Vector2[] ScalePolygon(Vector2[] points, Vector2 center, float scale)
        {
            var scaled = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
                scaled[i] = center + (points[i] - center) * scale;
            return scaled;
        }

        private static void DrawPolygonFill(Painter2D painter, Vector2[] points, Color color)
        {
            if (points.Length == 0)
                return;
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++)
                painter.LineTo(points[i]);
            painter.ClosePath();
            painter.Fill();
        }

        private static void DrawPolygonOutline(Painter2D painter, Vector2[] points, Color color, float width)
        {
            if (points.Length == 0)
                return;
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++)
                painter.LineTo(points[i]);
            painter.ClosePath();
            painter.Stroke();
        }
    }
}
