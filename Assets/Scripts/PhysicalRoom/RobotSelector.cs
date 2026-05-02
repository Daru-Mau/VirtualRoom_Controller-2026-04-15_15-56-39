// RobotSelector.cs
using UnityEngine;

public class RobotSelector : MonoBehaviour
{
    public PoseDetector poseDetector;
    public Transform headset;

    [Header("Neto Robots")]
    public NetoCommandPublisher[] netoRobots;   // assign Neto_1_Rig, Neto_2_Rig, Neto_3_Rig

    [Header("Sauron Robots")]
    public SauronCommandPublisher[] sauronRobots; // assign Sauron_1, Sauron_2

    [Header("Special Gesture Target")]
    public SauronCommandPublisher gestureTargetSauron; // e.g. Sauron_2

    // Active selections — only one of these will be non-null at a time
    public NetoCommandPublisher ActiveNeto { get; private set; }
    public SauronCommandPublisher ActiveSauron { get; private set; }

    private NetoCommandPublisher _prevNeto;
    private SauronCommandPublisher _prevSauron;

    void Update()
    {
        NetoCommandPublisher newNeto = null;
        SauronCommandPublisher newSauron = null;

        switch (poseDetector.CurrentPose)
        {
            case ControlPose.ChestMode:
                // No specific robot — InputDispatcher broadcasts
                break;

            case ControlPose.DirectionalLeft:
                ResolveDirection(poseDetector.LeftHandDirection, out newNeto, out newSauron);
                break;

            case ControlPose.DirectionalRight:
                ResolveDirection(poseDetector.RightHandDirection, out newNeto, out newSauron);
                break;

            case ControlPose.TwoHandGesture:
                newSauron = gestureTargetSauron;
                break;
        }

        // Fire selection change callbacks
        if (newNeto != _prevNeto || newSauron != _prevSauron)
        {
            _prevNeto?.ResetToDefaults();          // safe state on deselect
            _prevSauron?.CenterServos();

            ActiveNeto = newNeto;
            ActiveSauron = newSauron;
            _prevNeto = newNeto;
            _prevSauron = newSauron;

            if (newNeto != null) Debug.Log($"[Selector] Active Neto: {newNeto.name}");
            if (newSauron != null) Debug.Log($"[Selector] Active Sauron: {newSauron.name}");
        }
    }

    void ResolveDirection(Vector3 direction,
                          out NetoCommandPublisher bestNeto,
                          out SauronCommandPublisher bestSauron)
    {
        Vector3 origin = headset.position + Vector3.up * -0.15f;
        bestNeto = null;
        bestSauron = null;
        float bestAngle = 60f; // cone cutoff

        foreach (var r in netoRobots)
        {
            float angle = Vector3.Angle(direction,
                (r.transform.position - origin).normalized);
            if (angle < bestAngle) { bestAngle = angle; bestNeto = r; bestSauron = null; }
        }

        foreach (var r in sauronRobots)
        {
            float angle = Vector3.Angle(direction,
                (r.transform.position - origin).normalized);
            if (angle < bestAngle) { bestAngle = angle; bestSauron = r; bestNeto = null; }
        }
    }
}