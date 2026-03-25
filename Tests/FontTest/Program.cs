using System;
using CoyEngine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace FontTest
{
    /// <summary>
    /// Simple test to visualize the MonoGame.Extended BitmapFont rendering.
    /// Requires a font file to be present.
    /// </summary>
    public class FontTestGame : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch = null!;
        private TinyBitmapFont _font = null!;

        public FontTestGame()
        {
            _graphics = new GraphicsDeviceManager(this);
            _graphics.PreferredBackBufferWidth = 800;
            _graphics.PreferredBackBufferHeight = 600;
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            
            // Try to load a font file
            // For testing, we'll create a simple font at runtime
            // In production, use TinyBitmapFont.FromFile() with a pre-generated .fnt file
            
            try
            {
                // Try loading from Content directory
                _font = TinyBitmapFont.FromFile(GraphicsDevice, "Content/fonts/test.fnt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Font load failed: {ex.Message}");
                Console.WriteLine("Please generate a font file using Hiero or BMFont.");
                Console.WriteLine("See FONTS.md for instructions.");
                
                // Create a placeholder - in real usage, you'd have a font file
                throw;
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(40, 40, 50));

            _spriteBatch.Begin();

            // Title
            _font.DrawString(_spriteBatch, "BitmapFont Test - MonoGame.Extended", new Vector2(50, 20), Color.Yellow, 1.2f);

            // Uppercase letters
            _font.DrawString(_spriteBatch, "ABCDEFGHIJKLMNOPQRSTUVWXYZ", new Vector2(50, 60), Color.White);

            // Lowercase letters
            _font.DrawString(_spriteBatch, "abcdefghijklmnopqrstuvwxyz", new Vector2(50, 90), Color.LightGray);

            // Digits
            _font.DrawString(_spriteBatch, "0123456789", new Vector2(50, 130), Color.Cyan);

            // Punctuation and symbols
            _font.DrawString(_spriteBatch, ". , : ; ! ? \" ' - _ = + * / \\ | & # @ $ % ^ ~ < > ( ) [ ] { }", new Vector2(50, 170), Color.LightGreen);

            // Sample text
            _font.DrawString(_spriteBatch, "The quick brown fox jumps over the lazy dog.", new Vector2(50, 220), Color.White);
            _font.DrawString(_spriteBatch, "Pack my box with five dozen liquor jugs.", new Vector2(50, 250), Color.White);

            // Multi-line test
            _font.DrawString(_spriteBatch, "Line 1\nLine 2\nLine 3", new Vector2(50, 300), Color.Orange);

            // Scale test
            _font.DrawString(_spriteBatch, "Scaled: 0.5x", new Vector2(50, 450), Color.White, 0.5f);
            _font.DrawString(_spriteBatch, "Scaled: 2.0x", new Vector2(200, 450), Color.White, 2.0f);

            // Info
            _font.DrawString(_spriteBatch, $"Line Height: {_font.LineHeight}px", new Vector2(50, 520), Color.Gray);
            _font.DrawString(_spriteBatch, $"FPS: {1f / (float)gameTime.ElapsedGameTime.TotalSeconds:F0}", new Vector2(600, 520), Color.Gray);

            _spriteBatch.End();

            base.Draw(gameTime);
        }

        public static void Main(string[] args)
        {
            Console.WriteLine("Font Test - MonoGame.Extended");
            Console.WriteLine("==============================");
            Console.WriteLine();
            Console.WriteLine("This test requires a font file to run.");
            Console.WriteLine();
            Console.WriteLine("To generate a font file:");
            Console.WriteLine("1. Download Hiero: https://github.com/libgdx/libgdx/wiki/Hiero");
            Console.WriteLine("2. Select a font (e.g., DejaVu Sans, size 16)");
            Console.WriteLine("3. Export as BMFont format");
            Console.WriteLine("4. Copy test.fnt and test.png to Content/fonts/");
            Console.WriteLine();
            Console.WriteLine("Then run: dotnet run");
        }
    }
}
