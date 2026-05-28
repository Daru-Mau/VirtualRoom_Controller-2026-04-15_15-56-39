using UnityEngine;

/// <summary>
/// Drives the visual up/down movement of a Neto robot in the VR scene.
///
/// Attach to the Neto_X_Rig GameObject (same one that has NetoCommandPublisher).
///
/// How it works:
///   Motor unit   0 → lowest position (bottom).
///   Motor unit 180 → highest position (top, at the wall).
///
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

    [Header("Movement Bounds (local Y offset from start position)")]
    [Tooltip("How far the handle can move UP from its starting position (metres).")]
    [SerializeField] private float maxUpOffset = 1.5f;
    [Tooltip("How far the handle can move DOWN from its starting position (metres).")]
    [SerializeField] private float maxDownOffset = 1.5f;

    [Header("Smoothing")]
    [Tooltip("How fast the handle lerps toward the target position (higher = snappier).")]
    [SerializeField] private float smoothSpeed = 5f;

    [Header("Rope Visual (optional)")]
    [Tooltip("Original local Y scale of the RopeVisual at the start position. " +
             "Leave at 0 to auto-measure on Start.")]
    [SerializeField] private float ropeBaseScaleY = 0f;

    [Header("Telemetry (read only)")]
    [SerializeField, Range(0, 255)] private int latestMicLevel;

    // ── Runtime ──────────────────────────────────────────────────────────
    private float _startLocalY;
    private float _ropeBaseLength;

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

        _startLocalY = handle.localPosition.y;

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

        MoveHandle();
    }

    void OnDestroy()
    {
        if (telemetryReceiver != null)
            telemetryReceiver.NetoTelemetryReceived -= HandleNetoTelemetry;
    }

    // ── Handle Movement ───────────────────────────────────────────────────

    void MoveHandle()
    {
        int speedUnits = publisher.CurrentMotorSpeedUnits;

        // Map 0 (bottom) → _startLocalY - maxDownOffset (lowest)
        // Map 180 (top)  → _startLocalY + maxUpOffset (highest)
        float t = speedUnits / 180f;
        float targetY = (_startLocalY - maxDownOffset) + t * (maxDownOffset + maxUpOffset);

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
        // Draw movement range in the editor
        Vector3 worldBase = transform.TransformPoint(new Vector3(
            handle.localPosition.x,
            _startLocalY,
            handle.localPosition.z));

        Gizmos.color = Color.green;
        Gizmos.DrawLine(worldBase, worldBase + transform.up * maxUpOffset);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(worldBase, worldBase - transform.up * maxDownOffset);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(handle.position, 0.05f);
    }
#endif
}