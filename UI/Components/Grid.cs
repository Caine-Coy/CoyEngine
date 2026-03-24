using CoyEngine.Core;
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#nullable enable

namespace CoyEngine.UI.Components
{
    // Simple grid layout: place children row-major into a grid with fixed number of Columns.
    // Column widths and row heights are computed from children's Bounds (after Update).
    public class Grid : Container
    {
        public int Columns { get; set; } = 1;

        public override void Update(float dt)
        {
            // First let children compute their own bounds
            foreach (var c in Children)
            {
                if (c.Visible) c.Update(dt);
            }

            if (Columns <= 0) Columns = 1;

            int cols = Columns;
            int count = Children.Count;
            int rows = (int)Math.Ceiling((double)count / cols);
            if (rows == 0) rows = 0;

            int[] colWidths = new int[cols];
            int[] rowHeights = new int[rows > 0 ? rows : 0];

            for (int i = 0; i < count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                var b = Children[i].Bounds;
                if (colWidths[col] < b.Width) colWidths[col] = b.Width;
                if (rowHeights[row] < b.Height) rowHeights[row] = b.Height;
            }

            var offset = new Vector2(Padding, Padding) + Position;

            for (int r = 0; r < rows; r++)
            {
                var x = offset.X;
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    if (idx >= count) break;
                    var child = Children[idx];
                    int yOffset = 0;
                    for (int rr = 0; rr < r; rr++) yOffset += rowHeights[rr];
                    child.Position = new Vector2(x, offset.Y + yOffset);
                    // Update child once positioned so its Bounds reflect position (some components use position in Update)
                    child.Update(dt);
                    x += colWidths[c] + Spacing;
                }
            }
        }
    }
}
