using UnityEngine;
using UnityEngine.Events;
using PhysicalRoom.UnityBridge;

/// <summary>
/// Publishes commands to Sauron robots based on VR object interactions.
/// Handles both continuous (manual mode) and positional servo control.
/// </summary>
public class SauronCommandPublisher : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private VrRobotUdpBridge bridge;

    [Header("Robot Target")]
    [SerializeField]
    private bool autoComposeIp = true;

    [SerializeField]
    private SauronRobotSlot robotSlot = SauronRobotSlot.Sauron1;

    [SerializeField]
    private string robotIp = "192.168.104.120";

    [SerializeField]
    private int robotPort = 4210;

    [SerializeField]
    private bool isSauron2;

    [Header("Servo Control")]
    [SerializeField, Range(0, 180)]
    private int bottomServoAngle = 90;

    [SerializeField, Range(0, 180)]
    private int topServoAngle = 90;

    [SerializeField]
    private bool manualModeEnabled;

    [Header("Sauron 2 Mapping")]
    [SerializeField]
    private bool autoMapTopServo = true;

    [SerializeField]
    private int sauron2TopMin = 10;

    [SerializeField]
    private int sauron2TopMax = 70;

    [Header("Diagnostics")]
    public UnityEvent<VrRobotUdpBridge.SauronCommand> onCommandSent;

    private void Awake()
    {
        if (bridge == null)
        {
            bridge = FindObjectOfType<VrRobotUdpBridge>();
        }

        ApplyRobotSelector();
    }

    private void OnValidate()
    {
        ApplyRobotSelector();
    }

    public void SetBottomServo(int angle)
    {
        bottomServoAngle = Mathf.Clamp(angle, 0, 180);
        Debug.Log($"[SauronPublisher] VR Input: SetBottomServo(angle={angle})");
        SendCommand();
    }

    public void SetTopServo(int angle)
    {
        topServoAngle = Mathf.Clamp(angle, 0, 180);
        Debug.Log($"[SauronPublisher] VR Input: SetTopServo(angle={angle})");
        SendCommand();
    }

    public void SetBothServos(int bottom, int top)
    {
        bottomServoAngle = Mathf.Clamp(bottom, 0, 180);
        topServoAngle = Mathf.Clamp(top, 0, 180);
        Debug.Log($"[SauronPublisher] VR Input: SetBothServos(bottom={bottom}, top={top})");
        SendCommand();
    }

    public void SetManualMode(bool enabled)
    {
        manualModeEnabled = enabled;
        Debug.Log($"[SauronPublisher] VR Input: SetManualMode(enabled={enabled})");
        SendCommand();
    }

    public void SetBottomServoNormalized(float normalized)
    {
        bottomServoAngle = Mathf.RoundToInt(Mathf.Clamp01(normalized) * 180f);
        Debug.Log($"[SauronPublisher] VR Input: SetBottomServoNormalized(normalized={normalized:F2})");
        SendCommand();
    }

    public void SetTopServoNormalized(float normalized)
    {
        topServoAngle = Mathf.RoundToInt(Mathf.Clamp01(normalized) * 180f);
        Debug.Log($"[SauronPublisher] VR Input: SetTopServoNormalized(normalized={normalized:F2})");
        SendCommand();
    }

    public void CenterServos()
    {
        bottomServoAngle = 90;
        topServoAngle = 90;
        Debug.Log($"[SauronPublisher] VR Input: CenterServos()");
        SendCommand();
    }

    private void SendCommand()
    {
        if (bridge == null)
        {
            ApplyRobotSelector();
            return;
        }

        int? effectiveTop = topServoAngle;
        if (isSauron2 && autoMapTopServo)
        {
            effectiveTop = Mathf.RoundToInt(Mathf.Lerp(sauron2TopMin, sauron2TopMax, topServoAngle / 180f));
        }

        var command = new VrRobotUdpBridge.SauronCommand
        {
            BottomAngle = bottomServoAngle,
            TopAngle = effectiveTop,
            ManualMode = manualModeEnabled
        };

        bridge.SendSauronControl(robotIp, robotPort, command);
        Debug.Log($"[SauronPublisher] → Robot {robotIp}:{robotPort} | Bottom={command.BottomAngle}° Top={command.TopAngle}° ManualMode={command.ManualMode}");
        onCommandSent?.Invoke(command);
    }

    private void ApplyRobotSelector()
    {
        if (!autoComposeIp)
        {
            return;
        }

        var baseIp = bridge != null ? bridge.NetworkBase : "192.168.104";
        robotIp = $"{baseIp}.{(int)robotSlot}";
    }

    private enum SauronRobotSlot
    {
        Sauron1 = 120,
        Sauron2 = 121
    }
}
