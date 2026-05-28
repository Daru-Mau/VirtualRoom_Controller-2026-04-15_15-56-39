using UnityEngine;
using UnityEngine.InputSystem;

public class PoseInputDispatcher : MonoBehaviour
{
    public PoseDetector poseDetector;
    public RobotSelector robotSelector;
    public DeathTrapCoreController deathTrapCore;

    [Header("Sauron Input Actions")]
    public InputActionReference leftStickAction;
    public InputActionReference rightStickAction;
    public InputActionReference leftTriggerAction;
    public InputActionReference rightTriggerAction;
    public InputActionReference leftGripAction;
    public InputActionReference rightGripAction;

    [Header("Neto Input Actions")]
    public InputActionReference aButtonAction;
    public InputActionReference bButtonAction;
    public InputActionReference xButtonAction;
    public InputActionReference yButtonAction;

    [Header("Sauron Tuning")]
    public float sauronYawRate = 60f;
    public float sauronPitchRate = 45f;
    public float triggerDeadzone = 0.1f;
    public float sauronSendRateHz = 20f;

    [Header("Deathtrap Tuning")]
    [Tooltip("Hand separation distance at which the membrane is fully open.")]
    public float deathtrapMaxSeparation = 0.8f;

    [Header("Neto Tuning")]
    public float netoDeadzone = 0.12f;
    public float netoSendRateHz = 20f;

    [Header("Debug")]
    [Tooltip("Assign a world-space TextMeshPro text object to show live state in VR")]
    public TMPro.TextMeshProUGUI debugText;
    [Tooltip("Print changes to the Unity console as well")]
    public bool logToConsole = true;

    // ── Sauron servo state ──
    private float _s1Bottom = 90f, _s1Top = 90f;
    private float _s2Bottom = 90f, _s2Top = 90f;
    private float _sauronNextSend;

    // ── Neto motor state ──
    private float _netoNextSend;
    private int _lastNetoMotor = 90;
    private NetoCommandPublisher _previousNeto;

    // ── Neto presets: (sound, volume, ledRadius, ledBrightness) ──
    private static readonly (bool sound, int volume, int ledR, int ledB)[] _netoPresets =
    {
        (false,  0,  0,   0),   // [0] Y — all off
        (true,   6,  3,  80),   // [1] A — soft
        (true,  12,  6, 160),   // [2] B — medium
        (true,  18, 10, 255),   // [3] X — full
    };

    // ── Gesture tracking ──
    private ControlPose _previousPose = ControlPose.None;

    // ── Change-detection cache (prevents console spam) ──
    private ControlPose _lastLoggedPose = (ControlPose)(-1);
    private int _lastLoggedNetoId = -1;
    private int _lastLoggedSauronId = -1;
    private int _lastLoggedMotor = -1;
    private int _lastLoggedS1B = -1, _lastLoggedS1T = -1;
    private int _lastLoggedS2B = -1, _lastLoggedS2T = -1;
    private string _lastLoggedPreset = "";

    // ─────────────────────────────────────────────────────────────────

    void Update()
    {
        ControlPose currentPose = poseDetector.CurrentPose;

        if (_previousPose == ControlPose.TwoHandGesture && currentPose != ControlPose.TwoHandGesture)
        {
            if (deathTrapCore != null)
                deathTrapCore.EndExpose();
        }
        _previousPose = currentPose;

        switch (currentPose)
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
            case ControlPose.None:
                HandleNoneMode();
                break;
        }

