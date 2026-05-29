using UnityEngine;

public class RobotSelector : MonoBehaviour
{
    public PoseDetector poseDetector;

    [Header("Robot Lists")]
    public NetoCommandPublisher[] netoRobots;
    public SauronCommandPublisher[] sauronRobots;

    [Header("Deathtrap")]
    public DeathTrapCommandPublisher deathtrapRobot;

    [Header("Neto Body-Zone Mapping")]
    [Tooltip("Neto selected when the left shoulder zone is touched.")]
    public NetoCommandPublisher leftShoulderNeto;
    [Tooltip("Neto selected when the right shoulder zone is touched.")]
    public NetoCommandPublisher rightShoulderNeto;
    [Tooltip("Neto selected when the third zone is touched.")]
    public NetoCommandPublisher thirdZoneNeto;

    [Header("Sauron Selection")]
    [Tooltip("Max distance along the ray to consider a Sauron")]
    public float selectionMaxDistance = 30f;
    [Tooltip("Max distance from the ray to count as aligned")]
    public float selectionRayRadius = 0.5f;

    // ── In-VR Debug Ray ─────────────────────────────────────────────────────
    [Header("In-VR Debug Ray")]
    [Tooltip("Show a LineRenderer ray from the active hand in the VR headset")]
    public bool showDebugRay = false;

    [Tooltip("Radius of the sphere drawn at the ray tip / hit point")]
    public float debugTipRadius = 0.06f;

    [ColorUsage(true, true)]
    public Color debugRayColorIdle     = new Color(0f,  0.8f, 1f,  1f);
    [ColorUsage(true, true)]
    public Color debugRayColorSelected = new Color(0f,  1f,   0.3f, 1f);
    [ColorUsage(true, true)]
    public Color debugRayColorMiss     = new Color(1f,  0.3f, 0f,  1f);

    [Tooltip("Width of the debug ray tube")]
    public float debugRayWidth = 0.008f;

    // ── Scene-view gizmos (still useful in editor) ───────────────────────────
    [Header("Scene-View Gizmos")]
    public bool debugDrawGizmos = false;

    public enum Hand { None, Left, Right }

    // ─────────────────────────────────────────────────────────────────────────

    public NetoCommandPublisher   ActiveNeto   { get; private set; }
    public SauronCommandPublisher ActiveSauron { get; private set; }
    public Hand                  ActiveHand   { get; private set; }

    private NetoCommandPublisher   _prevNeto;
    private SauronCommandPublisher _prevSauron;

    private Camera _cam;

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

    void Awake()
    {
        _cam = Camera.main;
        BuildDebugVisuals();
        _tipMpb = new MaterialPropertyBlock();
    }

    void Update()
    {
        // ── Neto selection via body zones (always checked) ──
        if (poseDetector.LeftHandAtLeftShoulder && leftShoulderNeto != null)
        {
            ActiveNeto = leftShoulderNeto;
            ActiveHand = Hand.Left;
        }
        else if (poseDetector.RightHandAtRightShoulder && rightShoulderNeto != null)
        {
            ActiveNeto = rightShoulderNeto;
            ActiveHand = Hand.Right;
        }
        else if (poseDetector.LeftHandAtThirdZone && thirdZoneNeto != null)
        {
            ActiveNeto = thirdZoneNeto;
            ActiveHand = Hand.Left;
        }
        else if (poseDetector.RightHandAtThirdZone && thirdZoneNeto != null)
        {
            ActiveNeto = thirdZoneNeto;
            ActiveHand = Hand.Right;
        }
        else if (poseDetector.CurrentPose != ControlPose.ChestMode)
        {
            ClearNeto();
            ActiveHand = Hand.None;
        }

        // ── Sauron selection via chest mode ──
        switch (poseDetector.CurrentPose)
        {
            case ControlPose.ChestMode:
                ClearNeto();
                SetAllSauronsSelected();
                break;

            case ControlPose.TwoHandGesture:
                ClearNeto();
                ClearSauron();
                break;

            case ControlPose.None:
                ClearSauron();
                break;
        }

        if (ActiveNeto != _prevNeto || ActiveSauron != _prevSauron)
        {
            RefreshIndicators();
            _prevNeto   = ActiveNeto;
            _prevSauron = ActiveSauron;
        }

        RefreshDebugVisuals();
    }

    // ── State Helpers ────────────────────────────────────────────────────────

    void ClearNeto()
    {
        ActiveNeto = null;
    }

    void ClearSauron()
    {
        if (ActiveSauron != null) { ActiveSauron.CenterServos(); ActiveSauron = null; }
    }

    void SetAllSauronsSelected()
    {
        ActiveSauron = null;
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
        var lineGO = new GameObject("DebugRayLine");
        lineGO.transform.SetParent(transform, false);
        _rayLine                  = lineGO.AddComponent<LineRenderer>();
        _rayLine.positionCount    = 2;
        _rayLine.startWidth       = debugRayWidth;
        _rayLine.endWidth         = debugRayWidth;
        _rayLine.useWorldSpace    = true;
        _rayLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _rayLine.receiveShadows   = false;

        var rayMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        rayMat.EnableKeyword("_EMISSION");
        _rayLine.material = rayMat;

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

        Color col = ActiveNeto != null ? debugRayColorSelected
                  : _debugHasHit       ? debugRayColorIdle
                  :                      debugRayColorMiss;

        _rayLine.SetPosition(0, _debugRayOrigin);
        _rayLine.SetPosition(1, _debugTipPoint);

        var mat = _rayLine.material;
        mat.SetColor("_BaseColor",     col);
        mat.SetColor("_EmissionColor", col * 3f);

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

        // Draw estimated body zones
        if (debugDrawGizmos && poseDetector.headset != null)
        {
            Vector3 headPos = poseDetector.headset.position;

            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(headPos + poseDetector.leftShoulderOffset, poseDetector.bodyZoneRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(headPos + poseDetector.rightShoulderOffset, poseDetector.bodyZoneRadius);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(headPos + poseDetector.thirdZoneOffset, poseDetector.bodyZoneRadius);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(headPos + Vector3.up * poseDetector.chestHeightOffset, poseDetector.chestRadius);
        }

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
    }
}
