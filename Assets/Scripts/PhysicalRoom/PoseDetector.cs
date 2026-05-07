using UnityEngine;

public enum ControlPose
{
    None,
    ChestMode,        // Both hands near chest → universal control set
    DirectionalLeft,  // Left hand extended → control robot in that direction
    DirectionalRight, // Right hand extended → control robot in that direction
    TwoHandGesture    // Both hands extended down + movement → special robot
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
    public float twoHandDownAngle = 30f;

    [Header("Two-Hand Gesture")]
    public float twoHandMovementThreshold = 0.05f;
    public float twoHandGestureHoldTime = 0.4f;

    private Vector3 _prevLeftPos;
    private Vector3 _prevRightPos;
    private float _twoHandGestureTimer;

    public ControlPose CurrentPose { get; private set; } = ControlPose.None;
    public Vector3 LeftHandDirection { get; private set; }
    public Vector3 RightHandDirection { get; private set; }
    public Vector3 TwoHandMovementDelta { get; private set; }

    void Update()
    {
        Vector3 chestPos = headset.position + Vector3.up * chestHeightOffset;

        float leftDist = Vector3.Distance(leftController.position, chestPos);
        float rightDist = Vector3.Distance(rightController.position, chestPos);

        bool leftNearChest = leftDist < chestRadius;
        bool rightNearChest = rightDist < chestRadius;
        bool leftExtended = leftDist > extensionMinDistance;
        bool rightExtended = rightDist > extensionMinDistance;

        LeftHandDirection = (leftController.position - chestPos).normalized;
        RightHandDirection = (rightController.position - chestPos).normalized;

        // ── Chest Mode: both hands close to chest ──
        if (leftNearChest && rightNearChest)
        {
            CurrentPose = ControlPose.ChestMode;
            _twoHandGestureTimer = 0f;
            return;
        }

        // ── Two-Hand Gesture: both extended, pointing downward ──
        if (leftExtended && rightExtended)
        {
            bool leftPointsDown = Vector3.Angle(LeftHandDirection, Vector3.down) < twoHandDownAngle;
            bool rightPointsDown = Vector3.Angle(RightHandDirection, Vector3.down) < twoHandDownAngle;

            if (leftPointsDown && rightPointsDown)
            {
                TwoHandMovementDelta = ((leftController.position - _prevLeftPos) +
                                        (rightController.position - _prevRightPos)) * 0.5f;

                bool isMoving = TwoHandMovementDelta.magnitude > twoHandMovementThreshold;
                if (isMoving) _twoHandGestureTimer += Time.deltaTime;

                if (_twoHandGestureTimer >= twoHandGestureHoldTime)
                {
                    CurrentPose = ControlPose.TwoHandGesture;
                    _prevLeftPos = leftController.position;
                    _prevRightPos = rightController.position;
                    return;
                }
            }
            else
            {
                _twoHandGestureTimer = 0f;
            }
        }
        else
        {
            _twoHandGestureTimer = 0f;
        }

        // ── Directional: one hand extended ──
        if (leftExtended && !rightExtended)
            CurrentPose = ControlPose.DirectionalLeft;
        else if (rightExtended && !leftExtended)
            CurrentPose = ControlPose.DirectionalRight;
        else
            CurrentPose = ControlPose.None;

        _prevLeftPos = leftController.position;
        _prevRightPos = rightController.position;
        // NOTE: no Debug.Log here — change-only logging is in PoseInputDispatcher
    }
}