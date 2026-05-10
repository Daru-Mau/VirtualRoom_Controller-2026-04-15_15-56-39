using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates the mycelium room structure: organic filament strands that grow
/// between robot nodes, plus ambient scatter units filling the bubble volume.
///
/// Replaces BreathingRoomBuilder. BreathingRoomUnit and BreathingRoomController
/// are unchanged — only the geometry generation changes.
///
/// ── Concept ──────────────────────────────────────────────────────────────
/// Each pair of robot nodes is connected by several wandering filament strands.
/// Strands deviate from a straight line (controlled by strandWander) so they
/// read as biological rather than architectural. Units along each strand are
/// small capsules, oriented along the strand direction.
///
/// Ambient scatter units fill the bubble volume between strands — these are
/// what give the interior its volumetric density and react to the breath wave
/// propagating from the room centre outward.
///
/// ── Workflow ─────────────────────────────────────────────────────────────
/// 1. Add this component to the same GameObject as BreathingRoomController.
/// 2. Drag robot transforms into the Nodes list.
/// 3. Right-click component header → "Build Mycelium".
/// 4. Press Play — controller auto-gathers all generated units.
/// </summary>
public class MyceliumBuilder : MonoBehaviour
{
    [Header("Nodes")]
    [Tooltip("One entry per robot. Filament strands grow between every pair. " +
             "Order does not matter.")]
    [SerializeField] private List<Transform> nodes = new();

    [Header("Filament Strands")]
    [Tooltip("Number of independent strands between each node pair. " +
             "More strands = denser web. 6–12 is a good starting range.")]
    [SerializeField] private int strandsPerConnection = 8;

    [Tooltip("Number of segment units placed along each strand.")]
    [SerializeField] private int segmentsPerStrand = 14;

    [Tooltip("How far strand paths wander off the straight line between nodes. " +
             "0 = straight. 0.5–1.0 = noticeably organic.")]
    [SerializeField] private float strandWander = 0.5f;

    [Tooltip("Optional: strands avoid coming too close to the room centre " +
             "(so the interior stays open). Set 0 to disable.")]
    [SerializeField] private float centreClearanceRadius = 0.4f;

    [Header("Ambient Volume Scatter")]
    [Tooltip("Extra units scattered randomly within the bubble — gives interior depth. " +
             "These also respond to the breath wave.")]
    [SerializeField] private int ambientUnitCount = 300;

    [Tooltip("Radius of the bubble. Match this to your existing bubble mesh radius.")]
    [SerializeField] private float bubbleRadius = 3f;

    [Tooltip("Ambient units are denser toward the outer shell and sparser at centre. " +
             "0 = uniform distribution. 1 = fully shell-biased.")]
    [SerializeField, Range(0f, 1f)] private float shellBias = 0.6f;

    [Header("Segment Appearance")]
    [Tooltip("Length of each capsule segment along the filament.")]
    [SerializeField] private float segmentLength = 0.14f;

    [Tooltip("Radius of each capsule segment. Keep thin (0.005–0.015) for filament look.")]
    [SerializeField] private float segmentRadius = 0.007f;

    [Tooltip("Angular jitter applied to each segment's rotation (degrees). " +
             "Breaks mechanical regularity.")]
    [SerializeField] private float segmentRotationJitter = 12f;

    [Tooltip("Material for generated capsules. Use a semi-transparent, emissive, " +
             "or subsurface-scatter material for best results.")]
    [SerializeField] private Material filamentMaterial;

    [Tooltip("Optional custom prefab per segment. Must be oriented so local +Y " +
             "points along the filament. Leave null for default capsule.")]
    [SerializeField] private GameObject segmentPrefab;

    [Header("Scene Organisation")]
    [Tooltip("Generated segments are parented here. " +
             "Should match BreathingRoomController's 'Units Root'.")]
    [SerializeField] private Transform unitsRoot;

    // ─────────────────────────────────────────────────────────────────────

    [ContextMenu("Build Mycelium")]
    public void Build()
    {
        Transform root   = unitsRoot != null ? unitsRoot : transform;
        Vector3   centre = transform.position;

        // Clear previously generated children
        for (int i = root.childCount - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            DestroyImmediate(root.GetChild(i).gameObject);
#else
            Destroy(root.GetChild(i).gameObject);
#endif
        }

        int total = 0;

        // ── Filament strands between every node pair ───────────────────────
        for (int a = 0; a < nodes.Count; a++)
        {
            for (int b = a + 1; b < nodes.Count; b++)
            {
                if (nodes[a] == null || nodes[b] == null) continue;

                total += BuildConnection(
                    nodes[a].position,
                    nodes[b].position,
                    centre,
                    root,
                    $"Fil_{a}to{b}"
                );
            }
        }

        // ── Ambient scatter ────────────────────────────────────────────────
        for (int i = 0; i < ambientUnitCount; i++)
        {
            Vector3 pos = SampleBubblePoint(centre);
            // Ambient units get a fully random orientation — they read as
            // floating spores rather than directed filaments.
            Quaternion rot = Random.rotation;

            GameObject go = CreateSegment(pos, rot, root);
            go.name = $"Ambient_{i:D4}";
            EnsureUnit(go);
            total++;
        }

        Debug.Log($"[MyceliumBuilder] Built {total} units " +
                  $"({_units_filament} filament, {ambientUnitCount} ambient).");

        // Notify controller if present on same GameObject
        if (TryGetComponent<BreathingRoomController>(out var ctrl))
            ctrl.GatherUnits();
    }

    // ── Connection builder ────────────────────────────────────────────────

