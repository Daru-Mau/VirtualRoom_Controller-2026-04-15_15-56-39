using UnityEngine;

public class RobotSelector : MonoBehaviour
{
    public PoseDetector poseDetector;

    [Header("Robot Lists")]
    public NetoCommandPublisher[] netoRobots;
    public SauronCommandPublisher[] sauronRobots;

    [Header("Deathtrap")]
    public DeathTrapCommandPublisher deathtrapRobot;

    [Header("Selection")]
    [Tooltip("Max distance along the ray to consider a Neto")]
    public float selectionMaxDistance = 10f;
    [Tooltip("Max distance from the ray to count as aligned (geometric mode)")]
    public float selectionRayRadius = 0.35f;
    [Tooltip("Use physics sphere cast to select Netos by collider")]
    public bool usePhysicsSelection = true;
    public LayerMask selectionMask = ~0;

    // ── In-VR Debug Ray ─────────────────────────────────────────────────────
    [Header("In-VR Debug Ray")]
    [Tooltip("Show a LineRenderer ray from the active hand in the VR headset")]
    public bool showDebugRay = false;

    [Tooltip("Radius of the sphere drawn at the ray tip / hit point")]
    public float debugTipRadius = 0.06f;

    [ColorUsage(true, true)]
    public Color debugRayColorIdle     = new Color(0f,  0.8f, 1f,  1f);   // cyan
    [ColorUsage(true, true)]
    public Color debugRayColorSelected = new Color(0f,  1f,   0.3f, 1f);  // green
    [ColorUsage(true, true)]
    public Color debugRayColorMiss     = new Color(1f,  0.3f, 0f,  1f);   // orange

    [Tooltip("Width of the debug ray tube")]
    public float debugRayWidth = 0.008f;

    // ── Scene-view gizmos (still useful in editor) ───────────────────────────
    [Header("Scene-View Gizmos")]
    public bool debugDrawGizmos = false;

    // ─────────────────────────────────────────────────────────────────────────

    public NetoCommandPublisher   ActiveNeto   { get; private set; }
    public SauronCommandPublisher ActiveSauron { get; private set; }

    private NetoCommandPublisher   _prevNeto;
    private SauronCommandPublisher _prevSauron;

    // Debug ray state
    private Vector3 _debugRayOrigin;
    private Vector3 _debugRayDir;
    private Vector3 _debugTipPoint;
    private bool    _debugHasHit;

