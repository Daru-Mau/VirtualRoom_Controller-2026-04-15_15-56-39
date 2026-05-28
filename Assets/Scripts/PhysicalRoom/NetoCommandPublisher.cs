using UnityEngine;
using UnityEngine.Events;

public class NetoCommandPublisher : MonoBehaviour
{
    public enum NetoRobotId
    {
        Neto1_IP110 = 1,
        Neto2_IP111 = 2,
        Neto3_IP112 = 3,
    }

    [Header("References")]
    [SerializeField] private VrRobotUdpBridge bridge;

    [Header("Robot Identity")]
    [Tooltip("Must match hub registry. Neto1=IP.110, Neto2=IP.111, Neto3=IP.112")]
    [SerializeField] public NetoRobotId robotId = NetoRobotId.Neto1_IP110;

    [Header("Defaults")]
    [Tooltip("0 = lowest (bottom), 180 = highest (top). Neto starts at bottom physically.")]
    [SerializeField, Range(0, 180)] private int defaultMotorSpeedUnits = 0;
    [SerializeField, Range(0, 10)] private int defaultLedRadius = 0;
    [SerializeField, Range(0, 255)] private int defaultLedBrightness = 0;
    [SerializeField, Range(0, 20)] private int defaultVolume = 0;

    [Header("Runtime State — read only")]
    [SerializeField] private bool soundEnabled;
    [SerializeField] private int currentMotorSpeedUnits;
    [SerializeField] private int currentLedRadius;
    [SerializeField] private int currentLedBrightness;
    [SerializeField] private int currentVolume;

    // State caching — only send if changed (deduplication)
    private bool _lastSentSoundEnabled;
    private int _lastSentMotorSpeedUnits;
    private int _lastSentLedRadius;
    private int _lastSentLedBrightness;
    private int _lastSentVolume;

    [Header("Diagnostics")]
    public UnityEvent onCommandSent;

    // ── Public read access for visual drivers ────────────────────────────
    /// <summary>
    /// Current motor speed in servo units (0–180).
    /// 0 = lowest (bottom), 180 = highest (top).
    /// </summary>
    public int CurrentMotorSpeedUnits => currentMotorSpeedUnits;

    // ── Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (bridge == null)
            bridge = FindObjectOfType<VrRobotUdpBridge>();

        currentMotorSpeedUnits = defaultMotorSpeedUnits;
        currentLedRadius = defaultLedRadius;
        currentLedBrightness = defaultLedBrightness;
        currentVolume = defaultVolume;
    }

    // ── Public command API ───────────────────────────────────────────────

    public void SetState(bool sound, int volume, int motorSpeedUnits, int ledRadius, int ledBrightness)
    {
        soundEnabled = sound;
        currentVolume = Mathf.Clamp(volume, 0, 20);
        currentMotorSpeedUnits = Mathf.Clamp(motorSpeedUnits, 0, 180);
        currentLedRadius = Mathf.Clamp(ledRadius, 0, 10);
        currentLedBrightness = Mathf.Clamp(ledBrightness, 0, 255);
        SendCommand();
    }

    public void SetMotorSpeedUnits(int speedUnits)
    {
        currentMotorSpeedUnits = Mathf.Clamp(speedUnits, 0, 180);
        SendCommand();
    }

    public void SetPullNormalized(float n)
    {
        currentMotorSpeedUnits = Mathf.RoundToInt(Mathf.Lerp(0f, 180f, Mathf.Clamp01(n)));
        SendCommand();
    }

    public void SetReleaseNormalized(float n)
    {
        currentMotorSpeedUnits = Mathf.RoundToInt(Mathf.Lerp(0f, 180f, Mathf.Clamp01(n)));
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

    public void ResetToDefaults()
    {
        soundEnabled = false;
        currentMotorSpeedUnits = 0;
        currentLedRadius = 0;
        currentLedBrightness = 0;
        currentVolume = 0;
        SendCommand();
    }

    // ── Internal ─────────────────────────────────────────────────────────

    private void SendCommand()
    {
        // Only send if state actually changed (deduplication)
        if (soundEnabled == _lastSentSoundEnabled &&
            currentMotorSpeedUnits == _lastSentMotorSpeedUnits &&
            currentLedRadius == _lastSentLedRadius &&
            currentLedBrightness == _lastSentLedBrightness &&
            currentVolume == _lastSentVolume)
            return;

        if (bridge == null) return;

        bridge.SendNetoCommand(
            (int)robotId,
            sound: soundEnabled ? 1 : 0,
            volume: currentVolume,
            speedUnits: currentMotorSpeedUnits,
            ledRadius: currentLedRadius,
            ledBrightness: currentLedBrightness
        );

        _lastSentSoundEnabled = soundEnabled;
        _lastSentMotorSpeedUnits = currentMotorSpeedUnits;
        _lastSentLedRadius = currentLedRadius;
        _lastSentLedBrightness = currentLedBrightness;
        _lastSentVolume = currentVolume;

        onCommandSent?.Invoke();
    }
}