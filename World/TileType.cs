namespace CoyEngine
{
    /// <summary>
    /// Ground material type — what the terrain is made of.
    /// Water is NOT a tile type; it's a Liquid overlay on top of terrain.
    /// </summary>
    public enum TileType : byte
    {
        Empty = 0,
        Grass = 1,
        Sand = 2,
        Stone = 3,
        Gravel = 4,     // Riverbeds, shallow lake floors
        Dirt = 5,        // Wetland/marsh substrate
        Clay = 6,        // Deep lake/ocean floor
        Mountain = 7,    // Impassable high-altitude peaks
        Forest = 8,      // Dense forest ground cover
    }
}