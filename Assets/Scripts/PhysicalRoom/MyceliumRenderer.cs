using System.Collections.Generic;
using UnityEngine;
public class MyceliumRenderer : MonoBehaviour
{
    [Header("Mesh & Material")]
    [SerializeField] private Mesh capsuleMesh;
    [SerializeField] private Material capsuleMaterial;

    [Header("Robot Influence Sources")]
    [SerializeField] private Transform[] netoTransforms;
    [SerializeField] private NetoCommandPublisher[] netoPublishers;
    [SerializeField] private Transform[] sauronTransforms;
    [SerializeField] private SauronCommandPublisher[] sauronPublishers;
    [SerializeField] private Transform deathtrapTransform;
    [SerializeField] private DeathTrapCommandPublisher deathtrapPublisher;

    [Header("Influence Tuning")]
    [SerializeField] private float influenceRadius = 2.5f;
    [SerializeField] private float influenceStrength = 0.15f;
    [Header("Animation")]
    [SerializeField] private float smoothSpeed = 6f;

    [Header("Bridge Strands")]
    [SerializeField] private Transform[] netoHandleTransforms;
    [SerializeField] private Transform[] netoBridgeAnchors;
    [SerializeField][Range(4, 20)] private int bridgeSegments = 10;
    [SerializeField][Range(0f, 1f)] private float bridgeWander = 0.25f;
    [SerializeField][Range(0.02f, 0.5f)] private float bridgeThickness = 0.08f;
    [SerializeField][Range(0.01f, 0.3f)] private float anchorThickness = 0.04f;
    [SerializeField] private Material bridgeMaterial;
    [SerializeField] private HubTelemetryReceiver hubReceiver;

