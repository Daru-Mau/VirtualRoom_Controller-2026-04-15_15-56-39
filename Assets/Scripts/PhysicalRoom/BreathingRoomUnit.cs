using UnityEngine;

/// <summary>
/// One structural segment of the breathing room.
///
/// The BreathingRoomController calls SetTarget() every frame with a signed
/// normalised value:
///   +1 = full inhale  → segment pushes outward (away from room centre)
///   -1 = full exhale  → segment pulls inward
///    0 = hold / rest
///
/// Each unit has a unique Perlin noise seed so the surface ripples
/// organically — no two segments move identically.
/// </summary>
public class BreathingRoomUnit : MonoBehaviour
{
    [Header("Displacement")]
    [Tooltip("Maximum distance (metres) a segment moves from its rest position at full breath.")]
    [SerializeField] private float maxDisplacement = 0.12f;

    [Tooltip("How quickly the segment settles towards its target position. " +
             "Higher = snappier. Lower = sluggish / lagging.")]
    [SerializeField] private float followSpeed = 7f;

    [Header("Organic Noise")]
    [Tooltip("How much ambient Perlin noise blends in. " +
             "Scales with breath intensity so the room is quiet at rest.")]
    [SerializeField, Range(0f, 1f)] private float noiseInfluence = 0.25f;

    [Tooltip("Speed at which the noise field scrolls over time.")]
    [SerializeField] private float noiseScrollSpeed = 0.2f;

    // ── Set by BreathingRoomController at initialisation ──────────────────

    /// <summary>World-space rest position, snapshotted before any breathing starts.</summary>
    internal Vector3 RestWorldPosition;

    /// <summary>World-space unit vector pointing away from the room centre.</summary>
    internal Vector3 OutwardNormal;

    /// <summary>Unique random seed for Perlin noise so each unit differs.</summary>
    internal float NoiseSeed;

    // ── Private state ─────────────────────────────────────────────────────

    private float _target; // set by controller each frame, range ≈ [-1, +1]

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called every frame by BreathingRoomController.
    /// </summary>
    /// <param name="target">
    /// Signed normalised displacement:
    ///   +1 = maximum outward (inhale expanded),
    ///   -1 = maximum inward  (exhale contracted),
    ///    0 = rest.
    /// </param>
    public void SetTarget(float target) => _target = target;

    private void Update()
    {
        // Perlin noise: amplitude scales with breath so rest state stays quiet.
        float noise =
            (Mathf.PerlinNoise(
                NoiseSeed        + Time.unscaledTime * noiseScrollSpeed,
                NoiseSeed * 2.3f + Time.unscaledTime * noiseScrollSpeed * 0.6f
            ) * 2f - 1f)            // remap 0–1 → -1..+1
            * noiseInfluence
            * Mathf.Abs(_target);   // quiet at rest, louder at peak breath

        float   displacement = (_target + noise) * maxDisplacement;
        Vector3 targetPos    = RestWorldPosition + OutwardNormal * displacement;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPos,
            Time.deltaTime * followSpeed
        );
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(RestWorldPosition, OutwardNormal * maxDisplacement);
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawWireSphere(RestWorldPosition, 0.02f);
    }
#endif
}
