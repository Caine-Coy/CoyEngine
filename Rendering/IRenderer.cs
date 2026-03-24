using CoyEngine.Core;
using Microsoft.Xna.Framework;

#nullable enable

namespace CoyEngine.Rendering
{
    public interface IRenderer
    {
        // Optional per-frame update
        void Update(float dt);

        // Draw into the provided RenderContext. GameTime is provided for future timed effects.
        void Draw(GameTime gameTime, RenderContext ctx);
    }
}
