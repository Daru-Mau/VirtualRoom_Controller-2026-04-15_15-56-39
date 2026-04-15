using UnityEngine;
using UnityEngine.Events;
using PhysicalRoom.UnityBridge;

/// <summary>
/// Publishes commands to Neto robots based on VR object interactions.
/// Attach to interactable objects and configure for specific Neto instances.
/// </summary>
public class NetoCommandPublisher : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private VrRobotUdpBridge bridge;

    [Header("Robot Target")]
    [SerializeField]
    private bool autoComposeIp = true;

    [SerializeField]
    private NetoRobotSlot robotSlot = NetoRobotSlot.Neto1;

    [SerializeField]
    private string robotIp = "192.168.104.110";

    [SerializeField]
    private int robotPort = 4210;

    [Header("Control Mapping")]
    [SerializeField, Range(0, 180)]
    private int defaultMotorSpeedUnits = 90;

    [SerializeField, Range(0, 10)]
    private int defaultLedRadius = 5;

    [SerializeField, Range(0, 255)]
    private int defaultLedBrightness = 150;

    [SerializeField, Range(0, 20)]
    private int defaultVolume = 10;

    [Header("Runtime State")]
    [SerializeField]
    private bool soundEnabled;

    [SerializeField]
    private int currentMotorSpeedUnits = 90;

    [SerializeField]
    private int currentLedRadius;

    [SerializeField]
    private int currentLedBrightness;

    [SerializeField]
    private int currentVolume;

    [Header("Diagnostics")]
    public UnityEvent<VrRobotUdpBridge.NetoCommand> onCommandSent;

    private void Awake()
    {
        if (bridge == null)
        {
            bridge = FindObjectOfType<VrRobotUdpBridge>();
        }

        ApplyRobotSelector();

        currentMotorSpeedUnits = defaultMotorSpeedUnits;
        currentLedRadius = defaultLedRadius;
        currentLedBrightness = defaultLedBrightness;
        currentVolume = defaultVolume;
    }

    private void OnValidate()
    {
        ApplyRobotSelector();
    }

    public void SetSound(bool enabled, int volume = -1)
    {
        soundEnabled = enabled;
        if (volume >= 0)
        {
            currentVolume = Mathf.Clamp(volume, 0, 20);
        }
        Debug.Log($"[NetoPublisher] VR Input: SetSound(enabled={enabled}, volume={volume})");
        SendCommand();
    }

    public void SetState(bool sound, int volume, int motorSpeedUnits, int radius, int brightness)
    {
        soundEnabled = sound;
        currentVolume = Mathf.Clamp(volume, 0, 20);
        currentMotorSpeedUnits = Mathf.Clamp(motorSpeedUnits, 0, 180);
        currentLedRadius = Mathf.Clamp(radius, 0, 10);
        currentLedBrightness = Mathf.Clamp(brightness, 0, 255);
        SendCommand();
    }

    public void SetMotorSpeedUnits(int speedUnits)
    {
        currentMotorSpeedUnits = Mathf.Clamp(speedUnits, 0, 180);
        Debug.Log($"[NetoPublisher] VR Input: SetMotorSpeedUnits(speedUnits={speedUnits})");
        SendCommand();
    }

    // Backward-compatible alias for existing UnityEvents.
    public void SetServo(int angle)
    {
        SetMotorSpeedUnits(angle);
    }

    public void SetPullNormalized(float normalizedPull)
    {
        currentMotorSpeedUnits = Mathf.RoundToInt(Mathf.Lerp(90f, 0f, Mathf.Clamp01(normalizedPull)));
        Debug.Log($"[NetoPublisher] VR Input: SetPullNormalized(normalized={normalizedPull:F2})");
        SendCommand();
    }

    public void SetReleaseNormalized(float normalizedRelease)
    {
        currentMotorSpeedUnits = Mathf.RoundToInt(Mathf.Lerp(90f, 180f, Mathf.Clamp01(normalizedRelease)));
        Debug.Log($"[NetoPublisher] VR Input: SetReleaseNormalized(normalized={normalizedRelease:F2})");
        SendCommand();
    }

    public void SetLeds(int radius, int brightness)
    {
        currentLedRadius = Mathf.Clamp(radius, 0, 10);
        currentLedBrightness = Mathf.Clamp(brightness, 0, 255);
        Debug.Log($"[NetoPublisher] VR Input: SetLeds(radius={radius}, brightness={brightness})");
        SendCommand();
    }

    public void SetLedRadius(float normalizedRadius)
    {
        currentLedRadius = Mathf.RoundToInt(Mathf.Clamp01(normalizedRadius) * 10f);
        Debug.Log($"[NetoPublisher] VR Input: SetLedRadius(normalized={normalizedRadius:F2})");
        SendCommand();
    }

    public void SetLedBrightness(float normalizedBrightness)
    {
        currentLedBrightness = Mathf.RoundToInt(Mathf.Clamp01(normalizedBrightness) * 255f);
        Debug.Log($"[NetoPublisher] VR Input: SetLedBrightness(normalized={normalizedBrightness:F2})");
        SendCommand();
    }

    public void ResetToDefaults()
    {
        soundEnabled = false;
        currentMotorSpeedUnits = 90;
        currentLedRadius = 0;
        currentLedBrightness = 0;
        currentVolume = 0;
        Debug.Log($"[NetoPublisher] VR Input: ResetToDefaults()");
        SendCommand();
    }

    private void SendCommand()
    {
        if (bridge == null)
        {
            ApplyRobotSelector();
            return;
        }

        var command = new VrRobotUdpBridge.NetoCommand
        {
            Sound = soundEnabled ? 1 : 0,
            Volume = currentVolume,
            MotorSpeedUnits = currentMotorSpeedUnits,
            LedRadius = currentLedRadius,
            LedBrightness = currentLedBrightness
        };

        bridge.SendNetoControl(robotIp, robotPort, command);
        Debug.Log($"[NetoPublisher] → Robot {robotIp}:{robotPort} | Sound={command.Sound} Vol={command.Volume} Motor={command.MotorSpeedUnits} LED(R={command.LedRadius},B={command.LedBrightness})");
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

    private enum NetoRobotSlot
    {
        Neto1 = 110,
        Neto2 = 111,
        Neto3 = 112
    }
}
