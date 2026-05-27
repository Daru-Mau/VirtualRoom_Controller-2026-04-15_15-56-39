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

using UnityEngine;
public class MyceliumBuilder : MonoBehaviour
{
    [Header("Sphere Reference")]
    [SerializeField] private BreathingSphere breathingSphere;
    [Header("Capsule Settings")]
    [SerializeField] private int capsuleCount = 500;
    [SerializeField] private float segmentLength = 0.4f;
    [SerializeField] private float segmentRadius = 0.04f;
    [Header("Auto-Build")]
    [SerializeField] private bool buildOnStart;
    private MyceliumRenderer _renderer;
    private void Start()
    {
        if (buildOnStart) Build();
    }
    [ContextMenu("Build Mycelium")]
    public void Build()
    {
        if (breathingSphere == null)
            breathingSphere = FindObjectOfType<BreathingSphere>();
        if (_renderer == null)
            _renderer = GetComponent<MyceliumRenderer>();
        if (_renderer == null)
        {
            Debug.LogError("[MyceliumBuilder] No MyceliumRenderer on this GameObject.");
            return;
        }
        _renderer.Clear();
        Vector3 center = breathingSphere.transform.position;
        float worldRadius = breathingSphere.transform.lossyScale.x * 0.5f;
        float r = worldRadius * 0.98f;
        _renderer.OnBuilt(breathingSphere, worldRadius);
        _renderer.Allocate(capsuleCount);
        float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;
        for (int i = 0; i < capsuleCount; i++)
        {
            float theta = Mathf.Acos(1f - 2f * (i + 0.5f) / capsuleCount);
            float phi = 2f * Mathf.PI * i / goldenRatio;
            Vector3 dir = new Vector3(
                Mathf.Sin(theta) * Mathf.Cos(phi),
                Mathf.Sin(theta) * Mathf.Sin(phi),
                Mathf.Cos(theta)
            ).normalized;
            Vector3 pos = center + dir * r;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);
            Vector3 scale = new Vector3(
                segmentRadius * 2f, segmentLength * 0.5f, segmentRadius * 2f);
            _renderer.SetInstance(i, pos, rot, scale, dir);
        }
        Debug.Log($"[MyceliumBuilder] Built {capsuleCount} capsules on sphere surface (r={worldRadius:F1}).");
    }
    [ContextMenu("Clear Mycelium")]
    public void Clear()
    {
        if (_renderer == null) _renderer = GetComponent<MyceliumRenderer>();
        _renderer?.Clear();
    }
    [ContextMenu("Rebuild Mycelium")]
    public void Rebuild() { Clear(); Build(); }
}
