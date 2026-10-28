using System;

/// <summary>
/// No MonoBehaviour, no Unity API, no UI refresh logic.
/// UDPReceiver owns this object and serializes access with its sampleLock.
/// </summary>

namespace Zephyr.UnityLowLatencyHMI
{   // A value-type snapshot: UI and CSV receive the same results for one packet.
    public struct FilteredDistanceSample
    {
        public long SampleNumber;
        public int PacketId;
        public long ReceivedTicks;
        public float RawDistance;
        public float MedianDistance;
        public float MedianLpfDistance;
        public bool IsValid;
        public int FilterSampleCount;
        public int MedianWindow;
        public float Alpha;
    }

    public sealed class DistanceFilterPipeline
    {
        public const float MinValidDistance = 3f;
        public const float MaxValidDistance = 400f;

        private readonly float[] window;
        private readonly float[] sorted;
        private int nextIndex;
        private int windowCount;
        private int filterSampleCount;
        private long sampleNumber;
        private float previousOutput;
        private bool hasOutput;

        public int MedianWindow { get { return window.Length; } }
        public float Alpha { get; private set; }

        public DistanceFilterPipeline(int medianWindow, float alpha)
        {
            if (medianWindow < 1 || medianWindow > 31 || medianWindow % 2 == 0)
                throw new ArgumentOutOfRangeException(nameof(medianWindow), "Use an odd window from 1 to 31.");
            window = new float[medianWindow];
            sorted = new float[medianWindow];
            SetAlpha(alpha);
        }

        public void SetAlpha(float alpha)
        {
            if (float.IsNaN(alpha) || float.IsInfinity(alpha) || alpha < 0.01f || alpha > 1f)
                throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be between 0.01 and 1.");
            // A live change applies to the NEXT received sample; history is retained.
            Alpha = alpha;
        }

        public FilteredDistanceSample Process(int packetId, float rawDistance, long receivedTicks)
        {
            bool valid = !float.IsNaN(rawDistance) && !float.IsInfinity(rawDistance)
                && rawDistance >= MinValidDistance && rawDistance <= MaxValidDistance;
            float median = float.NaN;
            float output = float.NaN;

            if (valid)
            {
                window[nextIndex] = rawDistance;
                nextIndex = (nextIndex + 1) % window.Length;
                windowCount = Math.Min(windowCount + 1, window.Length);
                filterSampleCount++;

                // Sort a copy so the circular buffer retains arrival order.
                Array.Copy(window, sorted, windowCount);
                Array.Sort(sorted, 0, windowCount);
                int middle = windowCount / 2;
                // During startup, an even number of available samples is possible.
                median = windowCount % 2 == 1 ? sorted[middle]
                    : (sorted[middle - 1] + sorted[middle]) * 0.5f;

                // Seed [EMA] with the first median, rather than an artificial zero.
                output = hasOutput ? Alpha * median + (1f - Alpha) * previousOutput : median;
                previousOutput = output;
                hasOutput = true;
            }

            // Invalid inputs remain recordable but do not advance either filter.
            return new FilteredDistanceSample
            {
                SampleNumber = ++sampleNumber,
                PacketId = packetId,
                ReceivedTicks = receivedTicks,
                RawDistance = rawDistance,
                MedianDistance = median,
                MedianLpfDistance = output,
                IsValid = valid,
                FilterSampleCount = filterSampleCount,
                MedianWindow = MedianWindow,
                Alpha = Alpha
            };
        }
    }
}
