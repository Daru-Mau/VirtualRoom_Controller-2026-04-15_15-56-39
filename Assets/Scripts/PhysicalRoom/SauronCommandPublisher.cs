using UnityEngine;
using UnityEngine.Events;

public class SauronCommandPublisher : MonoBehaviour
{
    public enum SauronRobotId
    {
        Sauron1_IP120 = 4,   // top_servo_range (0,180)
        Sauron2_IP121 = 5,   // top_servo_range (10,70) — hub remaps automatically
    }

    [Header("References")]
    [SerializeField] private VrRobotUdpBridge bridge;

    [Header("Robot Identity")]
    [Tooltip("Hub remaps top servo range per robot automatically")]
    [SerializeField] public SauronRobotId robotId = SauronRobotId.Sauron1_IP120;

    [Header("Current State — read only")]
    [SerializeField, Range(0, 180)] private int bottomServoAngle = 90;
    [SerializeField, Range(0, 180)] private int topServoAngle = 90;

    [Header("Diagnostics")]
    public UnityEvent onCommandSent;

    // ── Public read access for visual drivers ────────────────────────────
    /// <summary>Bottom servo (yaw). 90 = centre, 0 = full left, 180 = full right.</summary>
    public int BottomServoAngle => bottomServoAngle;
    /// <summary>Top servo (tilt/pitch). 90 = centre, 0 = one extreme, 180 = other.</summary>
    public int TopServoAngle => topServoAngle;

    // ── Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (bridge == null)
            bridge = FindObjectOfType<VrRobotUdpBridge>();
    }

    // ── Public command API ───────────────────────────────────────────────

    public void SetBothServos(int bottom, int top)
    {
        bottomServoAngle = Mathf.Clamp(bottom, 0, 180);
        topServoAngle = Mathf.Clamp(top, 0, 180);
        SendCommand();
    }

    public void SetBottomServo(int angle)
    {
        bottomServoAngle = Mathf.Clamp(angle, 0, 180);
        SendCommand();
    }

    public void SetTopServo(int angle)
    {
        topServoAngle = Mathf.Clamp(angle, 0, 180);
        SendCommand();
    }

    public void SetBottomServoNormalized(float n)
    {
        bottomServoAngle = Mathf.RoundToInt(Mathf.Clamp01(n) * 180f);
        SendCommand();
    }

    public void SetTopServoNormalized(float n)
    {
        topServoAngle = Mathf.RoundToInt(Mathf.Clamp01(n) * 180f);
        SendCommand();
    }

    public void CenterServos()
    {
        bottomServoAngle = 90;
        topServoAngle = 90;
        SendCommand();
    }

    // ── Internal ─────────────────────────────────────────────────────────

    private void SendCommand()
    {
        if (bridge == null) return;
        bridge.SendSauronCommand((int)robotId, bottomServoAngle, topServoAngle);
        onCommandSent?.Invoke();
    }
}