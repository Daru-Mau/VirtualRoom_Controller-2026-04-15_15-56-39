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
    [Tooltip("Radius around the chest point for both hands to trigger ChestMode.")]
    public float chestRadius = 0.18f;
    [Tooltip("Vertical offset from headset to the chest centre.")]
    public float chestHeightOffset = -0.15f;

    [Header("Body Zones (Neto selection)")]
    [Tooltip("Offset from headset to the left shoulder zone centre.")]
    public Vector3 leftShoulderOffset = new Vector3(-0.25f, -0.25f, 0f);
    [Tooltip("Offset from headset to the right shoulder zone centre.")]
    public Vector3 rightShoulderOffset = new Vector3(0.25f, -0.25f, 0f);
    [Tooltip("Offset from headset to the third zone centre (e.g. chest/belt).")]
    public Vector3 thirdZoneOffset = new Vector3(0f, -0.45f, 0.15f);
    [Tooltip("Radius around each body zone to detect a controller.")]
    public float bodyZoneRadius = 0.2f;

    [Header("Extension Thresholds")]
    public float extensionMinDistance = 0.5f;

    [Header("Deathtrap Gesture")]
    [Tooltip("Max distance between hands to trigger Deathtrap mode.")]
    public float deathtrapHandProximity = 0.3f;
    [Tooltip("Max angle from palms-down orientation.")]
    public float deathtrapPalmDownAngle = 40f;

    public ControlPose CurrentPose { get; private set; } = ControlPose.None;

    // ── Body zone read-only flags (for RobotSelector) ──
    public bool LeftHandAtLeftShoulder   { get; private set; }
    public bool RightHandAtRightShoulder { get; private set; }
    public bool LeftHandAtThirdZone      { get; private set; }
    public bool RightHandAtThirdZone     { get; private set; }

    void Update()
    {
        Vector3 headPos = headset.position;

        Vector3 chestPos   = headPos + Vector3.up * chestHeightOffset;
        Vector3 leftShPos  = headPos + leftShoulderOffset;
        Vector3 rightShPos = headPos + rightShoulderOffset;
        Vector3 thirdPos   = headPos + thirdZoneOffset;

        float leftDist  = Vector3.Distance(leftController.position, chestPos);
        float rightDist = Vector3.Distance(rightController.position, chestPos);

        bool leftNearChest = leftDist < chestRadius;
        bool rightNearChest = rightDist < chestRadius;
        bool leftExtended = leftDist > extensionMinDistance;
        bool rightExtended = rightDist > extensionMinDistance;

        // ── Body zone detection (always computed, used by RobotSelector) ──
        LeftHandAtLeftShoulder   = Vector3.Distance(leftController.position, leftShPos)   < bodyZoneRadius;
        RightHandAtRightShoulder = Vector3.Distance(rightController.position, rightShPos)  < bodyZoneRadius;
        LeftHandAtThirdZone      = Vector3.Distance(leftController.position, thirdPos)     < bodyZoneRadius;
        RightHandAtThirdZone     = Vector3.Distance(rightController.position, thirdPos)    < bodyZoneRadius;

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