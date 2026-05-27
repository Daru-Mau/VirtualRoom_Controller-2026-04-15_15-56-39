using UnityEngine;

/// <summary>
/// Central spatial audio manager for the breathing room.
///
/// Drives AudioSources from HubTelemetryReceiver events, mapping
/// robot telemetry to state-driven or continuous audio.
///
/// ── Hierarchy to create ──────────────────────────────────────────────
///
///   SteamAudio (or any manager GameObject)
///   ├─ PadSource (non-spatialized Mycelium ambience)
///   │   AudioSource: Spatial Blend = 0, Play On Awake = false, Loop = true
///   │   → dragged into RoomAudioManager.ambience.padSource
///   │
///   BreathingRoom (the mycelium structure)
///   ├─ DroneSource
///   │   AudioSource: Spatial Blend = 1, Spatialize = true, Loop = true
///   │   → dragged into RoomAudioManager.breather.droneSource
///   ├─ TextureSource
///   │   AudioSource: Spatial Blend = 1, Spatialize = true, Loop = true
///   │   → dragged into RoomAudioManager.breather.textureSource
///   └─ ResonanceSource
///       AudioSource: Spatial Blend = 1, Spatialize = true, Loop = false
///       → dragged into RoomAudioManager.breather.resonanceSource
///       AudioClip → dragged into RoomAudioManager.breather.resonanceClip
///
///   Neto_1_Rig
///   └─ NetoSound
///       AudioSource: Spatial Blend = 1, Spatialize = true, Loop = false
///       → dragged into RoomAudioManager.netos[?].source
///       Clips assigned in inspector per state
///
///   Sauron_1
///   ├─ ShimmerSource
///   │   AudioSource: Spatial Blend = 1, Spatialize = true, Loop = true
///   │   → dragged into RoomAudioManager.saurons[?].shimmerSource
///   └─ TouchSource
///       AudioSource: Spatial Blend = 1, Spatialize = true, Loop = false
///       → dragged into RoomAudioManager.saurons[?].touchSource
///       AudioClip → dragged into RoomAudioManager.saurons[?].touchClip
///
///   Deathtrap
///   ├─ PulseSource
///   │   AudioSource: Spatial Blend = 1, Spatialize = true, Loop = true
///   │   → dragged into RoomAudioManager.deathtrap.pulseSource
///   └─ ImpactSource
///       AudioSource: Spatial Blend = 1, Spatialize = true, Loop = false
///       → dragged into RoomAudioManager.deathtrap.impactSource
///       AudioClip → dragged into RoomAudioManager.deathtrap.impactClip
///
/// ── AudioSource settings reminder ────────────────────────────────────
/// Every 3D AudioSource used here:
///   Spatial Blend  = 1.0
///   Spatialize     = true  (requires Steam Audio plugin)
///   Rolloff        = Logarithmic
///   Doppler Level  = 0
///   Play On Awake  = false (this script manages playback)
/// </summary>
public class RoomAudioManager : MonoBehaviour
{
    [Header("Telemetry")]
    [SerializeField] private HubTelemetryReceiver hubReceiver;

    [Header("Global")]
    [SerializeField] private float globalSmoothSpeed = 3f;

    [Header("Breather — the mycelium room's breathing")]
    [Tooltip("Drone = deep low hum of the structure, pitch follows inhale/exhale")]
    [SerializeField] private BreatherAudio breather;

    [Header("Neto — one entry per robot")]
    [SerializeField] private NetoAudio[] netos;

    [Header("Sauron — one entry per robot")]
    [SerializeField] private SauronAudio[] saurons;

    [Header("Deathtrap")]
    [SerializeField] private DeathtrapAudio deathtrap;

    [Header("Mycelium Ambience — non-spatialized room pad")]
    [SerializeField] private MyceliumAmbience ambience;

    [Header("Neto State Threshold")]
    [SerializeField, Range(0, 255)] private int micThreshold = 30;

    // ── Breather state ────────────────────────────────────────────────

    private float _breathLevel;
    private int _breathState;

    // ── Lifecycle ─────────────────────────────────────────────────────

