using System;
using System.Collections.Generic;

namespace CoyEngine
{
    /// <summary>
    /// Precomputed world plan at 2048×2048 resolution.
    /// Each cell covers CellSize×CellSize tiles (16×16 by default).
    /// Provides globally coherent elevation, biome, and river data
    /// that chunks read from during generation.
    ///
    /// Memory: ~50 MB (elevation 32 MB + biome 4 MB + rivers 4 MB + flow 8 MB)
    /// Generation time: ~5-10 seconds on modern hardware.
    /// </summary>
    public class WorldPlan
    {
        public const int DefaultResolution = 2048;

        /// <summary>World tile width / plan resolution = tiles per cell.</summary>
        public readonly int CellSize;

        public readonly int Width;
        public readonly int Height;
        public readonly int Seed;

        /// <summary>Coarse elevation [0,1] per cell. 0 = ocean, >0 = land.</summary>
        public readonly double[] Elevation;

        /// <summary>Biome classification per cell (Empty = ocean).</summary>
        public readonly TileType[] Biome;

        /// <summary>River intensity per cell. 0 = no river, 1-255 = flow strength.</summary>
        public readonly byte[] RiverIntensity;

        /// <summary>Flow direction per cell: index offset into grid, or -1 if none.</summary>
        public readonly int[] FlowDirection;

        /// <summary>
        /// Construct and generate the world plan.
        /// </summary>
        public WorldPlan(int seed, int resolution = DefaultResolution)
        {
            Width = resolution;
            Height = resolution;
            Seed = seed;

            int totalWorldW = WorldGenerator.WorldMapsX * 128;
            CellSize = totalWorldW / Width; // 32768 / 2048 = 16

            int n = Width * Height;
            Elevation = new double[n];
            Biome = new TileType[n];
            RiverIntensity = new byte[n];
            FlowDirection = new int[n];

            Generate();
        }

        private void Generate()
        {
            Console.WriteLine($"[WorldPlan] Generating {Width}×{Height} world plan (seed={Seed})...");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            ComputeElevationAndBiome();
            Console.WriteLine($"[WorldPlan] Elevation & biome: {sw.ElapsedMilliseconds}ms");

            ComputeRivers();
            Console.WriteLine($"[WorldPlan] Rivers: {sw.ElapsedMilliseconds}ms");

            Console.WriteLine($"[WorldPlan] Done in {sw.ElapsedMilliseconds}ms total.");
        }

        // ─── Elevation & Biome ───────────────────────────────────────────

        private void ComputeElevationAndBiome()
        {
            int totalW = WorldGenerator.WorldMapsX * 128;
            int totalH = WorldGenerator.WorldMapsY * 128;

            for (int cy = 0; cy < Height; cy++)
            {
                for (int cx = 0; cx < Width; cx++)
                {
                    // World coordinate at center of this cell
                    int wx = (int)(((cx + 0.5) / Width) * totalW);
                    int wy = (int)(((cy + 0.5) / Height) * totalH);

                    int idx = cy * Width + cx;
                    double mask = WorldGenerator.ContinentMask(wx, wy, Seed);

                    if (mask < 0.1)
                    {
                        Elevation[idx] = 0;
                        Biome[idx] = TileType.Empty; // ocean
                        continue;
                    }

                    // Coarse elevation: broad continental + mountain ridges
                    // Same formula as GenerateWorldRivers used, matching SampleElevation
                    double ridge = WorldGenerator.RidgeNoise2(wx, wy,
                        WorldGenerator.MountainRidgeFreq, 3, Seed + 11111);
                    double interiorR = Math.Clamp((mask - 0.15) / 0.35, 0.0, 1.0);
                    double broad = WorldGenerator.FractalNoise2(wx, wy,
                        WorldGenerator.BroadElevFreq, 2, Seed + 22222) * 0.5 + 0.5;
                    double coastal = 0.3 + 0.7 * ((mask - 0.1) / 0.9);
                    Elevation[idx] = (broad * 0.5 + ridge * interiorR * 0.5) * coastal;

                    // Biome classification
                    Biome[idx] = WorldGenerator.SampleBiome(wx, wy, mask, Seed);
                }
            }
        }

        // ─── River Network ───────────────────────────────────────────────

