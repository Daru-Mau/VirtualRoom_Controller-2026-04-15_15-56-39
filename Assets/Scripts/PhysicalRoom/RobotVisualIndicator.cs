using UnityEngine;

/// <summary>
/// Attach to each robot GameObject (Neto_X_Rig, Sauron_X).
/// RobotSelector calls SetState() to switch between visual states.
/// Creates its own indicator ring at runtime — no prefab needed.
/// </summary>
public class RobotVisualIndicator : MonoBehaviour
{
    public enum IndicatorState { Idle, Hovered, Selected }

    [Header("Ring Appearance")]
    [Tooltip("Radius of the selection ring in metres")]
    public float ringRadius = 0.25f;
    [Tooltip("How thick the ring tube is")]
    public float ringThickness = 0.03f;
    [Tooltip("Height offset of ring above the robot's local origin")]
    public float heightOffset = 0.1f;

    [Header("Colours")]
    public Color idleColor = new Color(0.3f, 0.3f, 0.3f, 0f);   // invisible
    public Color hoveredColor = new Color(1.0f, 0.8f, 0.0f, 1f);   // yellow
    public Color selectedColor = new Color(0.0f, 1.0f, 0.4f, 1f);   // green

    [Header("Pulse (selected only)")]
    public float pulseSpeed = 2.5f;
    public float pulseAmplitude = 0.15f; // fraction of base scale

    // ── internals ──
    private GameObject _ring;
    private MeshRenderer _renderer;
    private MaterialPropertyBlock _mpb;
    private IndicatorState _state = IndicatorState.Idle;
    private float _baseScale;

    // ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        _ring = BuildRing();
        _mpb = new MaterialPropertyBlock();
        _baseScale = ringRadius * 2f;
        Apply(IndicatorState.Idle);
    }

    void Update()
    {
        if (_state == IndicatorState.Selected)
        {
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmplitude;
            _ring.transform.localScale = Vector3.one * (_baseScale * pulse);
        }
    }

    // ── Public API called by RobotSelector ───────────────────────────

    public void SetState(IndicatorState state)
    {
        if (_state == state) return;
        _state = state;
        Apply(state);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    void Apply(IndicatorState state)
    {
        if (_renderer == null) return;

        Color c = state switch
        {
            IndicatorState.Hovered => hoveredColor,
            IndicatorState.Selected => selectedColor,
            _ => idleColor,
        };

        _renderer.enabled = (state != IndicatorState.Idle);
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_EmissionColor", c * 2f);
        _mpb.SetColor("_BaseColor", c);
        _renderer.SetPropertyBlock(_mpb);

        // Reset scale when not selected
        if (state != IndicatorState.Selected)
            _ring.transform.localScale = Vector3.one * _baseScale;
    }

    // Procedurally builds a torus-like ring from a cylinder scaled flat.
    // Simple, no mesh import required.
    GameObject BuildRing()
    {
        var go = new GameObject("SelectionRing");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * heightOffset;
        go.transform.localRotation = Quaternion.identity;

        // Use a flat cylinder as the ring base — good enough for a floor indicator
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");

        _renderer = go.AddComponent<MeshRenderer>();

        // Create an unlit emissive material at runtime
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.EnableKeyword("_EMISSION");
        mat.SetFloat("_Surface", 0); // opaque
        _renderer.sharedMaterial = mat;

        // Scale: very flat cylinder becomes a disc, then we'll use thickness
        float h = ringThickness;
        go.transform.localScale = new Vector3(ringRadius * 2f, h * 0.5f, ringRadius * 2f);

        _baseScale = ringRadius * 2f;

        return go;
    }
}