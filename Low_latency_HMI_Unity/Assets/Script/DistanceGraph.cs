using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Created by DashboardController. Do not attach this class to a GameObject.
/// </summary>

public class DistanceGraph : VisualElement
{
    private const double WindowSeconds = 10.0;
    private const float MinDistance = 0f;
    private const float MaxDistance = 250f;
    private const int MaxSamples = 1024;

    private struct GraphSample
    {
        public double Time;
        public float Distance;
    }

    // Only accessed from Unity's main thread.
    private readonly Queue<GraphSample> samples = new Queue<GraphSample>(MaxSamples);
    private double latestTime;

    public DistanceGraph()
    {
        name = "distance_graph";
        pickingMode = PickingMode.Ignore;
        style.flexGrow = 1f;
        style.minHeight = 0f;
        style.overflow = Overflow.Hidden;

        generateVisualContent += DrawGraph;
    }

    public void AddSample(double time, float distance)
    {
        if (double.IsNaN(time) || double.IsInfinity(time) ||
            float.IsNaN(distance) || float.IsInfinity(distance)) return;

        latestTime = time;
        samples.Enqueue(new GraphSample { Time = time, Distance = distance });

        double oldestVisibleTime = latestTime - WindowSeconds;

        while (samples.Count > 0 && samples.Peek().Time < oldestVisibleTime)
            samples.Dequeue();

        // Safety bound in addition to the 10-second time window.
        while (samples.Count > MaxSamples)
            samples.Dequeue();

        // Ask UI Toolkit to redraw using the updated buffer.
        MarkDirtyRepaint();
    }

    public void ClearSamples()
    {
        samples.Clear();
        MarkDirtyRepaint();
    }

    private void DrawGraph(MeshGenerationContext context)
    {
        Rect rect = contentRect;
        const float padding = 4f;
        if (rect.width <= padding * 2f || rect.height <= padding * 2f) return;

        Rect plot = new Rect(rect.xMin + padding, rect.yMin + padding,
            rect.width - padding * 2f, rect.height - padding * 2f);

        Painter2D painter = context.painter2D;

        // Grid: 2-second columns and 100-cm rows.
        painter.lineWidth = 1f;
        painter.strokeColor = new Color(0.23f, 0.26f, 0.31f);
        painter.BeginPath();
        for (int i = 0; i <= 5; i++)
        {
            float x = plot.xMin + plot.width * i / 5f;
            painter.MoveTo(new Vector2(x, plot.yMin));
            painter.LineTo(new Vector2(x, plot.yMax));
        }
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.yMin + plot.height * i / 4f;
            painter.MoveTo(new Vector2(plot.xMin, y));
            painter.LineTo(new Vector2(plot.xMax, y));
        }
        painter.Stroke();

        if (samples.Count < 2) return;

        painter.lineWidth = 2f;
        painter.strokeColor = new Color(0.25f, 0.85f, 0.75f);
        painter.BeginPath();

        double windowStart = latestTime - WindowSeconds;
        bool firstPoint = true;
        foreach (GraphSample sample in samples)
        {
            // X: actual UI sample time within the latest 10 seconds.
            float time01 = Mathf.Clamp01((float)((sample.Time - windowStart) / WindowSeconds));
            // Y: distance mapped to a fixed 0-250 cm range.
            // Clamping affects drawing only; the stored value is unchanged.
            float distance01 = Mathf.InverseLerp(MinDistance, MaxDistance, sample.Distance);
            Vector2 point = new Vector2(
                plot.xMin + time01 * plot.width,
                plot.yMax - distance01 * plot.height);

            if (firstPoint)
            {
                painter.MoveTo(point);
                firstPoint = false;
            }
            else painter.LineTo(point);
        }
        painter.Stroke();
    }
}