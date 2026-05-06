using UnityEngine;

public class RobotSelector : MonoBehaviour
{
    public PoseDetector poseDetector;
    public Transform headset;

    [Header("Robot Lists")]
    public NetoCommandPublisher[] netoRobots;
    public SauronCommandPublisher[] sauronRobots;

    [Header("Gesture Target")]
    public SauronCommandPublisher gestureTargetSauron;

    [Header("Selection")]
    [Tooltip("Half-angle of the cone used to pick the robot you're pointing at")]
    public float selectionConeAngle = 60f;

    public NetoCommandPublisher ActiveNeto { get; private set; }
    public SauronCommandPublisher ActiveSauron { get; private set; }

    private NetoCommandPublisher _prevNeto;
    private SauronCommandPublisher _prevSauron;

    // ─────────────────────────────────────────────────────────────────

    void Update()
    {
        switch (poseDetector.CurrentPose)
        {
            case ControlPose.DirectionalLeft:
                SelectNetoInDirection(poseDetector.LeftHandDirection);
                ClearSauron();
                break;

            case ControlPose.DirectionalRight:
                SelectNetoInDirection(poseDetector.RightHandDirection);
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

    void SelectNetoInDirection(Vector3 direction)
    {
        NetoCommandPublisher best = null;
        float bestDot = Mathf.Cos(selectionConeAngle * Mathf.Deg2Rad);

        foreach (var neto in netoRobots)
        {
            if (neto == null) continue;
            Vector3 toRobot = (neto.transform.position - headset.position).normalized;
            float dot = Vector3.Dot(direction, toRobot);
            if (dot > bestDot) { bestDot = dot; best = neto; }
        }

        if (best != ActiveNeto)
        {
            if (ActiveNeto != null) ActiveNeto.ResetToDefaults();
            ActiveNeto = best;
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