    private void Start()
    {
        if (hubReceiver != null)
        {
            hubReceiver.BreatherTelemetryReceived += OnBreather;
            hubReceiver.NetoTelemetryReceived += OnNeto;
            hubReceiver.SauronTelemetryReceived += OnSauron;
            hubReceiver.DeathtrapTelemetryReceived += OnDeathtrap;
        }

        PlayLoop(breather.droneSource);
        PlayLoop(breather.textureSource);
        PlayLoop(ambience.padSource);
        PlayLoop(deathtrap.pulseSource);

        foreach (var s in saurons)
            PlayLoop(s.shimmerSource);
    }

    private void OnDestroy()
    {
        if (hubReceiver != null)
        {
            hubReceiver.BreatherTelemetryReceived -= OnBreather;
            hubReceiver.NetoTelemetryReceived -= OnNeto;
            hubReceiver.SauronTelemetryReceived -= OnSauron;
            hubReceiver.DeathtrapTelemetryReceived -= OnDeathtrap;
        }
    }

    private void Update()
    {
        UpdateBreather();
        UpdateAmbience();
    }

    // ── Breather ──────────────────────────────────────────────────────
    // The mycelium room breathes as one organism.
    //   Drone    → continuous low hum of the structure
    //   Texture  → airy surface rustling of the capsules
    //   Resonance → deep pulse at peak inhale

    private void OnBreather(HubTelemetryReceiver.BreatherTelemetry t)
    {
        _breathState = t.State;
        _breathLevel = Mathf.Lerp(_breathLevel, t.Level / 100f, Time.deltaTime * globalSmoothSpeed);
    }

    private void UpdateBreather()
    {
        if (breather.droneSource == null) return;

        float pitchTarget = _breathState switch
        {
            1 => Mathf.Lerp(1f, 1.08f, _breathLevel),
            2 => Mathf.Lerp(0.95f, 1f, _breathLevel),
            _ => 1f
        };

        breather.droneSource.pitch = Mathf.Lerp(
            breather.droneSource.pitch, pitchTarget, Time.deltaTime * globalSmoothSpeed);

        if (breather.textureSource != null)
            breather.textureSource.volume = _breathLevel * 0.45f;

        if (_breathState == 1 && _breathLevel > 0.75f)
            TriggerOneShot(breather.resonanceSource, breather.resonanceClip, 0.3f);
    }

    // ── Neto ──────────────────────────────────────────────────────────
    // 4 states derived from DangerFlag + MicLevel:
    //   Idle   = DangerFlag=0, MicLevel ≤ threshold
    //   Alert  = DangerFlag=0, MicLevel > threshold
    //   Danger = DangerFlag=1, MicLevel ≤ threshold
    //   Shriek = DangerFlag=1, MicLevel > threshold
    //
    // Each robot has 1 AudioSource. Clip swaps on state change.

    private void OnNeto(HubTelemetryReceiver.NetoTelemetry t)
    {
        foreach (var n in netos)
        {
            if (n.robotId != t.RobotId) continue;

            NetoAudio.State newState = (t.DangerFlag > 0, t.MicLevel > micThreshold) switch
            {
                (false, false) => NetoAudio.State.Idle,
                (false, true)  => NetoAudio.State.Alert,
                (true,  false) => NetoAudio.State.Danger,
                (true,  true)  => NetoAudio.State.Shriek,
            };

            if (!n.hasState || newState != n.currentState)
            {
                n.hasState = true;
                n.currentState = newState;
                PlayClip(n.source, n.StateClip(newState));
            }

            break;
        }
    }

    // ── Sauron ────────────────────────────────────────────────────────
    // Continuous shimmer loop, pitch/volume follow attention.
    // Touch → one-shot bell strike.

    private void OnSauron(HubTelemetryReceiver.SauronTelemetry t)
    {
        foreach (var s in saurons)
        {
            if (s.robotId != t.RobotId) continue;

            float intensity = t.Touched > 0
                ? 1f
                : Mathf.Clamp01(t.DangerZone / 100f);

            if (s.shimmerSource != null)
            {
                s.shimmerSource.pitch = Mathf.Lerp(0.9f, 1.15f, intensity);
                s.shimmerSource.volume = Mathf.Lerp(0.02f, 0.12f, intensity);
            }

            if (t.Touched > 0)
                TriggerOneShot(s.touchSource, s.touchClip, 0.5f);

            break;
        }
    }

    // ── Deathtrap ─────────────────────────────────────────────────────
    // Pulse loop: closer = louder + faster.
    // Impact one-shot on touch.

