using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance;
    public bool IsSpectator = false;
    public bool isHost = false; 

    void Awake() {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // --- THE FIX: FORCE CREATION ON MAIN THREAD ---
            // This guarantees the Dispatcher exists before the background thread needs it.
            UnityMainThreadDispatcher.Instance(); 
            // ----------------------------------------------
        } else {
            Destroy(gameObject);
        }
    }

    // --- Configuration ---
    private const int TIMEOUT_MS = 500;
    private const int MAX_RETRIES = 3;
    
    private UdpClient udpClient;
    private IPEndPoint remoteEndPoint; 
    private Thread receiveThread;
    private bool isRunning = false;
    
    private int localSequenceNumber = 1; 
    private int remoteSequenceNumber = 0; 

    private struct PendingMessage {
        public PokeMessage message;
        public float timeSent;
        public int retryCount;
    }
    private Dictionary<int, PendingMessage> pendingMessages = new Dictionary<int, PendingMessage>();
    private object lockObj = new object();

    public delegate void MessageReceivedHandler(PokeMessage msg);
    public event MessageReceivedHandler OnMessageReceived;

    public void InitializeHost(int port)
    {
        isHost = true;
        udpClient = new UdpClient(port);
        StartListening();
        Debug.Log($"Host started on port {port}");
    }

    public void InitializeJoiner(string hostIP, int hostPort)
    {
        // 1. Reset State
        isHost = false;
        if (udpClient != null) udpClient.Close();
        
        // 2. Create Client
        udpClient = new UdpClient();
        
        // 3. Verify IP
        IPAddress ip;
        if (!IPAddress.TryParse(hostIP, out ip)) {
            Debug.LogError("Invalid IP Address: " + hostIP);
            return;
        }

        // 4. Connect
        remoteEndPoint = new IPEndPoint(ip, hostPort);
        udpClient.Connect(remoteEndPoint); 
        
        // 5. Start Logic
        StartListening();
        StartCoroutine(TryHandshakeLoop());
        
        Debug.Log($"Joiner initialized. Target: {hostIP}:{hostPort}");
    }

    public void InitializeSpectator(string hostIP, int hostPort) {
        IsSpectator = true;
        InitializeJoiner(hostIP, hostPort);
    }

    IEnumerator TryHandshakeLoop()
    {
        for(int i=0; i<20; i++) // Try 20 times (10 seconds)
        {
            string type = IsSpectator ? "SPECTATOR_REQUEST" : "HANDSHAKE_REQUEST";
            SendReliable(type, new Dictionary<string, string>());
            
            // If we received ANY packet from host (seq > 0), we are connected!
            if (remoteSequenceNumber > 0) yield break;
            
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void StartListening()
    {
        isRunning = true;
        receiveThread = new Thread(new ThreadStart(ReceiveLoop));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    public void SendReliable(string type, Dictionary<string, string> payload = null)
    {
        PokeMessage msg = new PokeMessage {
            message_type = type,
            sequence_number = localSequenceNumber++,
            data = payload ?? new Dictionary<string, string>()
        };

        lock (lockObj) {
            pendingMessages.Add(msg.sequence_number, new PendingMessage {
                message = msg, timeSent = Time.time, retryCount = 0
            });
        }
        SendRaw(msg);
    }

    private void SendAck(int seqToAck) {
        SendRaw(new PokeMessage { message_type = "ACK", ack_number = seqToAck });
    }

    private void SendRaw(PokeMessage msg)
    {
        if (udpClient == null) return; // <--- ADD THIS SAFETY CHECK

        try {
       string serialized = msg.Serialize();
       byte[] bytes = Encoding.UTF8.GetBytes(serialized);

       if (isHost) {
           // Host must have a remoteEndPoint set (from the handshake) to send back
           if (remoteEndPoint != null)
               udpClient.Send(bytes, bytes.Length, remoteEndPoint);
           else
               Debug.LogError("Host: Cannot send, remoteEndPoint is null.");
       } else {
           // Joiner is connected, so we can use Send without endpoint
           udpClient.Send(bytes, bytes.Length);
       }
   }
   catch (System.Exception e) { 
       Debug.LogError($"Send Error: {e.Message}"); 
   }}

    private void ReceiveLoop()
    {
        IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        while (isRunning)
        {
            try {
                if (udpClient.Available > 0) {
                    byte[] bytes = udpClient.Receive(ref sender);
                    string rawData = Encoding.UTF8.GetString(bytes);
                    PokeMessage msg = PokeMessage.Deserialize(rawData);

                    // === FIXED HOST LOGIC ===
                    // Always update endpoint and reply to Handshakes, even if we think we are connected
                    if (isHost)
                    {
                        if (remoteEndPoint == null ||
                            !remoteEndPoint.Address.Equals(sender.Address) ||
                            remoteEndPoint.Port != sender.Port)
                        {
                            remoteEndPoint = sender;
                            udpClient.Connect(remoteEndPoint);
                            Debug.Log("Host now connected to: " + sender.ToString());
                        }

                        // Respond to handshake
                        if (msg.message_type == "HANDSHAKE_REQUEST" || msg.message_type == "SPECTATOR_REQUEST")
                        {
                            var resp = new Dictionary<string, string> { { "seed", "12345" } };
                            SendReliable("HANDSHAKE_RESPONSE", resp);
                        }
                    }
                    // ========================

                    HandleIncomingMessage(msg);
                }
            }
            catch (System.Exception e) { Debug.Log($"Receive Error: {e.Message}"); }
            Thread.Sleep(10);
        }
    }

    private void HandleIncomingMessage(PokeMessage msg)
    {
        if (msg.message_type == "ACK") {
            lock (lockObj) {
                if (pendingMessages.ContainsKey(msg.ack_number)) 
                    pendingMessages.Remove(msg.ack_number);
            }
            return;
        }

        SendAck(msg.sequence_number);

        if (msg.message_type != "BATTLE_SETUP" &&
        msg.message_type != "HANDSHAKE_RESPONSE" &&
        msg.sequence_number <= remoteSequenceNumber)
        return;
        
        remoteSequenceNumber = msg.sequence_number;

        UnityMainThreadDispatcher.Instance().Enqueue(() => {
            Debug.Log("Dispatcher processing message: " + msg.message_type); // <--- Add this
            OnMessageReceived?.Invoke(msg);
        });
    }

    void Update()
    {
        lock (lockObj) {
            List<int> toResend = new List<int>();
            foreach (var kvp in pendingMessages) {
                if (Time.time - kvp.Value.timeSent > (TIMEOUT_MS / 1000f)) {
                    if (kvp.Value.retryCount < MAX_RETRIES) toResend.Add(kvp.Key);
                }
            }
            foreach (int seq in toResend) {
                var p = pendingMessages[seq];
                p.retryCount++;
                p.timeSent = Time.time;
                pendingMessages[seq] = p;
                SendRaw(p.message);
            }
        }
    }

    void OnDestroy() {
        isRunning = false;
        if (udpClient != null) udpClient.Close();
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
    }
}