        RefreshDebugDisplay();
    }

    // ── CHEST MODE ───────────────────────────────────────────────────

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

        var s1 = robotSelector.sauronRobots.Length > 0 ? robotSelector.sauronRobots[0] : null;
        if (s1 != null)
        {
            _s1Bottom = Mathf.Clamp(_s1Bottom + leftX * sauronYawRate * dt, 0f, 180f);
            float pitchInput = 0f;
            if (lTrig > triggerDeadzone) pitchInput = lTrig;
            if (lGrip > triggerDeadzone) pitchInput = -lGrip;
            _s1Top = Mathf.Clamp(_s1Top + pitchInput * sauronPitchRate * dt, 0f, 180f);
            if (canSend) s1.SetBothServos(Mathf.RoundToInt(_s1Bottom), Mathf.RoundToInt(_s1Top));
        }

        var s2 = robotSelector.sauronRobots.Length > 1 ? robotSelector.sauronRobots[1] : null;
        if (s2 != null)
        {
            _s2Bottom = Mathf.Clamp(_s2Bottom + rightX * sauronYawRate * dt, 0f, 180f);
            float pitchInput = 0f;
            if (rTrig > triggerDeadzone) pitchInput = rTrig;
            if (rGrip > triggerDeadzone) pitchInput = -rGrip;
            _s2Top = Mathf.Clamp(_s2Top + pitchInput * sauronPitchRate * dt, 0f, 180f);
            if (canSend) s2.SetBothServos(Mathf.RoundToInt(_s2Bottom), Mathf.RoundToInt(_s2Top));
        }

        if (canSend)
            _sauronNextSend = Time.time + 1f / sauronSendRateHz;
    }

    // ── DIRECTIONAL MODE ─────────────────────────────────────────────

    void HandleDirectional()
    {
        var neto = robotSelector.ActiveNeto;
        if (neto == null) return;

        if (neto != _previousNeto)
        {
            _lastNetoMotor = -1; // force resend on robot switch
            _previousNeto = neto;
        }

        float stickY = poseDetector.CurrentPose == ControlPose.DirectionalLeft
            ? leftStickAction.action.ReadValue<Vector2>().y
            : rightStickAction.action.ReadValue<Vector2>().y;

        if (Time.time >= _netoNextSend)
        {
            int motorUnits;

            if (stickY > netoDeadzone)
            {
                // Push up → move from bottom (0) toward top (180)
                motorUnits = Mathf.RoundToInt(Mathf.Lerp(0f, 180f, stickY));
            }
            else
            {
                // Neutral or push down → bottom (physical starting position)
                motorUnits = 0;
            }

            if (motorUnits != _lastNetoMotor)
            {
                neto.SetMotorSpeedUnits(motorUnits);
                _lastNetoMotor = motorUnits;
            }

            _netoNextSend = Time.time + 1f / netoSendRateHz;
        }

        if (aButtonAction.action.WasPressedThisFrame()) ApplyNetoPreset(neto, 1);
        if (bButtonAction.action.WasPressedThisFrame()) ApplyNetoPreset(neto, 2);
        if (xButtonAction.action.WasPressedThisFrame()) ApplyNetoPreset(neto, 3);
        if (yButtonAction.action.WasPressedThisFrame()) ApplyNetoPreset(neto, 0);
    }

    // ── TWO-HAND GESTURE ─────────────────────────────────────────────

    void HandleGesture()
    {
        if (deathTrapCore == null) return;

        float handDist = Vector3.Distance(
            poseDetector.leftController.position,
            poseDetector.rightController.position);

        float openness = Mathf.Clamp01(
            (handDist - poseDetector.deathtrapHandProximity) /
            (deathtrapMaxSeparation - poseDetector.deathtrapHandProximity));

        deathTrapCore.SetExpose(openness);
    }

    // ── NONE / TRANSITION ────────────────────────────────────────────

    void HandleNoneMode()
    {
        // Return last active Neto to bottom position (0)
        if (_previousNeto != null && _lastNetoMotor != 0)
        {
            _previousNeto.SetMotorSpeedUnits(0);
            _lastNetoMotor = 0;
        }
    }

    // ── HELPERS ──────────────────────────────────────────────────────

    void ApplyNetoPreset(NetoCommandPublisher neto, int index)
    {
        var (sound, volume, ledR, ledB) = _netoPresets[index];
        neto.SetSound(sound, volume);
        neto.SetLeds(ledR, ledB);
        // Motor is intentionally NOT touched — joystick keeps running

        string presetName = index == 0 ? "OFF" : index == 1 ? "A-SOFT" : index == 2 ? "B-MED" : "X-FULL";
        LogOnChange(ref _lastLoggedPreset, $"PRESET:{presetName}",
            $"[Neto {neto.robotId}] Preset → {presetName}");
    }

    // ── DEBUG: CHANGE-ONLY LOGGING ───────────────────────────────────

    void RefreshDebugDisplay()
    {
        var pose = poseDetector.CurrentPose;
        var neto = robotSelector.ActiveNeto;
        var sauron = robotSelector.ActiveSauron;

        int netoId = neto != null ? (int)neto.robotId : -1;
        int sauronId = sauron != null ? (int)sauron.robotId : -1;

        int s1b = Mathf.RoundToInt(_s1Bottom), s1t = Mathf.RoundToInt(_s1Top);
        int s2b = Mathf.RoundToInt(_s2Bottom), s2t = Mathf.RoundToInt(_s2Top);

        // ── Console: only log what actually changed ──
        if (logToConsole)
        {
            if (pose != _lastLoggedPose)
            {
                Debug.Log($"[Pose] {_lastLoggedPose} → {pose}");
                _lastLoggedPose = pose;
            }
            if (netoId != _lastLoggedNetoId)
            {
                Debug.Log($"[Neto] Active robot → {(netoId == -1 ? "none" : netoId.ToString())}");
                _lastLoggedNetoId = netoId;
            }
            if (sauronId != _lastLoggedSauronId)
            {
                Debug.Log($"[Sauron] Active robot → {(sauronId == -1 ? "none" : sauronId.ToString())}");
                _lastLoggedSauronId = sauronId;
            }
            if (_lastNetoMotor != _lastLoggedMotor)
            {
                Debug.Log($"[Neto {netoId}] Motor → {_lastNetoMotor}");
                _lastLoggedMotor = _lastNetoMotor;
            }
            if (pose == ControlPose.ChestMode)
            {
                if (s1b != _lastLoggedS1B || s1t != _lastLoggedS1T)
                {
                    // Only log Sauron servo changes if they moved more than 2 units
                    if (Mathf.Abs(s1b - _lastLoggedS1B) > 2 || Mathf.Abs(s1t - _lastLoggedS1T) > 2)
                    {
                        Debug.Log($"[Sauron 1] bottom={s1b} top={s1t}");
                        _lastLoggedS1B = s1b; _lastLoggedS1T = s1t;
                    }
                }
                if (s2b != _lastLoggedS2B || s2t != _lastLoggedS2T)
                {
                    if (Mathf.Abs(s2b - _lastLoggedS2B) > 2 || Mathf.Abs(s2t - _lastLoggedS2T) > 2)
                    {
                        Debug.Log($"[Sauron 2] bottom={s2b} top={s2t}");
                        _lastLoggedS2B = s2b; _lastLoggedS2T = s2t;
                    }
                }
            }
        }

        // ── Canvas: always refresh if assigned ──
        if (debugText == null) return;

        string modeBlock = pose switch
        {
            ControlPose.ChestMode => "MODE: CHEST",
            ControlPose.DirectionalLeft => "MODE: DIR LEFT",
            ControlPose.DirectionalRight => "MODE: DIR RIGHT",
            ControlPose.TwoHandGesture => "MODE: GESTURE",
            _ => "MODE: NONE",
        };

        string netoBlock = netoId == -1
            ? "NETO: —"
            : $"NETO: #{netoId}  motor={_lastNetoMotor}";

        string sauronBlock = pose == ControlPose.ChestMode
            ? $"S1: yaw={s1b} tilt={s1t}\nS2: yaw={s2b} tilt={s2t}"
            : (sauronId == -1 ? "SAURON: —" : $"SAURON: #{sauronId}");

        string deathtrapBlock = "";
        if (pose == ControlPose.TwoHandGesture && deathTrapCore != null)
        {
            var pub = deathTrapCore.GetComponentInParent<DeathTrapCommandPublisher>();
            if (pub != null)
                deathtrapBlock = $"DEATHTRAP: angle={pub.CurrentSphereAngle}";
        }

        debugText.text = $"{modeBlock}\n{netoBlock}\n{sauronBlock}\n{deathtrapBlock}";
    }

    void LogOnChange(ref string cached, string current, string message)
    {
        if (cached == current) return;
        cached = current;
        if (logToConsole) Debug.Log(message);
    }
}