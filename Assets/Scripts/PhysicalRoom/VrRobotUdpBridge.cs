using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Sends protocol_v2 packets to the RobotHub running on the PC (port 5600).
/// Hub translates them into 6-byte robot frames and forwards to each robot.
/// Robot IDs match hub's registry: neto1=1, neto2=2, neto3=3, sauron1=4, sauron2=5, deathtrap1=6
/// </summary>
public class VrRobotUdpBridge : MonoBehaviour
{
    [Header("Hub Connection")]
    [Tooltip("IP of the PC running RobotHub. On Quest via hotspot this is usually 192.168.137.1")]
    [SerializeField] private string hubIp = "192.168.137.1";
    [SerializeField] private int hubPort = 5600;

    [Header("Runtime")]
    [SerializeField] private bool autoStart = true;
    [SerializeField] private bool sendSafeStateOnStart = true;
    [SerializeField] private bool sendHelloOnStart = false;

    // Protocol constants
    private static readonly byte[] MAGIC = { (byte)'P', (byte)'R' };
    private const byte VERSION = 1;
    private const byte MSG_HELLO = 1;
    private const byte MSG_COMMAND = 4;

    // Command types — must match protocol_v2.py CommandType
    private const byte CMD_NETO_SET = 1;
    private const byte CMD_SAURON_SET = 10;
    private const byte CMD_BROADCAST = 255;

    // Broadcast command IDs
    private const byte BROADCAST_ENABLE = 17;
    private const byte BROADCAST_DISABLE = 18;

    // Robot IDs — must match hub's _id_to_name map
    public const int ID_NETO1 = 1;
    public const int ID_NETO2 = 2;
    public const int ID_NETO3 = 3;
    public const int ID_SAURON1 = 4;
    public const int ID_SAURON2 = 5;
    public const int ID_DEATHTRAP = 6;

    private UdpClient _client;
    private IPEndPoint _hubEndpoint;
    private ushort _seq = 1;
    private bool _emergencyStop;

    private readonly ConcurrentQueue<Action> _mainThread = new();
    private CancellationTokenSource _cts;

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public bool IsEmergencyStopped => _emergencyStop;

    public event Action<string> RobotConnectionLost;
    public event Action<string> RobotConnectionRestored;
    public event Action EmergencyStopActivated;
    public event Action EmergencyStopDeactivated;

    // ── Lifecycle ────────────────────────────────────────────────────────

    private void Start()
    {
        if (autoStart) StartBridge();
    }

    public void StartBridge()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _hubEndpoint = new IPEndPoint(IPAddress.Parse(hubIp), hubPort);
        _client = new UdpClient();
        _client.Client.SendTimeout = 500;

        if (sendHelloOnStart)
            SendHello();

        if (sendSafeStateOnStart)
            SendSafeStateToAllRobots();

