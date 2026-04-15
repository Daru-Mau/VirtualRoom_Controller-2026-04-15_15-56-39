using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PhysicalRoom.UnityBridge
{
    /// <summary>
    /// Manages the UDP transport between Unity and the vr_interface_node.
    /// Handles the binary control stream (Unity -> ROS) and the pose/sensor feedback streams (ROS -> Unity).
    /// </summary>
    public class VrRobotUdpBridge : MonoBehaviour
    {
        [Header("Network Configuration")]
        [SerializeField]
        private string networkBase = "192.168.104";

        [SerializeField]
        private int robotUdpPort = 4210;

        [SerializeField]
        private int feedbackPort = 5008;

        [Header("Runtime")]
        [SerializeField]
        private bool autoStart = true;

        [SerializeField]
        private bool sendSafeStateOnStart = true;

        [Header("Connection Status")]
        [SerializeField]
        private bool trackConnectionHealth = true;

        [SerializeField, Range(1f, 30f)]
        private float connectionTimeoutSeconds = 5f;

        public string NetworkBase => networkBase;
        public int RobotUdpPort => robotUdpPort;

        private readonly ConcurrentQueue<Action> mainThreadActions = new();
        private CancellationTokenSource cancellation;
        private UdpClient controlClient;
        private UdpClient feedbackListener;
        private bool emergencyStopActive;
        private readonly ConcurrentDictionary<string, float> lastFeedbackTime = new();

        public event Action<RobotSensorEvent> SensorEventReceived;
        public event Action<string> RobotConnectionLost;
        public event Action<string> RobotConnectionRestored;
        public event Action EmergencyStopActivated;
        public event Action EmergencyStopDeactivated;

        public bool IsRunning => cancellation != null && !cancellation.IsCancellationRequested;
        public bool IsEmergencyStopped => emergencyStopActive;

        private void Start()
        {
            if (autoStart)
            {
                StartBridge();
            }
        }

        public void StartBridge()
        {
            if (IsRunning)
            {
                return;
            }

            cancellation = new CancellationTokenSource();
            controlClient = new UdpClient();

            feedbackListener = CreateListener(feedbackPort);

            _ = Task.Run(() => ReceiveFeedbackLoop(cancellation.Token), cancellation.Token);

            if (trackConnectionHealth)
            {
                _ = Task.Run(() => MonitorConnectionHealth(cancellation.Token), cancellation.Token);
            }

            if (sendSafeStateOnStart)
            {
                SendSafeStateToAllRobots();
            }
        }

        public void StopBridge()
        {
            if (!IsRunning)
            {
                return;
            }

            cancellation.Cancel();

            controlClient?.Close();
            feedbackListener?.Close();

            controlClient = null;
            feedbackListener = null;
        }

        private void OnDestroy()
        {
            StopBridge();
        }

        private void Update()
        {
            while (mainThreadActions.TryDequeue(out var action))
            {
                action.Invoke();
            }
        }

        public void SendNetoControl(string robotIp, int port, NetoCommand command)
        {
            if (controlClient == null)
            {
                Debug.LogWarning("VrRobotUdpBridge: control client not ready");
                return;
            }

            if (emergencyStopActive)
            {
                Debug.LogWarning("VrRobotUdpBridge: Emergency stop active, blocking Neto command");
                return;
            }

            var payload = new byte[6];
            payload[0] = (byte)Mathf.Clamp(command.Sound, 0, 1);
            payload[1] = (byte)Mathf.Clamp(command.Volume, 0, 20);
            payload[2] = (byte)Mathf.Clamp(command.MotorSpeedUnits, 0, 180);
            payload[3] = (byte)Mathf.Clamp(command.LedRadius, 0, 10);
            payload[4] = (byte)Mathf.Clamp(command.LedBrightness, 0, 255);
            payload[5] = 0;

            try
            {
                var endpoint = new IPEndPoint(IPAddress.Parse(robotIp), port);
                controlClient.Send(payload, payload.Length, endpoint);
            }
            catch (Exception ex)
            {
                Debug.LogError($"VrRobotUdpBridge: failed to send Neto packet - {ex.Message}");
            }
        }

        public void SendSauronControl(string robotIp, int port, SauronCommand command)
        {
            if (controlClient == null)
            {
                Debug.LogWarning("VrRobotUdpBridge: control client not ready");
                return;
            }

            if (emergencyStopActive)
            {
                Debug.LogWarning("VrRobotUdpBridge: Emergency stop active, blocking Sauron command");
                return;
            }

            var payload = new byte[6];
            payload[0] = command.BottomAngle.HasValue ? (byte)Mathf.Clamp(command.BottomAngle.Value, 0, 180) : (byte)0;
            payload[1] = command.TopAngle.HasValue ? (byte)Mathf.Clamp(command.TopAngle.Value, 0, 180) : (byte)0;
            payload[2] = 0;
            payload[3] = 0;
            payload[4] = 0;
            payload[5] = command.ManualMode.HasValue ? (byte)(command.ManualMode.Value ? 1 : 0) : (byte)255;

            try
            {
                var endpoint = new IPEndPoint(IPAddress.Parse(robotIp), port);
                controlClient.Send(payload, payload.Length, endpoint);
            }
            catch (Exception ex)
            {
                Debug.LogError($"VrRobotUdpBridge: failed to send Sauron packet - {ex.Message}");
            }
        }

        public void ActivateEmergencyStop()
        {
            if (emergencyStopActive)
            {
                return;
            }

            emergencyStopActive = true;
            SendSafeStateToAllRobots();
            EnqueueMainThread(() => EmergencyStopActivated?.Invoke());
            Debug.LogWarning("VrRobotUdpBridge: EMERGENCY STOP ACTIVATED");
        }

        public void DeactivateEmergencyStop()
        {
            if (!emergencyStopActive)
            {
                return;
            }

            emergencyStopActive = false;
            EnqueueMainThread(() => EmergencyStopDeactivated?.Invoke());
            Debug.Log("VrRobotUdpBridge: Emergency stop deactivated");
        }

        public void SendSafeStateToAllRobots()
        {
            if (controlClient == null)
            {
                return;
            }

            // Send safe state to all Neto robots (110-112)
            var netoSafeState = new NetoCommand
            {
                Sound = 0,
                Volume = 0,
                MotorSpeedUnits = 90, // Stop position
                LedRadius = 0,
                LedBrightness = 0
            };

            for (int i = 110; i <= 112; i++)
            {
                string ip = $"{networkBase}.{i}";
                try
                {
                    var payload = new byte[6];
                    payload[0] = 0;
                    payload[1] = 0;
                    payload[2] = 90;
                    payload[3] = 0;
                    payload[4] = 0;
                    payload[5] = 0;

                    var endpoint = new IPEndPoint(IPAddress.Parse(ip), robotUdpPort);
                    controlClient.Send(payload, payload.Length, endpoint);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"VrRobotUdpBridge: Failed to send safe state to Neto {ip} - {ex.Message}");
                }
            }

            // Send safe state to all Sauron robots (120-121)
            var sauronSafeState = new SauronCommand
            {
                BottomAngle = 90, // Center position
                TopAngle = 90,
                ManualMode = false
            };

            for (int i = 120; i <= 121; i++)
            {
                string ip = $"{networkBase}.{i}";
                try
                {
                    var payload = new byte[6];
                    payload[0] = 90;
                    payload[1] = 90;
                    payload[2] = 0;
                    payload[3] = 0;
                    payload[4] = 0;
                    payload[5] = 0; // Manual mode off

                    var endpoint = new IPEndPoint(IPAddress.Parse(ip), robotUdpPort);
                    controlClient.Send(payload, payload.Length, endpoint);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"VrRobotUdpBridge: Failed to send safe state to Sauron {ip} - {ex.Message}");
                }
            }

            Debug.Log("VrRobotUdpBridge: Safe state sent to all robots");
        }

        public bool GetRobotConnectionStatus(string robotKey)
        {
            if (!trackConnectionHealth || !lastFeedbackTime.TryGetValue(robotKey, out float lastTime))
            {
                return false;
            }

            return (Time.time - lastTime) < connectionTimeoutSeconds;
        }

        private static UdpClient CreateListener(int port)
        {
            var client = new UdpClient(port);
            client.Client.ReceiveTimeout = 1000;
            return client;
        }

        private async Task ReceiveFeedbackLoop(CancellationToken token)
        {
            await ReceiveLoop(token, feedbackListener, buffer =>
            {
                var text = Encoding.ASCII.GetString(buffer).Trim('\0', '\n', '\r', ' ');
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                var sensorEvent = RobotSensorEvent.Parse(text);
                if (sensorEvent != null)
                {
                    // Update connection tracking
                    if (trackConnectionHealth)
                    {
                        string robotKey = $"{sensorEvent.RobotType}:{sensorEvent.RobotNumber}";
                        lastFeedbackTime[robotKey] = Time.time;
                    }

                    EnqueueMainThread(() => SensorEventReceived?.Invoke(sensorEvent));
                }
            });
        }

        private async Task MonitorConnectionHealth(CancellationToken token)
        {
            var knownRobots = new HashSet<string>();
            var previousStatus = new Dictionary<string, bool>();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, token); // Check every second

                    foreach (var kvp in lastFeedbackTime)
                    {
                        string robotKey = kvp.Key;
                        knownRobots.Add(robotKey);

                        bool wasConnected = previousStatus.TryGetValue(robotKey, out bool prev) && prev;
                        bool isConnected = (Time.time - kvp.Value) < connectionTimeoutSeconds;

                        if (wasConnected && !isConnected)
                        {
                            EnqueueMainThread(() => RobotConnectionLost?.Invoke(robotKey));
                        }
                        else if (!wasConnected && isConnected)
                        {
                            EnqueueMainThread(() => RobotConnectionRestored?.Invoke(robotKey));
                        }

                        previousStatus[robotKey] = isConnected;
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"VrRobotUdpBridge: connection monitor error - {ex.Message}");
                }
            }
        }

        private static async Task ReceiveLoop(CancellationToken token, UdpClient client, Action<byte[]> handler)
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await client.ReceiveAsync();
                    handler(result.Buffer);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    // Timeout or port closed; let the loop continue.
                }
                catch (Exception ex)
                {
                    Debug.LogError($"VrRobotUdpBridge: receive loop error - {ex.Message}");
                }
            }
        }

        private void EnqueueMainThread(Action action)
        {
            if (action != null)
            {
                mainThreadActions.Enqueue(action);
            }
        }

        [Serializable]
        public struct NetoCommand
        {
            public int Sound;
            public int Volume;
            public int MotorSpeedUnits;
            public int LedRadius;
            public int LedBrightness;
        }

        [Serializable]
        public struct SauronCommand
        {
            public int? BottomAngle;
            public int? TopAngle;
            public bool? ManualMode;
        }

        public class RobotSensorEvent
        {
            public string RobotType { get; private set; }
            public string RobotNumber { get; private set; }
            public int PayloadValue { get; private set; }
            public int SecondaryValue { get; private set; }
            public string RawMessage { get; private set; }

            public static RobotSensorEvent Parse(string raw)
            {
                var parts = raw.Split(':');
                if (parts.Length < 3)
                {
                    return null;
                }

                if (!int.TryParse(parts[2], out var payload))
                {
                    return null;
                }

                int secondary = 0;
                if (parts.Length > 3 && !string.IsNullOrEmpty(parts[3]))
                {
                    int.TryParse(parts[3].Trim('\0'), out secondary);
                }

                return new RobotSensorEvent
                {
                    RobotType = parts[0],
                    RobotNumber = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : "?",
                    PayloadValue = payload,
                    SecondaryValue = secondary,
                    RawMessage = raw
                };
            }
        }
    }
}