        private void ComputeRivers()
        {
            int n = Width * Height;

            // Step 1: Priority-flood to resolve depressions
            var filled = new double[n];
            Array.Copy(Elevation, filled, n);
            var pq = new PriorityQueue<int, double>();
            var visited = new bool[n];

            // Seed edges and ocean cells
            for (int cy = 0; cy < Height; cy++)
            {
                for (int cx = 0; cx < Width; cx++)
                {
                    int idx = cy * Width + cx;
                    if (Elevation[idx] <= 0 || cx == 0 || cy == 0 ||
                        cx == Width - 1 || cy == Height - 1)
                    {
                        pq.Enqueue(idx, filled[idx]);
                        visited[idx] = true;
                    }
                }
            }

            while (pq.Count > 0)
            {
                int idx = pq.Dequeue();
                int cy = idx / Width;
                int cx = idx % Width;
                double h = filled[idx];

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                        int ni = ny * Width + nx;
                        if (visited[ni]) continue;
                        visited[ni] = true;
                        filled[ni] = Math.Max(Elevation[ni], h + 0.0001);
                        pq.Enqueue(ni, filled[ni]);
                    }
                }
            }

            // Step 2: Flow directions (steepest descent on filled surface)
            for (int i = 0; i < n; i++) FlowDirection[i] = -1;

