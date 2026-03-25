using CoyEngine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

#nullable enable

namespace CoyEngine.Rendering
{

    // Holds common rendering resources and helpers for renderers.
    public class RenderContext
    {
        public GraphicsDevice GraphicsDevice { get; }
        public SpriteBatch SpriteBatch { get; }
        public Texture2D WhitePixel { get; }
        public SpriteFont? DebugFont { get; }
        public TinyBitmapFont? TinyFont { get; }
        public BitmapFont? BitmapFont { get; }

        // Mouse position in client (window) coordinates as updated by the game each frame
        public int MouseX { get; set; }
        public int MouseY { get; set; }

        // Diagnostics: client/backbuffer/window values updated by the game each frame
        public int ClientWidth { get; set; }
        public int ClientHeight { get; set; }
        public int BackbufferWidth { get; set; }
        public int BackbufferHeight { get; set; }
        public int WindowPosX { get; set; }
        public int WindowPosY { get; set; }

        private bool _isBatchActive;

        public RenderContext(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D whitePixel, SpriteFont? debugFont, TinyBitmapFont? tinyFont, BitmapFont? bitmapFont = null)
        {
            GraphicsDevice = graphicsDevice;
            SpriteBatch = spriteBatch;
            WhitePixel = whitePixel;
            DebugFont = debugFont;
            TinyFont = tinyFont;
            BitmapFont = bitmapFont;
        }

        public void BeginWorld(Matrix transform)
        {
            if (_isBatchActive)
                SpriteBatch.End();
            SpriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
            _isBatchActive = true;
        }

        public void BeginUI()
        {
            if (_isBatchActive)
                SpriteBatch.End();
            // Clear the depth buffer so vertex-rendered world geometry (buildings, dogs)
            // cannot occlude screen-space UI drawn via SpriteBatch.
            GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);
            SpriteBatch.Begin(
                samplerState: SamplerState.PointClamp,
                depthStencilState: DepthStencilState.None);
            _isBatchActive = true;
        }

        public void End()
        {
            if (_isBatchActive)
            {
                SpriteBatch.End();
                _isBatchActive = false;
            }
        }

        /// <summary>
        /// Ensure any dangling Begin is closed. Call between renderer draws for safety.
        /// </summary>
        public void EnsureEnded()
        {
            if (_isBatchActive)
            {
                try { SpriteBatch.End(); } catch { }
                _isBatchActive = false;
            }
        }

        // Small helpers could be added here (DrawLine, FilledRect, etc.) for reuse by renderers.
    }
}
