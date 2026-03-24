using CoyEngine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#nullable enable

namespace CoyEngine.UI.Components
{
    public class Label : UIComponent
    {
        public string Text { get; set; } = string.Empty;
        public Color Color { get; set; } = Color.White;
        private readonly SpriteFont? _font;
        private readonly TinyBitmapFont? _tinyFont;

        public Label(SpriteFont? font, TinyBitmapFont? tinyFont)
        {
            _font = font;
            _tinyFont = tinyFont;
            Bounds = new Rectangle(0, 0, 0, 0);
        }

        public override void Update(float dt)
        {
            // Update bounds for basic hit testing (width estimated)
            if (_font != null)
            {
                var size = _font.MeasureString(Text);
                Bounds = new Rectangle((int)Position.X, (int)Position.Y, (int)size.X, (int)size.Y);
            }
            else if (_tinyFont != null)
            {
                // Estimate width from glyph cell width; TinyBitmapFont doesn't expose it, so approximate
                Bounds = new Rectangle((int)Position.X, (int)Position.Y, Text.Length * 8, 12);
            }
            else
            {
                // No fonts available - a rough fallback estimation
                Bounds = new Rectangle((int)Position.X, (int)Position.Y, Text.Length * 8, 12);
            }
        }

        public override void Draw(SpriteBatch sb)
        {
            if (_font != null)
            {
                sb.DrawString(_font, Text, Position, Color);
            }
            else if (_tinyFont != null)
            {
                _tinyFont.DrawString(sb, Text, Position, Color, 1f);
            }
        }
    }
}
