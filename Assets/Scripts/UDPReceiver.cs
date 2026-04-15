using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

public class UdpReceiverDispatcher : MonoBehaviour
{
    public static UdpReceiverDispatcher Instance { get; private set; }

    [Header("UDP Ports (must match ROS)")]
    public int skeletonPort = 5007;  // ROS default udp_skeleton_port
    public int robotPort = 5006;     // ROS default udp_send_port

    [Header("Debug Logging")]
    public bool logSkeletonPackets = true;
    public bool logRobotPackets = true;
    public bool logWaitingHeartbeats = true;

    [Header("Deep Debug (Raw Skeleton Packet)")]
    public bool dumpSkeletonRawHex = false;       // stampa HEX del pacchetto
    public bool dumpSkeletonTriplets = true;      // stampa le 17 terne RAW (x,y,z)
    public bool dumpSkeletonUnity = true;         // stampa le 17 terne convertite per Unity
    public bool dumpSkeletonVisibility = true;    // stampa visibilità calcolata (x==0 && z==0)
    public int dumpSkeletonEveryNthPacket = 1;   // 1=ogni pacchetto; 5=uno ogni 5
    private int _skeletonDumpCounter = 0;

    private UdpClient skeletonClient;
    private UdpClient robotClient;

    private Thread skeletonThread;
    private Thread robotThread;
    private volatile bool running;

    private readonly object skeletonLock = new object();
    private readonly object robotLock = new object();

    private uint skeletonOrder = 0;
    private Vector3[] jointPositions = new Vector3[17];

    private uint robotOrder = 0;
    private Vector3 robotPosition = Vector3.zero;
    private Quaternion robotRotation = Quaternion.identity;
    private int audioVolume = 0;

    // pairing
    private string remoteIP = null;
    private volatile bool connectedToRemote = false;

    // joint map snapshot
    private readonly Dictionary<int, Vector3> _jointMap = new Dictionary<int, Vector3>(17);

