// RobotInputActivator.cs — put on PoseController
using UnityEngine;
using UnityEngine.InputSystem;

public class RobotInputActivator : MonoBehaviour
{
    public InputActionAsset actions;

    void OnEnable() => actions.Enable();
    void OnDisable() => actions.Disable();
}