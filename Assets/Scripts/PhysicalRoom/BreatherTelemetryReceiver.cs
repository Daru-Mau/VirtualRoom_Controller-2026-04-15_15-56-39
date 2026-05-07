using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class BreatherTelemetryReceiver : MonoBehaviour
{
    [Header("UDP Listen")]
    [Tooltip("Local IP to bind. Use 0.0.0.0 to listen on all interfaces.")]
    [SerializeField] private string listenIp = "0.0.0.0";

    [Tooltip("Telemetry port used by the Breather sensor (default 4211).")]
    [SerializeField] private int listenPort = 4211;

    [Header("Runtime")]
    [SerializeField] private bool autoStart = true;

    public enum BreathState
    {
        Hold = 0,
        Inhale = 1,
        Exhale = 2
    }

    public struct BreatherTelemetry
    {
        public int Raw;
        public int Smoothed;
        public int Centered;
        public BreathState State;
        public int Level;
        public int? Seq;
        public float ReceivedAt;
    }

    private UdpClient _client;
    private CancellationTokenSource _cts;
    private readonly ConcurrentQueue<Action> _mainThread = new();

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public BreatherTelemetry LastTelemetry { get; private set; }
    public event Action<BreatherTelemetry> TelemetryReceived;

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

        IPAddress bindIp = IPAddress.Any;
        if (!string.IsNullOrWhiteSpace(listenIp) && listenIp != "0.0.0.0")
        {
            bindIp = IPAddress.Parse(listenIp);
        }

        _client = new UdpClient(new IPEndPoint(bindIp, listenPort));
        _client.Client.ReceiveTimeout = 500;

        Task.Run(() => ReceiveLoop(_cts.Token));

        Debug.Log($"[BreatherTelemetryReceiver] Listening on {bindIp}:{listenPort}");
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

    private void ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _client.Receive(ref remote);
                if (data == null || data.Length == 0)
                {
                    continue;
                }

                string message = Encoding.UTF8.GetString(data)
                    .Replace("\0", string.Empty)
                    .Trim();

                if (!message.StartsWith("B:"))
                {
                    continue;
                }

                if (TryParseTelemetry(message, out BreatherTelemetry telemetry))
                {
                    telemetry.ReceivedAt = Time.unscaledTime;
                    _mainThread.Enqueue(() =>
                    {
                        LastTelemetry = telemetry;
                        TelemetryReceived?.Invoke(telemetry);
                    });
                }
            }
            catch (SocketException)
            {
                // Timeout, loop again.
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

    private static bool TryParseTelemetry(string message, out BreatherTelemetry telemetry)
    {
        telemetry = default;
        string[] parts = message.Split(':');
        if (parts.Length < 6)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int raw)) return false;
        if (!int.TryParse(parts[2], out int smoothed)) return false;
        if (!int.TryParse(parts[3], out int centered)) return false;
        if (!int.TryParse(parts[4], out int stateRaw)) return false;
        if (!int.TryParse(parts[5], out int level)) return false;

        int? seq = null;
        if (parts.Length >= 7 && int.TryParse(parts[6], out int seqVal))
        {
            seq = seqVal;
        }

        telemetry = new BreatherTelemetry
        {
            Raw = raw,
            Smoothed = smoothed,
            Centered = centered,
            State = stateRaw switch
            {
                1 => BreathState.Inhale,
                2 => BreathState.Exhale,
                _ => BreathState.Hold
            },
            Level = Mathf.Clamp(level, 0, 100),
            Seq = seq,
            ReceivedAt = 0f
        };

        return true;
    }
}