    // LineRenderer components (created at runtime)
    private LineRenderer _rayLine;
    private GameObject   _tipSphere;
    private MeshRenderer _tipRenderer;
    private MaterialPropertyBlock _tipMpb;

    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        BuildDebugVisuals();
        _tipMpb = new MaterialPropertyBlock();
    }

    void Update()
    {
        switch (poseDetector.CurrentPose)
        {
            case ControlPose.DirectionalLeft:
                SelectNetoInDirection(poseDetector.leftController.position,
                                      poseDetector.leftController.forward);
                ClearSauron();
                break;

            case ControlPose.DirectionalRight:
                SelectNetoInDirection(poseDetector.rightController.position,
                                      poseDetector.rightController.forward);
                ClearSauron();
                break;

            case ControlPose.ChestMode:
                ClearNeto();
                SetAllSauronsSelected();
                break;

            case ControlPose.TwoHandGesture:
                ClearNeto();
                ClearSauron();
                break;

            case ControlPose.None:
                ClearNeto();
                ClearSauron();
                break;
        }

        // Refresh indicators when selection changes
        if (ActiveNeto != _prevNeto || ActiveSauron != _prevSauron)
        {
            RefreshIndicators();
            _prevNeto   = ActiveNeto;
            _prevSauron = ActiveSauron;
        }

        RefreshDebugVisuals();
    }

    // ── Selection Logic ──────────────────────────────────────────────────────

    void SelectNetoInDirection(Vector3 origin, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;

        _debugRayOrigin = origin;
        _debugRayDir    = direction.normalized;
        _debugHasHit    = false;

        if (usePhysicsSelection)
        {
            SelectNetoWithSphereCast(origin, direction.normalized);
            return;
        }

        // ── Geometric 3-D mode (no flat projection) ───────────────────────
        NetoCommandPublisher best = null;
        float bestAlongRay = float.PositiveInfinity;
        float bestOffRay   = float.PositiveInfinity;
        Vector3 dir = direction.normalized;

        foreach (var neto in netoRobots)
        {
            if (neto == null) continue;
            Vector3 toRobot = neto.transform.position - origin;

            // Project robot onto ray
            float alongRay = Vector3.Dot(dir, toRobot);
            if (alongRay <= 0f || alongRay > selectionMaxDistance) continue;

            // Closest point on ray to robot
            Vector3 closestOnRay = origin + dir * alongRay;
            float   offRay       = Vector3.Distance(closestOnRay, neto.transform.position);
            if (offRay > selectionRayRadius) continue;

            // Prefer whichever is first along the ray; break ties by off-ray dist
            if (alongRay < bestAlongRay ||
                (Mathf.Approximately(alongRay, bestAlongRay) && offRay < bestOffRay))
            {
                bestAlongRay = alongRay;
                bestOffRay   = offRay;
                best         = neto;
                _debugTipPoint = closestOnRay;
                _debugHasHit   = true;
            }
        }

        // If no robot hit, put tip at max range for visualisation
        if (!_debugHasHit)
            _debugTipPoint = origin + dir * selectionMaxDistance;

        if (best != ActiveNeto)
        {
            if (ActiveNeto != null) ActiveNeto.ResetToDefaults();
            ActiveNeto = best;
        }
    }

    void SelectNetoWithSphereCast(Vector3 origin, Vector3 direction)
    {
        _debugHasHit   = false;
        _debugTipPoint = origin + direction * selectionMaxDistance;

        var hits = Physics.SphereCastAll(origin, selectionRayRadius, direction,
                                         selectionMaxDistance, selectionMask,
                                         QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
        {
            if (ActiveNeto != null) { ActiveNeto.ResetToDefaults(); ActiveNeto = null; }
            return;
        }

        float                bestDistance = float.PositiveInfinity;
        NetoCommandPublisher best         = null;

        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            var neto = hit.collider.GetComponentInParent<NetoCommandPublisher>();
            if (neto == null) continue;

            if (hit.distance < bestDistance)
            {
                bestDistance   = hit.distance;
                best           = neto;
                _debugTipPoint = hit.point;
                _debugHasHit   = true;
            }
        }

        // If sphere-cast returned hits but none had a NetoCommandPublisher, keep tip
        if (!_debugHasHit)
            _debugTipPoint = origin + direction * selectionMaxDistance;

        if (best != ActiveNeto)
        {
            if (ActiveNeto != null) ActiveNeto.ResetToDefaults();
            ActiveNeto = best;
        }
    }

    // ── State Helpers ────────────────────────────────────────────────────────

    void ClearNeto()
    {
        if (ActiveNeto != null) { ActiveNeto.ResetToDefaults(); ActiveNeto = null; }
    }

    void ClearSauron()
    {
        if (ActiveSauron != null) { ActiveSauron.CenterServos(); ActiveSauron = null; }
    }

    void SetAllSauronsSelected()
    {
        ActiveSauron = null; // chest mode drives Saurons directly
    }

    // ── Indicator Management ─────────────────────────────────────────────────

    void RefreshIndicators()
    {
        foreach (var neto in netoRobots)
        {
            if (neto == null) continue;
            var ind = neto.GetComponent<RobotVisualIndicator>();
            if (ind == null) continue;
            ind.SetState(neto == ActiveNeto
                ? RobotVisualIndicator.IndicatorState.Selected
                : RobotVisualIndicator.IndicatorState.Idle);
        }

        foreach (var sauron in sauronRobots)
        {
            if (sauron == null) continue;
            var ind = sauron.GetComponent<RobotVisualIndicator>();
            if (ind == null) continue;
            bool chestMode  = poseDetector.CurrentPose == ControlPose.ChestMode;
            bool isSelected = (sauron == ActiveSauron) || chestMode;
            ind.SetState(isSelected
                ? RobotVisualIndicator.IndicatorState.Selected
                : RobotVisualIndicator.IndicatorState.Idle);
        }
    }

    // ── In-VR Debug Visuals ──────────────────────────────────────────────────

    void BuildDebugVisuals()
    {
        // ── LineRenderer ──
        var lineGO = new GameObject("DebugRayLine");
        lineGO.transform.SetParent(transform, false);
        _rayLine                  = lineGO.AddComponent<LineRenderer>();
        _rayLine.positionCount    = 2;
        _rayLine.startWidth       = debugRayWidth;
        _rayLine.endWidth         = debugRayWidth;
        _rayLine.useWorldSpace    = true;
        _rayLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _rayLine.receiveShadows   = false;

        // Use a simple URP unlit material so it renders in VR without lighting
        var rayMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        rayMat.EnableKeyword("_EMISSION");
        _rayLine.material = rayMat;

        // ── Tip sphere ──
        _tipSphere            = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _tipSphere.name       = "DebugRayTip";
        Destroy(_tipSphere.GetComponent<Collider>());
        _tipSphere.transform.SetParent(transform, false);
        _tipSphere.transform.localScale = Vector3.one * debugTipRadius * 2f;

        _tipRenderer = _tipSphere.GetComponent<MeshRenderer>();
        _tipRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _tipRenderer.receiveShadows    = false;

        var tipMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        tipMat.EnableKeyword("_EMISSION");
        _tipRenderer.sharedMaterial = tipMat;

        // Start hidden
        _rayLine.enabled    = false;
        _tipSphere.SetActive(false);
    }

    void RefreshDebugVisuals()
    {
        bool directional = poseDetector.CurrentPose == ControlPose.DirectionalLeft ||
                           poseDetector.CurrentPose == ControlPose.DirectionalRight;

        bool show = showDebugRay && directional;

        _rayLine.enabled = show;
        _tipSphere.SetActive(show);

        if (!show) return;

        // Pick colour based on whether we have a target
        Color col = ActiveNeto != null ? debugRayColorSelected
                  : _debugHasHit       ? debugRayColorIdle
                  :                      debugRayColorMiss;

        // Update LineRenderer
        _rayLine.SetPosition(0, _debugRayOrigin);
        _rayLine.SetPosition(1, _debugTipPoint);

        var mat = _rayLine.material;
        mat.SetColor("_BaseColor",     col);
        mat.SetColor("_EmissionColor", col * 3f);

        // Update tip sphere
        _tipSphere.transform.position   = _debugTipPoint;
        _tipSphere.transform.localScale = Vector3.one * debugTipRadius * 2f;

        _tipRenderer.GetPropertyBlock(_tipMpb);
        _tipMpb.SetColor("_BaseColor",     col);
        _tipMpb.SetColor("_EmissionColor", col * 3f);
        _tipRenderer.SetPropertyBlock(_tipMpb);
    }

    // ── Scene-View Gizmos ────────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (!debugDrawGizmos || poseDetector == null) return;

        bool directional = poseDetector.CurrentPose == ControlPose.DirectionalLeft ||
                           poseDetector.CurrentPose == ControlPose.DirectionalRight;
        if (!directional) return;

        Gizmos.color = ActiveNeto != null ? Color.green : Color.cyan;
        Gizmos.DrawRay(_debugRayOrigin, _debugRayDir * selectionMaxDistance);

        if (_debugHasHit)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(_debugTipPoint, debugTipRadius);
        }

        // Draw each robot's off-ray distance circle for tuning selectionRayRadius
        if (netoRobots == null) return;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
        foreach (var neto in netoRobots)
        {
            if (neto == null) continue;
            Gizmos.DrawWireSphere(neto.transform.position, selectionRayRadius);
        }
    }
}