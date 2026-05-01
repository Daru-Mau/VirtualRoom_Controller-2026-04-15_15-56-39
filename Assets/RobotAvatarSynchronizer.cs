using UnityEngine;

namespace PhysicalRoom.UnityBridge
{
    /// <summary>
    /// Applies sensor feedback from robots to Unity visual effects.
    /// Listens to the bridge's sensor events and triggers particle systems, material changes, etc.
    /// </summary>
    public class RobotAvatarSynchronizer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private VrRobotUdpBridge bridge;

        [Header("Sensor Hooks")]
        [SerializeField]
        private ParticleSystem touchParticles;

        [SerializeField]
        private Renderer dangerRenderer;

        [SerializeField]
        private Color safeColor = Color.white;

        [SerializeField]
        private Color dangerColor = Color.red;

        [SerializeField]
        private AudioSource micAudioSource;

        [Header("Filtering")]
        [SerializeField]
        private string targetRobotType = "N";

        [SerializeField]
        private string targetRobotNumber = "";

        private void Awake()
        {
            if (bridge == null)
            {
                bridge = FindObjectOfType<VrRobotUdpBridge>();
            }
        }

        private void OnEnable()
        {
            if (bridge != null)
            {
                bridge.SensorEventReceived += HandleSensorEvent;
            }
        }

        private void OnDisable()
        {
            if (bridge != null)
            {
                bridge.SensorEventReceived -= HandleSensorEvent;
            }
        }

        private void HandleSensorEvent(VrRobotUdpBridge.RobotSensorEvent sensorEvent)
        {
            if (sensorEvent == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(targetRobotType) && !sensorEvent.RobotType.StartsWith(targetRobotType))
            {
                return;
            }

            if (!string.IsNullOrEmpty(targetRobotNumber) && sensorEvent.RobotNumber != targetRobotNumber)
            {
                return;
            }

            if (sensorEvent.RobotType.StartsWith("N"))
            {
                HandleNetoSensor(sensorEvent);
            }
            else if (sensorEvent.RobotType.StartsWith("S"))
            {
                HandleSauronSensor(sensorEvent);
            }
        }

        private void HandleNetoSensor(VrRobotUdpBridge.RobotSensorEvent sensorEvent)
        {
            var micValue = sensorEvent.PayloadValue;
            var isDanger = sensorEvent.SecondaryValue > 0;

            if (micAudioSource != null)
            {
                micAudioSource.volume = Mathf.InverseLerp(0f, 4095f, micValue);
            }

            if (isDanger)
            {
                if (touchParticles != null)
                {
                    touchParticles.Play();
                }

                if (dangerRenderer != null)
                {
                    dangerRenderer.material.color = dangerColor;
                }
            }
            else if (dangerRenderer != null)
            {
                dangerRenderer.material.color = Color.Lerp(dangerRenderer.material.color, safeColor, Time.deltaTime * 2f);
            }
        }

        private void HandleSauronSensor(VrRobotUdpBridge.RobotSensorEvent sensorEvent)
        {
            var touched = sensorEvent.PayloadValue;
            var isDanger = sensorEvent.SecondaryValue > 0;

            if (touched > 0 && touchParticles != null)
            {
                touchParticles.Play();
            }

            if (isDanger && dangerRenderer != null)
            {
                dangerRenderer.material.color = dangerColor;
            }
            else if (dangerRenderer != null)
            {
                dangerRenderer.material.color = Color.Lerp(dangerRenderer.material.color, safeColor, Time.deltaTime * 2f);
            }
        }
    }
}