    [Header("Bridge Glow")]
    [SerializeField] private Color idleColor = new Color(0.15f, 0.05f, 0.3f);
    [SerializeField] private Color alertColor = new Color(1f, 0.6f, 0f);
    [SerializeField] private Color dangerColor = new Color(1f, 0f, 0f);
    [SerializeField] private Color shriekColor = new Color(1f, 1f, 1f);
    [SerializeField] private float glowSmoothSpeed = 4f;
    private Vector3[] _localDirections;
    private Quaternion[] _baseRotations;
    private Vector3[] _baseScales;
    private Vector3[] _currentPositions;
    private List<Matrix4x4[]> _batches;
    private Matrix4x4[] _bridgeMatrices;
    private MaterialPropertyBlock _bridgeMpb;
    private enum NetoVisualState { Idle, Alert, Danger, Shriek }
    private NetoVisualState[] _netoStates;
    private float _currentGlow;
    private BreathingSphere _sphere;
    private float _baseSphereRadius;
    private int _capsuleCount;
    private const int MAX_BATCH = 1023;
    public int instanceCount => _capsuleCount;
    public void OnBuilt(BreathingSphere sphere, float baseRadius)
    {
        _sphere = sphere;
        _baseSphereRadius = baseRadius;
        if (hubReceiver != null)
            hubReceiver.NetoTelemetryReceived += OnNetoTelemetry;
        if (netoPublishers != null)
            _netoStates = new NetoVisualState[netoPublishers.Length]; // ← FIX: initialize array
    }
    public void Allocate(int count)
    {
        _capsuleCount = count;
        _localDirections = new Vector3[count];
        _baseRotations = new Quaternion[count];
        _baseScales = new Vector3[count];
        _currentPositions = new Vector3[count];
        _batches = new List<Matrix4x4[]>();
        int remaining = count;
        while (remaining > 0)
        {
            int bs = Mathf.Min(remaining, MAX_BATCH);
            _batches.Add(new Matrix4x4[bs]);
            remaining -= bs;
        }
    }
    public void SetInstance(int index, Vector3 pos, Quaternion rot, Vector3 scale, Vector3 localDir)
    {
        _localDirections[index] = localDir.normalized;
        _baseRotations[index] = rot;
        _baseScales[index] = scale;
        _currentPositions[index] = pos;
    }
    public void Clear()
    {
        if (hubReceiver != null)
            hubReceiver.NetoTelemetryReceived -= OnNetoTelemetry; // ← FIX: unsubscribe
        _localDirections = null;
        _baseRotations = null;
        _baseScales = null;
        _currentPositions = null;
        _batches = null;
        _bridgeMatrices = null;
        _netoStates = null;
        _capsuleCount = 0;
        _sphere = null;
    }
    private float GetNetoActivity(int i)
    {
        if (netoPublishers == null || i >= netoPublishers.Length || netoPublishers[i] == null)
            return 0f;
        return Mathf.Abs(netoPublishers[i].CurrentMotorSpeedUnits - 90) / 90f;
    }
    private void OnNetoTelemetry(HubTelemetryReceiver.NetoTelemetry t)
    {
        bool danger = t.DangerFlag > 0;
        bool loud = t.MicLevel > 30;
        NetoVisualState state = (danger, loud) switch  // ← FIX: typo fixed
        {
            (false, false) => NetoVisualState.Idle,
            (false, true) => NetoVisualState.Alert,
            (true, false) => NetoVisualState.Danger,
            (true, true) => NetoVisualState.Shriek,
        };
        for (int i = 0; i < (netoPublishers?.Length ?? 0); i++)
        {
            if (netoPublishers[i] != null && (int)netoPublishers[i].robotId == t.RobotId)
            { _netoStates[i] = state; break; }
        }
    }
    private float GetSauronActivity(int i)
    {
        if (sauronPublishers == null || i >= sauronPublishers.Length || sauronPublishers[i] == null)
            return 0f;
        float b = Mathf.Abs(sauronPublishers[i].BottomServoAngle - 90) / 90f;
        float t = Mathf.Abs(sauronPublishers[i].TopServoAngle - 90) / 90f;
        return Mathf.Max(b, t);
    }
    private float GetDeathtrapActivity()
    {
        if (deathtrapPublisher == null) return 0f;
        return deathtrapPublisher.IsSprayActive ? 1f : 0f;
    }
    private void LateUpdate()
    {
        if (_capsuleCount == 0 || _sphere == null) return;
        if (capsuleMesh == null || capsuleMaterial == null) return;
        float dt = Time.deltaTime;
        float currentRadius = _sphere.transform.lossyScale.x * 0.5f;
        Vector3 center = _sphere.transform.position;
        float sigma2 = influenceRadius * influenceRadius * 2f;
        int robotCount = 0;
        if (netoTransforms != null) robotCount += netoTransforms.Length;
        if (sauronTransforms != null) robotCount += sauronTransforms.Length;
        if (deathtrapTransform != null) robotCount++;
        float[] robotActivities = new float[robotCount];
        Vector3[] robotPositions = new Vector3[robotCount];
        int ri = 0;
        for (int i = 0; netoTransforms != null && i < netoTransforms.Length; i++)
        {
            if (netoTransforms[i] == null) continue;
            robotPositions[ri] = netoTransforms[i].position;
            robotActivities[ri] = GetNetoActivity(i);
            ri++;
        }
        for (int i = 0; sauronTransforms != null && i < sauronTransforms.Length; i++)
        {
            if (sauronTransforms[i] == null) continue;
            robotPositions[ri] = sauronTransforms[i].position;
            robotActivities[ri] = GetSauronActivity(i);
            ri++;
        }
        if (deathtrapTransform != null)
        {
            robotPositions[ri] = deathtrapTransform.position;
            robotActivities[ri] = GetDeathtrapActivity();
            ri++;
        }
        robotCount = ri;
        int n = _capsuleCount;
        Matrix4x4[] flat = _batches[0];
        int batchIdx = 0, written = 0;
        for (int i = 0; i < n; i++)
        {
            Vector3 breathPos = center + _localDirections[i] * currentRadius;
            float totalPush = 0f;
            for (int r = 0; r < robotCount; r++)
            {
                float activity = robotActivities[r];
                if (activity < 0.01f) continue;
                float dx = breathPos.x - robotPositions[r].x;
                float dy = breathPos.y - robotPositions[r].y;
                float dz = breathPos.z - robotPositions[r].z;
                float dist2 = dx * dx + dy * dy + dz * dz;
                totalPush += activity * Mathf.Exp(-dist2 / sigma2);
            }
            Vector3 target = breathPos + _localDirections[i] * (totalPush * influenceStrength);
            _currentPositions[i] = Vector3.Lerp(_currentPositions[i], target, dt * smoothSpeed);
            flat[written++] = Matrix4x4.TRS(
                _currentPositions[i], _baseRotations[i], _baseScales[i]);
            if (written < flat.Length && i < n - 1) continue;
            Graphics.DrawMeshInstanced(capsuleMesh, 0, capsuleMaterial,
                flat, written, null,
                UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows: false, gameObject.layer);
            written = 0;
            batchIdx++;
            if (batchIdx < _batches.Count)
                flat = _batches[batchIdx];
        }
        if (netoHandleTransforms == null || bridgeMaterial == null || _sphere == null)
            return;
        int handleCount = netoHandleTransforms.Length;
        if (handleCount == 0) return;
        int totalSegments = handleCount * bridgeSegments;
        if (_bridgeMatrices == null || _bridgeMatrices.Length != totalSegments)
            _bridgeMatrices = new Matrix4x4[totalSegments];
        if (_bridgeMpb == null)
            _bridgeMpb = new MaterialPropertyBlock();
        float cpRadius = _sphere.transform.lossyScale.x * 0.5f;
        Vector3 sphereCenter = _sphere.transform.position;
        int idx = 0;
        for (int h = 0; h < handleCount; h++)
        {
            if (netoBridgeAnchors == null || h >= netoBridgeAnchors.Length || netoBridgeAnchors[h] == null || netoHandleTransforms[h] == null)
                continue;
            float activity = GetNetoActivity(h);
            float tension = 1f - activity * 0.7f;
            float wander = bridgeWander * tension * cpRadius * 0.04f;
            Vector3 anchorPos = netoBridgeAnchors[h].position;
            Vector3 dirToAnchor = (anchorPos - sphereCenter).normalized;
            Vector3 surfacePos = sphereCenter + dirToAnchor * cpRadius;
            Vector3 prevPos = surfacePos;
            float segLen = Vector3.Distance(sphereCenter, anchorPos) / bridgeSegments;
            for (int s = 0; s < bridgeSegments; s++)
            {
                float t = (s + 0.5f) / bridgeSegments;
                float thickness = Mathf.Lerp(bridgeThickness, anchorThickness, t);
                Vector3 segScale = new Vector3(thickness, segLen * 0.5f, thickness);
                Vector3 pos = Vector3.Lerp(surfacePos, anchorPos, t);
                if (wander > 0.001f)
                {
                    Vector3 perp = Vector3.Cross(dirToAnchor, Vector3.up);
                    if (perp.sqrMagnitude < 0.001f)
                        perp = Vector3.Cross(dirToAnchor, Vector3.forward);
                    perp.Normalize();
                    float noise = Mathf.Sin(t * Mathf.PI * 3f + h * 1.7f) * wander;
                    pos += perp * noise;
                }
                Vector3 segDir = (pos - prevPos).normalized;
                if (segDir.sqrMagnitude < 0.0001f) segDir = dirToAnchor;
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, segDir);
                _bridgeMatrices[idx++] = Matrix4x4.TRS(pos, rot, segScale);
                prevPos = pos;
            }
        }
        // ── Glow from highest-priority state across all Netos ──
        float glowTarget = 0f;
        Color targetColor = idleColor;
        for (int i = 0; i < (netoPublishers?.Length ?? 0); i++)
        {
            if (netoPublishers[i] == null) continue;
            NetoVisualState st = (_netoStates != null && i < _netoStates.Length)
                ? _netoStates[i] : NetoVisualState.Idle;
            float a = GetNetoActivity(i);
            Color c = st switch
            {
                NetoVisualState.Alert => alertColor,
                NetoVisualState.Danger => dangerColor,
                NetoVisualState.Shriek => shriekColor,
                _ => idleColor
            };
            if ((int)st > (int)NetoVisualState.Idle || a > 0.1f)
            {
                targetColor = c;
                glowTarget = Mathf.Max(glowTarget, 0.3f + a * 0.7f);
            }
        }
        _currentGlow = Mathf.Lerp(_currentGlow, glowTarget, Time.deltaTime * glowSmoothSpeed);
        _bridgeMpb.SetColor("_EmissionColor", targetColor * _currentGlow);
        if (idx > 0)
        {
            Graphics.DrawMeshInstanced(capsuleMesh, 0, bridgeMaterial,
                _bridgeMatrices, idx, _bridgeMpb,
                UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows: false, gameObject.layer);
        }
    }
}