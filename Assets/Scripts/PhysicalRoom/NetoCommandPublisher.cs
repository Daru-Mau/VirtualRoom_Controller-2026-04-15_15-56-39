using UnityEngine;
using UnityEngine.Events;
using PhysicalRoom.UnityBridge;

/// <summary>
/// Sends Neto commands through VrRobotUdpBridge → RobotHub → robot.
/// Uses stable robot IDs (1=neto1, 2=neto2, 3=neto3) matching the hub registry.
/// </summary>
public class NetoCommandPublisher : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VrRobotUdpBridge bridge;

    [Header("Robot Identity")]
    [Tooltip("Hub robot ID: 1=neto1, 2=neto2, 3=neto3")]
    [SerializeField, Range(1, 3)] private int robotId = 1;

    [Header("Defaults")]
    [SerializeField, Range(0, 180)] private int defaultMotorSpeedUnits = 90;
    [SerializeField, Range(0, 10)] private int defaultLedRadius = 0;
    [SerializeField, Range(0, 255)] private int defaultLedBrightness = 0;
    [SerializeField, Range(0, 20)] private int defaultVolume = 0;

    [Header("Runtime State — read only")]
    [SerializeField] private bool soundEnabled;
    [SerializeField] private int currentMotorSpeedUnits = 90;
    [SerializeField] private int currentLedRadius;
    [SerializeField] private int currentLedBrightness;
    [SerializeField] private int currentVolume;

    [Header("Diagnostics")]
    public UnityEvent onCommandSent;

    private void Awake()
    {
        if (bridge == null)
            bridge = FindObjectOfType<VrRobotUdpBridge>();

        currentMotorSpeedUnits = defaultMotorSpeedUnits;
        currentLedRadius = defaultLedRadius;
        currentLedBrightness = defaultLedBrightness;
        currentVolume = defaultVolume;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Set all Neto parameters at once.</summary>
    public void SetState(bool sound, int volume, int motorSpeedUnits, int ledRadius, int ledBrightness)
    {
        soundEnabled = sound;
        currentVolume = Mathf.Clamp(volume, 0, 20);
        currentMotorSpeedUnits = Mathf.Clamp(motorSpeedUnits, 0, 180);
        currentLedRadius = Mathf.Clamp(ledRadius, 0, 10);
        currentLedBrightness = Mathf.Clamp(ledBrightness, 0, 255);
        SendCommand();
    }

    /// <summary>Set motor speed units directly (0-180, 90=stop).</summary>
    public void SetMotorSpeedUnits(int speedUnits)
    {
        currentMotorSpeedUnits = Mathf.Clamp(speedUnits, 0, 180);
        SendCommand();
    }

    /// <summary>Positive normalizedPull [0-1] maps to motor range [90→0] (pulling).</summary>
    public void SetPullNormalized(float n)
    {
        currentMotorSpeedUnits = Mathf.RoundToInt(Mathf.Lerp(90f, 0f, Mathf.Clamp01(n)));
        SendCommand();
    }

    /// <summary>Positive normalizedRelease [0-1] maps to motor range [90→180] (releasing).</summary>
    public void SetReleaseNormalized(float n)
    {
        currentMotorSpeedUnits = Mathf.RoundToInt(Mathf.Lerp(90f, 180f, Mathf.Clamp01(n)));
        SendCommand();
    }

    public void SetSound(bool enabled, int volume = -1)
    {
        soundEnabled = enabled;
        if (volume >= 0)
            currentVolume = Mathf.Clamp(volume, 0, 20);
        SendCommand();
    }

    public void SetLeds(int radius, int brightness)
    {
        currentLedRadius = Mathf.Clamp(radius, 0, 10);
        currentLedBrightness = Mathf.Clamp(brightness, 0, 255);
        SendCommand();
    }

    /// <summary>Stop motor, silence sound and LEDs.</summary>
    public void ResetToDefaults()
    {
        soundEnabled = false;
        currentMotorSpeedUnits = 90;
        currentLedRadius = 0;
        currentLedBrightness = 0;
        currentVolume = 0;
        SendCommand();
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private void SendCommand()
    {
        if (bridge == null) return;

        bridge.SendNetoCommand(
            robotId,
            sound: soundEnabled ? 1 : 0,
            volume: currentVolume,
            speedUnits: currentMotorSpeedUnits,
            ledRadius: currentLedRadius,
            ledBrightness: currentLedBrightness
        );

        onCommandSent?.Invoke();
    }
}