using UnityEngine;

/// <summary>
/// Drives the visual rotation of a Sauron robot in the VR scene.
///
/// Attach to the Sauron_X GameObject (same one that has SauronCommandPublisher).
///
/// How it works:
///   Bottom servo (yaw)  — rotates the base/body left/right around the Y axis.
///   Top servo    (tilt) — rotates the head/camera up/down around a chosen axis.
///
///   Servo 90  → centre (0°).
///   Servo 0   → one extreme  (−maxDegrees).
///   Servo 180 → other extreme (+maxDegrees).
///
/// Scene setup:
///   Assign the two pivot Transforms in the Inspector.
///   For a typical Sauron model:
///     • YawPivot  = the base rotating part  (child of the Sauron root)
///     • TiltPivot = the camera/head part     (child of YawPivot)
/// </summary>
public class SauronVisualDriver : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Filled automatically from this GameObject if left empty.")]
    [SerializeField] private SauronCommandPublisher publisher;

    [Tooltip("Transform that rotates around Y for the bottom servo (yaw/pan).")]
    [SerializeField] private Transform yawPivot;

    [Tooltip("Transform that rotates around X (or Z) for the top servo (tilt/pitch).")]
    [SerializeField] private Transform tiltPivot;

    [Header("Yaw (Bottom Servo)")]
    [Tooltip("Maximum yaw rotation in degrees at servo extremes (0 or 180).")]
    [SerializeField] private float yawMaxDegrees = 90f;
    [Tooltip("Flip yaw direction if the physical robot yaws the wrong way.")]
    [SerializeField] private bool invertYaw = false;

    [Header("Tilt (Top Servo)")]
    [Tooltip("Maximum tilt rotation in degrees at servo extremes (0 or 180).")]
    [SerializeField] private float tiltMaxDegrees = 45f;
    [Tooltip("Local axis the tilt pivot rotates around. Usually Vector3.right (X).")]
    [SerializeField] private Vector3 tiltAxis = Vector3.right;
    [Tooltip("Flip tilt direction if the physical robot tilts the wrong way.")]
    [SerializeField] private bool invertTilt = false;

    [Header("Smoothing")]
    [Tooltip("Lerp speed for rotation smoothing. Set to 0 to disable (instant).")]
    [SerializeField] private float smoothSpeed = 15f;

    [Header("TEMP: Direct Binding")]
    [Tooltip("When enabled, reads yaw/tilt pivot rotations and sends them as servo commands instead of driving pivots from servos.")]
    [SerializeField] private bool bindToPivot = false;

    // ── Runtime ──────────────────────────────────────────────────────────
    private Quaternion _yawTarget = Quaternion.identity;
    private Quaternion _tiltTarget = Quaternion.identity;

    // ─────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (publisher == null)
            publisher = GetComponent<SauronCommandPublisher>();

        if (publisher == null)
        {
            Debug.LogError($"[SauronVisualDriver] '{gameObject.name}': No SauronCommandPublisher found.");
            enabled = false;
            return;
        }

        // Initialise to current servo state (usually centred)
        if (yawPivot != null) _yawTarget = yawPivot.localRotation;
        if (tiltPivot != null) _tiltTarget = tiltPivot.localRotation;
    }

    void Update()
    {
        if (publisher == null) return;

        if (bindToPivot)
        {
            BindFromPivot();
        }
        else
        {
            UpdateYaw();
            UpdateTilt();
        }
    }

    // ── Yaw ───────────────────────────────────────────────────────────────

    void UpdateYaw()
    {
        if (yawPivot == null) return;

        // Map servo 0–180 → degrees: 90 = 0°, 0 = -max, 180 = +max
        float t = (publisher.BottomServoAngle - 90f) / 90f;  // −1 … +1
        float deg = t * yawMaxDegrees * (invertYaw ? -1f : 1f);

        _yawTarget = Quaternion.AngleAxis(deg, Vector3.up);

        yawPivot.localRotation = smoothSpeed > 0f
            ? Quaternion.Lerp(yawPivot.localRotation, _yawTarget, Time.deltaTime * smoothSpeed)
            : _yawTarget;
    }

    // ── Tilt ──────────────────────────────────────────────────────────────

    void UpdateTilt()
    {
        if (tiltPivot == null) return;

        float t = (publisher.TopServoAngle - 90f) / 90f;     // −1 … +1
        float deg = t * tiltMaxDegrees * (invertTilt ? -1f : 1f);

        _tiltTarget = Quaternion.AngleAxis(deg, tiltAxis.normalized);

        tiltPivot.localRotation = smoothSpeed > 0f
            ? Quaternion.Lerp(tiltPivot.localRotation, _tiltTarget, Time.deltaTime * smoothSpeed)
            : _tiltTarget;
    }

    // ── TEMP: Direct Binding (Pivot → Servo) ───────────────────────────

    void BindFromPivot()
    {
        if (yawPivot != null)
        {
            float yawAngle = NormalizeAngle(yawPivot.localEulerAngles.y);
            int bottom = Mathf.RoundToInt(Mathf.Lerp(0f, 180f,
                Mathf.InverseLerp(-yawMaxDegrees, yawMaxDegrees, yawAngle * (invertYaw ? -1f : 1f))));
            publisher.SetBottomServo(bottom);
        }

        if (tiltPivot != null)
        {
            Vector3 localEuler = tiltPivot.localEulerAngles;
            float tiltAngle = tiltAxis == Vector3.right ? NormalizeAngle(localEuler.x)
                           : tiltAxis == Vector3.forward ? NormalizeAngle(localEuler.z)
                           : Vector3.Dot(tiltPivot.localRotation * Vector3.forward, tiltAxis) * Mathf.Rad2Deg;
            int top = Mathf.RoundToInt(Mathf.Lerp(0f, 180f,
                Mathf.InverseLerp(-tiltMaxDegrees, tiltMaxDegrees, tiltAngle * (invertTilt ? -1f : 1f))));
            publisher.SetTopServo(top);
        }
    }

    static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Visualise the yaw arc in editor
        if (yawPivot == null) return;
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.5f);
        Vector3 origin = yawPivot.position;
        float   r      = 0.3f;
        int     steps  = 20;
        for (int i = 0; i < steps; i++)
        {
            float a0 = Mathf.Lerp(-yawMaxDegrees, yawMaxDegrees, i       / (float)steps);
            float a1 = Mathf.Lerp(-yawMaxDegrees, yawMaxDegrees, (i + 1) / (float)steps);
            Vector3 p0 = origin + Quaternion.AngleAxis(a0, Vector3.up) * yawPivot.forward * r;
            Vector3 p1 = origin + Quaternion.AngleAxis(a1, Vector3.up) * yawPivot.forward * r;
            Gizmos.DrawLine(p0, p1);
        }
    }
#endif
}