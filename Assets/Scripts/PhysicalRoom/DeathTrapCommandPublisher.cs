using UnityEngine;
using UnityEngine.Events;

public class DeathTrapCommandPublisher : MonoBehaviour
{
    public enum DeathtrapRobotId
    {
        Deathtrap1_IP130 = 6,
    }

    [Header("References")]
    [SerializeField] private VrRobotUdpBridge bridge;

    [Header("Robot Identity")]
    [Tooltip("Must match hub registry. Deathtrap1=IP.130")]
    [SerializeField] public DeathtrapRobotId robotId = DeathtrapRobotId.Deathtrap1_IP130;

    [Header("Defaults")]
    [SerializeField, Range(60, 105)] private int defaultSphereAngle = 105;

    [Header("Runtime State — read only")]
    [SerializeField] private bool sprayActive;
    [SerializeField] private int currentSphereAngle = 105;

    private bool _lastSentSprayActive;
    private int _lastSentSphereAngle = 105;

    [Header("Diagnostics")]
    public UnityEvent onCommandSent;

    public int CurrentSphereAngle => currentSphereAngle;
    public bool IsSprayActive => sprayActive;

    private void Awake()
    {
        if (bridge == null)
            bridge = FindObjectOfType<VrRobotUdpBridge>();

        currentSphereAngle = defaultSphereAngle;
    }

    public void SetSpray(bool activate)
    {
        sprayActive = activate;
        SendCommand();
    }

    public void SetSphereAngle(int angle)
    {
        currentSphereAngle = Mathf.Clamp(angle, 60, 105);
        SendCommand();
    }

    public void SetState(bool spray, int sphereAngle)
    {
        sprayActive = spray;
        currentSphereAngle = Mathf.Clamp(sphereAngle, 60, 105);
        SendCommand();
    }

    public void ResetToDefaults()
    {
        sprayActive = false;
        currentSphereAngle = 105;
        SendCommand();
    }

    private void SendCommand()
    {
        if (sprayActive == _lastSentSprayActive &&
            currentSphereAngle == _lastSentSphereAngle)
            return;

        if (bridge == null) return;

        bridge.SendDeathtrapCommand(
            (int)robotId,
            spray: sprayActive ? 1 : 0,
            sphereAngle: currentSphereAngle,
            autoMode: 0
        );

        _lastSentSprayActive = sprayActive;
        _lastSentSphereAngle = currentSphereAngle;

        onCommandSent?.Invoke();
    }
}
