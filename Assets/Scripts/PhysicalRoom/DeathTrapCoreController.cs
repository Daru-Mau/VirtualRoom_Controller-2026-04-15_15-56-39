using UnityEngine;

public class DeathTrapCoreController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DeathTrapCommandPublisher publisher;
    [SerializeField] private BreathingSphere membrane;

    [Header("Trajectory")]
    [Tooltip("Direction the core moves when exposing (local space). " +
             "Leave as (0,0,0) to auto-calculate toward the membrane center.")]
    [SerializeField] private Vector3 entryDirection = Vector3.zero;
    [Tooltip("How far (world units) the core travels at full openness.")]
    [SerializeField] private float entryDepth = 3f;

    [Header("Membrane Visual")]
    [Tooltip("Extra uniform scale applied to the membrane at full openness " +
             "(e.g. 0.1 = 10% bigger).")]
    [SerializeField] private float membraneScaleAmplitude = 0.1f;

    [Header("Animation")]
    [SerializeField] private float smoothSpeed = 5f;

    private Vector3 _restPosition;
    private float _exposeCurrent;
    private float _exposeTarget;

    private void Awake()
    {
        _restPosition = transform.localPosition;

        if (publisher == null)
            publisher = GetComponentInParent<DeathTrapCommandPublisher>();

        if (entryDirection == Vector3.zero && membrane != null)
        {
            Vector3 worldToCenter = (membrane.transform.position - transform.position).normalized;
            entryDirection = transform.InverseTransformDirection(worldToCenter).normalized;
        }
    }

    public void SetExpose(float openness)
    {
        _exposeTarget = Mathf.Clamp01(openness);
    }

    public void EndExpose()
    {
        _exposeTarget = 0f;
    }

    private void Update()
    {
        _exposeCurrent = Mathf.Lerp(_exposeCurrent, _exposeTarget, Time.deltaTime * smoothSpeed);

        if (_exposeCurrent < 0.001f && _exposeTarget < 0.001f)
            _exposeCurrent = 0f;

        Vector3 dir = entryDirection.normalized;
        transform.localPosition = _restPosition + dir * (entryDepth * _exposeCurrent);

        if (publisher != null)
        {
            int angle = Mathf.RoundToInt(Mathf.Lerp(60, 105, _exposeCurrent));
            publisher.SetSphereAngle(angle);
        }

        if (membrane != null)
            membrane.SetExposeFactor(_exposeCurrent * membraneScaleAmplitude);
    }
}
