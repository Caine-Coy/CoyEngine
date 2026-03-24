using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace CoyEngine.Core
{
    /// <summary>
    /// Handles mapping raw Keyboard/Mouse input to higher-level commands.
    /// Supports both arrow keys and WASD for movement, edge-triggered toggles for debug/bounds, and zoom via mouse wheel.
    /// </summary>
    public class InputController
    {
        public Vector2 Direction { get; private set; } = Vector2.Zero;
        public float ZoomDelta { get; private set; } = 0f;
        public bool ToggleDebugPressed { get; private set; } = false;
        public bool ToggleBoundsPressed { get; private set; } = false;
        public bool ReloadSettingsPressed { get; private set; } = false;
        public bool ReloadMapPressed { get; private set; } = false;
        public bool ExitPressed { get; private set; } = false;

        /// <summary>
        /// Process the current/previous input states and update properties.
        /// </summary>
        public void Update(KeyboardState currentKeyboard, KeyboardState previousKeyboard, MouseState currentMouse, MouseState previousMouse)
        {
            // Movement: accept both arrow keys and WASD
            float dx = 0f, dy = 0f;

            if (currentKeyboard.IsKeyDown(Keys.Left) || currentKeyboard.IsKeyDown(Keys.A)) dx -= 1f;
            if (currentKeyboard.IsKeyDown(Keys.Right) || currentKeyboard.IsKeyDown(Keys.D)) dx += 1f;
            if (currentKeyboard.IsKeyDown(Keys.Up) || currentKeyboard.IsKeyDown(Keys.W)) dy -= 1f;
            if (currentKeyboard.IsKeyDown(Keys.Down) || currentKeyboard.IsKeyDown(Keys.S)) dy += 1f;

            Direction = new Vector2(dx, dy);
            if (Direction.LengthSquared() > 1e-6f)
            {
                Direction = Vector2.Normalize(Direction);
            }

            // Zoom: convert raw scroll delta to same scale used previously
            int scrollDelta = currentMouse.ScrollWheelValue - previousMouse.ScrollWheelValue;
            ZoomDelta = scrollDelta * 0.001f;

            // Edge-triggered toggles
            ToggleDebugPressed = currentKeyboard.IsKeyDown(Keys.F3) && !previousKeyboard.IsKeyDown(Keys.F3);
            ToggleBoundsPressed = currentKeyboard.IsKeyDown(Keys.F4) && !previousKeyboard.IsKeyDown(Keys.F4);
            // Reload settings (edge triggered)
            ReloadSettingsPressed = currentKeyboard.IsKeyDown(Keys.F5) && !previousKeyboard.IsKeyDown(Keys.F5);

            // Reload map (edge triggered) using R key
            ReloadMapPressed = currentKeyboard.IsKeyDown(Keys.R) && !previousKeyboard.IsKeyDown(Keys.R);

            // Exit if ESC pressed
            ExitPressed = currentKeyboard.IsKeyDown(Keys.Escape);
        }
    }
}