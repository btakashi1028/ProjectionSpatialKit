using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ProjectionSpatialKit.Samples.TouchRippleContent
{
    /// <summary>
    /// Sample CONTENT: an ordinary touch game that knows NOTHING about the
    /// Projection Spatial Kit (it does not reference the kit assembly). It reads the
    /// standard Input System devices — every active Touchscreen touch plus the mouse —
    /// exactly like any venue content would, which is what makes it a valid Tier 0 test:
    /// the kit must drive it unmodified.
    ///
    /// Behaviour: each touch/click spawns a bouncing ball and an expanding ripple ring at
    /// the touched point. A second camera (Display 2) shows a scoreboard, so multi-display
    /// output routing can be verified.
    /// </summary>
    public sealed class TouchRippleController : MonoBehaviour
    {
        [SerializeField] private Camera mainCamera;
        [SerializeField] private TextMesh scoreText;
        [Tooltip("Distance from the camera at which touches land (the play plane).")]
        [SerializeField] private float playPlaneDistance = 10f;
        [SerializeField] private float ballLifetime = 6f;

        private int spawnCount;
#if ENABLE_INPUT_SYSTEM
        private readonly HashSet<int> activeTouchIds = new HashSet<int>();
        private bool mouseWasPressed;
#endif

        private void Awake()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }
        }

#if ENABLE_INPUT_SYSTEM
        private void Update()
        {
            // Touchscreen: spawn on every new touch (multi-touch native).
            bool anyTouchActive = false;
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (UnityEngine.InputSystem.Controls.TouchControl touch in touchscreen.touches)
                {
                    int id = touch.touchId.ReadValue();
                    bool pressed = touch.press.isPressed;
                    anyTouchActive |= pressed;
                    if (pressed && !activeTouchIds.Contains(id))
                    {
                        activeTouchIds.Add(id);
                        Spawn(touch.position.ReadValue());
                    }
                    else if (!pressed)
                    {
                        activeTouchIds.Remove(id);
                    }
                }
            }

            // Mouse: spawn on press (single pointer). Ignored while a touch is active —
            // the common pattern so a touch device that also mirrors a mouse pointer
            // does not double-fire.
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                bool pressed = mouse.leftButton.isPressed && !anyTouchActive;
                if (pressed && !mouseWasPressed)
                {
                    Spawn(mouse.position.ReadValue());
                }
                mouseWasPressed = pressed;
            }
        }
#endif

        private void Spawn(Vector2 screenPixel)
        {
            if (mainCamera == null)
            {
                return;
            }
            Vector3 world = mainCamera.ScreenToWorldPoint(
                new Vector3(screenPixel.x, screenPixel.y, playPlaneDistance));

            BouncyBall.Spawn(world, ballLifetime);
            RippleRing.Spawn(world);

            spawnCount++;
            if (scoreText != null)
            {
                scoreText.text = $"TOUCH RIPPLES\ntouches  {spawnCount}\nlast     {screenPixel.x:0},{screenPixel.y:0}";
            }
            Debug.Log($"[TouchRipple] spawn #{spawnCount} at px={screenPixel:F0}");
        }
    }
}
