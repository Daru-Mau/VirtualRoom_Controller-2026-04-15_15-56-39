using UnityEngine;

public class RobotSelector : MonoBehaviour
{
    public PoseDetector poseDetector;

    [Header("Robot Lists")]
    public NetoCommandPublisher[] netoRobots;
    public SauronCommandPublisher[] sauronRobots;

    [Header("Gesture Target")]
    public SauronCommandPublisher gestureTargetSauron;

    [Header("Selection")]
    [Tooltip("Max distance along the ray to consider a Neto")]
    public float selectionMaxDistance = 10f;
    [Tooltip("Max distance from the ray to count as aligned")]
    public float selectionRayRadius = 0.35f;
    [Tooltip("Use physics sphere cast to select Netos by collider")]
    public bool usePhysicsSelection = true;
    public LayerMask selectionMask = ~0;
    [Tooltip("Draw a debug ray from the active hand")]
    public bool debugDrawRay = false;
    public Color debugRayColor = new Color(0f, 1f, 1f, 1f);
    public Color debugSphereColor = new Color(1f, 0.5f, 0f, 1f);
    public float debugSphereRadius = 0.1f;

    public NetoCommandPublisher ActiveNeto { get; private set; }
    public SauronCommandPublisher ActiveSauron { get; private set; }

    private NetoCommandPublisher _prevNeto;
    private SauronCommandPublisher _prevSauron;
    private bool _hasDebugPoint;
    private Vector3 _debugPoint;
    private Vector3 _debugRayOrigin;
    private Vector3 _debugRayDirection;

    // ─────────────────────────────────────────────────────────────────

    void Update()
    {
        if (debugDrawRay && poseDetector != null)
        {
            if (poseDetector.CurrentPose == ControlPose.DirectionalLeft && poseDetector.leftController != null)
            {
                Debug.DrawRay(poseDetector.leftController.position,
                    poseDetector.leftController.forward * selectionMaxDistance, debugRayColor);
            }
            else if (poseDetector.CurrentPose == ControlPose.DirectionalRight && poseDetector.rightController != null)
            {
                Debug.DrawRay(poseDetector.rightController.position,
                    poseDetector.rightController.forward * selectionMaxDistance, debugRayColor);
            }
        }

        switch (poseDetector.CurrentPose)
        {
            case ControlPose.DirectionalLeft:
                SelectNetoInDirection(poseDetector.leftController.position, poseDetector.leftController.forward);
                ClearSauron();
                break;

            case ControlPose.DirectionalRight:
                SelectNetoInDirection(poseDetector.rightController.position, poseDetector.rightController.forward);
                ClearSauron();
                break;

            case ControlPose.ChestMode:
                // All Saurons active in chest mode — show all as selected
                ClearNeto();
                SetAllSauronsSelected();
                break;

            case ControlPose.TwoHandGesture:
                ClearNeto();
                SetGestureSauron();
                break;

            case ControlPose.None:
                ClearNeto();
                ClearSauron();
                break;
        }

        // Update indicators whenever selection changes
        if (ActiveNeto != _prevNeto || ActiveSauron != _prevSauron)
        {
            RefreshIndicators();
            _prevNeto = ActiveNeto;
            _prevSauron = ActiveSauron;
        }
    }

    // ── Selection Logic ──────────────────────────────────────────────

    void SelectNetoInDirection(Vector3 origin, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        _debugRayOrigin = origin;
        _debugRayDirection = direction.normalized;

        if (usePhysicsSelection)
        {
            SelectNetoWithSphereCast(origin, direction.normalized);
            return;
        }

        NetoCommandPublisher best = null;
        float bestAlongRay = float.PositiveInfinity;
        float bestOffRay = float.PositiveInfinity;
        Vector3 dir = direction.normalized;
        Vector3 dirFlat = new Vector3(dir.x, 0f, dir.z);
        if (dirFlat.sqrMagnitude < 0.0001f) return;
        dirFlat.Normalize();
        Vector3 originFlat = new Vector3(origin.x, 0f, origin.z);
        _hasDebugPoint = false;

        foreach (var neto in netoRobots)
        {
            if (neto == null) continue;
            Vector3 robotPos = neto.transform.position;
            Vector3 toRobot = new Vector3(robotPos.x, 0f, robotPos.z) - originFlat;
            float alongRay = Vector3.Dot(dirFlat, toRobot);
            if (alongRay <= 0f || alongRay > selectionMaxDistance) continue;

            Vector3 closestPoint = originFlat + dirFlat * alongRay;
            float offRay = Vector3.Distance(closestPoint, new Vector3(robotPos.x, 0f, robotPos.z));
            if (offRay > selectionRayRadius) continue;

            if (alongRay < bestAlongRay || (Mathf.Approximately(alongRay, bestAlongRay) && offRay < bestOffRay))
            {
                bestAlongRay = alongRay;
                bestOffRay = offRay;
                best = neto;
                _debugPoint = new Vector3(closestPoint.x, origin.y, closestPoint.z);
                _hasDebugPoint = true;
            }
        }

        if (best != ActiveNeto)
        {
            if (ActiveNeto != null) ActiveNeto.ResetToDefaults();
            ActiveNeto = best;
        }
    }

    void SelectNetoWithSphereCast(Vector3 origin, Vector3 direction)
    {
        _hasDebugPoint = false;
        var hits = Physics.SphereCastAll(origin, selectionRayRadius, direction, selectionMaxDistance,
            selectionMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return;

        float bestDistance = float.PositiveInfinity;
        NetoCommandPublisher best = null;

        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            var neto = hit.collider.GetComponentInParent<NetoCommandPublisher>();
            if (neto == null) continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                best = neto;
                _debugPoint = hit.point;
                _hasDebugPoint = true;
            }
        }

        if (best != ActiveNeto)
        {
            if (ActiveNeto != null) ActiveNeto.ResetToDefaults();
            ActiveNeto = best;
        }
    }

    void OnDrawGizmos()
    {
        if (!debugDrawRay) return;
        Gizmos.color = debugRayColor;
        Gizmos.DrawRay(_debugRayOrigin, _debugRayDirection * selectionMaxDistance);

        if (_hasDebugPoint)
        {
            Gizmos.color = debugSphereColor;
            Gizmos.DrawSphere(_debugPoint, debugSphereRadius);
        }
    }

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
        // In chest mode ActiveSauron isn't used (dispatcher drives both directly),
        // but we still want the indicators to show selected on all Saurons.
        ActiveSauron = null;
    }

    void SetGestureSauron()
    {
        ActiveSauron = gestureTargetSauron;
    }

    // ── Indicator Management ─────────────────────────────────────────

    void RefreshIndicators()
    {
        // Neto indicators
        foreach (var neto in netoRobots)
        {
            if (neto == null) continue;
            var ind = neto.GetComponent<RobotVisualIndicator>();
            if (ind == null) continue;

            if (neto == ActiveNeto)
                ind.SetState(RobotVisualIndicator.IndicatorState.Selected);
            else
                ind.SetState(RobotVisualIndicator.IndicatorState.Idle);
        }

        // Sauron indicators
        foreach (var sauron in sauronRobots)
        {
            if (sauron == null) continue;
            var ind = sauron.GetComponent<RobotVisualIndicator>();
            if (ind == null) continue;

            bool chestMode = poseDetector.CurrentPose == ControlPose.ChestMode;
            bool isSelected = (sauron == ActiveSauron) || chestMode;
            ind.SetState(isSelected
                ? RobotVisualIndicator.IndicatorState.Selected
                : RobotVisualIndicator.IndicatorState.Idle);
        }
    }
}