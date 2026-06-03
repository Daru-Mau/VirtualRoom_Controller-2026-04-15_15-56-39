using UnityEngine;

/// <summary>
/// Drives the visual up/down movement of a Neto robot in the VR scene
/// using real encoder telemetry (PositionMm / PositionMinMm / PositionMaxMm).
///
/// Attach to the Neto_X_Rig GameObject (same one that has NetoCommandPublisher).
/// The Handle transform (Neto_X_Handle) is moved along its local Y axis.
/// The RopeVisual capsule is stretched to always span from the anchor to the handle.
/// </summary>
public class NetoVisualDriver : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Filled automatically from this GameObject if left empty.")]
    [SerializeField] private NetoCommandPublisher publisher;

    [Tooltip("Optional: receive Neto mic telemetry from RobotHub.")]
    [SerializeField] private HubTelemetryReceiver telemetryReceiver;

    [Tooltip("The Neto_X_Handle transform — the carriage that travels up/down.")]
    [SerializeField] private Transform handle;

    [Tooltip("Optional: the RopeVisual capsule that is stretched to match the rope length.")]
    [SerializeField] private Transform ropeVisual;

    [Tooltip("Fixed world-space anchor point where the rope is attached to the ceiling.")]
    [SerializeField] private Transform ropeAnchor;

    [Header("VR Scene Position Bounds")]
    [Tooltip("Handle local Y when the Neto is at its highest (fully retracted / physical min).")]
    [SerializeField] private float handleTopY = 1.5f;
    [Tooltip("Handle local Y when the Neto is at its lowest (fully extended / physical max).")]
    [SerializeField] private float handleBottomY = -1.5f;

    [Header("Smoothing")]
    [Tooltip("How fast the handle lerps toward the target position (higher = snappier).")]
    [SerializeField, Range(0.5f, 10f)] private float smoothSpeed = 2f;

    [Header("Rope Visual (optional)")]
    [Tooltip("Original local Y scale of the RopeVisual at the start position. " +
             "Leave at 0 to auto-measure on Start.")]
    [SerializeField] private float ropeBaseScaleY = 0f;

    [Header("Telemetry")]
    [SerializeField, Range(0, 255)] private int latestMicLevel;

    [Tooltip("How long after the last telemetry packet to keep applying real position (otherwise the handle stays put).")]
    [SerializeField, Range(0.1f, 2f)] private float telemetryTimeout = 0.5f;

    // ── Runtime ──────────────────────────────────────────────────────────
    private float _ropeBaseLength;
    private float _lastTelemetryTime = -100f;
    private float _telemetryPositionMm;
    private float _telemetryMinMm;
    private float _telemetryMaxMm;

    // ─────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (publisher == null)
            publisher = GetComponent<NetoCommandPublisher>();

        if (telemetryReceiver == null)
            telemetryReceiver = FindObjectOfType<HubTelemetryReceiver>();

        if (telemetryReceiver != null)
            telemetryReceiver.NetoTelemetryReceived += HandleNetoTelemetry;

        if (handle == null)
        {
            Debug.LogError($"[NetoVisualDriver] '{gameObject.name}': Handle transform is not assigned.");
            enabled = false;
            return;
        }

        // Auto-measure rope base scale if not manually set
        if (ropeVisual != null)
        {
            if (ropeBaseScaleY <= 0f)
                ropeBaseScaleY = ropeVisual.localScale.y;

            _ropeBaseLength = ropeBaseScaleY; // store for proportional scaling
        }
    }

    void Update()
    {
        if (publisher == null || handle == null) return;

        if (Time.time - _lastTelemetryTime < telemetryTimeout)
            ApplyTelemetryPosition();
        else
            ApplySimulatedPosition();
    }

    void OnDestroy()
    {
        if (telemetryReceiver != null)
            telemetryReceiver.NetoTelemetryReceived -= HandleNetoTelemetry;
    }

    void ApplyTelemetryPosition()
    {
        float range = _telemetryMaxMm - _telemetryMinMm;
        if (range <= 0f) return;

        float t = Mathf.Clamp01((_telemetryPositionMm - _telemetryMinMm) / range);
        float targetY = Mathf.Lerp(handleTopY, handleBottomY, t);

        Vector3 pos = handle.localPosition;
        pos.y = Mathf.Lerp(pos.y, targetY, Time.deltaTime * smoothSpeed);
        handle.localPosition = pos;
    }

    void ApplySimulatedPosition()
    {
        int speed = publisher.CurrentMotorSpeedUnits;
        float targetY = handle.localPosition.y;

        if (speed > 90)
            targetY = handleTopY;
        else if (speed < 90)
            targetY = handleBottomY;

        Vector3 pos = handle.localPosition;
        pos.y = Mathf.Lerp(pos.y, targetY, Time.deltaTime * smoothSpeed);
        handle.localPosition = pos;
    }

    // ── Telemetry ───────────────────────────────────────────────────────

    void HandleNetoTelemetry(HubTelemetryReceiver.NetoTelemetry telemetry)
    {
        if (publisher == null) return;
        if ((int)publisher.robotId != telemetry.RobotId) return;

        latestMicLevel = Mathf.Clamp(telemetry.MicLevel, 0, 255);
        ApplyMicLevelVisuals(latestMicLevel);

        if (telemetry.PositionMm.HasValue && telemetry.PositionMinMm.HasValue && telemetry.PositionMaxMm.HasValue)
        {
            _telemetryPositionMm = telemetry.PositionMm.Value;
            _telemetryMinMm = telemetry.PositionMinMm.Value;
            _telemetryMaxMm = telemetry.PositionMaxMm.Value;
            _lastTelemetryTime = Time.time;
        }
    }


    void ApplyMicLevelVisuals(int micLevel)
    {
        // Placeholder for future mic-driven visuals.
        // micLevel is the raw mic intensity from the Neto telemetry.
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (handle == null) return;

        Vector3 top = transform.TransformPoint(
            handle.localPosition.x, handleTopY, handle.localPosition.z);
        Vector3 bottom = transform.TransformPoint(
            handle.localPosition.x, handleBottomY, handle.localPosition.z);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(top, 0.05f);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(bottom, 0.05f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(top, bottom);
        Gizmos.DrawWireSphere(handle.position, 0.05f);
    }
#endif
}