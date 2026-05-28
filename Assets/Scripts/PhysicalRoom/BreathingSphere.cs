using UnityEngine;
public class BreathingSphere : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HubTelemetryReceiver telemetryReceiver;
    [Header("Sphere Settings")]
    [Tooltip("Local scale at rest (no breathing). Set this to match your room size.")]
    [SerializeField] private float baseScale = 10f;

    [Header("Demo Mode")]
    [Tooltip("Synthetic breathing when no hub is connected. Auto-disabled when real telemetry arrives.")]
    [SerializeField] private bool demoMode = false;

    [Tooltip("How much the sphere expands/contracts as a fraction of baseScale. 0.1 = 10%")]
    [SerializeField, Range(0f, 0.5f)] private float breathMagnitude = 0.1f;
    [Tooltip("How fast the sphere lerps toward the target size.")]
    [SerializeField] private float smoothSpeed = 3f;
    private float _directionSign;
    private float _rawLevel;
    private float _smoothedLevel;
    private int _lastState = -1;
    private float _exposeFactor;
    private void Start()
    {
        if (telemetryReceiver == null)
            telemetryReceiver = FindObjectOfType<HubTelemetryReceiver>();
        if (telemetryReceiver != null)
            telemetryReceiver.BreatherTelemetryReceived += OnBreather;
    }
    private void OnDestroy()
    {
        if (telemetryReceiver != null)
            telemetryReceiver.BreatherTelemetryReceived -= OnBreather;
    }

    public void SetExposeFactor(float factor)
    {
        _exposeFactor = factor;
    }
    private void OnBreather(HubTelemetryReceiver.BreatherTelemetry t)
    {
        demoMode = false;  // ← auto turn off when hub sends data

        _rawLevel = t.Level / 100f;
        if (t.State != _lastState)
        {
            _lastState = t.State;
            _directionSign = t.State switch
            {
                1 => 1f,   // inhale → expand
                2 => -1f,  // exhale → contract
                _ => 0f    // hold → rest
            };
        }
    }
    private void Update()
    {
        if (demoMode)
        {
            // Smooth ~4-second inhale/exhale cycle
            float cycle = Time.time * (Mathf.PI * 0.5f);
            _rawLevel = Mathf.Abs(Mathf.Sin(cycle));
            _directionSign = Mathf.Cos(cycle) >= 0f ? 1f : -1f;
        }

        _smoothedLevel = Mathf.Lerp(_smoothedLevel, _rawLevel, Time.deltaTime * smoothSpeed);
        float breathOffset = _directionSign * _smoothedLevel * breathMagnitude;
        float target = baseScale * (1f + breathOffset) * (1f + _exposeFactor);
        float current = transform.localScale.x;
        float s = Mathf.Lerp(current, target, Time.deltaTime * smoothSpeed);
        transform.localScale = Vector3.one * s;
    }
}