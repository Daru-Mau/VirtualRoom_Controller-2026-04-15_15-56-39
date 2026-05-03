using UnityEngine;
using UnityEngine.InputSystem;

public class PoseInputDispatcher : MonoBehaviour
{
    public PoseDetector poseDetector;
    public RobotSelector robotSelector;

    [Header("Sauron Input Actions")]
    public InputActionReference leftStickAction;
    public InputActionReference rightStickAction;
    public InputActionReference leftTriggerAction;   // Sauron 1 tilt up
    public InputActionReference rightTriggerAction;  // Sauron 2 tilt up
    public InputActionReference leftGripAction;      // Sauron 1 tilt down
    public InputActionReference rightGripAction;     // Sauron 2 tilt down

    [Header("Neto Input Actions")]
    public InputActionReference aButtonAction;   // Right hand primary   → Neto preset 1
    public InputActionReference bButtonAction;   // Right hand secondary → Neto preset 2
    public InputActionReference xButtonAction;   // Left hand primary    → Neto preset 3
    public InputActionReference yButtonAction;   // Left hand secondary  → Neto all off

    [Header("Sauron Tuning")]
    [Tooltip("How fast the bottom servo sweeps in servo-units per second")]
    public float sauronYawRate = 60f;
    [Tooltip("How fast the top servo moves in servo-units per second")]
    public float sauronPitchRate = 45f;
    [Tooltip("Minimum trigger/grip value before pitch responds")]
    public float triggerDeadzone = 0.1f;
    [Tooltip("Max UDP send rate for Sauron to avoid flooding")]
    public float sauronSendRateHz = 20f;

    [Header("Neto Tuning")]
    [Tooltip("Stick dead zone for motor control")]
    public float netoDeadzone = 0.12f;
    [Tooltip("Max UDP send rate for Neto motor to avoid flooding")]
    public float netoSendRateHz = 20f;

    // ── Tracked Sauron servo angles (servo units 0-180, centre=90) ──
    private float _s1Bottom = 90f, _s1Top = 90f;
    private float _s2Bottom = 90f, _s2Top = 90f;
    private float _sauronNextSend;

    // ── Neto motor rate limiting ──
    private float _netoNextSend;
    private int _lastNetoMotor = 90;

    // ── Neto sound/LED presets ──
    // (sound, volume, ledRadius, ledBrightness) — motor stays at whatever joystick says
    // Aligned with your 3 physical states; tweak values once you test on real hardware
    private static readonly (bool sound, int volume, int ledR, int ledB)[] _netoPresets =
    {
        (false,  0,  0,   0),   // [0] All off
        (true,   6,  3,  80),   // [1] A button — soft state
        (true,  12,  6, 160),   // [2] B button — medium state
        (true,  18, 10, 255),   // [3] X button — full intensity
    };

    // ─────────────────────────────────────────────────────────────────

    void Update()
    {
        switch (poseDetector.CurrentPose)
        {
            case ControlPose.ChestMode: HandleChestMode(); break;
            case ControlPose.DirectionalLeft:
            case ControlPose.DirectionalRight: HandleDirectional(); break;
            case ControlPose.TwoHandGesture: HandleGesture(); break;
            case ControlPose.None:
                // Send motor stop if we just left directional mode
                SendNetoStop();
                break;
        }
    }

    // ── CHEST MODE: each hand independently controls one Sauron ─────

