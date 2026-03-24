namespace CoyEngine
{
    /// <summary>
    /// What kind of liquid sits on top of a tile.
    /// </summary>
    public enum LiquidType : byte
    {
        None = 0,
        FreshWater = 1,    // Rivers, lakes, springs
        SaltWater = 2,     // Ocean
        // Future: Lava = 3, Mud = 4, ...
    }

    /// <summary>
    /// Encodes 8 compass directions plus "still" for liquid flow.
    /// </summary>
    public enum FlowDirection : byte
    {
        Still = 0,
        N = 1,
        NE = 2,
        E = 3,
        SE = 4,
        S = 5,
        SW = 6,
        W = 7,
        NW = 8,
    }

    /// <summary>
    /// Liquid overlay for a tile. Water (or other liquids) sit ON TOP of the terrain.
    /// The terrain underneath is always a solid material (Sand, Gravel, Stone, etc.)
    /// 
    /// Surface level = terrain height + Depth.
    /// For ocean: terrain dips below 0, water fills to surface level 0.
    ///            So depth = -terrainHeight (e.g. terrain at -3 → depth = 3).
    /// 
    /// Flow is baked at gen-time, ready for future runtime simulation.
    /// </summary>
    public struct Liquid
    {
        public LiquidType Type;
        public byte Depth;              // liquid depth in height units (0 = no liquid)
        public FlowDirection Direction; // which way the liquid flows
        public byte FlowStrength;       // 0 = still (lake/ocean), 255 = raging rapids

        public static readonly Liquid None = default;

        public bool HasLiquid => Type != LiquidType.None && Depth > 0;

        public Liquid(LiquidType type, byte depth, FlowDirection dir = FlowDirection.Still, byte flowStrength = 0)
        {
            Type = type;
            Depth = depth;
            Direction = dir;
            FlowStrength = flowStrength;
        }
    }
}
