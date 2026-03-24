using CoyEngine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

#nullable enable

namespace CoyEngine.UI
{
    // Minimal UI base class for screen-space components
    public abstract class UIComponent
    {
        public bool Visible { get; set; } = true;

        // Screen-space bounds for hit testing
        public Rectangle Bounds { get; set; }

        // Screen-space position (used by container layouts)
        public Microsoft.Xna.Framework.Vector2 Position { get; set; } = Microsoft.Xna.Framework.Vector2.Zero;

        // Update with delta seconds
        public virtual void Update(float dt) { }

        // Draw to an already begun SpriteBatch (screen-space)
        public abstract void Draw(SpriteBatch sb);

        // Handle mouse input. Return true if the event was handled and should stop propagation.
        public virtual bool HandleMouse(MouseState current, MouseState previous) => false;
    }
}
