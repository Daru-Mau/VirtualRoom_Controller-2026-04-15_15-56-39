using UnityEngine;

/// <summary>
/// Attach to each robot GameObject (Neto_X_Rig, Sauron_X).
/// RobotSelector calls SetState() to switch between visual states.
/// Creates its own indicator disc at runtime — no prefab needed.
///
/// Quest 2 notes:
///   • Uses URP/Unlit so no shader-variant stripping issues.
///   • Uses renderer.material (per-instance clone) — no MaterialPropertyBlock
///     trickery needed. Color changes go straight on the material.
///   • Cylinder mesh is grabbed from Resources instead of
///     CreatePrimitive+Destroy, which is unreliable in builds.
/// </summary>
public class RobotVisualIndicator : MonoBehaviour
{
    public enum IndicatorState { Idle, Hovered, Selected }

    [Header("Ring Appearance")]
    [Tooltip("Radius of the selection disc in metres")]
    public float ringRadius = 0.25f;
    [Tooltip("Thickness (height) of the disc")]
    public float ringThickness = 0.03f;
    [Tooltip("Height offset above the robot's local origin")]
    public float heightOffset = 0.1f;

    [Header("Colours")]
    public Color idleColor = new Color(0.3f, 0.3f, 0.3f, 0f);  // invisible
    public Color hoveredColor = new Color(1.0f, 0.8f, 0.0f, 1f);  // yellow
    public Color selectedColor = new Color(0.0f, 1.0f, 0.4f, 1f);  // green

    [Header("Pulse (selected only)")]
    public float pulseSpeed = 2.5f;
    public float pulseAmplitude = 0.15f;

    // ── internals ──────────────────────────────────────────────────────────
    private GameObject _ring;
    private MeshRenderer _renderer;
    private Material _mat;          // per-instance material (no shared state)
    private IndicatorState _state = IndicatorState.Idle;
    private Vector3 _baseScale;

    // ── shader name: URP Unlit is always included in Quest builds ──────────
    private const string SHADER_NAME = "Universal Render Pipeline/Unlit";

    // ──────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _ring = BuildDisc();
        Apply(IndicatorState.Idle);
    }

    void Update()
    {
        if (_state == IndicatorState.Selected)
        {
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmplitude;
            _ring.transform.localScale = _baseScale * pulse;
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void SetState(IndicatorState state)
    {
        if (_state == state) return;
        _state = state;
        Apply(state);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    void Apply(IndicatorState state)
    {
        if (_renderer == null || _mat == null) return;

        bool visible = (state != IndicatorState.Idle);
        _renderer.enabled = visible;

        if (!visible)
        {
            // Reset scale so it doesn't pulse when re-shown
            _ring.transform.localScale = _baseScale;
            return;
        }

        Color c = state switch
        {
            IndicatorState.Hovered => hoveredColor,
            IndicatorState.Selected => selectedColor,
            _ => idleColor,
        };

        // With URP/Unlit, _BaseColor drives the visible colour directly.
        // Set it on the per-instance material — no property block needed.
        _mat.SetColor("_BaseColor", c);

        // Reset scale (pulse will take over next frame for Selected)
        _ring.transform.localScale = _baseScale;
    }

    /// <summary>
    /// Builds a flat disc using a procedural cylinder mesh baked at build time
    /// via MeshFilter, so we never need to call CreatePrimitive at runtime.
    /// </summary>
    GameObject BuildDisc()
    {
        var go = new GameObject("SelectionDisc");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * heightOffset;
        go.transform.localRotation = Quaternion.identity;

        // ── Mesh ──
        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = BuildCylinderMesh(24);   // 24-sided cylinder, built in code

        // ── Renderer ──
        _renderer = go.AddComponent<MeshRenderer>();
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;

        // ── Material ──
        // Use renderer.material to get a per-instance clone immediately.
        Shader shader = Shader.Find(SHADER_NAME);
        if (shader == null)
        {
            Debug.LogError($"[RobotVisualIndicator] Shader '{SHADER_NAME}' not found. " +
                           "Make sure it is in 'Always Included Shaders' or referenced by a material in your build.");
            // Fall back to the error shader so we at least see something pink
            shader = Shader.Find("Hidden/InternalErrorShader");
        }

        // Create the material, then grab the per-instance copy via .material
        _renderer.sharedMaterial = new Material(shader);
        _mat = _renderer.material;   // this clones it per-instance

        // Make it fully opaque (Surface = 0 in URP) so the colour is always solid
        _mat.SetFloat("_Surface", 0f);
        _mat.renderQueue = 2000; // opaque queue

        // ── Scale (flat disc) ──
        _baseScale = new Vector3(ringRadius * 2f, ringThickness * 0.5f, ringRadius * 2f);
        go.transform.localScale = _baseScale;

        return go;
    }

    /// <summary>
    /// Procedurally builds a simple closed cylinder mesh (top cap + bottom cap + sides).
    /// Avoids any dependency on Unity's built-in primitive meshes at runtime.
    /// </summary>
    static Mesh BuildCylinderMesh(int segments)
    {
        var mesh = new Mesh { name = "IndicatorDisc" };

        // We'll make just the top and bottom caps (no side wall needed for a flat disc)
        int vertCount = segments * 2 + 2; // top-center + ring*2 + bottom-center
        var verts = new Vector3[vertCount];
        var norms = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        float halfH = 0.5f;

        // Top cap center
        verts[0] = new Vector3(0f, halfH, 0f);
        norms[0] = Vector3.up;
        uvs[0] = new Vector2(0.5f, 0.5f);

        // Bottom cap center
        verts[1] = new Vector3(0f, -halfH, 0f);
        norms[1] = Vector3.down;
        uvs[1] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * 0.5f;   // radius 0.5 → scale drives size
            float z = Mathf.Sin(angle) * 0.5f;

            // Top ring
            int ti = 2 + i;
            verts[ti] = new Vector3(x, halfH, z);
            norms[ti] = Vector3.up;
            uvs[ti] = new Vector2(x + 0.5f, z + 0.5f);

            // Bottom ring
            int bi = 2 + segments + i;
            verts[bi] = new Vector3(x, -halfH, z);
            norms[bi] = Vector3.down;
            uvs[bi] = new Vector2(x + 0.5f, z + 0.5f);
        }

        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvs;

        // Triangles: top cap, bottom cap, side quads
        int triCount = segments * 3   // top cap  (1 tri × 3 indices each)
                     + segments * 3   // bottom cap
                     + segments * 6;  // sides (2 tris × 3 indices each)
        var tris = new int[triCount];
        int t = 0;

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int ti = 2 + i;
            int tNext = 2 + next;
            int bi = 2 + segments + i;
            int bNext = 2 + segments + next;

            // Top cap (CCW from above)
            tris[t++] = 0; tris[t++] = tNext; tris[t++] = ti;

            // Bottom cap (CW from above → CCW from below)
            tris[t++] = 1; tris[t++] = bi; tris[t++] = bNext;

            // Side quad (two triangles)
            tris[t++] = ti; tris[t++] = bNext; tris[t++] = bi;
            tris[t++] = ti; tris[t++] = tNext; tris[t++] = bNext;
        }

        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}