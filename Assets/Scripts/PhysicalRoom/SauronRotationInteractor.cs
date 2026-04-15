using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PhysicalRoom.UnityBridge
{
    /// <summary>
    /// Allows a VR user to manually rotate a floor-mounted Sauron avatar by grabbing it and dragging.
    ///
    ///   Horizontal drag  → yaw  → bottom servo [0..180]
    ///   Vertical drag    → pitch → top servo   [0..180]
    ///
    /// The Sauron object never moves position; only the visual pivot rotates.
    /// Use XRSimpleInteractable on this GameObject (not XRGrabInteractable).
    ///
    /// Setup per Sauron:
    ///   1. Add XRSimpleInteractable to the Sauron root (or a trigger-collider child).
    ///   2. Attach this script. Assign Publisher and VisualPivot.
    ///   3. Configure yaw/pitch ranges to match the physical servo limits.
    ///   4. The visual pivot should be the child transform that visually rotates
    ///      (e.g. SauronX_Head or the root itself if there's no separate head).
    /// </summary>
    [RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable))]
    public class SauronRotationInteractor : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        // Inspector
        // ──────────────────────────────────────────────

        [Header("References")]
        [Tooltip("SauronCommandPublisher on this robot. Auto-found on same GameObject if null.")]
        [SerializeField] private SauronCommandPublisher publisher;

        [Tooltip("The child transform that rotates visually in the scene (yaw + pitch applied here).")]
        [SerializeField] private Transform visualPivot;

        [Header("Yaw — Bottom Servo")]
        [Tooltip("Total yaw sweep in degrees (centred at 90 servo units).")]
        [SerializeField, Range(0f, 180f)] private float yawRangeDegrees = 150f;

        [Tooltip("Degrees of yaw per metre of horizontal controller travel.")]
        [SerializeField] private float yawSensitivity = 200f;

        [Header("Pitch — Top Servo")]
        [SerializeField] private bool enablePitch = true;

        [Tooltip("Total pitch sweep in degrees (centred at 90 servo units).")]
        [SerializeField, Range(0f, 90f)] private float pitchRangeDegrees = 80f;

        [Tooltip("Degrees of pitch per metre of vertical controller travel.")]
        [SerializeField] private float pitchSensitivity = 200f;

        [Header("Command Rate")]
        [SerializeField, Range(5f, 60f)] private float sendRateHz = 20f;

        [Header("On Release")]
        [Tooltip("Hold last position when released (true = stays pointing, false = returns to centre).")]
        [SerializeField] private bool holdPositionOnRelease = true;

        [Tooltip("If not holding, speed at which servos return to centre (degrees / second).")]
        [SerializeField, Min(1f)] private float returnSpeedDegsPerSec = 90f;

        // ──────────────────────────────────────────────
        // Private state
        // ──────────────────────────────────────────────

        private UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable interactable;
        private UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor currentInteractor;

        // Servo space: 0–180, centre = 90.
        private float currentYawServo = 90f;
        private float currentPitchServo = 90f;

        private Vector3 lastControllerPosition;
        private float nextSendTime;
        private int lastSentBottom = -1;
        private int lastSentTop = -1;
        private bool isGrabbed;

        // ──────────────────────────────────────────────
        // Lifecycle
        // ──────────────────────────────────────────────

        private void Awake()
        {
            interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();

            if (publisher == null) publisher = GetComponent<SauronCommandPublisher>();
            if (visualPivot == null) visualPivot = transform;

            interactable.selectEntered.AddListener(OnGrabbed);
            interactable.selectExited.AddListener(OnReleased);
        }

        private void OnEnable()
        {
            if (publisher != null)
                publisher.SetManualMode(false);

            // Reset visual to match current servo state.
            ApplyVisualRotation();
        }

        // ──────────────────────────────────────────────
        // XRI callbacks
        // ──────────────────────────────────────────────

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            isGrabbed = true;
            currentInteractor = args.interactorObject;
            lastControllerPosition = GetControllerWorldPosition();
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            isGrabbed = false;
            currentInteractor = null;
        }

        // ──────────────────────────────────────────────
        // Per-frame update
        // ──────────────────────────────────────────────

        private void Update()
        {
            if (isGrabbed && currentInteractor != null)
            {
                Vector3 controllerPos = GetControllerWorldPosition();
                Vector3 worldDelta = controllerPos - lastControllerPosition;
                lastControllerPosition = controllerPos;

                // Project controller delta into the Sauron's local space so that
                // "horizontal" always means along Sauron's local X regardless of room orientation.
                Vector3 localDelta = transform.InverseTransformDirection(worldDelta);

                float halfYaw = yawRangeDegrees * 0.5f;
                float halfPitch = pitchRangeDegrees * 0.5f;

                currentYawServo = Mathf.Clamp(
                    currentYawServo + localDelta.x * yawSensitivity,
                    90f - halfYaw, 90f + halfYaw
                );

                if (enablePitch)
                {
                    currentPitchServo = Mathf.Clamp(
                        currentPitchServo + localDelta.y * pitchSensitivity,
                        90f - halfPitch, 90f + halfPitch
                    );
                }
            }
            else if (!holdPositionOnRelease)
            {
                // Gently return to centre.
                float step = returnSpeedDegsPerSec * Time.deltaTime;
                currentYawServo = Mathf.MoveTowards(currentYawServo, 90f, step);
                currentPitchServo = Mathf.MoveTowards(currentPitchServo, 90f, step);
            }

            ApplyVisualRotation();
            TrySendCommand();
        }

        // ──────────────────────────────────────────────
        // Visual feedback
        // ──────────────────────────────────────────────

        private void ApplyVisualRotation()
        {
            // Convert servo space back to degrees relative to centre for rotation.
            float yawDeg = currentYawServo - 90f;  // negative = left
            float pitchDeg = currentPitchServo - 90f;  // positive = up

            // Euler: pitch rotates around local X (tilt), yaw rotates around local Y (spin).
            visualPivot.localRotation = Quaternion.Euler(-pitchDeg, yawDeg, 0f);
        }

        // ──────────────────────────────────────────────
        // UDP send
        // ──────────────────────────────────────────────

        private void TrySendCommand()
        {
            if (Time.time < nextSendTime) return;
            nextSendTime = Time.time + 1f / Mathf.Max(1f, sendRateHz);

            int bottom = Mathf.RoundToInt(currentYawServo);
            int top = Mathf.RoundToInt(currentPitchServo);

            if (bottom == lastSentBottom && top == lastSentTop) return;

            lastSentBottom = bottom;
            lastSentTop = top;
            publisher.SetBothServos(bottom, top);
        }

        // ──────────────────────────────────────────────
        // Public utilities
        // ──────────────────────────────────────────────

        /// <summary>Snap both servos to centre (90, 90).</summary>
        public void CentreServos()
        {
            currentYawServo = 90f;
            currentPitchServo = 90f;
            publisher.CenterServos();
            ApplyVisualRotation();
        }

        /// <summary>Set yaw servo directly in servo units [0..180].</summary>
        public void SetYawServo(int value)
        {
            currentYawServo = Mathf.Clamp(value, 0, 180);
        }

        /// <summary>Set pitch servo directly in servo units [0..180].</summary>
        public void SetPitchServo(int value)
        {
            currentPitchServo = Mathf.Clamp(value, 0, 180);
        }

        // ──────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────

        private Vector3 GetControllerWorldPosition()
        {
            return currentInteractor.GetAttachTransform(interactable).position;
        }

        // ──────────────────────────────────────────────
        // Editor gizmos
        // ──────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw yaw sweep arc.
            float halfYaw = yawRangeDegrees * 0.5f;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position,
                Quaternion.Euler(0, -halfYaw, 0) * transform.forward * 0.6f);
            Gizmos.DrawRay(transform.position,
                Quaternion.Euler(0,  halfYaw, 0) * transform.forward * 0.6f);

            // Draw pitch sweep arc.
            if (enablePitch)
            {
                float halfPitch = pitchRangeDegrees * 0.5f;
                Gizmos.color = Color.magenta;
                Gizmos.DrawRay(transform.position,
                    Quaternion.Euler(-halfPitch, 0, 0) * transform.forward * 0.5f);
                Gizmos.DrawRay(transform.position,
                    Quaternion.Euler( halfPitch, 0, 0) * transform.forward * 0.5f);
            }

            // Draw current facing direction.
            Gizmos.color = Color.yellow;
            float yawDeg   = currentYawServo   - 90f;
            float pitchDeg = currentPitchServo - 90f;
            Gizmos.DrawRay(transform.position,
                transform.rotation * Quaternion.Euler(-pitchDeg, yawDeg, 0) * Vector3.forward * 0.4f);
        }
#endif
    }
}
