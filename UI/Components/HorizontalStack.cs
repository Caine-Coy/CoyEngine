using CoyEngine.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#nullable enable

namespace CoyEngine.UI.Components
{
    // Convenience wrapper for a horizontal StackPanel
    public class HorizontalStack : StackPanel
    {
        public HorizontalStack()
        {
            Vertical = false;
        }
    }
}
