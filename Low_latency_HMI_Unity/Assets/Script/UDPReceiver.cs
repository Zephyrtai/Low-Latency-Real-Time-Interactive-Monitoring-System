using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;


[Serializable]
public class SensorData
{
    public float speed;
    public float raw_distance;
} 

public class UDPReceiver : MonoBehaviour
{
    Thread receiveThread;
    UdpClient client;
    public int port = 5005;
    private bool isRunning = true;

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
                //2. Ready to receive data
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
               //3. Parse
                string text = Encoding.UTF8.GetString(data);
               //4. read data
                SensorData sensor = JsonUtility.FromJson<SensorData>(text);
                Debug.Log($"speed: {sensor.speed} | raw distance: {sensor.raw_distance}");
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
