using CoyEngine.Core;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using CoyEngine.UI.Components;

#nullable enable

namespace CoyEngine.UI
{
    public class UIManager
    {
        private readonly List<UIComponent> _children = new List<UIComponent>();

        // The component currently under the mouse cursor (last seen)
        public UIComponent? Hovered { get; private set; }

        // When true, renderers may draw debug bounds for UI components
        public bool DebugShowBounds { get; set; } = false;

        // Read-only access for renderers and debug tooling
        public IReadOnlyList<UIComponent> Children => _children.AsReadOnly();

        public void Add(UIComponent comp)
        {
            if (comp == null) return;
            _children.Add(comp);
        }

        public void Update(float dt)
        {
            // Always call Update on children so components that animate while hiding (e.g. SlidePanel)
            // can progress their animations even when their Visible flag is false.
            foreach (var c in _children)
            {
                c.Update(dt);
            }
        }

        public void Draw(SpriteBatch sb)
        {
            foreach (var c in _children)
            {
                if (c.Visible) c.Draw(sb);
            }
        }

        // Simple mouse handling: dispatch to children in order; stop on first handler
        public void HandleMouse(MouseState current, MouseState previous)
        {
            Hovered = null;
            int mx = current.X;
            int my = current.Y;

            // Iterate in reverse so topmost children get hit-tested first
            for (int i = _children.Count - 1; i >= 0; i--)
            {
                var c = _children[i];
                if (!c.Visible) continue;

                // Ensure components can compute bounds (some components update bounds in Update)
                c.Update(0f);

                // If component is a container, allow it to pick a deepest child that contains the point
                if (c is Container cont)
                {
                    var picked = cont.Pick(mx, my);
                    if (picked != null)
                    {
                        if (Hovered == null) Hovered = picked;
                        if (picked.HandleMouse(current, previous)) break;
                        continue; // continue to next top-level component if it didn't handle
                    }
                }

                // Update hover state based on bounds (first topmost match)
                var testRect = c.Bounds;
                // If bounds origin doesn't match current position, use position as source of truth
                if ((int)c.Position.X != testRect.X || (int)c.Position.Y != testRect.Y)
                {
                    testRect = new Rectangle((int)c.Position.X, (int)c.Position.Y, testRect.Width, testRect.Height);
                }

                if (Hovered == null && testRect.Contains(mx, my))
                {
                    Hovered = c;
                }

                if (c.HandleMouse(current, previous)) break;
            }

            // Final fallback: if nothing was flagged as hovered, attempt a generic position+size hit-test (useful for tests that set Position/Size but haven't updated Bounds)
            if (Hovered == null)
            {
                for (int i = _children.Count - 1; i >= 0; i--)
                {
                    var c = _children[i];
                    if (!c.Visible) continue;

                    int w = c.Bounds.Width;
                    int h = c.Bounds.Height;

                    // Try to read a Size property if available
                    var t = c.GetType();
                    var pi = t.GetProperty("Size");
                    if (pi != null)
                    {
                        var v = pi.GetValue(c);
                        if (v is Microsoft.Xna.Framework.Vector2 vec)
                        {
                            w = (int)vec.X;
                            h = (int)vec.Y;
                        }
                    }

                    var r = new Microsoft.Xna.Framework.Rectangle((int)c.Position.X, (int)c.Position.Y, Math.Max(0, w), Math.Max(0, h));
                    if (r.Contains(mx, my)) { Hovered = c; break; }
                }
            }
        }
    }
}
