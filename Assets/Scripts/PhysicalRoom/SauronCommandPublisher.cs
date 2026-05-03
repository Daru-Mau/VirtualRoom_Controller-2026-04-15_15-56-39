using UnityEngine;
using UnityEngine.Events;
using PhysicalRoom.UnityBridge;

public class SauronCommandPublisher : MonoBehaviour
{
    public enum SauronRobotId
    {
        Sauron1_IP120 = 4,   // top_servo_range (0,180) — full range
        Sauron2_IP121 = 5,   // top_servo_range (10,70) — hub remaps automatically
    }

    [Header("References")]
    [SerializeField] private VrRobotUdpBridge bridge;

    [Header("Robot Identity")]
    [Tooltip("Hub remaps top servo range per robot automatically")]
    [SerializeField] private SauronRobotId robotId = SauronRobotId.Sauron1_IP120;

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

    private void SendCommand()
    {
        if (bridge == null) return;
        bridge.SendSauronCommand((int)robotId, bottomServoAngle, topServoAngle);
        onCommandSent?.Invoke();
    }
}