    void HandleChestMode()
    {
        bool canSend = Time.time >= _sauronNextSend;

        float dt = Time.deltaTime;
        float leftX = leftStickAction.action.ReadValue<Vector2>().x;
        float rightX = rightStickAction.action.ReadValue<Vector2>().x;
        float lTrig = leftTriggerAction.action.ReadValue<float>();
        float rTrig = rightTriggerAction.action.ReadValue<float>();
        float lGrip = leftGripAction.action.ReadValue<float>();
        float rGrip = rightGripAction.action.ReadValue<float>();

        // Left hand → Sauron 1
        var s1 = robotSelector.sauronRobots.Length > 0 ? robotSelector.sauronRobots[0] : null;
        if (s1 != null)
        {
            _s1Bottom = Mathf.Clamp(_s1Bottom + leftX * sauronYawRate * dt, 0f, 180f);

            float pitchInput = 0f;
            if (lTrig > triggerDeadzone) pitchInput = lTrig;  // trigger = up
            if (lGrip > triggerDeadzone) pitchInput = -lGrip; // grip    = down
            _s1Top = Mathf.Clamp(_s1Top + pitchInput * sauronPitchRate * dt, 0f, 180f);

            if (canSend)
                s1.SetBothServos(Mathf.RoundToInt(_s1Bottom), Mathf.RoundToInt(_s1Top));
        }

        // Right hand → Sauron 2
        var s2 = robotSelector.sauronRobots.Length > 1 ? robotSelector.sauronRobots[1] : null;
        if (s2 != null)
        {
            _s2Bottom = Mathf.Clamp(_s2Bottom + rightX * sauronYawRate * dt, 0f, 180f);

            float pitchInput = 0f;
            if (rTrig > triggerDeadzone) pitchInput = rTrig;
            if (rGrip > triggerDeadzone) pitchInput = -rGrip;
            _s2Top = Mathf.Clamp(_s2Top + pitchInput * sauronPitchRate * dt, 0f, 180f);

            if (canSend)
                s2.SetBothServos(Mathf.RoundToInt(_s2Bottom), Mathf.RoundToInt(_s2Top));
        }

        if (canSend)
            _sauronNextSend = Time.time + 1f / sauronSendRateHz;
    }

    // ── DIRECTIONAL MODE: joystick = Neto motor, buttons = presets ──

    void HandleDirectional()
    {
        var neto = robotSelector.ActiveNeto;
        if (neto == null) return;

        // Use whichever hand is extended
        float stickY = poseDetector.CurrentPose == ControlPose.DirectionalLeft
            ? leftStickAction.action.ReadValue<Vector2>().y
            : rightStickAction.action.ReadValue<Vector2>().y;

        // Rate-limited motor commands
        if (Time.time >= _netoNextSend)
        {
            int motorUnits;
            if (Mathf.Abs(stickY) > netoDeadzone)
            {
                // Positive Y = pull (toward 0), negative Y = release (toward 180)
                motorUnits = stickY > 0
                    ? Mathf.RoundToInt(Mathf.Lerp(90f, 0f, stickY))
                    : Mathf.RoundToInt(Mathf.Lerp(90f, 180f, -stickY));
            }
            else
            {
                motorUnits = 90; // dead zone = stop
            }

            // Only send if value changed to reduce chatter
            if (motorUnits != _lastNetoMotor)
            {
                neto.SetMotorSpeedUnits(motorUnits);
                _lastNetoMotor = motorUnits;
            }

            _netoNextSend = Time.time + 1f / netoSendRateHz;
        }

        // Buttons fire sound+LED presets (motor stays at current joystick position)
        if (aButtonAction.action.WasPressedThisFrame()) ApplyNetoPreset(neto, 1);
        if (bButtonAction.action.WasPressedThisFrame()) ApplyNetoPreset(neto, 2);
        if (xButtonAction.action.WasPressedThisFrame()) ApplyNetoPreset(neto, 3);
        if (yButtonAction.action.WasPressedThisFrame()) ApplyNetoPreset(neto, 0);
    }

    // ── TWO-HAND GESTURE: special robot placeholder ──────────────────

    void HandleGesture()
    {
        // Reserved for the eyelid/sphere robot once added to scene
        // gestureTargetSauron is already assigned in RobotSelector
        // Will implement once robot is defined
    }

    // ── Helpers ──────────────────────────────────────────────────────

    void ApplyNetoPreset(NetoCommandPublisher neto, int index)
    {
        var (sound, volume, ledR, ledB) = _netoPresets[index];
        // Preserve current motor speed — SetState sets motor too, use 90 (stop on preset trigger)
        neto.SetState(sound, volume, 90, ledR, ledB);
        _lastNetoMotor = 90;
    }

    void SendNetoStop()
    {
        // Called when leaving directional mode — ensure motor is stopped
        if (_lastNetoMotor != 90 && robotSelector.ActiveNeto != null)
        {
            robotSelector.ActiveNeto.SetMotorSpeedUnits(90);
            _lastNetoMotor = 90;
        }
    }
}