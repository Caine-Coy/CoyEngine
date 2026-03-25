using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace CoyEngine.Core
{
    /// <summary>
    /// Lightweight bitmap font wrapper using MonoGame.Extended's BitmapFont.
    /// Provides a simple API for rendering bitmap fonts cross-platform.
    /// 
    /// For best results, generate font atlases using:
    /// - BMFont (Windows): http://www.angelcode.com/products/bmfont/
    /// - FontBuilder (Cross-platform): https://github.com/libgdx/libgdx/wiki/Hiero
    /// - Online: https://snowb.org/
    /// </summary>
    public class TinyBitmapFont
    {
        private readonly BitmapFont _font;

        private TinyBitmapFont(BitmapFont font)
        {
            _font = font;
        }

        /// <summary>
        /// Loads a bitmap font from an .fnt file (BMFont format).
        /// The .fnt file and associated texture(s) must be in the same directory.
        /// </summary>
        public static TinyBitmapFont FromFile(GraphicsDevice graphicsDevice, string path)
        {
            var font = BitmapFont.FromFile(graphicsDevice, path);
            return new TinyBitmapFont(font);
        }

        /// <summary>
        /// Loads a bitmap font from a stream.
        /// </summary>
        public static TinyBitmapFont FromStream(GraphicsDevice graphicsDevice, FileStream fontStream)
        {
            var font = BitmapFont.FromStream(graphicsDevice, fontStream);
            return new TinyBitmapFont(font);
        }

        /// <summary>
        /// Creates a TinyBitmapFont from a MonoGame.Extended BitmapFont.
        /// </summary>
        public static TinyBitmapFont FromBitmapFont(BitmapFont font)
        {
            return new TinyBitmapFont(font);
        }

        /// <summary>
        /// Draws a string at the specified position.
        /// </summary>
        public void DrawString(SpriteBatch sb, string text, Vector2 pos, Color color, float scale = 1f)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            // Handle newlines manually since BitmapFont doesn't support them directly
            string[] lines = text.Split('\n');
            float y = pos.Y;
            float lineHeight = _font.LineHeight * scale;
            
            foreach (string line in lines)
            {
                sb.DrawString(_font, line, new Vector2(pos.X, y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                y += lineHeight;
            }
        }

        /// <summary>
        /// Draws a string with rotation and origin.
        /// </summary>
        public void DrawString(SpriteBatch sb, string text, Vector2 pos, Color color, float rotation, Vector2 origin, float scale = 1f, SpriteEffects effects = SpriteEffects.None, float layerDepth = 0f)
        {
            sb.DrawString(_font, text, pos, color, rotation, origin, scale, effects, layerDepth);
        }

        /// <summary>
        /// Measures the size of a string in pixels.
        /// </summary>
        public Vector2 MeasureString(string text, float scale = 1f)
        {
            if (string.IsNullOrEmpty(text)) return Vector2.Zero;
            
            // Handle multi-line text
            string[] lines = text.Split('\n');
            float maxWidth = 0;
            
            foreach (string line in lines)
            {
                var size = _font.MeasureString(line);
                maxWidth = Math.Max(maxWidth, size.Width * scale);
            }
            
            float height = _font.LineHeight * lines.Length * scale;
            return new Vector2(maxWidth, height);
        }

        /// <summary>
        /// Gets the line height for this font.
        /// </summary>
        public int LineHeight => _font.LineHeight;

        /// <summary>
        /// Gets the underlying MonoGame.Extended BitmapFont.
        /// </summary>
        public BitmapFont GetBitmapFont() => _font;
    }
}