    public event Action OnSkeletonDataReceived;
    public event Action OnRobotDataReceived;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        try
        {
            skeletonClient = new UdpClient(new IPEndPoint(IPAddress.Any, skeletonPort));
            robotClient = new UdpClient(new IPEndPoint(IPAddress.Any, robotPort));

            running = true;

            skeletonThread = new Thread(SkeletonListenLoop) { IsBackground = true, Name = "UDP_Skeleton_Thread" };
            robotThread = new Thread(RobotListenLoop) { IsBackground = true, Name = "UDP_Robot_Thread" };
            skeletonThread.Start();
            robotThread.Start();

            Debug.Log($"[UDP] Listening on skeleton:{skeletonPort} robot:{robotPort} (Any interface). Waiting for pairing (SetRemote)...");
        }
        catch (Exception e)
        {
            Debug.LogError("[UDP] Init error: " + e.Message);
        }
    }

    void OnApplicationQuit()
    {
        running = false;
        try { skeletonClient?.Close(); } catch { }
        try { robotClient?.Close(); } catch { }
        try { skeletonThread?.Join(200); } catch { }
        try { robotThread?.Join(200); } catch { }
    }

    public void SetRemote(string ip)
    {
        try
        {
            remoteIP = ip;
            skeletonClient.Connect(remoteIP, skeletonPort);
            robotClient.Connect(remoteIP, robotPort);
            connectedToRemote = true;
            Debug.Log($"[UDP] Paired with {remoteIP} (skel={skeletonPort}, robot={robotPort}). Now filtering on this peer.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[UDP] SetRemote failed: " + e.Message);
        }
    }

    private void SkeletonListenLoop()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        double lastWait = 0;

        while (running)
        {
            try
            {
                if (skeletonClient.Available == 0)
                {
                    if (logWaitingHeartbeats && (Time.realtimeSinceStartupAsDouble - lastWait) > 2.0)
                    {
                        lastWait = Time.realtimeSinceStartupAsDouble;
                        Debug.Log($"[SKELETON] Waiting on UDP {skeletonPort}...");
                    }
                    Thread.Sleep(10);
                    continue;
                }

                byte[] data = skeletonClient.Receive(ref ep);

                if (connectedToRemote && ep.Address.ToString() != remoteIP)
                {
                    // pacchetto da IP inatteso -> ignora
                    continue;
                }

                // Lunghezze note
                const int LEN_V1 = 4 + 51 * 4;          // 208 bytes: order(uint32) + 51 floats
                const int LEN_V2 = 4 + 17 * (4 + 12);   // 276 bytes: order(uint32) + 17*(id:int32 + 3 floats)

                if (data.Length < Math.Min(LEN_V1, LEN_V2))
                {
                    if (logSkeletonPackets)
                        Debug.LogWarning($"[SKELETON] From {ep.Address}:{ep.Port} -> {data.Length}B (expected {LEN_V1}B or {LEN_V2}B)");
                    continue;
                }

                // Dump PRIMA del parsing/copy
                if (dumpSkeletonRawHex || dumpSkeletonTriplets || dumpSkeletonUnity || dumpSkeletonVisibility)
                {
                    DumpSkeletonPacket(data, ep);
                }

                uint order = BitConverter.ToUInt32(data, 0);
                Vector3[] joints = new Vector3[17];

                // Inizializza invisibile coerente con la tua regola (x==0 && z==0)
                for (int i = 0; i < 17; i++) joints[i] = new Vector3(0f, 0.7f, 0f);

                if (data.Length >= LEN_V2)
                {
                    // V2: order + 17*(id:int32, x,y,z:float32)
                    for (int i = 0; i < 17; i++)
                    {
                        int baseOff = 4 + i * 16;
                        int jid = BitConverter.ToInt32(data, baseOff);
                        float x = BitConverter.ToSingle(data, baseOff + 4);
                        float y = BitConverter.ToSingle(data, baseOff + 8);
                        float z = BitConverter.ToSingle(data, baseOff + 12);

                        if (jid >= 0 && jid < 17)
                            joints[jid] = new Vector3(-x, -y + 0.7f, -z);
                    }
                }
                else if (data.Length >= LEN_V1)
                {
                    // V1: order + 17 terne float in ordine 0..16
                    for (int i = 0; i < 17; i++)
                    {
                        float x = BitConverter.ToSingle(data, 4 + i * 12);
                        float y = BitConverter.ToSingle(data, 4 + i * 12 + 4);
                        float z = BitConverter.ToSingle(data, 4 + i * 12 + 8);
                        joints[i] = new Vector3(-x, -y + 0.7f, -z);
                    }
                }
                else
                {
                    if (logSkeletonPackets)
                        Debug.LogWarning($"[SKELETON] Unknown packet size {data.Length}B");
                    continue;
                }

                lock (skeletonLock)
                {
                    skeletonOrder = order;
                    Array.Copy(joints, jointPositions, 17);

                    _jointMap.Clear();
                    for (int i = 0; i < 17; i++)
                        _jointMap[i] = jointPositions[i];
                }

                if (logSkeletonPackets)
                {
                    var j5 = joints[5]; var j6 = joints[6]; var j11 = joints[11];
                    Debug.Log($"[SKELETON] {data.Length}B from {ep.Address}:{ep.Port} | order={order} | " +
                              $"unity[5]=({j5.x:F3},{j5.y:F3},{j5.z:F3}) [6]=({j6.x:F3},{j6.y:F3},{j6.z:F3}) [11]=({j11.x:F3},{j11.y:F3},{j11.z:F3})");
                }

                OnSkeletonDataReceived?.Invoke();
            }
            catch (SocketException) { /* socket closed */ }
            catch (Exception e)
            {
                if (running) Debug.LogWarning("[SKELETON] UDP error: " + e.Message);
            }
        }
    }

    private void RobotListenLoop()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        double lastWait = 0;

        while (running)
        {
            try
            {
                if (robotClient.Available == 0)
                {
                    if (logWaitingHeartbeats && (Time.realtimeSinceStartupAsDouble - lastWait) > 2.0)
                    {
                        lastWait = Time.realtimeSinceStartupAsDouble;
                        Debug.Log($"[ROBOT] Waiting on UDP {robotPort}...");
                    }
                    Thread.Sleep(10);
                    continue;
                }

                byte[] data = robotClient.Receive(ref ep);

                if (connectedToRemote && ep.Address.ToString() != remoteIP)
                    continue;

                if (data.Length < 24)
                {
                    if (logRobotPackets)
                        Debug.LogWarning($"[ROBOT] From {ep.Address}:{ep.Port} -> {data.Length}B (expected 24B)");
                    continue;
                }

                uint order = BitConverter.ToUInt32(data, 0);
                int vol = BitConverter.ToInt32(data, 4);
                float x = BitConverter.ToSingle(data, 8);
                float y = BitConverter.ToSingle(data, 12);
                float z = BitConverter.ToSingle(data, 16);
                float w = BitConverter.ToSingle(data, 20);

                Vector3 pos = new Vector3(-y, 0, x);
                Quaternion rot = new Quaternion(0, -z, 0, w);

                lock (robotLock)
                {
                    robotOrder = order;
                    audioVolume = vol;
                    robotPosition = pos;
                    robotRotation = rot;
                }

                if (logRobotPackets)
                {
                    Debug.Log($"[ROBOT] {data.Length}B from {ep.Address}:{ep.Port} | order={order} vol={vol} | " +
                              $"pos=({pos.x:F3},{pos.y:F3},{pos.z:F3}) rot=({rot.x:F3},{rot.y:F3},{rot.z:F3},{rot.w:F3})");
                }

                OnRobotDataReceived?.Invoke();
            }
            catch (SocketException) { /* socket closed */ }
            catch (Exception e)
            {
                if (running) Debug.LogWarning("[ROBOT] UDP error: " + e.Message);
            }
        }
    }

    // ---- RAW dump helper ----
    private void DumpSkeletonPacket(byte[] data, IPEndPoint ep)
    {
        _skeletonDumpCounter++;
        if (dumpSkeletonEveryNthPacket > 1 && (_skeletonDumpCounter % dumpSkeletonEveryNthPacket) != 0)
            return;

        try
        {
            var sb = new StringBuilder(8192);
            sb.Append("[SKELETON][DUMP] ")
              .Append(data?.Length ?? 0).Append("B from ")
              .Append(ep?.Address).Append(':').Append(ep?.Port);

            if (data == null || data.Length < 4)
            {
                sb.Append(" | PACKET TOO SHORT");
                Debug.Log(sb.ToString());
                return;
            }

            uint order = BitConverter.ToUInt32(data, 0);
            sb.Append(" | order=").Append(order);

            if (dumpSkeletonRawHex)
            {
                sb.Append(" | HEX=");
                for (int i = 0; i < data.Length; i++)
                    sb.Append(data[i].ToString("X2"));
            }

            // Try V2 layout first
            const int LEN_V2 = 4 + 17 * 16;
            const int LEN_V1 = 4 + 51 * 4;

            if (data.Length >= LEN_V2)
            {
                for (int i = 0; i < 17; i++)
                {
                    int off = 4 + i * 16;
                    int jid = BitConverter.ToInt32(data, off);
                    float x = BitConverter.ToSingle(data, off + 4);
                    float y = BitConverter.ToSingle(data, off + 8);
                    float z = BitConverter.ToSingle(data, off + 12);

                    float ux = -x, uy = -y + 0.7f, uz = -z;
                    bool vis = !(Mathf.Approximately(ux, 0f) && Mathf.Approximately(uz, 0f));

                    sb.Append("\n  [blk ").Append(i).Append("] id=").Append(jid);
                    if (dumpSkeletonTriplets) sb.Append(" raw=(").Append(x.ToString("F4")).Append(',').Append(y.ToString("F4")).Append(',').Append(z.ToString("F4")).Append(')');
                    if (dumpSkeletonUnity) sb.Append(" unity=(").Append(ux.ToString("F4")).Append(',').Append(uy.ToString("F4")).Append(',').Append(uz.ToString("F4")).Append(')');
                    if (dumpSkeletonVisibility) sb.Append(" vis=").Append(vis ? "1" : "0");
                }
            }
            else if (data.Length >= LEN_V1)
            {
                for (int i = 0; i < 17; i++)
                {
                    int off = 4 + i * 12;
                    float x = BitConverter.ToSingle(data, off);
                    float y = BitConverter.ToSingle(data, off + 4);
                    float z = BitConverter.ToSingle(data, off + 8);
                    float ux = -x, uy = -y + 0.7f, uz = -z;
                    bool vis = !(Mathf.Approximately(ux, 0f) && Mathf.Approximately(uz, 0f));

                    sb.Append("\n  [").Append(i).Append("]");
                    if (dumpSkeletonTriplets) sb.Append(" raw=(").Append(x.ToString("F4")).Append(',').Append(y.ToString("F4")).Append(',').Append(z.ToString("F4")).Append(')');
                    if (dumpSkeletonUnity) sb.Append(" unity=(").Append(ux.ToString("F4")).Append(',').Append(uy.ToString("F4")).Append(',').Append(uz.ToString("F4")).Append(')');
                    if (dumpSkeletonVisibility) sb.Append(" vis=").Append(vis ? "1" : "0");
                }
            }
            else
            {
                sb.Append(" | WARNING: payload size not matching V1/V2");
            }

            Debug.Log(sb.ToString());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SKELETON][DUMP] error: " + ex.Message);
        }
    }

    // ---- GETTERS ----
    public Vector3 GetJointPosition(int index)
    {
        if (index < 0 || index >= 17) return Vector3.zero;
        lock (skeletonLock) return jointPositions[index];
    }

    public Vector3[] GetAllJointPositions()
    {
        lock (skeletonLock)
        {
            var copy = new Vector3[17];
            Array.Copy(jointPositions, copy, 17);
            return copy;
        }
    }

    public uint GetSkeletonOrder() { lock (skeletonLock) return skeletonOrder; }

    public (uint seq, Dictionary<int, Vector3> joints) GetJointMapSnapshot()
    {
        lock (skeletonLock)
        {
            return (skeletonOrder, new Dictionary<int, Vector3>(_jointMap));
        }
    }

    public Vector3 GetRobotPosition() { lock (robotLock) return robotPosition; }
    public Quaternion GetRobotRotation() { lock (robotLock) return robotRotation; }
    public int GetAudioVolume() { lock (robotLock) return audioVolume; }
    public uint GetRobotOrder() { lock (robotLock) return robotOrder; }
}