        Debug.Log($"[Bridge] Started — hub at {hubIp}:{hubPort}");
    }

    public void StopBridge()
    {
        if (!IsRunning) return;
        _cts.Cancel();
        _client?.Close();
        _client = null;
    }

    private void OnDestroy() => StopBridge();

    private void Update()
    {
        while (_mainThread.TryDequeue(out var action))
            action.Invoke();
    }

    // ── Public send API ──────────────────────────────────────────────────

    public void SendNetoCommand(int robotId, int sound, int volume,
                                 int speedUnits, int ledRadius, int ledBrightness)
    {
        if (!CanSend()) return;

        SendPacket(robotId, BuildNetoPayload(sound, volume, speedUnits, ledRadius, ledBrightness));
    }

    public void SendSauronCommand(int robotId, int bottom, int top)
    {
        if (!CanSend()) return;

        SendPacket(robotId, BuildSauronPayload(bottom, top));
    }

    public void SendSafeStateToAllRobots()
    {
        if (_client == null) return;

        // Stop all Neto motors, silence LEDs
        for (int id = ID_NETO1; id <= ID_NETO3; id++)
            SendNetoCommand(id, sound: 0, volume: 0, speedUnits: 90,
                            ledRadius: 0, ledBrightness: 0);

        // Centre both Saurons
        SendSauronCommand(ID_SAURON1, 90, 90);
        SendSauronCommand(ID_SAURON2, 90, 90);

        Debug.Log("[Bridge] Safe state sent to all robots");
    }

    public void ActivateEmergencyStop()
    {
        if (_emergencyStop) return;
        _emergencyStop = true;
        SendSafeStateToAllRobots_Unchecked();  // bypass CanSend guard
        EmergencyStopActivated?.Invoke();
    }

    private void SendSafeStateToAllRobots_Unchecked()
    {
        if (_client == null) return;
        for (int id = ID_NETO1; id <= ID_NETO3; id++)
            SendPacket(id, BuildNetoPayload(0, 0, 90, 0, 0));
        SendPacket(ID_SAURON1, BuildSauronPayload(90, 90));
        SendPacket(ID_SAURON2, BuildSauronPayload(90, 90));
    }

    public void DeactivateEmergencyStop()
    {
        if (!_emergencyStop) return;
        _emergencyStop = false;
        EmergencyStopDeactivated?.Invoke(); // was calling itself
        Debug.Log("[Bridge] Emergency stop cleared");
    }

    // ── Internal helpers ─────────────────────────────────────────────────

    private bool CanSend()
    {
        if (_client == null)
        {
            Debug.LogWarning("[Bridge] Not started");
            return false;
        }
        if (_emergencyStop)
        {
            Debug.LogWarning("[Bridge] Emergency stop active");
            return false;
        }
        return true;
    }

    private void SendPacket(int robotId, byte[] payload)
    {
        try
        {
            byte[] packet = BuildPacket(MSG_COMMAND, robotId, payload);
            _client.Send(packet, packet.Length, _hubEndpoint);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Bridge] Send failed: {ex.Message}");
        }
    }

    private byte[] BuildNetoPayload(int sound, int volume, int speedUnits, int ledRadius, int ledBrightness)
    {
        byte[] payload = new byte[6];
        payload[0] = CMD_NETO_SET;
        payload[1] = (byte)Mathf.Clamp(sound, 0, 1);
        payload[2] = (byte)Mathf.Clamp(volume, 0, 20);
        payload[3] = (byte)Mathf.Clamp(speedUnits, 0, 180);
        payload[4] = (byte)Mathf.Clamp(ledRadius, 0, 10);
        payload[5] = (byte)Mathf.Clamp(ledBrightness, 0, 255);
        return payload;
    }

    private byte[] BuildSauronPayload(int bottom, int top)
    {
        // manual_mode = -1 (0xFF as byte) tells hub "do not change manual mode"
        byte[] payload = new byte[4];
        payload[0] = CMD_SAURON_SET;
        payload[1] = (byte)Mathf.Clamp(bottom, 0, 180);
        payload[2] = (byte)Mathf.Clamp(top, 0, 180);
        payload[3] = 0xFF; // leave manual mode unchanged
        return payload;
    }

    private void SendHello()
    {
        if (_client == null) return;

        byte[] payload = Encoding.UTF8.GetBytes("vr");
        byte[] packet = BuildPacket(MSG_HELLO, 0, payload);
        _client.Send(packet, packet.Length, _hubEndpoint);
    }

    /// <summary>
    /// Builds a protocol_v2 packet matching Python's Packet.encode():
    /// magic(2) version(1) msgtype(1) seq(2 LE) robot_id(1) payload_len(2 LE) payload(N)
    /// </summary>
    private byte[] BuildPacket(byte msgType, int robotId, byte[] payload)
    {
        int headerSize = 9;
        byte[] packet = new byte[headerSize + payload.Length];

        // Magic "PR"
        packet[0] = MAGIC[0];
        packet[1] = MAGIC[1];
        // Version
        packet[2] = VERSION;
        // MsgType
        packet[3] = msgType;
        // Seq — little-endian uint16
        packet[4] = (byte)(_seq & 0xFF);
        packet[5] = (byte)((_seq >> 8) & 0xFF);
        _seq = (ushort)((_seq + 1) & 0xFFFF);
        // robot_id
        packet[6] = (byte)robotId;
        // payload_len — little-endian uint16
        packet[7] = (byte)(payload.Length & 0xFF);
        packet[8] = (byte)((payload.Length >> 8) & 0xFF);
        // payload
        Array.Copy(payload, 0, packet, headerSize, payload.Length);

        return packet;
    }
}