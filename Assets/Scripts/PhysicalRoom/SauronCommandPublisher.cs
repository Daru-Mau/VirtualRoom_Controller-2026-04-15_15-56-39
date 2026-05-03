using UnityEngine;
using UnityEngine.Events;
using PhysicalRoom.UnityBridge;

/// <summary>
/// Sends Sauron commands through VrRobotUdpBridge → RobotHub → robot.
/// The hub handles per-robot top servo range remapping automatically.
/// Uses stable robot IDs: 4=sauron1, 5=sauron2.
/// </summary>
public class SauronCommandPublisher : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VrRobotUdpBridge bridge;

    [Header("Robot Identity")]
    [Tooltip("Hub robot ID: 4=sauron1, 5=sauron2")]
    [SerializeField, Range(4, 5)] private int robotId = 4;

    [Header("Current State — read only")]
    [SerializeField, Range(0, 180)] private int bottomServoAngle = 90;
    [SerializeField, Range(0, 180)] private int topServoAngle = 90;

    [Header("Diagnostics")]
    public UnityEvent onCommandSent;

    private void Awake()
    {
        if (bridge == null)
            bridge = FindObjectOfType<VrRobotUdpBridge>();
    }

    // ── Public API ───────────────────────────────────────────────────────────

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

    // ── Internal ─────────────────────────────────────────────────────────────

    private void SendCommand()
    {
        if (bridge == null) return;

        // Hub remaps topServoAngle to the physical range per robot:
        // sauron1: top_servo_range=(0,180)  → no change
        // sauron2: top_servo_range=(10,70)  → hub maps 0-180 → 10-70
        bridge.SendSauronCommand(robotId, bottomServoAngle, topServoAngle);

        onCommandSent?.Invoke();
    }
}