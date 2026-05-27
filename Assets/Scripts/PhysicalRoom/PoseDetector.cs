using UnityEngine;

public enum ControlPose
{
    None,
    ChestMode,
    DirectionalLeft,
    DirectionalRight,
    TwoHandGesture
}

public class PoseDetector : MonoBehaviour
{
    [Header("References")]
    public Transform headset;
    public Transform leftController;
    public Transform rightController;

    [Header("Chest Zone")]
    public float chestRadius = 0.35f;
    public float chestHeightOffset = -0.15f;

    [Header("Extension Thresholds")]
    public float extensionMinDistance = 0.5f;

    [Header("Deathtrap Gesture")]
    [Tooltip("Max distance between hands to trigger Deathtrap mode.")]
    public float deathtrapHandProximity = 0.3f;
    [Tooltip("Max angle from palms-down orientation.")]
    public float deathtrapPalmDownAngle = 40f;

    public ControlPose CurrentPose { get; private set; } = ControlPose.None;

    void Update()
    {
        Vector3 chestPos = headset.position + Vector3.up * chestHeightOffset;

        float leftDist = Vector3.Distance(leftController.position, chestPos);
        float rightDist = Vector3.Distance(rightController.position, chestPos);

        bool leftNearChest = leftDist < chestRadius;
        bool rightNearChest = rightDist < chestRadius;
        bool leftExtended = leftDist > extensionMinDistance;
        bool rightExtended = rightDist > extensionMinDistance;

        // ── Chest Mode ──
        if (leftNearChest && rightNearChest)
        {
            CurrentPose = ControlPose.ChestMode;
            return;
        }

        // ── Deathtrap: both extended + close together + palms down ──
        if (leftExtended && rightExtended)
        {
            float handsDist = Vector3.Distance(leftController.position, rightController.position);
            bool handsClose = handsDist < deathtrapHandProximity;
            bool leftPalmDown = Vector3.Angle(leftController.up, Vector3.down) < deathtrapPalmDownAngle;
            bool rightPalmDown = Vector3.Angle(rightController.up, Vector3.down) < deathtrapPalmDownAngle;

            if (handsClose && leftPalmDown && rightPalmDown)
            {
                CurrentPose = ControlPose.TwoHandGesture;
                return;
            }
        }

        // ── Directional ──
        if (leftExtended && !rightExtended)
            CurrentPose = ControlPose.DirectionalLeft;
        else if (rightExtended && !leftExtended)
            CurrentPose = ControlPose.DirectionalRight;
        else
            CurrentPose = ControlPose.None;
    }
}