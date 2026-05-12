using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Central spatial audio manager for the breathing room.
///
/// Subscribes to HubTelemetryReceiver events for all robots and drives
/// their AudioSource layers to reflect biological / nervous-system states
/// rather than mechanical operation.
///
/// ── Setup ─────────────────────────────────────────────────────────────────
/// 1. Place this component on your AudioManager GameObject.
/// 2. Assign the HubTelemetryReceiver reference.
/// 3. Expand each robot audio group in the Inspector and assign:
///    - The robot's Transform (for 3D positioning)
///    - One AudioSource per layer (create child GameObjects on each robot)
///    - AudioClips for each layer
/// 4. Assign a BreatherTelemetryReceiver if using direct UDP for the breather.
///
/// ── AudioSource settings reminder ────────────────────────────────────────
/// Every AudioSource used here:
///   Spatial Blend  = 1.0
///   Spatialize     = true  (requires Meta XR Audio or Steam Audio plugin)
///   Rolloff        = Logarithmic
///   Doppler Level  = 0
///   Play On Awake  = false (this script manages playback)
/// </summary>
public class RoomAudioManager : MonoBehaviour
{
    // ── References ────────────────────────────────────────────────────────

    [Header("Telemetry")]
    [SerializeField] private HubTelemetryReceiver hubReceiver;

    [Header("Global")]
    [Tooltip("Optional mixer for master volume / effects routing.")]
    [SerializeField] private AudioMixer mixer;

    [Tooltip("Low-pass filter speed for all level smoothing. " +
             "Lower = more lag but smoother transitions.")]
    [SerializeField] private float globalSmoothSpeed = 3f;

    // ── Per-robot audio groups ─────────────────────────────────────────────

    [Header("Breather Audio")]
    [SerializeField] private BreatherAudio breather;

    [Header("Sauron Audio (one entry per Sauron robot)")]
    [SerializeField] private SauronAudio[] saurons;

    [Header("Neto Audio (one entry per Neto robot)")]
    [SerializeField] private NetoAudio[] netos;

    [Header("Deathtrap Audio")]
    [SerializeField] private DeathtrapAudio deathtrap;

    [Header("Mycelium Ambience")]
    [SerializeField] private MyceliumAmbience ambience;

    // ── Private state ──────────────────────────────────────────────────────

    private float _breathLevel;       // smoothed 0–1
    private int _breathState;       // 0 hold, 1 inhale, 2 exhale

    // ─────────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (hubReceiver != null)
        {
            hubReceiver.BreatherTelemetryReceived += OnHubBreather;
            hubReceiver.SauronTelemetryReceived += OnSauron;
            hubReceiver.NetoTelemetryReceived += OnNeto;
            hubReceiver.DeathtrapTelemetryReceived += OnDeathtrap;
        }

