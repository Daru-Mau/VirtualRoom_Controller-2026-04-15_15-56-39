using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Receives protocol_v2 telemetry packets from the RobotHub running on the PC.
/// Parses and dispatches Neto, Sauron, Deathtrap, and Breather telemetry to
/// typed events — all fired on the Unity main thread.
///
/// ── Robot ID registry (must match hub's _id_to_name map) ────────────────
///   Neto1 = 1 | Neto2 = 2 | Neto3 = 3
///   Sauron1 = 4 | Sauron2 = 5
///   Deathtrap = 6
///   Breather = 7   (hub-forwarded; or use BreatherTelemetryReceiver for direct UDP)
///
/// ── Hello handshake ──────────────────────────────────────────────────────
/// This receiver sends the hello so the hub knows which UDP port to reply to.
/// VrRobotUdpBridge.sendHelloOnStart must be FALSE to avoid the double-hello bug
/// (two different source ports both registering, hub routes telemetry to the wrong one).
/// </summary>
public class HubTelemetryReceiver : MonoBehaviour
{
    [Header("Hub Connection")]
    [Tooltip("IP of the PC running RobotHub. On Quest via hotspot this is usually 192.168.137.1")]
    [SerializeField] private string hubIp = "192.168.137.1";
    [SerializeField] private int hubPort = 5600;

    [Header("UDP Listen")]
    [Tooltip("Local UDP port to bind. 0 lets the OS choose a free port (recommended).")]
    [SerializeField] private int listenPort = 0;

    [Header("Runtime")]
    [SerializeField] private bool autoStart = true;
    [SerializeField] private bool sendHelloOnStart = true;

    [Header("Debug Logging")]
    [Tooltip("Log each parsed Breather packet to the console.")]
    [SerializeField] private bool logBreatherTelemetry = false;
    [Tooltip("Log each parsed Neto packet to the console.")]
    [SerializeField] private bool logNetoTelemetry = false;
    [Tooltip("Log each parsed Sauron packet to the console.")]
    [SerializeField] private bool logSauronTelemetry = false;
    [Tooltip("Log each parsed Deathtrap packet to the console.")]
    [SerializeField] private bool logDeathtrapTelemetry = false;
    [Tooltip("Log raw packet decode details (very verbose — disable in production).")]
    [SerializeField] private bool logPacketDecode = false;

    // ── Protocol constants ────────────────────────────────────────────────

    private static readonly byte[] MAGIC = { (byte)'P', (byte)'R' };
    private const byte VERSION = 1;
    private const byte MSG_HELLO = 1;
    private const byte MSG_TELEMETRY = 5;
    private const int HEADER_SIZE = 9;

    // ── Robot ID constants ────────────────────────────────────────────────

    public const int ID_NETO1 = 1;
    public const int ID_NETO2 = 2;
    public const int ID_NETO3 = 3;
    public const int ID_SAURON1 = 4;
    public const int ID_SAURON2 = 5;
    public const int ID_DEATHTRAP = 6;
    public const int ID_BREATHER = 7;   // ← hub-forwarded breather

    // ── Telemetry structs ─────────────────────────────────────────────────

    public struct NetoTelemetry
    {
        public int RobotId;
        public int MicLevel;
        public int DangerFlag;
        public float? PositionMm;
        public int? Load;
        public int? Temperature;
        public int PositionValid;
        public float ReceivedAt;
        public string Raw;
    }

    public struct SauronTelemetry
    {
        public int RobotId;
        public int Touched;
        public int DangerZone;
        public char Marker;
        public float ReceivedAt;
        public string Raw;
    }

    public struct DeathtrapTelemetry
    {
        public int RobotId;
        public int TouchLevel;
        public float MinDistance;
        public float ReceivedAt;
        public string Raw;
    }

    public struct BreatherTelemetry
    {
        public int RobotId;
        public int Raw;
        public int Smoothed;
        public int Centered;
        public int State;       // 0 Hold | 1 Inhale | 2 Exhale
        public int Level;       // 0–100
        public int? Seq;
        public float ReceivedAt;
        public string RawMessage;
    }

