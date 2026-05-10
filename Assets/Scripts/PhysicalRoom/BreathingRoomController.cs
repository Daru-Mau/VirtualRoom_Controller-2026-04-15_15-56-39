using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Subscribes to HubTelemetryReceiver breather events and drives every
/// BreathingRoomUnit in the scene so the room breathes in sync with the user.
///
/// ── Ant-bridge wave behaviour ────────────────────────────────────────────
/// When the breath STATE changes (e.g. Hold → Inhale), the signal propagates
/// outward from the room centre at <waveSpeed> m/s. Units close to the centre
/// respond first; units near the walls respond later. This gives the impression
/// of a single living structure rather than every segment moving simultaneously.
///
/// The breath LEVEL (0–100) continuously scales the displacement magnitude.
/// A low-pass filter smooths the level so jitter in the raw signal doesn't
/// cause the whole room to shudder.
///
/// ── Scene setup ─────────────────────────────────────────────────────────
/// 1. Place this component on a manager GameObject (e.g. "BreathingRoom").
/// 2. Ensure HubTelemetryReceiver exists somewhere in the scene.
/// 3. Set roomCentre to the centre of your VR room.
/// 4. Set unitsRoot to the parent of all BreathingRoomUnit children
///    (or leave null to search descendants of this GameObject).
/// 5. Use BreathingRoomBuilder (same GameObject) to generate the units,
///    or add BreathingRoomUnit manually to your own meshes.
/// </summary>
public class BreathingRoomController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found in scene if left empty.")]
    [SerializeField] private HubTelemetryReceiver telemetryReceiver;

    [Tooltip("World-space centre of the room. Outward normals and wave distances " +
             "are measured from here. Usually the room floor-centre.")]
    [SerializeField] private Transform roomCentre;

    [Tooltip("Parent whose descendants are all BreathingRoomUnits. " +
             "Leave null to use this GameObject.")]
    [SerializeField] private Transform unitsRoot;

    [Tooltip("Filter telemetry to a specific Breather robot ID. " +
             "Set -1 to accept any robot.")]
    [SerializeField] private int targetRobotId = -1;

    [Header("Wave Propagation")]
    [Tooltip("Speed (m/s) at which the breath wave travels outward from the room centre. " +
             "Lower = slower ripple, more pronounced ant-bridge feel.")]
    [SerializeField] private float waveSpeed = 3f;

    [Tooltip("Time (seconds) for a segment to fully ramp up after the wave front reaches it.")]
    [SerializeField] private float waveRampDuration = 0.35f;

    [Header("Level Smoothing")]
    [Tooltip("Low-pass filter speed for raw telemetry level. " +
             "Lower = smoother but more lag; higher = more responsive but jitterier.")]
    [SerializeField] private float levelSmoothSpeed = 4f;

    [Header("Debug (read-only)")]
    [SerializeField, Range(-1f, 1f)] private float debugDirection;
    [SerializeField, Range(0f, 1f)]  private float debugSmoothedLevel;
    [SerializeField] private int debugUnitCount;

    // ── Runtime state ─────────────────────────────────────────────────────

    private readonly List<(BreathingRoomUnit unit, float dist)> _units = new();

    private float _directionSign;   // +1 inhale, -1 exhale, 0 hold
    private float _rawLevel;        // 0–1 from telemetry Level / 100
    private float _smoothedLevel;   // filtered each frame
    private float _stateChangeTime; // Time.unscaledTime when last state changed
    private int   _lastStateRaw = -1;

    // ─────────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (telemetryReceiver == null)
            telemetryReceiver = FindObjectOfType<HubTelemetryReceiver>();

        if (telemetryReceiver == null)
        {
            Debug.LogError("[BreathingRoomController] No HubTelemetryReceiver found in scene. " +
                           "Assign one in the Inspector or ensure it exists in the scene.");
            enabled = false;
            return;
        }

        telemetryReceiver.BreatherTelemetryReceived += OnBreatherTelemetry;
        GatherUnits();

        debugUnitCount = _units.Count;
    }

    private void OnDestroy()
    {
        if (telemetryReceiver != null)
            telemetryReceiver.BreatherTelemetryReceived -= OnBreatherTelemetry;
    }

    // ── Unit collection ───────────────────────────────────────────────────

    /// <summary>
    /// Scans for all BreathingRoomUnit descendants, bakes their rest position and
    /// outward normal, and assigns a unique noise seed to each.
    ///
    /// Call this again at runtime if you dynamically add/remove units.
    /// </summary>
    public void GatherUnits()
    {
        _units.Clear();

        Transform root   = unitsRoot != null ? unitsRoot : transform;
        Vector3   centre = roomCentre != null ? roomCentre.position : transform.position;

        foreach (var unit in root.GetComponentsInChildren<BreathingRoomUnit>(true))
        {
            Vector3 toUnit = unit.transform.position - centre;
            float   dist   = toUnit.magnitude;

            unit.RestWorldPosition = unit.transform.position;
            unit.OutwardNormal     = dist > 0.001f ? toUnit.normalized : Vector3.forward;
            unit.NoiseSeed         = Random.Range(0f, 99f);

            _units.Add((unit, dist));
        }

        Debug.Log($"[BreathingRoomController] Gathered {_units.Count} breathing units.");
    }

    // ── Telemetry (already dispatched to main thread by HubTelemetryReceiver) ──

    private void OnBreatherTelemetry(HubTelemetryReceiver.BreatherTelemetry t)
    {
        if (targetRobotId >= 0 && t.RobotId != targetRobotId)
            return;

        // Level is continuous — always update raw value.
        _rawLevel = t.Level / 100f;

        // State is discrete — only trigger a new wave on change.
        if (t.State == _lastStateRaw)
            return;

        _lastStateRaw    = t.State;
        _stateChangeTime = Time.unscaledTime;

        _directionSign = t.State switch
        {
            1 => +1f,   // Inhale → segments push outward
            2 => -1f,   // Exhale → segments pull inward
            _ =>  0f    // Hold   → segments drift back toward rest
        };
    }

    // ── Update loop ───────────────────────────────────────────────────────

    private void Update()
    {
        // Smooth the level so sensor noise doesn't cause shuddering.
        _smoothedLevel = Mathf.Lerp(
            _smoothedLevel, _rawLevel,
            Time.deltaTime * levelSmoothSpeed
        );

        float timeSinceChange = Time.unscaledTime - _stateChangeTime;

        foreach (var (unit, dist) in _units)
        {
            float envelope = WaveEnvelope(dist, timeSinceChange);
            unit.SetTarget(_directionSign * _smoothedLevel * envelope);
        }

        // Debug inspector
        debugDirection     = _directionSign * _smoothedLevel;
        debugSmoothedLevel = _smoothedLevel;
    }

    // ── Wave maths ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a 0→1 weight that rises smoothly as the wave front arrives at
    /// a unit located <dist> metres from the room centre.
    ///
    /// Units close to the centre receive the wave first (arrivalDelay ≈ 0).
    /// Distant units follow after (arrivalDelay = dist / waveSpeed seconds).
    /// </summary>
    private float WaveEnvelope(float dist, float timeSinceChange)
    {
        float arrivalDelay = dist / Mathf.Max(0.001f, waveSpeed);
        float elapsed      = timeSinceChange - arrivalDelay;
        return Mathf.SmoothStep(0f, 1f, elapsed / Mathf.Max(0.001f, waveRampDuration));
    }
}