        StartAllLoops();
    }

    private void OnDestroy()
    {
        if (hubReceiver != null)
        {
            hubReceiver.BreatherTelemetryReceived -= OnHubBreather;
            hubReceiver.SauronTelemetryReceived -= OnSauron;
            hubReceiver.NetoTelemetryReceived -= OnNeto;
            hubReceiver.DeathtrapTelemetryReceived -= OnDeathtrap;
        }

    }

    private void StartAllLoops()
    {
        PlayLoop(breather.droneSource);
        PlayLoop(breather.textureSource);
        PlayLoop(ambience.padSource);
        PlayLoop(deathtrap.pulseSource);

        foreach (var s in saurons)
            PlayLoop(s.shimmerSource);

        foreach (var n in netos)
            PlayLoop(n.tensionSource);
    }

    // ── Update ────────────────────────────────────────────────────────────

    private void Update()
    {
        UpdateBreatherAudio();
        UpdateAmbienceAudio();
    }

    // ── Breather ──────────────────────────────────────────────────────────

    private void OnHubBreather(HubTelemetryReceiver.BreatherTelemetry t)
    {
        _breathState = t.State;
        _breathLevel = Mathf.Lerp(_breathLevel, t.Level / 100f,
                                  Time.deltaTime * globalSmoothSpeed);
    }

    private void OnDirectBreather(HubTelemetryReceiver.BreatherTelemetry t)
    {
        _breathState = (int)t.State;
        _breathLevel = Mathf.Lerp(_breathLevel, t.Level / 100f,
                                  Time.deltaTime * globalSmoothSpeed);
    }

    private void UpdateBreatherAudio()
    {
        if (breather.droneSource == null) return;

        // Pitch: rises on inhale, falls on exhale, neutral on hold
        float pitchTarget = _breathState switch
        {
            1 => Mathf.Lerp(1f, 1.08f, _breathLevel),  // inhale
            2 => Mathf.Lerp(0.95f, 1f, _breathLevel),  // exhale
            _ => 1f                                         // hold
        };

        breather.droneSource.pitch = Mathf.Lerp(
            breather.droneSource.pitch, pitchTarget,
            Time.deltaTime * globalSmoothSpeed
        );

        // Breath texture volume scales with level
        if (breather.textureSource != null)
            breather.textureSource.volume = _breathLevel * 0.45f;

        // Resonance bloom: a one-shot that fires when inhale peaks above threshold
        if (_breathState == 1 && _breathLevel > 0.75f)
            TriggerOneShot(breather.resonanceSource, breather.resonanceClip, 0.3f);
    }

    // ── Sauron ────────────────────────────────────────────────────────────

    private void OnSauron(HubTelemetryReceiver.SauronTelemetry t)
    {
        // Find matching sauron entry by robot ID
        foreach (var s in saurons)
        {
            if (s.robotId != t.RobotId) continue;

            // Pitch follows pan angle (0–180 mapped to pitch range)
            // Marker 'S' = Sauron, 'H' = Head variant — both handled
            float normAngle = (t.Touched > 0)
                ? 1f
                : Mathf.Clamp01((float)t.DangerZone / 100f);

            if (s.shimmerSource != null)
            {
                s.shimmerSource.pitch = Mathf.Lerp(0.9f, 1.15f, normAngle);
                s.shimmerSource.volume = Mathf.Lerp(0.02f, 0.12f, normAngle);
            }

            // Touch event → bell strike one-shot
            if (t.Touched > 0)
                TriggerOneShot(s.touchSource, s.touchClip, 0.5f);

            // Danger zone → add dissonance via pitch of second shimmer layer
            if (s.dangerShimmerSource != null)
            {
                float dissonance = t.DangerZone > 0 ? 0.08f : 0f;
                s.dangerShimmerSource.volume = Mathf.Lerp(
                    s.dangerShimmerSource.volume, t.DangerZone > 0 ? 0.1f : 0f,
                    Time.deltaTime * globalSmoothSpeed
                );
                s.dangerShimmerSource.pitch = s.shimmerSource != null
                    ? s.shimmerSource.pitch + dissonance
                    : 1f + dissonance;
            }

            break;
        }
    }

    // ── Neto ──────────────────────────────────────────────────────────────

    private void OnNeto(HubTelemetryReceiver.NetoTelemetry t)
    {
        foreach (var n in netos)
        {
            if (n.robotId != t.RobotId) continue;

            // Mic level in physical room → sympathetic resonance in VR
            float micNorm = Mathf.Clamp01(t.MicLevel / 255f);
            if (micNorm > 0.6f)
                TriggerOneShot(n.sympathySource, n.sympathyClip,
                               micNorm * 0.4f);

            // Danger flag → tighten the tension source
            if (n.tensionSource != null)
            {
                n.tensionSource.volume = Mathf.Lerp(
                    n.tensionSource.volume,
                    n.dangerFlag ? 0.35f : 0.08f,
                    Time.deltaTime * globalSmoothSpeed
                );
                n.dangerFlag = t.DangerFlag > 0;
            }

            break;
        }
    }

    // ── Deathtrap ─────────────────────────────────────────────────────────

    private void OnDeathtrap(HubTelemetryReceiver.DeathtrapTelemetry t)
    {
        if (deathtrap.pulseSource == null) return;

        // Proximity: MinDistance in metres — closer = louder, faster pulse
        float proximity = 1f - Mathf.Clamp01(t.MinDistance / deathtrap.maxDistance);

        // Pulse rate: maps proximity to pitch (pulse frequency)
        // At proximity 0 → silence. At proximity 1 → fast low pulse.
        deathtrap.pulseSource.volume = proximity * 0.5f;
        deathtrap.pulseSource.pitch = Mathf.Lerp(0.5f, 1.4f, proximity);

        // Touch event → pressure impact one-shot
        if (t.TouchLevel > 0)
            TriggerOneShot(deathtrap.touchImpactSource, deathtrap.touchImpactClip,
                           Mathf.Clamp01(t.TouchLevel / 100f) * 0.6f);
    }

    // ── Mycelium ambience ─────────────────────────────────────────────────

    private void UpdateAmbienceAudio()
    {
        if (ambience.padSource == null) return;

        // Pad subtly brightens on inhale via pitch micro-shift
        float pitchNudge = _breathState == 1 ? _breathLevel * 0.015f : 0f;
        ambience.padSource.pitch = Mathf.Lerp(
            ambience.padSource.pitch, 1f + pitchNudge,
            Time.deltaTime * (globalSmoothSpeed * 0.3f)   // very slow, geological
        );
    }

    // ── Utility ───────────────────────────────────────────────────────────

    /// <summary>
    /// Plays a one-shot clip on a source only if it isn't already playing,
    /// preventing rapid-fire retriggering.
    /// </summary>
    private static void TriggerOneShot(AudioSource source, AudioClip clip, float volume)
    {
        if (source == null || clip == null || source.isPlaying) return;
        source.PlayOneShot(clip, volume);
    }

    private static void PlayLoop(AudioSource source)
    {
        if (source == null || source.isPlaying) return;
        source.loop = true;
        source.Play();
    }
}

