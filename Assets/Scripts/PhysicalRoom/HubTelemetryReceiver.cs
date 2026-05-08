using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class HubTelemetryReceiver : MonoBehaviour
{
    [Header("Hub Connection")]
    [Tooltip("IP of the PC running RobotHub. On Quest via hotspot this is usually 192.168.137.1")]
    [SerializeField] private string hubIp = "192.168.137.1";
    [SerializeField] private int hubPort = 5600;

    [Header("UDP Listen")]
    [Tooltip("Local UDP port to bind. 0 lets the OS choose a free port.")]
    [SerializeField] private int listenPort = 0;

    [Header("Runtime")]
    [SerializeField] private bool autoStart = true;
    [SerializeField] private bool sendHelloOnStart = true;

    [Header("Debug Logging")]
    [SerializeField] private bool logBreatherTelemetry = true;

    private static readonly byte[] MAGIC = { (byte)'P', (byte)'R' };
    private const byte VERSION = 1;
    private const byte MSG_HELLO = 1;
    private const byte MSG_TELEMETRY = 5;
    private const int HEADER_SIZE = 9;

    private UdpClient _client;
    private IPEndPoint _hubEndpoint;
    private CancellationTokenSource _cts;
    private ushort _seq = 1;

    private readonly ConcurrentQueue<Action> _mainThread = new();

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public BreatherTelemetry LastBreatherTelemetry { get; private set; }

    public event Action<NetoTelemetry> NetoTelemetryReceived;
    public event Action<SauronTelemetry> SauronTelemetryReceived;
    public event Action<DeathtrapTelemetry> DeathtrapTelemetryReceived;
    public event Action<BreatherTelemetry> BreatherTelemetryReceived;

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
        public int State;
        public int Level;
        public int? Seq;
        public float ReceivedAt;
        public string RawMessage;
    }

    private void Start()
    {
        if (autoStart)
        {
            StartListening();
        }
    }

    private void OnDestroy()
    {
        StopListening();
    }

    private void Update()
    {
        while (_mainThread.TryDequeue(out var action))
        {
            action.Invoke();
        }
    }

    public void StartListening()
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _hubEndpoint = new IPEndPoint(IPAddress.Parse(hubIp), hubPort);

        _client = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));
        _client.Client.ReceiveTimeout = 500;

        if (sendHelloOnStart)
        {
            SendHello();
        }

        Task.Run(() => ReceiveLoop(_cts.Token));

        Debug.Log($"[HubTelemetryReceiver] Listening on {((IPEndPoint)_client.Client.LocalEndPoint).Port}");
    }

    public void StopListening()
    {
        if (!IsRunning)
        {
            return;
        }

        _cts.Cancel();
        _client?.Close();
        _client = null;
        _cts = null;
    }

    private void SendHello()
    {
        if (_client == null) return;

        byte[] payload = Encoding.UTF8.GetBytes("vr");
        byte[] packet = BuildPacket(MSG_HELLO, 0, payload);
        _client.Send(packet, packet.Length, _hubEndpoint);
    }

    private void ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _client.Receive(ref remote);
                if (data == null || data.Length < HEADER_SIZE)
                {
                    continue;
                }

                if (!TryDecodePacket(data, out byte msgType, out int robotId, out byte[] payload))
                {
                    continue;
                }

                if (msgType != MSG_TELEMETRY)
                {
                    continue;
                }

                string message = Encoding.UTF8.GetString(payload)
                    .Replace("\0", string.Empty)
                    .Trim();

                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                if (TryParseNeto(robotId, message, out var neto))
                {
                    _mainThread.Enqueue(() => NetoTelemetryReceived?.Invoke(neto));
                }
                else if (TryParseSauron(robotId, message, out var sauron))
                {
                    _mainThread.Enqueue(() => SauronTelemetryReceived?.Invoke(sauron));
                }
                else if (TryParseDeathtrap(robotId, message, out var deathtrap))
                {
                    _mainThread.Enqueue(() => DeathtrapTelemetryReceived?.Invoke(deathtrap));
                }
                else if (TryParseBreather(robotId, message, out var breather))
                {
                    _mainThread.Enqueue(() =>
                    {
                        LastBreatherTelemetry = breather;
                        BreatherTelemetryReceived?.Invoke(breather);

                        if (logBreatherTelemetry)
                        {
                            Debug.Log($"[BREATHER] robot={breather.RobotId} raw={breather.Raw} smoothed={breather.Smoothed} centered={breather.Centered} state={breather.State} level={breather.Level} seq={(breather.Seq.HasValue ? breather.Seq.Value.ToString() : "-")}");
                        }
                    });
                }
            }
            catch (SocketException)
            {
                // Timeout; keep looping.
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception)
            {
                // Ignore malformed packets.
            }
        }
    }

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

    private static bool TryDecodePacket(byte[] data, out byte msgType, out int robotId, out byte[] payload)
    {
        msgType = 0;
        robotId = 0;
        payload = Array.Empty<byte>();

        if (data.Length < HEADER_SIZE)
        {
            return false;
        }

        if (data[0] != MAGIC[0] || data[1] != MAGIC[1])
        {
            return false;
        }

        if (data[2] != VERSION)
        {
            return false;
        }

        msgType = data[3];
        robotId = data[6];
        int payloadLen = data[7] | (data[8] << 8);
        if (payloadLen < 0 || data.Length < HEADER_SIZE + payloadLen)
        {
            return false;
        }

        payload = new byte[payloadLen];
        Array.Copy(data, HEADER_SIZE, payload, 0, payloadLen);
        return true;
    }

    private static bool TryParseNeto(int robotId, string message, out NetoTelemetry telemetry)
    {
        telemetry = default;
        if (!message.StartsWith("N:"))
        {
            return false;
        }

        string[] parts = message.Split(':');
        if (parts.Length < 4)
        {
            return false;
        }

        int micLevel;
        int dangerFlag;
        int optionalFieldStartIndex;

        // Try Format 1: N:robotId:micLevel:dangerFlag:...
        // robotId is in parts[1], micLevel in parts[2], dangerFlag in parts[3]
        if (int.TryParse(parts[1], out int parsedRobotId) &&
            int.TryParse(parts[2], out micLevel) &&
            int.TryParse(parts[3], out dangerFlag))
        {
            robotId = parsedRobotId;
            optionalFieldStartIndex = 4;
        }
        // Try Format 2: N:micLevel:dangerFlag:...
        // micLevel is in parts[1], dangerFlag in parts[2]
        else if (int.TryParse(parts[1], out micLevel) &&
                 int.TryParse(parts[2], out dangerFlag))
        {
            // robotId comes from packet header; message doesn't contain it
            optionalFieldStartIndex = 3;
        }
        else
        {
            return false;
        }

        float? positionMm = null;
        int? load = null;
        int? temperature = null;
        int positionValid = 0;

        // Parse optional fields (positionMm, load, temperature, positionValid)
        // based on which format was matched
        if (parts.Length > optionalFieldStartIndex)
        {
            if (float.TryParse(parts[optionalFieldStartIndex], out float pos))
                positionMm = pos;
        }

        if (parts.Length > optionalFieldStartIndex + 1)
        {
            if (int.TryParse(parts[optionalFieldStartIndex + 1], out int loadVal))
                load = loadVal;
        }

        if (parts.Length > optionalFieldStartIndex + 2)
        {
            if (int.TryParse(parts[optionalFieldStartIndex + 2], out int tempVal))
                temperature = tempVal;
        }

        if (parts.Length > optionalFieldStartIndex + 3)
        {
            int.TryParse(parts[optionalFieldStartIndex + 3], out positionValid);
        }

        telemetry = new NetoTelemetry
        {
            RobotId = robotId,
            MicLevel = micLevel,
            DangerFlag = dangerFlag,
            PositionMm = positionMm,
            Load = load,
            Temperature = temperature,
            PositionValid = positionValid,
            ReceivedAt = Time.unscaledTime,
            Raw = message
        };

        return true;
    }

    private static bool TryParseSauron(int robotId, string message, out SauronTelemetry telemetry)
    {
        telemetry = default;
        if (!(message.StartsWith("S:") || message.StartsWith("H:")))
        {
            return false;
        }

        string[] parts = message.Split(':');
        if (parts.Length < 4)
        {
            return false;
        }

        if (!int.TryParse(parts[2], out int touched)) return false;
        if (!int.TryParse(parts[3], out int dangerZone)) return false;

        telemetry = new SauronTelemetry
        {
            RobotId = robotId,
            Touched = touched,
            DangerZone = dangerZone,
            Marker = message[0],
            ReceivedAt = Time.unscaledTime,
            Raw = message
        };

        return true;
    }

    private static bool TryParseDeathtrap(int robotId, string message, out DeathtrapTelemetry telemetry)
    {
        telemetry = default;
        if (!message.StartsWith("D:"))
        {
            return false;
        }

        string[] parts = message.Split(':');
        if (parts.Length < 3)
        {
            return false;
        }

        int touchIdx = parts.Length >= 4 ? 2 : 1;
        int distIdx = parts.Length >= 4 ? 3 : 2;

        if (!int.TryParse(parts[touchIdx], out int touchLevel)) return false;
        if (!float.TryParse(parts[distIdx], out float minDistance)) return false;

        telemetry = new DeathtrapTelemetry
        {
            RobotId = robotId,
            TouchLevel = touchLevel,
            MinDistance = minDistance,
            ReceivedAt = Time.unscaledTime,
            Raw = message
        };

        return true;
    }

    private static bool TryParseBreather(int robotId, string message, out BreatherTelemetry telemetry)
    {
        telemetry = default;
        if (!message.StartsWith("B:"))
        {
            return false;
        }

        string[] parts = message.Split(':');
        if (parts.Length < 6)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int raw)) return false;
        if (!int.TryParse(parts[2], out int smoothed)) return false;
        if (!int.TryParse(parts[3], out int centered)) return false;
        if (!int.TryParse(parts[4], out int state)) return false;
        if (!int.TryParse(parts[5], out int level)) return false;

        int? seq = null;
        if (parts.Length >= 7 && int.TryParse(parts[6], out int seqVal))
        {
            seq = seqVal;
        }

        telemetry = new BreatherTelemetry
        {
            RobotId = robotId,
            Raw = raw,
            Smoothed = smoothed,
            Centered = centered,
            State = state,
            Level = Mathf.Clamp(level, 0, 100),
            Seq = seq,
            ReceivedAt = Time.unscaledTime,
            RawMessage = message
        };

        return true;
    }
}
