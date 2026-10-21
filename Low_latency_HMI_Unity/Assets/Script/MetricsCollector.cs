using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// receive -> memory buffer -> CSV when recording stops.
/// Attach this component to an active GameObject in the scene.(MonoBehaviour)
/// </summary>

namespace Zephyr.UnityLowLatencyHMI
{
    public class MetricsCollector : MonoBehaviour
    {
        [SerializeField] private bool startOnPlay = true;

        private struct ReceiveSample
        {
            public int PacketId;
            public float RawDistance;
            public long ReceivedTicks;
        }

        // The receive thread writes; the main thread starts/stop recording.
        // Use the same lock to protect recoding.
        private readonly object bufferLock = new object(); // readonly: keep the referrence at the same table
        private readonly List<ReceiveSample> samples = new List<ReceiveSample>(8200);

        private bool isRecording;
        private bool needsExport;
        private long startTicks;
        private string outputDirectory;
        private string outputPath;

        private void Awake()
        {
            outputDirectory = Path.Combine(Application.persistentDataPath, "Metrics");
            if (startOnPlay) BeginRecording();
        }

        public static long GetTimeStamp()
        {
            return Stopwatch.GetTimestamp();
        }

        [ContextMenu("Begin Recording")]
        public void BeginRecording()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;

            string filename = "metrics_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
                + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".csv";

            //"Lock" to protect recording
            lock (bufferLock)
            {
                if (isRecording)
                {
                    Debug.LogWarning("Metrics: The recording is running.");
                    return;
                }
                if (needsExport)
                {
                    Debug.LogWarning("Metrics: Export the previous buffer first using Stop Recording And Export.");
                    return;
                }

                samples.Clear();
                outputPath = Path.Combine(outputDirectory, filename);
                startTicks = GetTimeStamp();
                isRecording = true;
                needsExport = true;
            }
            Debug.Log("Metrics: recording started.");
        }

        public void RecordSample(int packetId, float rawDistance, long receivedTicks)
        {
            lock (bufferLock)
            {  // Ignore samples before this session or after stopping.
                if (!isRecording || receivedTicks < startTicks) return;

                samples.Add(new ReceiveSample
                {
                    PacketId = packetId,
                    ReceivedTicks = receivedTicks,
                    RawDistance = rawDistance
                });
            }
        }

        [ContextMenu("Stop Recording And Export")]
        public void EndRecordingAndExport()
        {
            ReceiveSample[] snapshot;
            long sessionStart;
            string path;

            lock (bufferLock)
            {  // Repeated stop callbacks do not creat duolicate CSVs.
                if (!needsExport) return;
                isRecording = false;
                snapshot = samples.ToArray();
                sessionStart = startTicks;
                path = outputPath;
            }
            try
            {
                Directory.CreateDirectory(outputDirectory);
                // `Using` ensure the file resources are released after writing is complete.
                using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("packet_id,receive_since_start_ms,receive_interval_ms,raw_distance");

                    for (int i = 0; i < snapshot.Length; i++)
                    {
                        double receiveSinceStartMs = ToMilliseconds(snapshot[i].ReceivedTicks - sessionStart);

                        // First sample has no predecessor: leave interval empty.
                        string interval = i ==0 ? "" : ToMilliseconds(
                            snapshot[i].ReceivedTicks - snapshot[i - 1].ReceivedTicks)
                            .ToString("F3", CultureInfo.InvariantCulture); // InvariantCulture: ensure the value use a decimal point.

                        // Always use decimal points, independent of PC language.
                        writer.WriteLine(
                            snapshot[i].PacketId.ToString(CultureInfo.InvariantCulture) + "," +
                            receiveSinceStartMs.ToString("F3", CultureInfo.InvariantCulture) + "," +
                            interval + "," +
                            snapshot[i].RawDistance.ToString("R", CultureInfo.InvariantCulture));
                    }
                }
                lock (bufferLock) needsExport = false;
                Debug.Log("Metrics: exported " + snapshot.Length + " samples to:\n" + path);
                if (snapshot.Length == 0)
                    Debug.LogWarning("Metrics: no samples recorded. Check the receiver reference and RecordSample call.");
            }
            catch (Exception error)
            {
                Debug.LogError("Metrics: Export failed; buffer retained. Retry Stop Recording And Export before starting a new session.\n" + error);
            }

        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private void OnDisable()
        {
            EndRecordingAndExport();
        }

        private void OnApplicationQuit()
        {
            EndRecordingAndExport();
        }
    }
}