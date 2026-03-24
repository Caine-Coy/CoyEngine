using System;

namespace CoyEngine
{
    /// <summary>
    /// Ground tile — the solid terrain. Heights are signed so terrain can dip
    /// below sea level (0). Liquid overlays sit on top, stored separately.
    /// </summary>
    public struct Tile
    {
        public TileType Type;
        public sbyte Height;
        public sbyte CornerNW;
        public sbyte CornerNE;
        public sbyte CornerSE;
        public sbyte CornerSW;
        public int OwnerID;    //Which dog owns this? (-1 for nature, 0 for the state)

        public Tile(TileType type, sbyte height = 0)
        {
            Type = type;
            Height = height;
            CornerNW = CornerNE = CornerSE = CornerSW = height;
            OwnerID = -1;
        }

        public Tile(TileType type, sbyte nw, sbyte ne, sbyte se, sbyte sw)
        {
            Type = type;
            CornerNW = nw;
            CornerNE = ne;
            CornerSE = se;
            CornerSW = sw;
            Height = (sbyte)((nw + ne + se + sw) / 4);
            OwnerID = -1;
        }

        /// <summary>
        /// Compute display-clamped corner heights, matching TileRenderer's slope-limit
        /// rendering. No corner exceeds min + slopeLimit; slopes beyond that become
        /// cliff walls. This ensures picking and selection outlines match the rendered
        /// terrain surface exactly.
        /// </summary>
        public readonly (int dNW, int dNE, int dSE, int dSW) GetDisplayHeights(int slopeLimit = 1)
        {
            int mn = Math.Min(Math.Min(CornerNW, CornerNE), Math.Min(CornerSE, CornerSW));
            int cap = mn + slopeLimit;
            return (
                Math.Min(CornerNW, cap),
                Math.Min(CornerNE, cap),
                Math.Min(CornerSE, cap),
                Math.Min(CornerSW, cap)
            );
        }
    }
}