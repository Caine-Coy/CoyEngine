using System;

namespace CoyEngine
{
    public class GameMap
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public Tile[] Tiles { get; private set; }
        public Liquid[] Liquids { get; private set; }
        public VegetationType[] Vegetation { get; private set; }
        public byte[]? Rivers { get; private set; }
        public byte[]? PreviewElevation { get; private set; }
        public byte[]? PreviewFlowDirection { get; private set; }

        /// <summary>
        /// Per-tile remaining resource yield. Depleted by gathering; when zero the
        /// tile/vegetation changes to its exhausted form.
        /// </summary>
        public int[] ResourceYield { get; private set; }

        /// <summary>
        /// Per-tile regeneration timer (seconds remaining until +1 yield).
        /// Only ticks for renewable resource tiles (Forest, Grass vegetation).
        /// </summary>
        public float[] RegenTimer { get; private set; }

        public GameMap(int width, int height)
        {
            Width = width;
            Height = height;
            Tiles = new Tile[width * height];
            Liquids = new Liquid[width * height];
            Vegetation = new VegetationType[width * height];
            ResourceYield = new int[width * height];
            RegenTimer = new float[width * height];
        }

        // Helper to get 2D coordinates from our 1D array
        public Tile GetTile(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return new Tile(TileType.Empty); // Return air if out of bounds
            
            return Tiles[y * Width + x];
        }