    private void OnDeathtrap(HubTelemetryReceiver.DeathtrapTelemetry t)
    {
        if (deathtrap.pulseSource == null) return;

        float proximity = 1f - Mathf.Clamp01(t.MinDistance / deathtrap.maxDistance);

        deathtrap.pulseSource.volume = proximity * 0.5f;
        deathtrap.pulseSource.pitch = Mathf.Lerp(0.5f, 1.4f, proximity);

        if (t.TouchLevel > 0)
            TriggerOneShot(deathtrap.impactSource, deathtrap.impactClip,
                           Mathf.Clamp01(t.TouchLevel / 100f) * 0.6f);
    }

    // ── Mycelium ambience ─────────────────────────────────────────────

    private void UpdateAmbience()
    {
        if (ambience.padSource == null) return;

        float pitchNudge = _breathState == 1 ? _breathLevel * 0.015f : 0f;
        ambience.padSource.pitch = Mathf.Lerp(
            ambience.padSource.pitch, 1f + pitchNudge,
            Time.deltaTime * (globalSmoothSpeed * 0.3f));
    }

    // ── Utility ───────────────────────────────────────────────────────

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

    private static void PlayClip(AudioSource source, AudioClip clip)
    {
        if (source == null || clip == null) return;
        if (source.isPlaying) source.Stop();
        source.clip = clip;
        source.Play();
    }
}

// ── Data structures ───────────────────────────────────────────────────

[System.Serializable]
public class BreatherAudio
{
    [Tooltip("Deep continuous hum of the mycelium structure. " +
             "Pitch rises on inhale, falls on exhale. " +
             "Place on the BreathingRoom GameObject.")]
    public AudioSource droneSource;

    [Tooltip("Airy surface rustling of the breathing capsules. " +
             "Volume follows breath level. " +
             "Place on the BreathingRoom GameObject.")]
    public AudioSource textureSource;

    [Tooltip("One-shot deep pulse at peak inhale — the room's heartbeat. " +
             "Place on the BreathingRoom GameObject.")]
    public AudioSource resonanceSource;
    public AudioClip resonanceClip;
}

[System.Serializable]
public class NetoAudio
{
    public enum State { Idle, Alert, Danger, Shriek }

    public int robotId;

    [Tooltip("Single AudioSource. Clip swaps as the robot's state changes " +
             "(Idle → Alert → Danger → Shriek). " +
             "Place as a child on the robot's Rig GameObject.")]
    public AudioSource source;

    public AudioClip idleClip;
    public AudioClip alertClip;
    public AudioClip dangerClip;
    public AudioClip shriekClip;

    [System.NonSerialized]
    public State currentState;
    [System.NonSerialized]
    public bool hasState;

    public AudioClip StateClip(State state) => state switch
    {
        State.Idle   => idleClip,
        State.Alert  => alertClip,
        State.Danger => dangerClip,
        State.Shriek => shriekClip,
        _            => idleClip
    };
}

[System.Serializable]
public class SauronAudio
{
    public int robotId;

    [Tooltip("Continuous shimmer loop. Pitch/volume follow attention. " +
             "Place as a child on the Sauron GameObject.")]
    public AudioSource shimmerSource;

    [Tooltip("One-shot bell strike on touch event. " +
             "Place as a child on the Sauron GameObject.")]
    public AudioSource touchSource;
    public AudioClip touchClip;
}

[System.Serializable]
public class DeathtrapAudio
{
    [Tooltip("Looping subsonic pulse. Volume/pitch follow proximity. " +
             "Place as a child on the Deathtrap GameObject.")]
    public AudioSource pulseSource;

    [Tooltip("Distance (m) at which Deathtrap audio starts. Beyond this = silence.")]
    public float maxDistance = 4f;

    [Tooltip("One-shot pressure impact on touch. " +
             "Place as a child on the Deathtrap GameObject.")]
    public AudioSource impactSource;
    public AudioClip impactClip;
}

[System.Serializable]
public class MyceliumAmbience
{
    [Tooltip("The room's resting voice — a long slow evolving pad. " +
             "Non-spatialized (Spatial Blend = 0). " +
             "Place on the AudioManager GameObject itself, not on a robot.")]
    public AudioSource padSource;
}
