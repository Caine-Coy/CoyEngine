using CoyEngine.Core;
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

#nullable enable
namespace CoyEngine.UI.Components
{
    /// <summary>
    /// Reusable drag-box (rubber band) selection component.
    /// The start point is anchored in world space so panning the camera
    /// expands/contracts the box naturally. The end point tracks the mouse cursor.
    /// Can be used for selecting dogs, tiles, units, or any world-space entities.
    /// </summary>
    public class DragBox
    {
        // Drag state
        private bool _isDragging;
        private Vector2 _dragStartWorld;   // Anchored in world space
        private Vector2 _dragStartScreen;  // Current screen-space position of start (recomputed each frame)
        private Vector2 _dragEnd;          // Current mouse position (screen space)

        // Camera reference for world↔screen conversion
        private readonly Camera _camera;

        // Minimum drag distance (in pixels) before we consider it a drag vs a click
        private const float MIN_DRAG_DISTANCE = 8f;

        // Visual settings
        private static readonly Color FILL_COLOR = new Color(80, 180, 255, 40);
        private static readonly Color BORDER_COLOR = new Color(80, 200, 255, 180);
        private const float BORDER_THICKNESS = 1.5f;

        public DragBox(Camera camera)
        {
            _camera = camera;
        }

        /// <summary>True while the user is actively dragging.</summary>
        public bool IsDragging => _isDragging && DragDistance >= MIN_DRAG_DISTANCE;

        /// <summary>The screen-space rectangle being dragged (min-corner origin).</summary>
        public Rectangle DragRect
        {
            get
            {
                int x = (int)MathF.Min(_dragStartScreen.X, _dragEnd.X);
                int y = (int)MathF.Min(_dragStartScreen.Y, _dragEnd.Y);
                int w = (int)MathF.Abs(_dragEnd.X - _dragStartScreen.X);
                int h = (int)MathF.Abs(_dragEnd.Y - _dragStartScreen.Y);
                return new Rectangle(x, y, w, h);
            }
        }

        /// <summary>Total drag distance in pixels from start to current position.</summary>
        public float DragDistance
        {
            get
            {
                float dx = _dragEnd.X - _dragStartScreen.X;
                float dy = _dragEnd.Y - _dragStartScreen.Y;
                return MathF.Sqrt(dx * dx + dy * dy);
            }
        }

        /// <summary>
        /// Fired when a drag-selection completes (mouse released after a valid drag).
        /// Provides the final screen-space rectangle.
        /// </summary>
        public Action<Rectangle>? OnDragComplete;

        /// <summary>
        /// Process mouse input. Call this each frame.
        /// Returns true if the drag box consumed the input (is actively dragging or just completed).
        /// </summary>
        public bool HandleMouse(MouseState current, MouseState previous)
        {
            bool leftDown = current.LeftButton == ButtonState.Pressed;
            bool wasDown = previous.LeftButton == ButtonState.Pressed;

            if (leftDown && !wasDown)
            {
                // Mouse just pressed — anchor start point in world space
                _isDragging = true;
                _dragStartWorld = _camera.ScreenToWorld(new Vector2(current.X, current.Y));
                _dragStartScreen = new Vector2(current.X, current.Y);
                _dragEnd = _dragStartScreen;
                return false; // Don't consume yet — could be a click
            }

            if (leftDown && _isDragging)
            {
                // Mouse held — reproject world anchor to screen (handles camera pan/zoom)
                _dragStartScreen = _camera.WorldToScreen(_dragStartWorld);
                _dragEnd = new Vector2(current.X, current.Y);

                // Only consume input once we've exceeded the minimum drag distance
                return DragDistance >= MIN_DRAG_DISTANCE;
            }

            if (!leftDown && wasDown && _isDragging)
            {
                // Mouse released — finish drag
                _isDragging = false;
                _dragStartScreen = _camera.WorldToScreen(_dragStartWorld);
                _dragEnd = new Vector2(current.X, current.Y);

                if (DragDistance >= MIN_DRAG_DISTANCE)
                {
                    OnDragComplete?.Invoke(DragRect);
                    return true; // Consumed: was a real drag
                }

                // Under threshold — treat as a click (not consumed)
                return false;
            }

            return false;
        }

        /// <summary>
        /// Draw the drag box overlay. Call during screen-space UI rendering.
        /// </summary>
        public void Draw(SpriteBatch sb, Texture2D whitePixel)
        {
            if (!IsDragging) return;

            var rect = DragRect;

            // Semi-transparent fill
            sb.Draw(whitePixel, rect, FILL_COLOR);

            // Border lines
            sb.Draw(whitePixel, new Rectangle(rect.X, rect.Y, rect.Width, (int)BORDER_THICKNESS), BORDER_COLOR);
            sb.Draw(whitePixel, new Rectangle(rect.X, rect.Bottom - (int)BORDER_THICKNESS, rect.Width, (int)BORDER_THICKNESS), BORDER_COLOR);
            sb.Draw(whitePixel, new Rectangle(rect.X, rect.Y, (int)BORDER_THICKNESS, rect.Height), BORDER_COLOR);
            sb.Draw(whitePixel, new Rectangle(rect.Right - (int)BORDER_THICKNESS, rect.Y, (int)BORDER_THICKNESS, rect.Height), BORDER_COLOR);
        }

        /// <summary>
        /// Cancel any in-progress drag without firing the completion event.
        /// </summary>
        public void Cancel()
        {
            _isDragging = false;
        }
    }
}