// ── Data structures ───────────────────────────────────────────────────────

[System.Serializable]
public class BreatherAudio
{
    [Tooltip("Continuous low drone, pitched by breath state.")]
    public AudioSource droneSource;

    [Tooltip("Breath texture layer — filtered air sound. Volume follows Level.")]
    public AudioSource textureSource;

    [Tooltip("One-shot resonance bloom source. Fires at peak inhale.")]
    public AudioSource resonanceSource;
    public AudioClip resonanceClip;
}

[System.Serializable]
public class SauronAudio
{
    [Tooltip("Must match the RobotId coming from HubTelemetryReceiver.")]
    public int robotId;

    [Tooltip("Continuous high shimmer, volume/pitch follow attention state.")]
    public AudioSource shimmerSource;

    [Tooltip("Second shimmer layer, slightly detuned when DangerZone active.")]
    public AudioSource dangerShimmerSource;

    [Tooltip("One-shot bell on touch event.")]
    public AudioSource touchSource;
    public AudioClip touchClip;

    [System.NonSerialized]
    public bool wasInDanger;
}

[System.Serializable]
public class NetoAudio
{
    public int robotId;

    [Tooltip("Continuous low tension hum. Volume reflects danger/load state.")]
    public AudioSource tensionSource;

    [Tooltip("One-shot sympathetic resonance when mic level spikes in physical room.")]
    public AudioSource sympathySource;
    public AudioClip sympathyClip;

    [System.NonSerialized]
    public bool dangerFlag;
}

[System.Serializable]
public class DeathtrapAudio
{
    [Tooltip("Looping subsonic pulse. Volume and pitch follow proximity.")]
    public AudioSource pulseSource;

    [Tooltip("Distance (metres) at which Deathtrap audio starts. " +
             "Beyond this = silence.")]
    public float maxDistance = 4f;

    [Tooltip("One-shot pressure impact on touch.")]
    public AudioSource touchImpactSource;
    public AudioClip touchImpactClip;
}

[System.Serializable]
public class MyceliumAmbience
{
    [Tooltip("The room's resting voice — a long slow evolving pad. " +
             "Should not be 3D spatialised — attach to the AudioManager itself, " +
             "not a robot. Spatial Blend = 0 (fully 2D).")]
    public AudioSource padSource;
}
