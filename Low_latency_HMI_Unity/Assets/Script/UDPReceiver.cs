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
    Thread receiveThread;
    UdpClient client;
    public int port = 5005;
    private bool isRunning = true;

    private MetricsCollector collector;

    // Receive thread writes; Unity main thread reads.
    private readonly object latestLock = new object();
    private float latestRawDistance;
    private bool hasLatestDistance;

    private void Awake()
    {
        collector = GetComponent<MetricsCollector>();

        if (collector == null) Debug.LogError("There's no MetricsCollector component on this object, ", this);
    }

    private void Start()
    {
        if (receiveThread != null && receiveThread.IsAlive)
        {
            Debug.LogWarning("Receiver is already running.");
            return;
        }
        //1. Create a new Thread
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        //2. Set background mode to prevent from being unable to close
        receiveThread.IsBackground = true; //The default is foreground thread
        //3. Start thread
        receiveThread.Start();
        Debug.Log("UDP Receiver started.");
    }

    private void ReceiveData()
    {  
        while (isRunning)
        {
            try
            {   //1. Create client
                if (client == null) client = new UdpClient(port);
                //2. Receive the packet and obtain the sender's endpoint
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref sender);
                //3. Get the received Timestamp
                long recievedTicks = MetricsCollector.GetTimeStamp();
               //4. Decode
                string text = Encoding.UTF8.GetString(data);
               //5. Read data
                SensorData sensor = JsonUtility.FromJson<SensorData>(text);
                //6. Create a checking with the same ID
                CheckingData check = new CheckingData
                {
                    type = "checking",
                    packet_id = sensor.packet_id,
                };
                //7. Object to JSON String, then to bytes
                string checkJson = JsonUtility.ToJson(check);
                byte[] checkBytes = Encoding.UTF8.GetBytes(checkJson); // Encode

                //8. Send it back to the sender
                client.Send(checkBytes, checkBytes.Length, sender);

                //9. Record
                collector?.RecordSample(
                    sensor.packet_id,
                    sensor.raw_distance,
                    recievedTicks);

                // Publish the latest distance for the dashboard.
                lock (latestLock)
                {
                    latestRawDistance = sensor.raw_distance;
                    hasLatestDistance = true;
                }

                Debug.Log(
                    $"ID: {sensor.packet_id} | " +
                    //$"speed: {sensor.speed} | " +
                    $"raw distance: {sensor.raw_distance}"
                    );
            }
            catch (SocketException e)
            {  
                if (isRunning) Debug.LogWarning($"UDP receive exception {e.Message}");
                else Debug.Log("UDP has been shutdown normally.");
            }
            catch (Exception err)
            {
                Debug.LogError(err.ToString());
            }
        }
    }

    public bool TryGetLatestDistance(out float distance)
    {
        lock (latestLock)
        {
            distance = latestRawDistance;
            return hasLatestDistance;
        }
    }

    private void OnApplicationQuit()
    {   
        isRunning = false;

        //1. if client exists, close it for releasing port to prevent errors caused by the port being occupaid
        if (client != null) {
            client.Close();
            client = null;
        }
        if (receiveThread != null) {
            receiveThread.Join();
            receiveThread = null;
        }
    }
}