    private int _units_filament;

    private int BuildConnection(Vector3 from, Vector3 to,
                                Vector3 centre, Transform root, string label)
    {
        _units_filament = 0;
        int count = 0;

        for (int s = 0; s < strandsPerConnection; s++)
        {
            // Generate a wandering path between from → to.
            // Control points are offset perpendicular to the straight line.
            Vector3[] pts = GenerateStrandPath(from, to, centre);

            for (int p = 0; p < pts.Length - 1; p++)
            {
                Vector3 dir = pts[p + 1] - pts[p];
                if (dir.sqrMagnitude < 0.0001f) continue;

                Vector3    pos = (pts[p] + pts[p + 1]) * 0.5f;
                Quaternion rot = Quaternion.LookRotation(dir.normalized)
                                 * Quaternion.Euler(90f, 0f, 0f); // capsule Y along dir

                // Small angular jitter for organic feel
                rot *= Quaternion.Euler(
                    Random.Range(-segmentRotationJitter, segmentRotationJitter),
                    Random.Range(-segmentRotationJitter, segmentRotationJitter),
                    Random.Range(-180f, 180f)   // free spin around filament axis
                );

                GameObject go = CreateSegment(pos, rot, root);
                go.name = $"{label}_S{s:D2}_P{p:D2}";
                EnsureUnit(go);
                count++;
            }
        }

        _units_filament += count;
        return count;
    }

    /// <summary>
    /// Generates a curved path between two points using random interior
    /// waypoints offset perpendicular to the chord direction.
    /// </summary>
    private Vector3[] GenerateStrandPath(Vector3 from, Vector3 to, Vector3 centre)
    {
        int n = segmentsPerStrand + 1;
        Vector3[] pts = new Vector3[n];

        pts[0]     = from;
        pts[n - 1] = to;

        // Perpendicular plane for wander offsets
        Vector3 chord = (to - from).normalized;
        Vector3 perp1 = Vector3.Cross(chord, Vector3.up).normalized;
        if (perp1.sqrMagnitude < 0.01f)
            perp1 = Vector3.Cross(chord, Vector3.right).normalized;
        Vector3 perp2 = Vector3.Cross(chord, perp1).normalized;

        for (int i = 1; i < n - 1; i++)
        {
            float   t        = (float)i / (n - 1);
            float   envelope = Mathf.Sin(t * Mathf.PI); // zero at endpoints, peak at mid
            Vector3 straight = Vector3.Lerp(from, to, t);

            Vector3 offset = (perp1 * Random.Range(-1f, 1f)
                            + perp2 * Random.Range(-1f, 1f))
                           * strandWander * envelope;

            Vector3 candidate = straight + offset;

            // Push away from centre if inside clearance radius
            Vector3 toCentre = candidate - centre;
            if (centreClearanceRadius > 0f && toCentre.magnitude < centreClearanceRadius)
                candidate = centre + toCentre.normalized * centreClearanceRadius;

            pts[i] = candidate;
        }

        return pts;
    }

    // ── Ambient point sampling ─────────────────────────────────────────────

    /// <summary>
    /// Samples a point inside the bubble, biased toward the outer shell
    /// when shellBias > 0. This makes the structure feel hollow in the
    /// centre (where the user stands) and denser at the periphery.
    /// </summary>
    private Vector3 SampleBubblePoint(Vector3 centre)
    {
        Vector3 dir    = Random.onUnitSphere;
        float   tUnif  = Random.value;                           // 0–1 uniform
        float   tBiased = Mathf.Lerp(tUnif, Mathf.Sqrt(tUnif), shellBias); // push toward 1 (shell)
        float   radius  = tBiased * bubbleRadius;

        return centre + dir * radius;
    }

    // ── Segment creation ──────────────────────────────────────────────────

    private GameObject CreateSegment(Vector3 position, Quaternion rotation, Transform parent)
    {
        GameObject go;

        if (segmentPrefab != null)
        {
            go = Instantiate(segmentPrefab, position, rotation, parent);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.SetPositionAndRotation(position, rotation);
            // Capsule: X/Z = diameter, Y = half-height in Unity's capsule
            go.transform.localScale = new Vector3(
                segmentRadius * 2f,
                segmentLength * 0.5f,
                segmentRadius * 2f
            );

            if (filamentMaterial != null)
                go.GetComponent<Renderer>().sharedMaterial = filamentMaterial;

            // Strip collider — purely visual
            if (go.TryGetComponent<Collider>(out var col))
            {
#if UNITY_EDITOR
                DestroyImmediate(col);
#else
                Destroy(col);
#endif
            }
        }

        return go;
    }

    private static void EnsureUnit(GameObject go)
    {
        if (!go.TryGetComponent<BreathingRoomUnit>(out _))
            go.AddComponent<BreathingRoomUnit>();
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 centre = transform.position;

        // Bubble outline
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.15f);
        Gizmos.DrawWireSphere(centre, bubbleRadius);

        // Centre clearance
        if (centreClearanceRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.15f);
            Gizmos.DrawWireSphere(centre, centreClearanceRadius);
        }

        // Node connections preview
        Gizmos.color = new Color(0.6f, 1f, 0.8f, 0.4f);
        for (int a = 0; a < nodes.Count; a++)
        {
            if (nodes[a] == null) continue;
            Gizmos.DrawWireSphere(nodes[a].position, 0.08f);

            for (int b = a + 1; b < nodes.Count; b++)
            {
                if (nodes[b] == null) continue;
                Gizmos.DrawLine(nodes[a].position, nodes[b].position);
            }
        }
    }
#endif
}
