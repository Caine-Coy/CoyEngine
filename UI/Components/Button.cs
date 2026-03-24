using CoyEngine.Core;
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

#nullable enable

namespace CoyEngine.UI.Components
{
    public class Button : UIComponent
    {
        public string Text { get; set; } = string.Empty;
        public Vector2 Size { get; set; } = new Vector2(120, 28);
        public Color Background { get; set; } = Color.DarkSlateGray;
        public Color HoverBackground { get; set; } = Color.Gray;
        public Color TextColor { get; set; } = Color.White;
        public Action? OnClick { get; set; }

        private readonly SpriteFont? _font;
        private readonly TinyBitmapFont? _tinyFont;
        private bool _isHover = false;
        public bool IsHover => _isHover;

        public Button(SpriteFont? font, TinyBitmapFont? tinyFont)
        {
            _font = font;
            _tinyFont = tinyFont;
            Bounds = new Rectangle(0, 0, (int)Size.X, (int)Size.Y);
        }

        public override void Update(float dt)
        {
            Bounds = new Rectangle((int)Position.X, (int)Position.Y, (int)Size.X, (int)Size.Y);
        }

        public override void Draw(SpriteBatch sb)
        {
            var bg = _isHover ? HoverBackground : Background;
            // Draw background (requires a white pixel in host DebugPanel or owner to draw it)
            // We'll expect the owning container to draw background; for portability, we don't draw it here.

            if (_font != null)
            {
                var textSize = _font.MeasureString(Text);
                var textPos = Position + new Vector2((Size.X - textSize.X) / 2f, (Size.Y - textSize.Y) / 2f);
                // If a white pixel texture is desired for background, owner should draw it before drawing components.
                _font.LineSpacing.ToString(); // no-op to avoid warnings
                sb.DrawString(_font, Text, textPos, TextColor);
            }
            else if (_tinyFont != null)
            {
                var textPos = Position + new Vector2(4, 4);
                _tinyFont.DrawString(sb, Text, textPos, TextColor, 1f);
            }
        }

        public override bool HandleMouse(MouseState current, MouseState previous)
        {
            var mx = current.X;
            var my = current.Y;
            var inside = Bounds.Contains(mx, my);
            _isHover = inside;

            bool leftPressedPrev = previous.LeftButton == ButtonState.Pressed;
            bool leftReleasedNow = current.LeftButton == ButtonState.Released && leftPressedPrev;

            if (inside && leftReleasedNow)
            {
                OnClick?.Invoke();
                return true;
            }

            return false;
        }
    }
}