    // ── Private state ─────────────────────────────────────────────────────

    private UdpClient _client;
    private IPEndPoint _hubEndpoint;
    private CancellationTokenSource _cts;
    private ushort _seq = 1;
    private readonly ConcurrentQueue<Action> _mainThread = new();

    // ── Public API ────────────────────────────────────────────────────────

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    /// <summary>Most recent Breather packet, set on the main thread.</summary>
    public BreatherTelemetry LastBreatherTelemetry { get; private set; }

    public event Action<NetoTelemetry> NetoTelemetryReceived;
    public event Action<SauronTelemetry> SauronTelemetryReceived;
    public event Action<DeathtrapTelemetry> DeathtrapTelemetryReceived;
    public event Action<BreatherTelemetry> BreatherTelemetryReceived;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Start()
    {
        if (autoStart) StartListening();
    }

    private void OnDestroy() => StopListening();

    private void Update()
    {
        while (_mainThread.TryDequeue(out var action))
            action.Invoke();
    }

    // ── Control ───────────────────────────────────────────────────────────

    public void StartListening()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _hubEndpoint = new IPEndPoint(IPAddress.Parse(hubIp), hubPort);
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));
        _client.Client.ReceiveTimeout = 500;

        if (sendHelloOnStart)
            SendHello();

        Task.Run(() => ReceiveLoop(_cts.Token));

        int boundPort = ((IPEndPoint)_client.Client.LocalEndPoint).Port;
        Debug.Log($"[HubTelemetryReceiver] Listening on port {boundPort}, hub={hubIp}:{hubPort}");
    }

    public void StopListening()
    {
        if (!IsRunning) return;
        _cts.Cancel();
        _client?.Close();
        _client = null;
        _cts = null;
    }

    // ── Hello ─────────────────────────────────────────────────────────────

    private void SendHello()
    {
        if (_client == null) return;
        byte[] payload = Encoding.UTF8.GetBytes("vr");
        byte[] packet = BuildPacket(MSG_HELLO, 0, payload);
        _client.Send(packet, packet.Length, _hubEndpoint);
        Debug.Log("[HubTelemetryReceiver] Hello sent to hub");
    }

    // ── Background receive loop ───────────────────────────────────────────

    private void ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _client.Receive(ref remote);

                if (data == null || data.Length < HEADER_SIZE)
                    continue;

                if (!TryDecodePacket(data, out byte msgType, out int robotId, out byte[] payload))
                {
                    if (logPacketDecode)
                        Debug.LogWarning($"[HubTelemetryReceiver] Decode failed — " +
                                         $"magic={data[0]:X2}{data[1]:X2} ver={data[2]} type={data[3]}");
                    continue;
                }

                if (logPacketDecode)
                    Debug.Log($"[HubTelemetryReceiver] pkt msgType={msgType} " +
                              $"robotId={robotId} payloadLen={payload.Length}");

                if (msgType != MSG_TELEMETRY)
                    continue;

                string message = Encoding.UTF8.GetString(payload)
                    .Replace("\0", string.Empty)
                    .Trim();

                if (string.IsNullOrWhiteSpace(message))
                    continue;

                // Dispatch to the right parser. ReceivedAt is always set inside
                // the main-thread lambda — never on this background thread.
                if (TryParseNeto(robotId, message, out var neto))
                {
                    _mainThread.Enqueue(() =>
                    {
                        neto.ReceivedAt = Time.unscaledTime;
                        NetoTelemetryReceived?.Invoke(neto);
                        if (logNetoTelemetry)
                            Debug.Log($"[NETO id={neto.RobotId}] " +
                                      $"mic={neto.MicLevel} danger={neto.DangerFlag} " +
                                      $"pos={neto.PositionMm?.ToString("F1") ?? "-"}mm");
                    });
                }
                else if (TryParseSauron(robotId, message, out var sauron))
                {
                    _mainThread.Enqueue(() =>
                    {
                        sauron.ReceivedAt = Time.unscaledTime;
                        SauronTelemetryReceived?.Invoke(sauron);
                        if (logSauronTelemetry)
                            Debug.Log($"[SAURON id={sauron.RobotId}] " +
                                      $"touched={sauron.Touched} danger={sauron.DangerZone} " +
                                      $"marker={sauron.Marker}");
                    });
                }
                else if (TryParseDeathtrap(robotId, message, out var deathtrap))
                {
                    _mainThread.Enqueue(() =>
                    {
                        deathtrap.ReceivedAt = Time.unscaledTime;
                        DeathtrapTelemetryReceived?.Invoke(deathtrap);
                        if (logDeathtrapTelemetry)
                            Debug.Log($"[DEATHTRAP id={deathtrap.RobotId}] " +
                                      $"touch={deathtrap.TouchLevel} " +
                                      $"minDist={deathtrap.MinDistance:F2}m");
                    });
                }
                else if (TryParseBreather(robotId, message, out var breather))
                {
                    _mainThread.Enqueue(() =>
                    {
                        breather.ReceivedAt = Time.unscaledTime;
                        LastBreatherTelemetry = breather;
                        BreatherTelemetryReceived?.Invoke(breather);
                        if (logBreatherTelemetry)
                            Debug.Log($"[BREATHER id={breather.RobotId}] " +
                                      $"state={breather.State} level={breather.Level}% " +
                                      $"raw={breather.Raw} smoothed={breather.Smoothed} " +
                                      $"centered={breather.Centered} seq={breather.Seq}");
                    });
                }
                else if (logPacketDecode)
                {
                    Debug.LogWarning($"[HubTelemetryReceiver] No parser matched: '{message}'");
                }
            }
            catch (SocketException)
            {
                // Normal 500 ms receive timeout — keep looping silently.
            }
            catch (ObjectDisposedException)
            {
                return; // Socket closed by StopListening.
            }
            catch (Exception ex)
            {
                _mainThread.Enqueue(() =>
                    Debug.LogError($"[HubTelemetryReceiver] Unexpected error: " +
                                   $"{ex.GetType().Name} — {ex.Message}"));
            }
        }
    }

    // ── Packet codec ──────────────────────────────────────────────────────

    private byte[] BuildPacket(byte msgType, int robotId, byte[] payload)
    {
        byte[] packet = new byte[HEADER_SIZE + payload.Length];
        packet[0] = MAGIC[0];
        packet[1] = MAGIC[1];
        packet[2] = VERSION;
        packet[3] = msgType;
        packet[4] = (byte)(_seq & 0xFF);
        packet[5] = (byte)((_seq >> 8) & 0xFF);
        _seq = (ushort)((_seq + 1) & 0xFFFF);
        packet[6] = (byte)robotId;
        packet[7] = (byte)(payload.Length & 0xFF);
        packet[8] = (byte)((payload.Length >> 8) & 0xFF);
        Array.Copy(payload, 0, packet, HEADER_SIZE, payload.Length);
        return packet;
    }

    private static bool TryDecodePacket(byte[] data,
                                         out byte msgType,
                                         out int robotId,
                                         out byte[] payload)
    {
        msgType = 0; robotId = 0; payload = Array.Empty<byte>();

        if (data.Length < HEADER_SIZE) return false;
        if (data[0] != MAGIC[0] || data[1] != MAGIC[1]) return false;
        if (data[2] != VERSION) return false;

        msgType = data[3];
        robotId = data[6];
        int payloadLen = data[7] | (data[8] << 8);
        if (payloadLen < 0 || data.Length < HEADER_SIZE + payloadLen) return false;

        payload = new byte[payloadLen];
        Array.Copy(data, HEADER_SIZE, payload, 0, payloadLen);
        return true;
    }

    // ── Parsers ───────────────────────────────────────────────────────────

    private static bool TryParseNeto(int robotId, string message, out NetoTelemetry telemetry)
    {
        telemetry = default;
        if (!message.StartsWith("N:")) return false;

        string[] parts = message.Split(':');
        if (parts.Length < 3) return false;

        int micLevel, dangerFlag, optStart;

        // Format A: N:robotId:micLevel:dangerFlag:...
        if (parts.Length >= 4 &&
            int.TryParse(parts[1], out int embeddedId) &&
            int.TryParse(parts[2], out micLevel) &&
            int.TryParse(parts[3], out dangerFlag))
        {
            robotId = embeddedId;
            optStart = 4;
        }
        // Format B: N:micLevel:dangerFlag:...  (robotId from packet header)
        else if (int.TryParse(parts[1], out micLevel) &&
                 int.TryParse(parts[2], out dangerFlag))
        {
            optStart = 3;
        }
        else return false;

        float? positionMm = null;
        int? load = null;
        int? temperature = null;
        int positionValid = 0;

        if (parts.Length > optStart && float.TryParse(parts[optStart], out float pos)) positionMm = pos;
        if (parts.Length > optStart + 1 && int.TryParse(parts[optStart + 1], out int lv)) load = lv;
        if (parts.Length > optStart + 2 && int.TryParse(parts[optStart + 2], out int tv)) temperature = tv;
        if (parts.Length > optStart + 3) int.TryParse(parts[optStart + 3], out positionValid);

        telemetry = new NetoTelemetry
        {
            RobotId = robotId,
            MicLevel = micLevel,
            DangerFlag = dangerFlag,
            PositionMm = positionMm,
            Load = load,
            Temperature = temperature,
            PositionValid = positionValid,
            ReceivedAt = 0f,  // set on main thread
            Raw = message
        };
        return true;
    }

    private static bool TryParseSauron(int robotId, string message, out SauronTelemetry telemetry)
    {
        telemetry = default;
        if (!message.StartsWith("S:") && !message.StartsWith("H:")) return false;

        string[] parts = message.Split(':');
        if (parts.Length < 4) return false;

        if (!int.TryParse(parts[2], out int touched)) return false;
        if (!int.TryParse(parts[3], out int dangerZone)) return false;

        telemetry = new SauronTelemetry
        {
            RobotId = robotId,
            Touched = touched,
            DangerZone = dangerZone,
            Marker = message[0],
            ReceivedAt = 0f,  // set on main thread
            Raw = message
        };
        return true;
    }

    private static bool TryParseDeathtrap(int robotId, string message, out DeathtrapTelemetry telemetry)
    {
        telemetry = default;
        if (!message.StartsWith("D:")) return false;

        string[] parts = message.Split(':');
        if (parts.Length < 3) return false;

        // Format A: D:robotId:touchLevel:minDistance
        // Format B: D:touchLevel:minDistance
        int touchIdx = parts.Length >= 4 ? 2 : 1;
        int distIdx = parts.Length >= 4 ? 3 : 2;

        if (!int.TryParse(parts[touchIdx], out int touchLevel)) return false;
        if (!float.TryParse(parts[distIdx], out float minDistance)) return false;

        telemetry = new DeathtrapTelemetry
        {
            RobotId = robotId,
            TouchLevel = touchLevel,
            MinDistance = minDistance,
            ReceivedAt = 0f,  // set on main thread
            Raw = message
        };
        return true;
    }

    private static bool TryParseBreather(int robotId, string message, out BreatherTelemetry telemetry)
    {
        telemetry = default;
        if (!message.StartsWith("B:")) return false;

        string[] parts = message.Split(':');
        if (parts.Length < 6) return false;

        if (!int.TryParse(parts[1], out int raw)) return false;
        if (!int.TryParse(parts[2], out int smoothed)) return false;
        if (!int.TryParse(parts[3], out int centered)) return false;
        if (!int.TryParse(parts[4], out int state)) return false;
        if (!int.TryParse(parts[5], out int level)) return false;

        int? seq = null;
        if (parts.Length >= 7 && int.TryParse(parts[6], out int seqVal))
            seq = seqVal;

        telemetry = new BreatherTelemetry
        {
            RobotId = robotId,
            Raw = raw,
            Smoothed = smoothed,
            Centered = centered,
            State = state,
            Level = Mathf.Clamp(level, 0, 100),
            Seq = seq,
            ReceivedAt = 0f,  // set on main thread
            RawMessage = message
        };
        return true;
    }
}
