using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#if SYSTEM_DRAWING
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Imaging;
#endif

namespace CoyEngine.Core
{
    // Lightweight runtime bitmap font built from a system font when SpriteFont is unavailable.
    public class TinyBitmapFont
    {
        private Texture2D _atlas;
        private Dictionary<char, Rectangle> _glyphs = new Dictionary<char, Rectangle>();
        private int _cellW, _cellH;

        private TinyBitmapFont() { }

        // Try to build an atlas of ASCII characters from start..end using the specified system font.
        // Falls back to a tiny numeric-only glyph set if System.Drawing is unavailable or fails.
        public static TinyBitmapFont Create(GraphicsDevice gd, string fontFamily = "DejaVu Sans", int size = 14)
        {
            var font = new TinyBitmapFont();

#if SYSTEM_DRAWING
            try
            {
                int cols = 16;
                int rows = 6; // 16*6 = 96 chars (covering 32..127)
                int cellW = size + 4;
                int cellH = size + 6;
                font._cellW = cellW;
                font._cellH = cellH;

                using (var bmp = new Bitmap(cols * cellW, rows * cellH))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    using (var sysFont = new Font(fontFamily, size, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var brush = new SolidBrush(Color.White))
                    {
                        int ch = 32;
                        for (int r = 0; r < rows; r++)
                        {
                            for (int c = 0; c < cols; c++)
                            {
                                if (ch > 126) break;
                                var chStr = ((char)ch).ToString();
                                int x = c * cellW + 2;
                                int y = r * cellH + 1;
                                g.DrawString(chStr, sysFont, brush, x, y);
                                font._glyphs[(char)ch] = new Rectangle(c * cellW, r * cellH, cellW, cellH);
                                ch++;
                            }
                        }
                    }

                    // Copy to Texture2D
                    var data = new Color[bmp.Width * bmp.Height];
                    var bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        unsafe
                        {
                            byte* src = (byte*)bmpData.Scan0;
                            int stride = bmpData.Stride;
                            for (int y = 0; y < bmp.Height; y++)
                            {
                                for (int x = 0; x < bmp.Width; x++)
                                {
                                    int idx = y * bmp.Width + x;
                                    int baseIdx = y * stride + x * 4;
                                    byte b = src[baseIdx + 0];
                                    byte gcol = src[baseIdx + 1];
                                    byte rcol = src[baseIdx + 2];
                                    byte a = src[baseIdx + 3];
                                    data[idx] = new Color(rcol / 255f, gcol / 255f, b / 255f, a / 255f);
                                }
                            }
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(bmpData);
                    }

                    font._atlas = new Texture2D(gd, bmp.Width, bmp.Height);
                    font._atlas.SetData(data);
                }

                return font;
            }
            catch (Exception ex)
            {
                // If System.Drawing failed for any reason, fallback
                Console.WriteLine("TinyBitmapFont: System.Drawing build failed: " + ex.Message);
            }
#endif

            // Fallback: create a very small numeric-only atlas for digits, colon, dot and some letters
            // Cell 8x8, chars: 0-9 : . A-Z a-z - _ ?
            int fcell = 8;
            font._cellW = fcell;
            font._cellH = fcell;
            string charset = "0123456789:.ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_,?";
            int colsSmall = 16;
            int rowsSmall = (int)Math.Ceiling((double)charset.Length / colsSmall);
            var texW = colsSmall * fcell;
            var texH = rowsSmall * fcell;
            var dataSmall = new Color[texW * texH];
            for (int i = 0; i < dataSmall.Length; i++) dataSmall[i] = Color.Transparent;

            for (int i = 0; i < charset.Length; i++)
            {
                int c = i % colsSmall;
                int r = i / colsSmall;
                int x0 = c * fcell;
                int y0 = r * fcell;
                char ch = charset[i];
                font._glyphs[ch] = new Rectangle(x0, y0, fcell, fcell);

                // Draw a very simple blocky glyph: for digits, draw number of vertical bars to hint shape.
                int bars = (char.IsDigit(ch) ? (ch - '0' + 1) : 3);
                for (int bx = 1; bx <= bars; bx++)
                {
                    int px = x0 + bx;
                    for (int py = y0 + 1; py < y0 + fcell - 1; py++)
                        dataSmall[py * texW + px] = Color.White;
                }
            }

            font._atlas = new Texture2D(gd, texW, texH);
            font._atlas.SetData(dataSmall);
            return font;
        }

        public void DrawString(SpriteBatch sb, string text, Vector2 pos, Color color, float scale = 1f)
        {
            if (string.IsNullOrEmpty(text)) return;
            float x = pos.X;
            float y = pos.Y;
            foreach (char ch in text)
            {
                if (!_glyphs.TryGetValue(ch, out var r))
                {
                    if (!_glyphs.TryGetValue('?', out r)) continue;
                }
                sb.Draw(_atlas, new Vector2(x, y), r, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                x += r.Width * scale;
            }
        }

        public Vector2 MeasureString(string text, float scale = 1f)
        {
            if (string.IsNullOrEmpty(text)) return Vector2.Zero;
            float w = 0f;
            float h = 0f;
            foreach (char ch in text)
            {
                if (!_glyphs.TryGetValue(ch, out var r))
                {
                    if (!_glyphs.TryGetValue('?', out r)) continue;
                }
                w += r.Width * scale;
                h = Math.Max(h, r.Height * scale);
            }
            return new Vector2(w, h);
        }
    }
}