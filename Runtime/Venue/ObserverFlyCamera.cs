using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Free-fly camera for the venue observer view.
    /// WASD/QE to move, hold right mouse button to look, hold middle button to pan.
    /// Reads the physical mouse captured at enable time so virtual injected devices do
    /// not steer the camera.
    /// </summary>
    public sealed class ObserverFlyCamera : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 2.5f;
        [SerializeField] private float fastMultiplier = 3f;
        [SerializeField] private float lookSensitivity = 0.15f;
        [Tooltip("Metres moved per pixel of middle-button drag (pan/truck).")]
        [SerializeField] private float panSensitivity = 0.01f;

        private float yaw;
        private float pitch;
#if ENABLE_INPUT_SYSTEM
        private Mouse physicalMouse;
        private Keyboard keyboard;
#endif

        private void OnEnable()
        {
            Vector3 euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x;
#if ENABLE_INPUT_SYSTEM
            physicalMouse = Mouse.current;
            keyboard = Keyboard.current;
#endif
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (physicalMouse == null)
            {
                physicalMouse = Mouse.current;
            }
            if (keyboard == null)
            {
                keyboard = Keyboard.current;
            }
            if (physicalMouse == null || keyboard == null)
            {
                return;
            }

            if (physicalMouse.rightButton.isPressed)
            {
                Vector2 delta = physicalMouse.delta.ReadValue();
                yaw += delta.x * lookSensitivity;
                pitch -= delta.y * lookSensitivity;
                pitch = Mathf.Clamp(pitch, -85f, 85f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            // Middle-button drag: pan (truck) — the view follows the drag like grabbing the scene.
            if (physicalMouse.middleButton.isPressed)
            {
                Vector2 delta = physicalMouse.delta.ReadValue();
                transform.position -= (transform.right * delta.x + transform.up * delta.y) * panSensitivity;
            }

            Vector3 move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += transform.forward;
            if (keyboard.sKey.isPressed) move -= transform.forward;
            if (keyboard.dKey.isPressed) move += transform.right;
            if (keyboard.aKey.isPressed) move -= transform.right;
            if (keyboard.eKey.isPressed) move += Vector3.up;
            if (keyboard.qKey.isPressed) move -= Vector3.up;

            float speed = keyboard.leftShiftKey.isPressed ? moveSpeed * fastMultiplier : moveSpeed;
            transform.position += move.normalized * (speed * Time.unscaledDeltaTime);
#endif
        }
    }
}
