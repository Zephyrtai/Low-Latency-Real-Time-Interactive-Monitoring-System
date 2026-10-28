using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Zephyr.UnityLowLatencyHMI;

/// <summary>
/// Attach this component to an active GameObject in the scene.
/// </summary>

[Serializable]
public class SensorData
{
    public int packet_id;
    public float speed;
    public float raw_distance;
}

[Serializable]
public class CheckingData
{
    public string type;
    public int packet_id;
} 

public class UDPReceiver : MonoBehaviour
{
    [SerializeField, Range(1, 31)] private int medianWindow = 3;
    [SerializeField, Range(0.01f, 1f)] private float filterAlpha = 0.2f;
    [SerializeField] private bool logPackets = false;

    private Thread receiveThread;
    private UdpClient client;
    public int port = 5005;
    private volatile bool isRunning; //volatile: ensure Visibility/Acquire-Release Semantics
    private MetricsCollector collector;

    // Protect BOTH filter state and the complete latest snapshot with one lock.
    private readonly object sampleLock = new object();
    private DistanceFilterPipeline pipeline;
    private FilteredDistanceSample latestSample;
    private bool hasLatestSample;

    public float FilterAlpha
    {
        get { lock (sampleLock) return pipeline == null ? filterAlpha : pipeline.Alpha; }
    }

    private void Awake()
    {
        collector = GetComponent<MetricsCollector>();

        if (collector == null) Debug.LogError("There's no MetricsCollector component on this object, ", this);
    }

    private void OnValidate()
    {
        medianWindow = Mathf.Clamp(medianWindow, 1, 31);
        if (medianWindow % 2 == 0) medianWindow++;
        if (float.IsNaN(filterAlpha) || float.IsInfinity(filterAlpha)) filterAlpha = 0.2f;
        filterAlpha = Mathf.Clamp(filterAlpha, 0.01f, 1f);
    }

    private void OnEnable()
    {
        if (receiveThread != null && receiveThread.IsAlive)
        {
            Debug.LogWarning("Receiver is already running.");
            enabled = false; // Trigger OnDisable(), call StopReceiver(), to clear resources.
            return;
        }

        OnValidate();
        lock (sampleLock)
        {
            pipeline = new DistanceFilterPipeline(medianWindow, filterAlpha);
            hasLatestSample = false;
        }
        try
        {   //Starting thread after Binding so that catch faild error.
            //1. Create a new Thread
            client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            UdpClient socket = client;//Ensure this receiver thread always uses the object it obtained when it was started.
            /* If client == null, the referrence of socket won't change together.
             however, it won't be used when the same client be closed.*/
            isRunning = true;
            receiveThread = new Thread(() => ReceiveData(socket))
            {//2. Set background mode to prevent from being unable to close.
                IsBackground = true //The default is foreground thread
            };
            //3. Start thread
            receiveThread.Start();
            Debug.Log("UDP Receiver started.");
        }
        catch (Exception error)
        {
            Debug.LogError("UDP startup failed: " + error.Message, this);
            enabled = false;
        }
    }

    private void ReceiveData(UdpClient socket)
    {  
        while (isRunning)
        {
            try
            {   //1. Receive the packet and obtain the sender's endpoint
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = socket.Receive(ref sender);
                //2. Get the received Timestamp
                long receivedTicks = MetricsCollector.GetTimeStamp();
               //3. Decode
                string text = Encoding.UTF8.GetString(data);
               //4. Read data
                SensorData sensor = JsonUtility.FromJson<SensorData>(text);
                //5. Create a checking with the same ID
                CheckingData check = new CheckingData
                {
                    type = "checking",
                    packet_id = sensor.packet_id,
                };
                //6. Object to JSON String, then to bytes
                string checkJson = JsonUtility.ToJson(check);
                byte[] checkBytes = Encoding.UTF8.GetBytes(checkJson); // Encode
                //7. Send it back to the sender
                try { socket.Send(checkBytes, checkBytes.Length, sender); }
                catch (SocketException error)
                { if (isRunning) Debug.LogWarning("UDP check failed: " + error.Message);}

                FilteredDistanceSample sample;
                lock (sampleLock)
                {
                    sample = pipeline.Process(sensor.packet_id, sensor.raw_distance, receivedTicks);
                    latestSample = sample;
                    hasLatestSample = true;
                }
                //8. Record
                collector?.RecordSample(sample);

                if (logPackets) 
                { Debug.Log($"ID: {sample.PacketId} | " +
                    $"Raw: {sample.RawDistance} | " +
                    $"Median: {sample.MedianDistance} | " +
                    $"Median+LPF: {sample.MedianLpfDistance}");
                }
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException error)
            {
                if (isRunning) Debug.LogWarning("UDP socket: " + error.Message);
            }
            catch (Exception error)
            {
                if (isRunning) Debug.LogWarning("UDP packet rejected: " + error.Message);
            }
        }
    }

    public bool TryGetLatestSample(out FilteredDistanceSample sample)
    {
        lock (sampleLock)
        {
            sample = latestSample;
            return hasLatestSample;
        }
    }

    public void SetFilterAlpha(float alpha)
    {
        if (float.IsNaN(alpha) || float.IsInfinity(alpha)) return;
        lock (sampleLock)
        {
            filterAlpha = Mathf.Clamp(alpha, 0.01f, 1f);
            pipeline?.SetAlpha(filterAlpha);
        }
    }

    private void StopReceiver()
    {
        isRunning = false;
        // Unblock Receive before waiting for the worker.
        client?.Close(); //Equals if(client != null) client.Close()
        client = null;
        if (receiveThread == null) return;
        if (receiveThread.Join(1000)) receiveThread = null;
        else Debug.LogWarning("UDP: receive thread is still stopping.", this);
    }

    private void OnDisable() { StopReceiver(); }
    private void OnApplicationQuit() { StopReceiver(); }

    //private void OnApplicationQuit()
    //{   
    //    isRunning = false;

    //    //1. if client exists, close it for releasing port to prevent errors caused by the port being occupaid
    //    if (client != null) {
    //        client.Close();
    //        client = null;
    //    }
    //    if (receiveThread != null) {
    //        receiveThread.Join();
    //        receiveThread = null;
    //    }
    //}
}