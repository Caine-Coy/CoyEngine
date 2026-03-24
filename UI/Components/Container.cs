using CoyEngine.Core;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#nullable enable

namespace CoyEngine.UI.Components
{
    public class Container : UIComponent
    {
        public List<UIComponent> Children { get; } = new List<UIComponent>();
        public int Padding { get; set; } = 4;
        public int Spacing { get; set; } = 2;
        public Color Background { get; set; } = Color.Transparent;

        public void Add(UIComponent c)
        {
            if (c == null) return;
            Children.Add(c);
        }

        public override void Update(float dt)
        {
            foreach (var c in Children)
            {
                if (c.Visible) c.Update(dt);
            }
        }

        public override void Draw(SpriteBatch sb)
        {
            // Container doesn't draw a background here; owners may draw using shared texture.
            foreach (var c in Children)
            {
                if (c.Visible) c.Draw(sb);
            }
        }

        // Return the deepest visible child that contains the point (or null if none)
        public UIComponent? Pick(int x, int y)
        {
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                var c = Children[i];
                if (!c.Visible) continue;

                // Ensure bounds are up to date
                c.Update(0f);

                var testRect = c.Bounds;
                if ((int)c.Position.X != testRect.X || (int)c.Position.Y != testRect.Y)
                {
                    testRect = new Rectangle((int)c.Position.X, (int)c.Position.Y, testRect.Width, testRect.Height);
                }

                if (testRect.Contains(x, y))
                {
                    if (c is Container cont)
                    {
                        var inner = cont.Pick(x, y);
                        return inner ?? c;
                    }

                    return c;
                }
            }

            return null;
        }
    }
}
