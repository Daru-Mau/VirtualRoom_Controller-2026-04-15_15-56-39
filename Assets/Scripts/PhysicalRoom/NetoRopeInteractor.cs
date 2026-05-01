using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PhysicalRoom.UnityBridge
{
    /// <summary>
    /// Ceiling-hung handle that only moves vertically in a limited range.
    /// Maps Y displacement to Neto motor speed commands via OpenXR grab.
    ///
    /// Setup per Neto:
    ///   1. Create an empty anchor object at ceiling height (NetoX_Anchor).
    ///   2. Create the handle object as a child of a neutral-rest parent below the anchor.
    ///   3. Add XRGrabInteractable + Rigidbody to the handle.
    ///   4. Attach this script. Assign Publisher.
    ///   5. Set MaxPullDownMeters to match the real rope's travel range.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class NetoRopeInteractor : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        // Inspector
        // ──────────────────────────────────────────────

        [Header("References")]
        [Tooltip("NetoCommandPublisher on the same robot root. Auto-found in parent if null.")]
        [SerializeField] private NetoCommandPublisher publisher;

        [Header("Movement Range")]
        [Tooltip("Maximum distance the handle can travel downward from its neutral position (metres).")]
        [SerializeField, Min(0.01f)] private float maxPullDownMeters = 0.3f;

        [Tooltip("Small upward travel above neutral. Usually 0 for a real rope.")]
        [SerializeField, Min(0f)] private float maxPushUpMeters = 0.0f;

        [Header("Command Mapping")]
        [Tooltip("Normalised dead-zone around neutral. Handles rest jitter.")]
        [SerializeField, Range(0f, 0.3f)] private float deadZoneNormalized = 0.05f;

        [Tooltip("Flip pull/release if the motor runs backwards.")]
        [SerializeField] private bool invertDirection;

        [Header("Effects")]
        [SerializeField] private bool driveSoundFromTension = true;
        [SerializeField] private bool driveLedsFromTension  = true;
        [SerializeField, Range(0, 20)]  private int   tensionSoundVolume     = 12;
        [SerializeField, Range(0f, 1f)] private float ledActivationThreshold = 0.15f;

        [Header("Spring Return")]
        [Tooltip("Smoothly snap the handle back to neutral when released.")]
        [SerializeField] private bool springReturn = true;
        [SerializeField, Min(0.1f)] private float springSpeed = 4f;   // metres per second

        // ──────────────────────────────────────────────
        // Private state
        // ──────────────────────────────────────────────

        private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab;
        private Rigidbody           rb;

        // Captured at Awake — the handle's resting local position.
        private Vector3 neutralLocalPosition;

        // Captured at the moment of grab so we can compute relative delta.
        private float grabControllerWorldY;
        private float grabHandleLocalY;

        private VrRobotUdpBridge.NetoCommand lastSentCommand;
        private bool isGrabbed;

        // ──────────────────────────────────────────────
        // Lifecycle
        // ──────────────────────────────────────────────

        private void Awake()
        {
            grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            rb   = GetComponent<Rigidbody>();

            // We drive position entirely ourselves.
            grab.trackPosition = false;
            grab.trackRotation = false;

            // Kinematic so physics doesn't fight us, but XRI still fires events.
            rb.isKinematic = true;

            if (publisher == null)
                publisher = GetComponentInParent<NetoCommandPublisher>();

            neutralLocalPosition = transform.localPosition;

            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }

        private void OnEnable()
        {
            SendNeutral();
        }

        // ──────────────────────────────────────────────
        // XRI callbacks
        // ──────────────────────────────────────────────

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            isGrabbed = true;

            // Record world Y of the controller and the current handle local Y
            // so we can compute per-frame delta correctly.
            grabControllerWorldY = GetControllerWorldY(args.interactorObject);
            grabHandleLocalY     = transform.localPosition.y;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            isGrabbed = false;

            if (!springReturn)
            {
                // Snap immediately to neutral.
                SetHandleLocalY(neutralLocalPosition.y);
                SendNeutral();
            }
        }

        // ──────────────────────────────────────────────
        // Per-frame update
        // ──────────────────────────────────────────────

        private void Update()
        {
            if (isGrabbed)
            {
                // Resolve current interactor.
                if (!grab.isSelected)
                {
                    isGrabbed = false;
                    return;
                }

                var interactor = grab.interactorsSelecting[0];
                float controllerWorldY = GetControllerWorldY(interactor);
                float worldDeltaY      = controllerWorldY - grabControllerWorldY;

                // Convert world Y delta to local space (handles rotated parents).
                Vector3 localDelta = transform.parent != null
                    ? transform.parent.InverseTransformDirection(new Vector3(0f, worldDeltaY, 0f))
                    : new Vector3(0f, worldDeltaY, 0f);

                float targetLocalY = Mathf.Clamp(
                    grabHandleLocalY + localDelta.y,
                    neutralLocalPosition.y - maxPullDownMeters,
                    neutralLocalPosition.y + maxPushUpMeters
                );

                SetHandleLocalY(targetLocalY);
                UpdateCommand();
            }
            else if (springReturn)
            {
                float currentY  = transform.localPosition.y;
                float targetY   = neutralLocalPosition.y;

                if (!Mathf.Approximately(currentY, targetY))
                {
                    float newY = Mathf.MoveTowards(currentY, targetY, springSpeed * Time.deltaTime);
                    SetHandleLocalY(newY);
                    UpdateCommand();
                }
                else
                {
                    SendNeutral();
                }
            }
        }

        // ──────────────────────────────────────────────
        // Position helper
        // ──────────────────────────────────────────────

        private void SetHandleLocalY(float y)
        {
            transform.localPosition = new Vector3(
                neutralLocalPosition.x,
                y,
                neutralLocalPosition.z
            );
        }

        private static float GetControllerWorldY(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRInteractor interactor)
        {
            // Use the interactor's attach transform as the reference point.
            return interactor.transform.position.y;
        }

        // ──────────────────────────────────────────────
        // Command logic
        // ──────────────────────────────────────────────

        private void UpdateCommand()
        {
            float delta = transform.localPosition.y - neutralLocalPosition.y;

            // Pulling down → delta < 0 → normalized > 0 → motor toward 0.
            // Pushing up   → delta > 0 → normalized < 0 → motor toward 180.
            float range      = maxPullDownMeters > 0f ? maxPullDownMeters : 0.001f;
            float normalized = Mathf.Clamp(invertDirection ? (delta / range) : (-delta / range), -1f, 1f);
            float absNorm    = Mathf.Abs(normalized);

            if (absNorm <= deadZoneNormalized)
            {
                SendNeutral();
                return;
            }

            // Map to motor units: 90 = stop, 0 = full pull, 180 = full release.
            int speedUnits = normalized >= 0f
                ? Mathf.RoundToInt(Mathf.Lerp(90f,   0f, absNorm))
                : Mathf.RoundToInt(Mathf.Lerp(90f, 180f, absNorm));

            // LEDs scale with tension above threshold.
            int radius = 0, brightness = 0;
            if (driveLedsFromTension && absNorm > ledActivationThreshold)
            {
                float ledNorm = (absNorm - ledActivationThreshold) / (1f - ledActivationThreshold);
                radius     = Mathf.RoundToInt(Mathf.Lerp( 1f,  10f, ledNorm));
                brightness = Mathf.RoundToInt(Mathf.Lerp(30f, 255f, ledNorm));
            }

            bool soundOn = driveSoundFromTension;
            var cmd = new VrRobotUdpBridge.NetoCommand
            {
                Sound          = soundOn ? 1 : 0,
                Volume         = soundOn ? tensionSoundVolume : 0,
                MotorSpeedUnits = speedUnits,
                LedRadius      = radius,
                LedBrightness  = brightness
            };

            if (!CommandEquals(cmd, lastSentCommand))
            {
                lastSentCommand = cmd;
                publisher.SetState(soundOn, cmd.Volume, speedUnits, radius, brightness);
            }
        }

        private void SendNeutral()
        {
            var neutral = new VrRobotUdpBridge.NetoCommand
            {
                Sound = 0, Volume = 0, MotorSpeedUnits = 90, LedRadius = 0, LedBrightness = 0
            };
            if (!CommandEquals(neutral, lastSentCommand))
            {
                lastSentCommand = neutral;
                publisher.SetState(false, 0, 90, 0, 0);
            }
        }

        private static bool CommandEquals(VrRobotUdpBridge.NetoCommand a, VrRobotUdpBridge.NetoCommand b) =>
            a.Sound           == b.Sound    &&
            a.Volume          == b.Volume   &&
            a.MotorSpeedUnits == b.MotorSpeedUnits &&
            a.LedRadius       == b.LedRadius &&
            a.LedBrightness   == b.LedBrightness;

        // ──────────────────────────────────────────────
        // Editor gizmos
        // ──────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 neutralWorld = transform.parent != null
                ? transform.parent.TransformPoint(neutralLocalPosition)
                : neutralLocalPosition;

            // Draw allowed travel range.
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(neutralWorld, 0.03f);

            Gizmos.color = Color.yellow;
            Vector3 maxPull = neutralWorld + Vector3.down * maxPullDownMeters;
            Gizmos.DrawLine(neutralWorld, maxPull);
            Gizmos.DrawWireSphere(maxPull, 0.02f);

            if (maxPushUpMeters > 0f)
            {
                Gizmos.color = Color.cyan;
                Vector3 maxUp = neutralWorld + Vector3.up * maxPushUpMeters;
                Gizmos.DrawLine(neutralWorld, maxUp);
                Gizmos.DrawWireSphere(maxUp, 0.02f);
            }
        }
#endif
    }
}