            for (int cy = 0; cy < Height; cy++)
            {
                for (int cx = 0; cx < Width; cx++)
                {
                    int idx = cy * Width + cx;
                    if (Elevation[idx] <= 0) continue;

                    double bestDrop = 0;
                    int bestTarget = -1;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                            {
                                // Edge drain
                                if (filled[idx] > bestDrop)
                                {
                                    bestDrop = filled[idx];
                                    bestTarget = -2; // drains off edge
                                }
                                continue;
                            }
                            double d = filled[idx] - filled[ny * Width + nx];
                            if (d > bestDrop)
                            {
                                bestDrop = d;
                                bestTarget = ny * Width + nx;
                            }
                        }
                    }
                    FlowDirection[idx] = bestTarget;
                }
            }

            // Step 3: Flow accumulation (topological sort, highest first)
            var indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = i;
            Array.Sort(indices, (a, b) => filled[b].CompareTo(filled[a]));

            var accumulation = new double[n];
            for (int i = 0; i < n; i++) accumulation[i] = 1.0;

            foreach (int idx in indices)
            {
                if (Elevation[idx] <= 0) continue;
                int target = FlowDirection[idx];
                if (target >= 0)
                    accumulation[target] += accumulation[idx];
            }

            // Step 4: Mark river cells (only significant drainage basins)
            const int riverThresh = 500;
            for (int i = 0; i < n; i++)
            {
                if (Elevation[i] > 0 && accumulation[i] >= riverThresh)
                {
                    // Sqrt scaling preserves dynamic range in a byte:
                    // accum  500 → ~0, 1500 → ~95, 5000 → ~201, 8000+ → 255
                    int val = (int)(Math.Sqrt(accumulation[i] - riverThresh) * 3.0);
                    RiverIntensity[i] = (byte)Math.Clamp(val, 1, 255);
                }
            }
        }

        // ─── Lookup Helpers ──────────────────────────────────────────────

        /// <summary>
        /// Convert world tile coordinate to plan cell index.
        /// </summary>
        private (int cx, int cy) WorldToCell(int worldX, int worldY)
        {
            int totalW = WorldGenerator.WorldMapsX * 128;
            int totalH = WorldGenerator.WorldMapsY * 128;
            int cx = (int)((long)worldX * Width / totalW);
            int cy = (int)((long)worldY * Height / totalH);
            cx = Math.Clamp(cx, 0, Width - 1);
            cy = Math.Clamp(cy, 0, Height - 1);
            return (cx, cy);
        }

        /// <summary>Get the biome at a world tile coordinate.</summary>
        public TileType BiomeAt(int worldX, int worldY)
        {
            var (cx, cy) = WorldToCell(worldX, worldY);
            return Biome[cy * Width + cx];
        }

        /// <summary>Get coarse elevation at a world tile coordinate (no interpolation).</summary>
        public double ElevationAt(int worldX, int worldY)
        {
            var (cx, cy) = WorldToCell(worldX, worldY);
            return Elevation[cy * Width + cx];
        }

        /// <summary>
        /// Get bilinear-interpolated elevation at a world tile coordinate.
        /// Smoother than direct cell lookup — good for height blending.
        /// </summary>
        public double ElevationBilinear(int worldX, int worldY)
        {
            int totalW = WorldGenerator.WorldMapsX * 128;
            int totalH = WorldGenerator.WorldMapsY * 128;

            // Continuous cell coordinate
            double fcx = (double)worldX * Width / totalW - 0.5;
            double fcy = (double)worldY * Height / totalH - 0.5;

            int x0 = (int)Math.Floor(fcx);
            int y0 = (int)Math.Floor(fcy);
            double fx = fcx - x0;
            double fy = fcy - y0;

            x0 = Math.Clamp(x0, 0, Width - 2);
            y0 = Math.Clamp(y0, 0, Height - 2);

            double e00 = Elevation[y0 * Width + x0];
            double e10 = Elevation[y0 * Width + x0 + 1];
            double e01 = Elevation[(y0 + 1) * Width + x0];
            double e11 = Elevation[(y0 + 1) * Width + x0 + 1];

            double ix0 = e00 + (e10 - e00) * fx;
            double ix1 = e01 + (e11 - e01) * fx;
            return ix0 + (ix1 - ix0) * fy;
        }

        /// <summary>Get river intensity at a world tile coordinate.</summary>
        public byte RiverAt(int worldX, int worldY)
        {
            var (cx, cy) = WorldToCell(worldX, worldY);
            return RiverIntensity[cy * Width + cx];
        }

        /// <summary>
        /// Returns what fraction of a chunk's plan cells are ocean (elevation == 0).
        /// 0.0 = entirely land, 1.0 = entirely ocean.
        /// </summary>
        public double ChunkOceanFraction(int mapX, int mapY)
        {
            int cellsPerChunkX = Width / WorldGenerator.WorldMapsX;
            int cellsPerChunkY = Height / WorldGenerator.WorldMapsY;
            int startCX = mapX * cellsPerChunkX;
            int startCY = mapY * cellsPerChunkY;

            int total = 0, ocean = 0;
            for (int cy = startCY; cy < startCY + cellsPerChunkY && cy < Height; cy++)
            {
                for (int cx = startCX; cx < startCX + cellsPerChunkX && cx < Width; cx++)
                {
                    total++;
                    if (Elevation[cy * Width + cx] <= 0) ocean++;
                }
            }
            return total == 0 ? 1.0 : (double)ocean / total;
        }

        /// <summary>Check if a chunk is mostly ocean (>70% ocean cells).</summary>
        public bool IsChunkMostlyOcean(int mapX, int mapY) => ChunkOceanFraction(mapX, mapY) > 0.7;

        /// <summary>
        /// Classify a chunk's zone using WorldPlan data for accuracy.
        /// Avoids labeling flooded chunks as "Plains".
        /// </summary>
        public string ClassifyZone(int mapX, int mapY)
        {
            // If the chunk is mostly ocean in the plan, call it Ocean
            double oceanFrac = ChunkOceanFraction(mapX, mapY);
            if (oceanFrac > 0.7) return "Ocean";

            int cellsPerChunkX = Width / WorldGenerator.WorldMapsX;
            int cellsPerChunkY = Height / WorldGenerator.WorldMapsY;
            int startCX = mapX * cellsPerChunkX;
            int startCY = mapY * cellsPerChunkY;

            // Check if ocean is near any edge (coastal detection)
            bool nOcean = false, sOcean = false, eOcean = false, wOcean = false;
            for (int cx = startCX; cx < startCX + cellsPerChunkX && cx < Width; cx++)
            {
                if (startCY > 0 && Elevation[(startCY - 1) * Width + cx] <= 0) nOcean = true;
                int sy = Math.Min(startCY + cellsPerChunkY, Height - 1);
                if (Elevation[sy * Width + cx] <= 0) sOcean = true;
            }
            for (int cy = startCY; cy < startCY + cellsPerChunkY && cy < Height; cy++)
            {
                if (startCX > 0 && Elevation[cy * Width + startCX - 1] <= 0) wOcean = true;
                int sx = Math.Min(startCX + cellsPerChunkX, Width - 1);
                if (Elevation[cy * Width + sx] <= 0) eOcean = true;
            }

            if (nOcean || sOcean || eOcean || wOcean)
            {
                var parts = new System.Collections.Generic.List<string>();
                if (nOcean) parts.Add("North");
                if (sOcean) parts.Add("South");
                if (eOcean) parts.Add("East");
                if (wOcean) parts.Add("West");
                return string.Join(" ", parts) + " Coast";
            }

            // Count biome types in the chunk's cells for dominant classification
            int mountains = 0, stone = 0, forest = 0, sand = 0, grass = 0;
            for (int cy = startCY; cy < startCY + cellsPerChunkY && cy < Height; cy++)
            {
                for (int cx = startCX; cx < startCX + cellsPerChunkX && cx < Width; cx++)
                {
                    var b = Biome[cy * Width + cx];
                    switch (b)
                    {
                        case TileType.Mountain: mountains++; break;
                        case TileType.Stone: stone++; break;
                        case TileType.Forest: forest++; break;
                        case TileType.Sand: sand++; break;
                        default: grass++; break;
                    }
                }
            }

            int max = Math.Max(mountains, Math.Max(stone, Math.Max(forest, Math.Max(sand, grass))));
            if (max == mountains) return "Mountains";
            if (max == stone) return "Highlands";
            if (max == forest) return "Forest";
            if (max == sand) return "Coast";
            return "Plains";
        }

        /// <summary>Check if a chunk has any world-level river passing through it.</summary>
        public bool HasRiverInChunk(int mapX, int mapY)
        {
            // A 128-tile chunk spans Width/WorldMapsX = 2048/256 = 8 cells in each direction
            int cellsPerChunkX = Width / WorldGenerator.WorldMapsX;
            int cellsPerChunkY = Height / WorldGenerator.WorldMapsY;

            int startCX = mapX * cellsPerChunkX;
            int startCY = mapY * cellsPerChunkY;

            // Check a slightly wider area (±1 cell) for rivers near borders
            int x0 = Math.Max(0, startCX - 1);
            int y0 = Math.Max(0, startCY - 1);
            int x1 = Math.Min(Width - 1, startCX + cellsPerChunkX);
            int y1 = Math.Min(Height - 1, startCY + cellsPerChunkY);

            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                    if (RiverIntensity[cy * Width + cx] > 0)
                        return true;
            return false;
        }

        /// <summary>
        /// Get the flow direction at a world tile coordinate as (dx, dy) in cell units.
        /// Returns (0,0) if no flow or ocean.
        /// </summary>
        public (int dx, int dy) FlowDirectionAt(int worldX, int worldY)
        {
            var (cx, cy) = WorldToCell(worldX, worldY);
            int idx = cy * Width + cx;
            int target = FlowDirection[idx];
            if (target < 0) return (0, 0);

            int tx = target % Width;
            int ty = target / Width;
            return (tx - cx, ty - cy);
        }

        /// <summary>
        /// Get the dominant flow direction for a chunk as (dx, dy).
        /// Averages flow directions of river cells within the chunk.
        /// Returns (0,0) if no rivers.
        /// </summary>
        public (double dx, double dy) ChunkFlowDirection(int mapX, int mapY)
        {
            int cellsPerChunkX = Width / WorldGenerator.WorldMapsX;
            int cellsPerChunkY = Height / WorldGenerator.WorldMapsY;
            int startCX = mapX * cellsPerChunkX;
            int startCY = mapY * cellsPerChunkY;

            double totalDx = 0, totalDy = 0;
            int count = 0;

            for (int cy = startCY; cy < startCY + cellsPerChunkY && cy < Height; cy++)
            {
                for (int cx = startCX; cx < startCX + cellsPerChunkX && cx < Width; cx++)
                {
                    int idx = cy * Width + cx;
                    if (RiverIntensity[idx] == 0) continue;
                    int target = FlowDirection[idx];
                    if (target < 0) continue;

                    int tx = target % Width;
                    int ty = target / Width;
                    totalDx += tx - cx;
                    totalDy += ty - cy;
                    count++;
                }
            }

            if (count == 0) return (0, 0);
            return (totalDx / count, totalDy / count);
        }

        /// <summary>
        /// Generate preview tile data directly from the plan.
        /// Much faster than re-sampling noise per pixel.
        /// </summary>
        public TileType[] GeneratePreview(int previewSize)
        {
            var tiles = new TileType[previewSize * previewSize];
            for (int py = 0; py < previewSize; py++)
            {
                for (int px = 0; px < previewSize; px++)
                {
                    // Map preview pixel to plan cell
                    int cx = (int)((double)px / previewSize * Width);
                    int cy = (int)((double)py / previewSize * Height);
                    cx = Math.Clamp(cx, 0, Width - 1);
                    cy = Math.Clamp(cy, 0, Height - 1);
                    tiles[py * previewSize + px] = Biome[cy * Width + cx];
                }
            }
            return tiles;
        }

        /// <summary>
        /// Generate preview river data directly from the plan.
        /// </summary>
        public byte[] GeneratePreviewRivers(int previewSize)
        {
            var rivers = new byte[previewSize * previewSize];
            for (int py = 0; py < previewSize; py++)
            {
                for (int px = 0; px < previewSize; px++)
                {
                    int cx = (int)((double)px / previewSize * Width);
                    int cy = (int)((double)py / previewSize * Height);
                    cx = Math.Clamp(cx, 0, Width - 1);
                    cy = Math.Clamp(cy, 0, Height - 1);
                    rivers[py * previewSize + px] = RiverIntensity[cy * Width + cx];
                }
            }
            return rivers;
        }

        /// <summary>
        /// Generate preview flow direction data.
        /// Each byte: 0 = no river, 1-8 = N,NE,E,SE,S,SW,W,NW (matches FlowDirection enum).
        /// </summary>
        public byte[] GeneratePreviewFlowDirection(int previewSize)
        {
            var flow = new byte[previewSize * previewSize];
            for (int py = 0; py < previewSize; py++)
            {
                for (int px = 0; px < previewSize; px++)
                {
                    int cx = (int)((double)px / previewSize * Width);
                    int cy = (int)((double)py / previewSize * Height);
                    cx = Math.Clamp(cx, 0, Width - 1);
                    cy = Math.Clamp(cy, 0, Height - 1);
                    int idx = cy * Width + cx;
                    if (RiverIntensity[idx] == 0) continue;
                    int target = FlowDirection[idx];
                    if (target < 0) continue;
                    int tx = target % Width;
                    int ty = target / Width;
                    int dx = tx - cx;
                    int dy = ty - cy;
                    // Normalize to unit and encode as FlowDirection enum
                    if (dx > 0) dx = 1; else if (dx < 0) dx = -1;
                    if (dy > 0) dy = 1; else if (dy < 0) dy = -1;
                    byte dir = (dx, dy) switch
                    {
                        (0, -1) => 1,  // N
                        (1, -1) => 2,  // NE
                        (1, 0) => 3,   // E
                        (1, 1) => 4,   // SE
                        (0, 1) => 5,   // S
                        (-1, 1) => 6,  // SW
                        (-1, 0) => 7,  // W
                        (-1, -1) => 8, // NW
                        _ => 0
                    };
                    flow[py * previewSize + px] = dir;
                }
            }
            return flow;
        }

        /// <summary>
        /// Generate preview elevation data (byte: 0=ocean, 1-255 = elevation) from the plan.
        /// Used by the client for topographic shading.
        /// </summary>
        public byte[] GeneratePreviewElevation(int previewSize)
        {
            var elev = new byte[previewSize * previewSize];
            for (int py = 0; py < previewSize; py++)
            {
                for (int px = 0; px < previewSize; px++)
                {
                    int cx = (int)((double)px / previewSize * Width);
                    int cy = (int)((double)py / previewSize * Height);
                    cx = Math.Clamp(cx, 0, Width - 1);
                    cy = Math.Clamp(cy, 0, Height - 1);
                    double e = Elevation[cy * Width + cx];
                    elev[py * previewSize + px] = (byte)Math.Clamp((int)(e * 255), 0, 255);
                }
            }
            return elev;
        }
    }
}
