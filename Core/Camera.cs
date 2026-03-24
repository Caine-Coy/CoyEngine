using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CoyEngine;

namespace CoyEngine.Core
{
    public class Camera
    {
        public Vector2 Position { get; set; }
        public float Zoom { get; set; }

        // Edge-centering is always enabled (map edges may be centered on screen for composition).
        
        // Center of the screen (for zooming relative to center)
        private Vector2 _screenCenter; 
        private Viewport _viewport; // store latest viewport

        public Camera(Viewport viewport)
        {
            _viewport = viewport;
            _screenCenter = new Vector2(viewport.Width / 2f, viewport.Height / 2f);
            Zoom = 1.0f;
            Position = Vector2.Zero;
        }

        // Call this when the window resizes
        public void UpdateViewport(Viewport viewport)
        {
            _viewport = viewport;
            _screenCenter = new Vector2(viewport.Width / 2f, viewport.Height / 2f);
        }

        // The Magic Matrix
        // 1. Move world so camera position is at (0,0)
        // 2. Scale the world (Zoom)
        // 3. Move world so (0,0) is at the center of the screen
        public Matrix GetViewMatrix()
        {
            return Matrix.CreateTranslation(new Vector3(-Position, 0)) *
                   Matrix.CreateScale(new Vector3(Zoom, Zoom, 1)) *
                   Matrix.CreateTranslation(new Vector3(_screenCenter, 0));
        }

        // Helper: Convert Screen Pixel (Mouse) to World Coordinate
        public Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            return Vector2.Transform(screenPosition, Matrix.Invert(GetViewMatrix()));
        }

        // Helper: Convert World Coordinate to Screen Pixel
        public Vector2 WorldToScreen(Vector2 worldPosition)
        {
            return Vector2.Transform(worldPosition, GetViewMatrix());
        }

        public void Move(Vector2 delta)
        {
            // Assign whole Vector2 rather than mutate components
            Position = Position + delta;
        }

        public void SetPosition(Vector2 pos)
        {
            Position = pos;
        }

        /// <summary>
        /// Compute the isometric bounding box for a map in O(1) using the four corner tiles.
        /// In a standard isometric projection the extremes are:
        ///   minX  = tile (0, H-1)            — leftmost diamond point
        ///   maxX  = tile (W-1, 0) + tileWidth — rightmost diamond point
        ///   minY  = tile (0, 0)               — topmost diamond point
        ///   maxY  = tile (W-1, H-1) + tileHeight — bottommost diamond point
        /// </summary>
        public static (float minX, float maxX, float minY, float maxY) ComputeIsoBounds(int mapW, int mapH, int tileWidth, int tileHeight)
        {
            // Left-most point: grid (0, H-1)
            var pLeft = GameMap.GridToIso(0, mapH - 1, tileWidth, tileHeight);
            float minX = pLeft.x;

            // Right-most point: grid (W-1, 0)
            var pRight = GameMap.GridToIso(mapW - 1, 0, tileWidth, tileHeight);
            float maxX = pRight.x + tileWidth;

            // Top-most point: grid (0, 0)
            var pTop = GameMap.GridToIso(0, 0, tileWidth, tileHeight);
            float minY = pTop.y;

            // Bottom-most point: grid (W-1, H-1)
            var pBottom = GameMap.GridToIso(mapW - 1, mapH - 1, tileWidth, tileHeight);
            float maxY = pBottom.y + tileHeight;

            return (minX, maxX, minY, maxY);
        }

        public void ClampToMapBounds(GameMap map, int tileWidth, int tileHeight, float padding = 32f)
        {
            // If map is empty, don't attempt to compute bounds (avoids uninitialized min/max)
            if (map == null || map.Width <= 0 || map.Height <= 0)
            {
                Position = Vector2.Zero;
                return;
            }

            var (minX, maxX, minY, maxY) = ComputeIsoBounds(map.Width, map.Height, tileWidth, tileHeight);

            // Edge-centering is always enabled: allow centering edge tiles at any zoom/viewport size
            float clampedX = MathHelper.Clamp(Position.X, minX, maxX);
            float clampedY = MathHelper.Clamp(Position.Y, minY, maxY);
            Position = new Vector2(clampedX, clampedY);
            return;
        }
    }
}