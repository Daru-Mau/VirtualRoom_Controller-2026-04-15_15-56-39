// PoseInputDispatcher.cs
using UnityEngine;
using UnityEngine.InputSystem;

public class PoseInputDispatcher : MonoBehaviour
{
    public PoseDetector poseDetector;
    public RobotSelector robotSelector;

    [Header("Input Action References")]
    public InputActionReference leftStickAction;
    public InputActionReference rightStickAction;
    public InputActionReference primaryButtonAction;   // A button
    public InputActionReference secondaryButtonAction; // B button

    [Header("Neto Tuning")]
    [Tooltip("How far the motor moves from joystick input per second (servo units)")]
    public float netoMotorRate = 60f;

    void Update()
    {
        switch (poseDetector.CurrentPose)
        {
            case ControlPose.ChestMode:
                HandleChestMode();
                break;

            case ControlPose.DirectionalLeft:
            case ControlPose.DirectionalRight:
                HandleDirectional();
                break;

            case ControlPose.TwoHandGesture:
                HandleGesture();
                break;
        }
    }

    // ── Chest Mode: broadcast to all robots ──────────────────────

    void HandleChestMode()
    {
        if (primaryButtonAction.action.WasPressedThisFrame())
        {
            // Example: emergency stop / safe state all robots
            FindObjectOfType<PhysicalRoom.UnityBridge.VrRobotUdpBridge>()
                ?.SendSafeStateToAllRobots();
            Debug.Log("[ChestMode] Safe state broadcast");
        }

        if (secondaryButtonAction.action.WasPressedThisFrame())
        {
            // Example: reset all Neto to defaults
            foreach (var n in robotSelector.netoRobots)
                n.ResetToDefaults();
        }
    }

    // ── Directional: control the robot the hand points at ────────

    void HandleDirectional()
    {
        Vector2 leftStick = leftStickAction.action.ReadValue<Vector2>();
        Vector2 rightStick = rightStickAction.action.ReadValue<Vector2>();
        bool primary = primaryButtonAction.action.WasPressedThisFrame();
        bool secondary = secondaryButtonAction.action.WasPressedThisFrame();

        // Active Neto: left stick Y = motor (pull/release), right stick X = LED radius
        if (robotSelector.ActiveNeto != null)
        {
            if (Mathf.Abs(leftStick.y) > 0.1f)
            {
                // Map stick to normalized pull/release
                float norm = Mathf.Clamp01(leftStick.y);
                if (leftStick.y > 0)
                    robotSelector.ActiveNeto.SetPullNormalized(norm);
                else
                    robotSelector.ActiveNeto.SetReleaseNormalized(Mathf.Clamp01(-leftStick.y));
            }
            else
            {
                robotSelector.ActiveNeto.ResetToDefaults();
            }

            if (primary)
                robotSelector.ActiveNeto.SetSound(true);
            if (secondary)
                robotSelector.ActiveNeto.SetSound(false);
        }

        // Active Sauron: left stick = yaw, right stick Y = pitch
        if (robotSelector.ActiveSauron != null)
        {
            if (leftStick.magnitude > 0.1f || rightStick.magnitude > 0.1f)
            {
                // Map stick to servo angles (0-180, centre = 90)
                int bottom = Mathf.RoundToInt(Mathf.Lerp(0, 180, (leftStick.x + 1f) * 0.5f));
                int top = Mathf.RoundToInt(Mathf.Lerp(0, 180, (rightStick.y + 1f) * 0.5f));
                robotSelector.ActiveSauron.SetBothServos(bottom, top);
            }
        }
    }

    // ── Two-Hand Gesture: control the special Sauron ─────────────

    void HandleGesture()
    {
        if (robotSelector.ActiveSauron == null) return;

        // Map the physical hand sweep delta to Sauron servo
        // TwoHandMovementDelta.x → yaw, .z → ignored (depth), .y → pitch
        Vector3 delta = poseDetector.TwoHandMovementDelta;

        if (delta.magnitude > 0.001f)
        {
            // Accumulate — read current angles and offset them
            // Scale factor converts metres/frame to servo degrees
            float yawDelta = delta.x * 300f;
            float pitchDelta = delta.y * 300f;

            // Clamp within 0-180 range (you'll need to track current angles)
            // Simplest approach: send normalized from the delta directly
            int bottom = 90 + Mathf.RoundToInt(yawDelta);
            int top = 90 + Mathf.RoundToInt(pitchDelta);
            bottom = Mathf.Clamp(bottom, 0, 180);
            top = Mathf.Clamp(top, 0, 180);

            robotSelector.ActiveSauron.SetBothServos(bottom, top);
        }

        if (primaryButtonAction.action.WasPressedThisFrame())
            robotSelector.ActiveSauron.CenterServos();
    }
}