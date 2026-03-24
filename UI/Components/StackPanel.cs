using CoyEngine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#nullable enable

namespace CoyEngine.UI.Components
{
    public class StackPanel : Container
    {
        public bool Vertical { get; set; } = true;

        public override void Update(float dt)
        {
            // Compute layout starting from Position + Padding
            var offset = new Vector2(Padding, Padding);

            foreach (var c in Children)
            {
                if (!c.Visible) continue;

                // Set child's position
                c.Position = this.Position + offset;

                // Update child so it can compute its Bounds
                c.Update(dt);

                // Advance offset
                if (Vertical)
                {
                    offset.Y += c.Bounds.Height + Spacing;
                }
                else
                {
                    offset.X += c.Bounds.Width + Spacing;
                }
            }
        }
    }
}
