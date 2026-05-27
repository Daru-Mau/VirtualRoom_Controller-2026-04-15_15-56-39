using UnityEngine;

public class DeathTrapVisualDriver : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Filled automatically from this GameObject if left empty.")]
    [SerializeField] private DeathTrapCommandPublisher publisher;

    [Tooltip("Receive Deathtrap telemetry from RobotHub.")]
    [SerializeField] private HubTelemetryReceiver telemetryReceiver;

    [Header("Proximity Feedback")]
    [SerializeField] private Transform sphereVisual;
    [SerializeField] private float maxProximityDistance = 2f;
    [SerializeField] private Color farColor = new Color(0.2f, 0.4f, 0.8f);
    [SerializeField] private Color closeColor = new Color(0.9f, 0.1f, 0.1f);
    [SerializeField] private Color touchColor = new Color(1f, 0.5f, 0f);

    [Header("Touch Feedback")]
    [SerializeField] private float touchFlashDuration = 0.5f;

    [Header("Runtime — read only")]
    [SerializeField] private float latestDistanceCm = 400f;
    [SerializeField] private int latestTouchLevel;

    private Renderer _sphereRenderer;
    private MaterialPropertyBlock _propBlock;
    private Color _currentColor;
    private float _touchFlashTimer;

    void Start()
    {
        if (publisher == null)
            publisher = GetComponent<DeathTrapCommandPublisher>();

        if (telemetryReceiver == null)
            telemetryReceiver = FindObjectOfType<HubTelemetryReceiver>();

        if (telemetryReceiver != null)
            telemetryReceiver.DeathtrapTelemetryReceived += HandleTelemetry;

        _sphereRenderer = sphereVisual?.GetComponent<Renderer>();
        _propBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (sphereVisual == null) return;

        float distanceM = latestDistanceCm / 100f;
        float proximity = 1f - Mathf.Clamp01(distanceM / maxProximityDistance);

        sphereVisual.localScale = Vector3.Lerp(Vector3.one * 0.1f, Vector3.one * 0.6f, proximity);

        if (_sphereRenderer != null)
        {
            _sphereRenderer.GetPropertyBlock(_propBlock);

            if (_touchFlashTimer > 0f)
            {
                _touchFlashTimer -= Time.deltaTime;
                float flash = Mathf.PingPong(_touchFlashTimer * 10f, 1f);
                _currentColor = Color.Lerp(_currentColor, touchColor, flash);
            }

            Color baseColor = Color.Lerp(farColor, closeColor, proximity);
            _currentColor = Color.Lerp(_currentColor, baseColor, Time.deltaTime * 3f);
            _propBlock.SetColor("_Color", _currentColor);
            _sphereRenderer.SetPropertyBlock(_propBlock);
        }
    }

    void OnDestroy()
    {
        if (telemetryReceiver != null)
            telemetryReceiver.DeathtrapTelemetryReceived -= HandleTelemetry;
    }

    private void HandleTelemetry(HubTelemetryReceiver.DeathtrapTelemetry t)
    {
        if (publisher != null && (int)publisher.robotId != t.RobotId)
            return;

        latestDistanceCm = t.MinDistance;

        if (t.TouchLevel > 0 && t.TouchLevel != latestTouchLevel)
        {
            _touchFlashTimer = touchFlashDuration;
        }
        latestTouchLevel = t.TouchLevel;
    }
}