        // Helper to set tiles
        public void SetTile(int x, int y, Tile tile)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
            {
                Tiles[y * Width + x] = tile;
            }
        }

        public Liquid GetLiquid(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return Liquid.None;

            return Liquids[y * Width + x];
        }

        public void SetLiquid(int x, int y, Liquid liquid)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
            {
                Liquids[y * Width + x] = liquid;
            }
        }

        /// <summary>
        /// Check if a tile is passable (not empty, not covered by water).
        /// Shared between client and server for consistent movement rules.
        /// </summary>
        public bool IsPassable(int x, int y)
        {
            var tile = GetTile(x, y);
            if (tile.Type == TileType.Empty) return false;
            if (tile.Type == TileType.Mountain) return false;

            var liquid = GetLiquid(x, y);
            if (liquid.HasLiquid) return false;

            return true;
        }

        public VegetationType GetVegetation(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return VegetationType.None;

            return Vegetation[y * Width + x];
        }

        public void SetRivers(byte[] rivers) { Rivers = rivers; }

        public void SetPreviewElevation(byte[] elevation) { PreviewElevation = elevation; }

        public void SetPreviewFlowDirection(byte[] flow) { PreviewFlowDirection = flow; }

        public byte GetPreviewFlowDirection(int x, int y)
        {
            if (PreviewFlowDirection == null || x < 0 || x >= Width || y < 0 || y >= Height) return 0;
            return PreviewFlowDirection[y * Width + x];
        }

        public byte GetPreviewElevation(int x, int y)
        {
            if (PreviewElevation == null || x < 0 || x >= Width || y < 0 || y >= Height) return 0;
            return PreviewElevation[y * Width + x];
        }

        public bool HasRiver(int x, int y)
        {
            if (Rivers == null || x < 0 || x >= Width || y < 0 || y >= Height) return false;
            return Rivers[y * Width + x] > 0;
        }

        public void SetVegetation(int x, int y, VegetationType veg)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
            {
                Vegetation[y * Width + x] = veg;
            }
        }

        // --- RESOURCE YIELD HELPERS ---

        /// <summary>
        /// Initialize resource yields for all tiles based on terrain and vegetation.
        /// Call after map is fully loaded/generated.
        /// </summary>
        public void InitializeResourceYields()
        {
            for (int i = 0; i < Width * Height; i++)
            {
                ResourceYield[i] = GetDefaultYield(Tiles[i].Type, Vegetation[i]);
                RegenTimer[i] = 0f;
            }
        }

        /// <summary>
        /// Get the default resource yield for a tile based on its type and vegetation.
        /// </summary>
        public static int GetDefaultYield(TileType tile, VegetationType veg)
        {
            int yield = 0;
            // Terrain-based yield
            yield += tile switch
            {
                TileType.Stone => 15,
                TileType.Gravel => 8,
                TileType.Clay => 12,
                TileType.Forest => 10,
                _ => 0,
            };
            // Vegetation-based yield (additive)
            yield += veg switch
            {
                VegetationType.Forest => 10,
                VegetationType.Grass => 6,
                _ => 0,
            };
            return yield;
        }

        /// <summary>
        /// Get the remaining resource yield at a tile.
        /// </summary>
        public int GetResourceYield(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return 0;
            return ResourceYield[y * Width + x];
        }

        /// <summary>
        /// Deplete resource yield at a tile. Returns amount actually depleted.
        /// When exhausted, changes terrain/vegetation to depleted form.
        /// </summary>
        public int DepleteResource(int x, int y, int amount)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return 0;
            int idx = y * Width + x;
            int available = ResourceYield[idx];
            int taken = Math.Min(amount, available);
            ResourceYield[idx] -= taken;

            if (ResourceYield[idx] <= 0)
            {
                // Terrain depletion transformations
                var tile = Tiles[idx];
                switch (tile.Type)
                {
                    case TileType.Stone:
                        tile.Type = TileType.Gravel;
                        Tiles[idx] = tile;
                        break;
                    case TileType.Gravel:
                        tile.Type = TileType.Dirt;
                        Tiles[idx] = tile;
                        break;
                    case TileType.Clay:
                        tile.Type = TileType.Dirt;
                        Tiles[idx] = tile;
                        break;
                }

                // Vegetation depletion
                if (Vegetation[idx] == VegetationType.Forest)
                {
                    Vegetation[idx] = VegetationType.Grass;
                    // Start regen timer for regrowth
                    RegenTimer[idx] = 0f;
                }
                else if (Vegetation[idx] == VegetationType.Grass)
                {
                    Vegetation[idx] = VegetationType.None;
                    RegenTimer[idx] = 0f;
                }
            }

            return taken;
        }

        /// <summary>
        /// Tick regeneration for renewable resources. Call once per game update.
        /// </summary>
        public void UpdateResourceRegeneration(float deltaSeconds)
        {
            for (int i = 0; i < Width * Height; i++)
            {
                // Only regenerate tiles that are depleted but have renewable potential
                if (ResourceYield[i] >= GetDefaultYield(Tiles[i].Type, Vegetation[i]))
                    continue;

                // Grass vegetation regrows on dirt/grass tiles (fast: 30s per yield point)
                // Forest regrows on tiles adjacent to other forest (slow: 120s per yield point)
                bool canRegen = false;
                float regenRate = 0f;

                if (Vegetation[i] == VegetationType.Grass)
                {
                    canRegen = true;
                    regenRate = 20f;
                }
                else if (Vegetation[i] == VegetationType.None)
                {
                    // Check if this was originally a grassland (Grass/Forest/Dirt tile)
                    var tt = Tiles[i].Type;
                    if (tt == TileType.Grass || tt == TileType.Dirt || tt == TileType.Forest)
                    {
                        canRegen = true;
                        regenRate = 45f; // Slower: bare ground needs more time to regrow vegetation
                    }
                }

                if (!canRegen) continue;

                RegenTimer[i] += deltaSeconds;
                if (RegenTimer[i] >= regenRate)
                {
                    RegenTimer[i] = 0f;
                    ResourceYield[i]++;

                    // When yield recovers enough, restore vegetation
                    if (Vegetation[i] == VegetationType.None && ResourceYield[i] >= 3)
                    {
                        Vegetation[i] = VegetationType.Grass;
                    }
                    else if (Vegetation[i] == VegetationType.Grass && ResourceYield[i] >= 8)
                    {
                        // Forest can regrow only if adjacent to existing forest
                        int x = i % Width;
                        int y = i / Width;
                        if (HasAdjacentForest(x, y))
                        {
                            Vegetation[i] = VegetationType.Forest;
                        }
                    }
                }
            }
        }

        private bool HasAdjacentForest(int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < Width && ny >= 0 && ny < Height)
                {
                    if (Vegetation[ny * Width + nx] == VegetationType.Forest)
                        return true;
                }
            }
            return false;
        }

        // --- ISOMETRIC MATH HELPERS ---
        // We put this in Shared so both Client (Mouse Click) and Server (Range Check) use the same math.
        
        // Convert Grid(x,y) to Screen(x,y)
        // tileWidth/Height represents the size of the sprite asset
        public static (int x, int y) GridToIso(int x, int y, int tileWidth, int tileHeight)
        {
            int screenX = (x - y) * (tileWidth / 2);
            int screenY = (x + y) * (tileHeight / 2);
            return (screenX, screenY);
        }

        /// <summary>
        /// Float overload for sub-tile positioning (smooth dog movement, etc.)
        /// </summary>
        public static (float x, float y) GridToIso(float x, float y, int tileWidth, int tileHeight)
        {
            float screenX = (x - y) * (tileWidth / 2f);
            float screenY = (x + y) * (tileHeight / 2f);
            return (screenX, screenY);
        }

        // --- NETWORK SERIALIZATION (The "Manual" Way) ---
        // This converts the entire map into a byte array to send over the network.
        public byte[] Serialize()
        {
            using (var ms = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(ms))
            {
                writer.Write(Width);
                writer.Write(Height);
                
                foreach (var tile in Tiles)
                {
                    writer.Write((byte)tile.Type);
                    // Write per-corner heights as signed bytes
                    writer.Write(tile.CornerNW);
                    writer.Write(tile.CornerNE);
                    writer.Write(tile.CornerSE);
                    writer.Write(tile.CornerSW);
                    writer.Write(tile.OwnerID);
                }

                foreach (var liquid in Liquids)
                {
                    writer.Write((byte)liquid.Type);
                    writer.Write(liquid.Depth);
                    writer.Write((byte)liquid.Direction);
                    writer.Write(liquid.FlowStrength);
                }

                foreach (var veg in Vegetation)
                {
                    writer.Write((byte)veg);
                }

                return ms.ToArray();
            }
        }

        public static GameMap Deserialize(byte[] data)
        {
            using (var ms = new System.IO.MemoryStream(data))
            using (var reader = new System.IO.BinaryReader(ms))
            {
                int w = reader.ReadInt32();
                int h = reader.ReadInt32();
                var map = new GameMap(w, h);

                for (int i = 0; i < w * h; i++)
                {
                    TileType type = (TileType)reader.ReadByte();
                    // read corners NW,NE,SE,SW as signed
                    sbyte nw = reader.ReadSByte();
                    sbyte ne = reader.ReadSByte();
                    sbyte se = reader.ReadSByte();
                    sbyte sw = reader.ReadSByte();
                    int owner = reader.ReadInt32();
                    
                    map.Tiles[i] = new Tile { Type = type, CornerNW = nw, CornerNE = ne, CornerSE = se, CornerSW = sw, OwnerID = owner };
                }

                // Read liquid data if present
                if (ms.Position < ms.Length)
                {
                    for (int i = 0; i < w * h; i++)
                    {
                        var lType = (LiquidType)reader.ReadByte();
                        byte depth = reader.ReadByte();
                        var dir = (FlowDirection)reader.ReadByte();
                        byte strength = reader.ReadByte();
                        map.Liquids[i] = new Liquid(lType, depth, dir, strength);
                    }
                }

                // Read vegetation data if present (1 byte per tile)
                long remaining = ms.Length - ms.Position;
                if (remaining >= (long)w * h)
                {
                    for (int i = 0; i < w * h; i++)
                    {
                        map.Vegetation[i] = (VegetationType)reader.ReadByte();
                    }
                }

                return map;
            }
        }
    }
}