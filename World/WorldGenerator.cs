using System;
using System.Collections.Generic;

namespace CoyEngine
{
    // Deterministic noise-based world generator that supports sampling at arbitrary world coordinates.
    // Uses multi-scale value noise, hydraulic erosion, Gaussian smoothing, and terracing.
    //
    // Water is produced as a liquid overlay (separate from terrain TileType):
    //   1. Ocean — SaltWater at height-0 tiles flood-filled from world edges
    //   2. Water sources — noise-driven spring points at medium elevations
    //   3. Rivers — FreshWater traced downhill from sources toward ocean or lowpoints
    //   4. Lakes — pooled FreshWater at local minima that rivers flow into
    //   5. Wetlands — shallow FreshWater where rivers terminate at non-ocean low areas
    //
    // Terrain under water gets geological substrates:
    //   Ocean floor → Sand/Clay, Riverbeds → Gravel, Lake/Wetland beds → Dirt
    public static class WorldGenerator
    {
        // Configurable parameters
        public const int WorldMapsX = 256;
        public const int WorldMapsY = 256;
        public const int DefaultSeed = 12345;
        private const int DefaultChunkSize = 128;  // for continent-scale coordinate normalization

        // Tile type noise (continent-scale biomes)
        private const double BaseFrequency = 1.0 / 256.0;

        // Height generation — multi-scale
        private const double ContinentFreq = 1.0 / 80.0;  // very broad rolling landmass
        private const double HillFreq = 1.0 / 25.0;       // medium hill features
        private const double DetailFreq = 1.0 / 8.0;       // small bumps and ridges
        private const double ContinentWeight = 0.55;       // broad shapes dominate
        private const double HillWeight = 0.50;            // moderate hills
        private const double DetailWeight = 0.15;          // visible detail

        private const int TerraceSteps = 10;       // fine plateau levels
        private const int MaxTerrainHeight = 8;    // 4 is gentle terrain

        // Erosion — gentle for natural drainage patterns
        private const int ErosionDroplets = 1200;
        private const double ErosionInertia = 0.4;
        private const double ErosionCapacity = 5.0;
        private const double ErosionDeposition = 0.2;
        private const double ErosionStrength = 0.25;
        private const double ErosionEvapRate = 0.02;
        private const int ErosionMaxSteps = 56;

        // Smoothing
        private const int SmoothPasses = 2;  // moderate blur

        // Water generation
        private const double SpringFrequency = 1.0 / 100.0;  // noise scale for water sources
        private const double SpringThreshold = 0.82;         // noise value above which a spring exists (higher = fewer springs)
        private const int RiverMaxSteps = 400;               // max tiles a river can traverse
        private const double WetlandSpreadChance = 0.35;     // chance of wetland spreading to neighbors

        // World-scale biome noise — coherent at preview (128px) zoom
        public const double MountainRidgeFreq = 1.0 / 6000.0; // mountain chain spacing
        public const double BiomeFreq = 1.0 / 5000.0;         // forest / meadow biome regions
        public const double BroadElevFreq = 1.0 / 3000.0;     // continental elevation bands

        // Public API: sample a TileType at integer world coordinates (fast single-tile preview).
        // Returns the ground material only. Ocean tiles return Empty so the preview can render them as water blue.
        public static TileType SampleTileAt(int worldX, int worldY, int seed = DefaultSeed)
        {
            double mask = ContinentMask(worldX, worldY, seed);
            if (mask < 0.1) return TileType.Empty;  // ocean
            return SampleBiome(worldX, worldY, mask, seed);
        }

        // Public API: sample whether a tile would be ocean at given world coordinates (preview use).
        public static bool SampleIsOceanAt(int worldX, int worldY, int seed = DefaultSeed)
        {
            return ContinentMask(worldX, worldY, seed) < 0.1;
        }

        // Public API: classify a map chunk's terrain zone based on continent mask and elevation.
        // Returns a human-readable label like "East Coast", "Mountains", "Plains", etc.
        public static string ClassifyZone(int mapX, int mapY, int seed = DefaultSeed, int chunkSize = 128)
        {
            int baseX = mapX * chunkSize;
            int baseY = mapY * chunkSize;
            int halfW = chunkSize / 2;
            int halfH = chunkSize / 2;

            // Sample continent mask at center and edge midpoints
            double center = ContinentMask(baseX + halfW, baseY + halfH, seed);
            double north  = ContinentMask(baseX + halfW, baseY + 2, seed);
            double south  = ContinentMask(baseX + halfW, baseY + chunkSize - 2, seed);
            double east   = ContinentMask(baseX + chunkSize - 2, baseY + halfH, seed);
            double west   = ContinentMask(baseX + 2, baseY + halfH, seed);

            const double waterLine = 0.15;

            if (center < waterLine) return "Ocean";

            bool nWater = north < waterLine;
            bool sWater = south < waterLine;
            bool eWater = east  < waterLine;
            bool wWater = west  < waterLine;

            if (nWater || sWater || eWater || wWater)
            {
                var parts = new System.Collections.Generic.List<string>();
                if (nWater) parts.Add("North");
                if (sWater) parts.Add("South");
                if (eWater) parts.Add("East");
                if (wWater) parts.Add("West");
                return string.Join(" ", parts) + " Coast";
            }

            // Interior classification using biome sampling
            var tile = SampleBiome(baseX + halfW, baseY + halfH, center, seed);
            return tile switch
            {
                TileType.Mountain => "Mountains",
                TileType.Stone => "Highlands",
                TileType.Forest => "Forest",
                TileType.Sand => "Coast",
                _ => "Plains"
            };
        }

        // Public: generate a map-sized tile, liquid, and vegetation array for given map coordinates.
        // Runs the full pipeline: land tiles, water overlay, then vegetation placement.
        public static (TileType[] Tiles, Liquid[] Liquids, sbyte[] CornerHeights, VegetationType[] Vegetation) GenerateMapAt(int mapX, int mapY, int width, int height, int seed = DefaultSeed)
        {
            // --- Pass 1: land tile types from noise (no water) ---
            var tiles = new TileType[width * height];
            var liquids = new Liquid[width * height];
            int baseWorldX = mapX * width;
            int baseWorldY = mapY * height;

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    tiles[y * width + x] = SampleLandTileAt(baseWorldX + x, baseWorldY + y, seed);

            // --- Generate corner heights (same pipeline as GenerateCornerHeightsAt) ---
            // We use the terraced heights for the water pass so that "sea level = all
            // corners 0" matches exactly what the client renders.
            var cornerHeights = GenerateCornerHeightsAt(mapX, mapY, width, height, seed);

            // Build a per-tile height grid from terraced corners (average of 4 corners).
            // This keeps water pass decisions consistent with rendered tile elevations.
            var heightGrid = new double[width * height];
            for (int i = 0; i < width * height; i++)
            {
                int ci = i * 4;
                heightGrid[i] = (cornerHeights[ci] + cornerHeights[ci + 1] + cornerHeights[ci + 2] + cornerHeights[ci + 3]) / 4.0;
            }

            // Also keep raw noise height grid for flow simulation (rivers need continuous slope)
            var rawHeightGrid = BuildHeightGrid(baseWorldX, baseWorldY, width, height, seed);

            // Check if this chunk has a world-level river passing through it
            bool hasWorldRiver = HasWorldRiver(mapX, mapY, seed);

            // --- Pass 1b: world-level river influence (carve depression for correct flow) ---
            if (hasWorldRiver)
                ApplyWorldRiverBias(heightGrid, rawHeightGrid, width, height, mapX, mapY, seed);

            // --- Pass 2: dynamic water placement (populates liquids[], adjusts terrain substrates) ---
            // This also carves river valleys into cornerHeights.
            // Only generate rivers/lakes on chunks tagged with a world river.
            ApplyWaterPass(tiles, liquids, heightGrid, rawHeightGrid, cornerHeights, width, height, baseWorldX, baseWorldY, seed, hasWorldRiver);

            // --- Pass 3: vegetation placement (forest/grass) ---
            var vegetation = GenerateVegetation(tiles, liquids, cornerHeights, width, height, baseWorldX, baseWorldY, seed);

            return (tiles, liquids, cornerHeights, vegetation);
        }

        // =========================================================
        //  PLAN-BASED GENERATION
        //  When a WorldPlan is provided, biomes are read from the plan
        //  (globally coherent), rivers use the plan's drainage network,
        //  and corner heights skip chunk-local erosion/smoothing for
        //  seamless borders between neighboring chunks.
        // =========================================================

        /// <summary>
        /// Generate a map chunk using a precomputed WorldPlan for global coherence.
        /// Rivers are stamped directly from the plan's global drainage network
        /// instead of per-chunk water simulation, ensuring cross-chunk continuity.
        /// </summary>
        public static (TileType[] Tiles, Liquid[] Liquids, sbyte[] CornerHeights, VegetationType[] Vegetation)
            GenerateMapAt(int mapX, int mapY, int width, int height, WorldPlan plan)
        {
            int seed = plan.Seed;
            var tiles = new TileType[width * height];
            var liquids = new Liquid[width * height];
            int baseWorldX = mapX * width;
            int baseWorldY = mapY * height;

            // --- Pass 1: biome tiles from per-tile noise (globally coherent, smooth boundaries) ---
            // Use ContinentMask for ocean/land split, SampleBiome for land types.
            // Calling SampleBiome per-tile instead of plan.BiomeAt avoids the 16×16
            // block staircase that plan-resolution nearest-neighbor lookup produces.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int wx = baseWorldX + x;
                    int wy = baseWorldY + y;
                    double mask = ContinentMask(wx, wy, seed);
                    if (mask < 0.1)
                    {
                        tiles[y * width + x] = TileType.Sand; // ocean floor substrate
                    }
                    else
                    {
                        tiles[y * width + x] = SampleBiome(wx, wy, mask, seed);
                    }
                }
            }

            // --- Corner heights: noise + WorldPlan base, NO erosion/smoothing ---
            var cornerHeights = GenerateCornerHeightsFromPlan(plan, mapX, mapY, width, height);

            // Raw continuous height grid (for ocean substrate decisions)
            var rawHeightGrid = BuildHeightGrid(baseWorldX, baseWorldY, width, height, seed);

            // --- Pass 2a: Ocean placement from WorldPlan biome (not corner-height BFS) ---
            PlaceOceanFromPlan(tiles, liquids, cornerHeights, rawHeightGrid,
                width, height, baseWorldX, baseWorldY, plan);

            // --- Pass 2b: Stamp rivers directly from WorldPlan ---
            StampRiversFromPlan(tiles, liquids, cornerHeights, width, height,
                mapX, mapY, plan);

            // --- Pass 3: vegetation ---
            var vegetation = GenerateVegetation(tiles, liquids, cornerHeights,
                width, height, baseWorldX, baseWorldY, seed);

            return (tiles, liquids, cornerHeights, vegetation);
        }

        /// <summary>
        /// Generate corner heights using WorldPlan for globally coherent elevation.
        /// Skips chunk-local erosion and smoothing so borders match perfectly.
        /// </summary>
        private static sbyte[] GenerateCornerHeightsFromPlan(WorldPlan plan, int mapX, int mapY,
            int width, int height, int maxHeight = MaxTerrainHeight)
        {
            int seed = plan.Seed;
            int baseWorldX = mapX * width;
            int baseWorldY = mapY * height;

            int cw = width + 1;
            int ch = height + 1;
            var cornerMap = new double[cw * ch];

            for (int cy = 0; cy < ch; cy++)
            {
                for (int cx = 0; cx < cw; cx++)
                {
                    int wx = baseWorldX + cx;
                    int wy = baseWorldY + cy;

                    double mask = ContinentMask(wx, wy, seed);

                    if (mask < 0.1)
                    {
                        cornerMap[cy * cw + cx] = 0.0; // ocean
                        continue;
                    }

                    // Broad elevation from WorldPlan (bilinear for smooth gradients)
                    double planElev = plan.ElevationBilinear(wx, wy);

                    // Add local detail noise for texture (deterministic per world coord)
                    double hills = FractalNoise2(wx, wy, HillFreq, 3, seed + 55555);
                    double detail = FractalNoise2(wx, wy, DetailFreq, 2, seed + 77777);
                    double localDetail = hills * 0.25 + detail * 0.10;

                    // Blend plan elevation with local detail
                    double h = planElev + localDetail;
                    h = Math.Clamp(h, 0.0, 1.0);

                    // Coastal scaling
                    double coastal = 0.3 + 0.7 * ((mask - 0.1) / 0.9);
                    cornerMap[cy * cw + cx] = h * coastal;
                }
            }

            // Smooth the corner map to reduce jagged coastlines and height noise.
            // 3×3 box blur — averaged with original for a gentle smoothing pass.
            var smoothed = new double[cw * ch];
            for (int sy = 0; sy < ch; sy++)
            {
                for (int sx = 0; sx < cw; sx++)
                {
                    double sum = 0;
                    int count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = sx + dx, ny = sy + dy;
                            if (nx < 0 || nx >= cw || ny < 0 || ny >= ch) continue;
                            sum += cornerMap[ny * cw + nx];
                            count++;
                        }
                    }
                    double avg = sum / count;
                    double orig = cornerMap[sy * cw + sx];
                    // Blend: 60% smoothed, 40% original to keep some local detail
                    smoothed[sy * cw + sx] = avg * 0.6 + orig * 0.4;
                }
            }
            cornerMap = smoothed;

            // Terrace and pack into per-tile corner sbytes
            const double OceanThreshold = 0.08;
            var heights = new sbyte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double hNW = cornerMap[y * cw + x];
                    double hNE = cornerMap[y * cw + (x + 1)];
                    double hSE = cornerMap[(y + 1) * cw + (x + 1)];
                    double hSW = cornerMap[(y + 1) * cw + x];

                    var idx = (y * width + x) * 4;
                    heights[idx + 0] = TerraceCorner(hNW, maxHeight, TerraceSteps, OceanThreshold);
                    heights[idx + 1] = TerraceCorner(hNE, maxHeight, TerraceSteps, OceanThreshold);
                    heights[idx + 2] = TerraceCorner(hSE, maxHeight, TerraceSteps, OceanThreshold);
                    heights[idx + 3] = TerraceCorner(hSW, maxHeight, TerraceSteps, OceanThreshold);
                }
            }

            return heights;
        }

        // =========================================================
        //  PLAN-BASED OCEAN PLACEMENT
        //  Uses WorldPlan biome data instead of corner-height BFS,
        //  so low-elevation coastal land isn't mis-classified as ocean.
        // =========================================================

        /// <summary>
        /// Place ocean tiles using the continuous ContinentMask function.
        /// This produces smooth, organic coastlines instead of 16-tile blocks.
        /// Also adds coastal beach transitions with sloped corners into the sea.
        /// </summary>
        private static void PlaceOceanFromPlan(
            TileType[] tiles, Liquid[] liquids, sbyte[] cornerHeights,
            double[] rawHeightGrid, int width, int height,
            int baseWorldX, int baseWorldY, WorldPlan plan)
        {
            int seed = plan.Seed;
            var oceanTiles = new HashSet<int>();

            // Use the same ContinentMask threshold (0.1) as corner height generation.
            // This ensures the ocean boundary aligns perfectly with where corners go to 0.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int wx = baseWorldX + x;
                    int wy = baseWorldY + y;
                    double mask = ContinentMask(wx, wy, seed);
                    if (mask >= 0.1) continue; // land

                    int idx = y * width + x;
                    tiles[idx] = rawHeightGrid[idx] < 0.03 ? TileType.Clay : TileType.Sand;
                    byte depth = (byte)Math.Clamp((int)((0.08 - rawHeightGrid[idx]) * 50), 1, 255);
                    liquids[idx] = new Liquid(LiquidType.SaltWater, depth, FlowDirection.Still, 0);
                    // Flatten to sea level
                    int ci = idx * 4;
                    cornerHeights[ci] = 0;
                    cornerHeights[ci + 1] = 0;
                    cornerHeights[ci + 2] = 0;
                    cornerHeights[ci + 3] = 0;
                    oceanTiles.Add(idx);
                }
            }

            // Ensure non-ocean tiles never sit at height 0 — raise to at least 1.
            // This prevents dry land at the same level as the sea surface.
            for (int i = 0; i < width * height; i++)
            {
                if (oceanTiles.Contains(i)) continue;
                int ci = i * 4;
                for (int c = 0; c < 4; c++)
                {
                    if (cornerHeights[ci + c] < 1)
                        cornerHeights[ci + c] = 1;
                }
            }

            // Coastal expansion: land tiles directly adjacent to ocean get a
            // beach transition — lower corners facing the ocean to 0 and add
            // shallow saltwater overlay.
            var coastalTiles = new List<int>();
            int[] dxs = { 0, 1, 0, -1 };
            int[] dys = { -1, 0, 1, 0 };
            for (int i = 0; i < width * height; i++)
            {
                if (oceanTiles.Contains(i)) continue;

                int cx = i % width;
                int cy = i / width;
                bool adjOcean = false;
                for (int d = 0; d < 4 && !adjOcean; d++)
                {
                    int nx = cx + dxs[d], ny = cy + dys[d];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    if (oceanTiles.Contains(ny * width + nx))
                        adjOcean = true;
                }
                if (adjOcean)
                    coastalTiles.Add(i);
            }

            foreach (int idx in coastalTiles)
            {
                tiles[idx] = TileType.Sand;
                liquids[idx] = new Liquid(LiquidType.SaltWater, 1, FlowDirection.Still, 0);
                // Lower corners shared with ocean neighbors to 0 for a slope into the sea
                int cx2 = idx % width;
                int cy2 = idx / width;
                // Check each neighbor direction and lower the corresponding corners
                // NW=0, NE=1, SE=2, SW=3
                // North neighbor → lower NW(0) and NE(1)
                if (cy2 > 0 && oceanTiles.Contains((cy2 - 1) * width + cx2))
                {
                    int ci = idx * 4;
                    cornerHeights[ci + 0] = 0; // NW
                    cornerHeights[ci + 1] = 0; // NE
                }
                // South neighbor → lower SE(2) and SW(3)
                if (cy2 < height - 1 && oceanTiles.Contains((cy2 + 1) * width + cx2))
                {
                    int ci = idx * 4;
                    cornerHeights[ci + 2] = 0; // SE
                    cornerHeights[ci + 3] = 0; // SW
                }
                // West neighbor → lower NW(0) and SW(3)
                if (cx2 > 0 && oceanTiles.Contains(cy2 * width + cx2 - 1))
                {
                    int ci = idx * 4;
                    cornerHeights[ci + 0] = 0; // NW
                    cornerHeights[ci + 3] = 0; // SW
                }
                // East neighbor → lower NE(1) and SE(2)
                if (cx2 < width - 1 && oceanTiles.Contains(cy2 * width + cx2 + 1))
                {
                    int ci = idx * 4;
                    cornerHeights[ci + 1] = 0; // NE
                    cornerHeights[ci + 2] = 0; // SE
                }
            }
        }

        // =========================================================
        //  PLAN-BASED RIVER STAMPING
        //  Traces rivers from the WorldPlan's global drainage network
        //  directly onto chunk tiles. Rivers follow plan flow directions
        //  and naturally cross chunk boundaries.
        // =========================================================

        /// <summary>
        /// Stamp rivers from WorldPlan directly onto chunk tiles.
        /// For each plan cell with a river, traces a line from cell center
        /// to its flow target's center. Meander noise adds natural curves.
        /// </summary>
        private static void StampRiversFromPlan(
            TileType[] tiles, Liquid[] liquids, sbyte[] cornerHeights,
            int width, int height, int mapX, int mapY, WorldPlan plan)
        {
            int seed = plan.Seed;
            int baseWorldX = mapX * width;
            int baseWorldY = mapY * height;

            int totalWorldW = WorldGenerator.WorldMapsX * 128;
            int totalWorldH = WorldGenerator.WorldMapsY * 128;

            // Plan cells overlapping this chunk (±2 cell border for smooth connections)
            int cellsPerChunkX = plan.Width / WorldGenerator.WorldMapsX; // 8
            int cellsPerChunkY = plan.Height / WorldGenerator.WorldMapsY;
            int startCX = mapX * cellsPerChunkX;
            int startCY = mapY * cellsPerChunkY;

            int cx0 = Math.Max(0, startCX - 2);
            int cy0 = Math.Max(0, startCY - 2);
            int cx1 = Math.Min(plan.Width - 1, startCX + cellsPerChunkX + 1);
            int cy1 = Math.Min(plan.Height - 1, startCY + cellsPerChunkY + 1);

            // Collect all river tile indices with their flow info
            var riverTileSet = new HashSet<int>();
            var riverFlow = new Dictionary<int, (int dx, int dy, byte intensity)>();

            for (int pcy = cy0; pcy <= cy1; pcy++)
            {
                for (int pcx = cx0; pcx <= cx1; pcx++)
                {
                    int pidx = pcy * plan.Width + pcx;
                    byte intensity = plan.RiverIntensity[pidx];
                    if (intensity < 5) continue; // skip marginal drainage

                    int target = plan.FlowDirection[pidx];
                    if (target < 0) continue;

                    int tcx = target % plan.Width;
                    int tcy = target / plan.Width;

                    // World-space center of source and target cells
                    double srcWX = ((pcx + 0.5) / plan.Width) * totalWorldW;
                    double srcWY = ((pcy + 0.5) / plan.Height) * totalWorldH;
                    double tgtWX = ((tcx + 0.5) / plan.Width) * totalWorldW;
                    double tgtWY = ((tcy + 0.5) / plan.Height) * totalWorldH;

                    // River half-width in tiles (based on flow intensity)
                    // With sqrt-scaled intensity: 5-30=brook, 30-100=stream, 100-180=river, 180+=major
                    int halfW = intensity < 30 ? 0 : intensity < 100 ? 1 : intensity < 180 ? 2 : 3;

                    // Flow direction for liquid overlays
                    int flowDx = tcx - pcx;
                    int flowDy = tcy - pcy;

                    // Trace the river segment with sub-tile steps
                    int steps = Math.Max(20, (int)(Math.Sqrt(
                        (tgtWX - srcWX) * (tgtWX - srcWX) + (tgtWY - srcWY) * (tgtWY - srcWY)) / 2.0));

                    for (int s = 0; s <= steps; s++)
                    {
                        double t = (double)s / steps;
                        double wx = srcWX + (tgtWX - srcWX) * t;
                        double wy = srcWY + (tgtWY - srcWY) * t;

                        // Meander: noise-based lateral offset perpendicular to flow
                        double perpX = -(tgtWY - srcWY);
                        double perpY = tgtWX - srcWX;
                        double perpLen = Math.Sqrt(perpX * perpX + perpY * perpY);
                        if (perpLen > 0.01)
                        {
                            perpX /= perpLen;
                            perpY /= perpLen;
                            // Large-scale meander (broad curves)
                            double meander = FractalNoise2((int)wx, (int)wy, 1.0 / 80.0, 2, seed + 90000) * 4.0;
                            // Small-scale wobble
                            meander += FractalNoise2((int)wx, (int)wy, 1.0 / 20.0, 2, seed + 91000) * 1.0;
                            wx += perpX * meander;
                            wy += perpY * meander;
                        }

                        // Convert to local chunk coordinates
                        int lx = (int)wx - baseWorldX;
                        int ly = (int)wy - baseWorldY;

                        // Stamp a circle of river tiles around this point
                        for (int dy = -halfW; dy <= halfW; dy++)
                        {
                            for (int dx = -halfW; dx <= halfW; dx++)
                            {
                                // Circular mask
                                if (dx * dx + dy * dy > (halfW + 0.5) * (halfW + 0.5)) continue;

                                int tx = lx + dx;
                                int ty = ly + dy;
                                if (tx < 0 || tx >= width || ty < 0 || ty >= height) continue;

                                int idx = ty * width + tx;
                                riverTileSet.Add(idx);
                                // Store flow info (last write wins = downstream wins)
                                riverFlow[idx] = (flowDx, flowDy, intensity);
                            }
                        }
                    }
                }
            }

            // Apply river tiles: set liquid, substrate, carve terrain
            foreach (int idx in riverTileSet)
            {
                // Don't overwrite ocean
                if (liquids[idx].Type == LiquidType.SaltWater) continue;

                // Substrate
                tiles[idx] = TileType.Gravel;

                // Flow direction
                var (fdx, fdy, intensity) = riverFlow[idx];
                var flowDir = ToFlowDirection(fdx, fdy);
                byte flowStr = (byte)Math.Clamp(intensity / 2, 10, 200);
                byte depth = intensity < 50 ? (byte)2 : intensity < 150 ? (byte)3 : (byte)4;
                liquids[idx] = new Liquid(LiquidType.FreshWater, depth, flowDir, flowStr);

                // Carve terrain: lower corners by 2 below the tile's own minimum.
                // This ensures rivers are always genuinely depressed below
                // surrounding terrain. Fresh water renders at minCorner + 0.65,
                // so subtracting 2 puts it well below original land level.
                int ci = idx * 4;
                int minH = Math.Min(
                    Math.Min(cornerHeights[ci], cornerHeights[ci + 1]),
                    Math.Min(cornerHeights[ci + 2], cornerHeights[ci + 3]));
                int targetH = Math.Max(0, minH - 2);
                cornerHeights[ci]     = (sbyte)targetH;
                cornerHeights[ci + 1] = (sbyte)targetH;
                cornerHeights[ci + 2] = (sbyte)targetH;
                cornerHeights[ci + 3] = (sbyte)targetH;
            }
        }

        /// <summary>Convert (dx,dy) direction to FlowDirection enum.</summary>
        private static FlowDirection ToFlowDirection(int dx, int dy)
        {
            // Normalize to unit direction
            if (dx > 0) dx = 1; else if (dx < 0) dx = -1;
            if (dy > 0) dy = 1; else if (dy < 0) dy = -1;

            return (dx, dy) switch
            {
                (0, -1) => FlowDirection.N,
                (1, -1) => FlowDirection.NE,
                (1, 0)  => FlowDirection.E,
                (1, 1)  => FlowDirection.SE,
                (0, 1)  => FlowDirection.S,
                (-1, 1) => FlowDirection.SW,
                (-1, 0) => FlowDirection.W,
                (-1, -1) => FlowDirection.NW,
                _ => FlowDirection.Still
            };
        }

        // Public helper: generate vegetation for an existing map chunk (tiles+liquids+corners).
        public static VegetationType[] GenerateVegetationAt(int mapX, int mapY, int width, int height,
            TileType[] tiles, Liquid[] liquids, sbyte[] cornerHeights, int seed = DefaultSeed)
        {
            if (tiles.Length != width * height) throw new ArgumentException("tiles length mismatch");
            if (liquids.Length != width * height) throw new ArgumentException("liquids length mismatch");
            if (cornerHeights.Length != width * height * 4) throw new ArgumentException("cornerHeights length mismatch");

            int baseWorldX = mapX * width;
            int baseWorldY = mapY * height;
            return GenerateVegetation(tiles, liquids, cornerHeights, width, height, baseWorldX, baseWorldY, seed);
        }

        // Vegetation pass: spawn forest and grass tufts on suitable terrain.
        private static VegetationType[] GenerateVegetation(TileType[] tiles, Liquid[] liquids, sbyte[] cornerHeights,
            int width, int height, int baseWorldX, int baseWorldY, int seed)
        {
            var vegetation = new VegetationType[width * height];

            const double ForestClusterFreq = 1.0 / 48.0;
            const double GrassClusterFreq = 1.0 / 12.0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    if (liquids[idx].HasLiquid) continue;

                    var tile = tiles[idx];
                    if (tile == TileType.Empty || tile == TileType.Sand || tile == TileType.Stone || tile == TileType.Clay || tile == TileType.Mountain)
                        continue;

                    int ci = idx * 4;
                    int minC = Math.Min(Math.Min(cornerHeights[ci], cornerHeights[ci + 1]),
                        Math.Min(cornerHeights[ci + 2], cornerHeights[ci + 3]));
                    int maxC = Math.Max(Math.Max(cornerHeights[ci], cornerHeights[ci + 1]),
                        Math.Max(cornerHeights[ci + 2], cornerHeights[ci + 3]));
                    int slope = maxC - minC;

                    int wx = baseWorldX + x;
                    int wy = baseWorldY + y;

                    // Forest tiles: dense tree cover (biome-driven)
                    if (tile == TileType.Forest && slope <= 2)
                    {
                        double forestRand = Hash01(wx, wy, seed + 44444);
                        if (forestRand > 0.12) // ~88% coverage
                        {
                            vegetation[idx] = VegetationType.Forest;
                            continue;
                        }
                    }

                    // Grass tiles: occasional clustered trees + grass tufts
                    if (tile == TileType.Grass && slope <= 1)
                    {
                        double forestCluster = FractalNoise2(wx, wy, ForestClusterFreq, 2, seed + 22222) * 0.5 + 0.5;
                        double forestRand = Hash01(wx, wy, seed + 44444);
                        if (forestCluster > 0.62 && forestRand > 0.75)
                        {
                            vegetation[idx] = VegetationType.Forest;
                            continue;
                        }
                    }

                    // Grass tufts: on grass/dirt/gravel/forest clearings, light scatter
                    if (tile == TileType.Grass || tile == TileType.Dirt || tile == TileType.Gravel || tile == TileType.Forest)
                    {
                        double grassCluster = FractalNoise2(wx, wy, GrassClusterFreq, 2, seed + 33333) * 0.5 + 0.5;
                        double grassRand = Hash01(wx + 17, wy - 23, seed + 55555);
                        if (grassCluster > 0.28 && grassRand > 0.45)
                        {
                            vegetation[idx] = VegetationType.Grass;
                        }
                    }
                }
            }

            return vegetation;
        }

        // Core biome sampling — shared by SampleTileAt and SampleLandTileAt.
        // Uses large-scale noise for coherent mountain ranges, forests, and meadows.
        public static TileType SampleBiome(int worldX, int worldY, double mask, int seed)
        {
            double interior = Math.Clamp((mask - 0.15) / 0.35, 0.0, 1.0);

            // Mountain ridges — ridge noise creates long coherent chains
            double ridge = RidgeNoise2(worldX, worldY, MountainRidgeFreq, 3, seed + 11111);
            if (ridge > 0.72 && interior > 0.4) return TileType.Mountain;

            // Highlands — broad elevated zones near ridges
            if (ridge > 0.55 && interior > 0.25) return TileType.Stone;

            // Coastal sand strip
            if (mask < 0.18) return TileType.Sand;

            // Forest vs grassland / meadow
            double biome = FractalNoise2(worldX, worldY, BiomeFreq, 3, seed + 33333) * 0.5 + 0.5;
            double localVar = FractalNoise2(worldX, worldY, 1.0 / 500.0, 2, seed + 44444) * 0.5 + 0.5;
            double forestScore = biome * 0.7 + localVar * 0.3;
            if (forestScore > 0.52) return TileType.Forest;

            return TileType.Grass;
        }

        // Land-only tile sampling (first pass) — pure geological material
        private static TileType SampleLandTileAt(int worldX, int worldY, int seed)
        {
            double mask = ContinentMask(worldX, worldY, seed);
            if (mask < 0.1) return TileType.Sand;    // ocean floor substrate
            return SampleBiome(worldX, worldY, mask, seed);
        }

        // Quick elevation sample — broad elevation + ridge contribution.
        // Used by ClassifyZone and height integration.
        private static double SampleElevation(int worldX, int worldY, int seed)
        {
            double broad = FractalNoise2(worldX, worldY, BroadElevFreq, 2, seed + 22222) * 0.5 + 0.5;
            double ridge = RidgeNoise2(worldX, worldY, MountainRidgeFreq, 3, seed + 11111);
            double mask = ContinentMask(worldX, worldY, seed);
            double interior = Math.Clamp((mask - 0.15) / 0.35, 0.0, 1.0);
            return Math.Clamp(broad * 0.6 + ridge * interior * 0.4, 0.0, 1.0);
        }

        // Build a per-tile continuous height grid for flow simulation.
        // Uses the same noise as corner heights but returns a single value per tile.
        private static double[] BuildHeightGrid(int baseWorldX, int baseWorldY, int width, int height, int seed)
        {
            var grid = new double[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int wx = baseWorldX + x;
                    int wy = baseWorldY + y;

                    double continent = FractalNoise2(wx, wy, ContinentFreq, 3, seed + 9999);
                    double hills = FractalNoise2(wx, wy, HillFreq, 3, seed + 55555);
                    double detail = FractalNoise2(wx, wy, DetailFreq, 2, seed + 77777);

                    double h = continent * ContinentWeight
                             + hills * HillWeight
                             + detail * DetailWeight;
                    h = Math.Clamp(h * 0.5 + 0.5, 0.0, 1.0);

                    // Apply continent mask: hard ocean cut at coastline, gentle scaling inland.
                    // This ensures the coastline matches SampleTileAt's mask < 0.1 threshold.
                    double mask = ContinentMask(wx, wy, seed);

                    // Boost height in mountain ridge areas for consistent elevated terrain
                    double ridge = RidgeNoise2(wx, wy, MountainRidgeFreq, 3, seed + 11111);
                    double interior = Math.Clamp((mask - 0.15) / 0.35, 0.0, 1.0);
                    h = Math.Clamp(h + ridge * interior * 0.35, 0.0, 1.0);

                    if (mask < 0.1)
                        grid[y * width + x] = 0.0;  // ocean floor
                    else
                    {
                        // Remap mask [0.1, 1.0] → [0.3, 1.0] so coastal land isn't too flat
                        double coastal = 0.3 + 0.7 * ((mask - 0.1) / 0.9);
                        grid[y * width + x] = h * coastal;
                    }
                }
            }
            return grid;
        }

        // =========================================================
        //  PASS 2 — Hydrological water placement (RCT2 / SimCity 4 inspired)
        //
        //  Delegates to WaterSimulation which uses:
        //    1. Priority-flood depression filling (every cell drains to edge/ocean)
        //    2. Flow-accumulation rivers (steepest-descent drainage network)
        //    3. Basin-fill lakes at depressions (flat water at spill level)
        //    4. Ocean flood-fill from world edges
        // =========================================================

        private static void ApplyWaterPass(TileType[] tiles, Liquid[] liquids, double[] heightGrid,
            double[] rawHeightGrid, sbyte[] cornerHeights, int width, int height,
            int baseWorldX, int baseWorldY, int seed, bool hasWorldRiver)
        {
            WaterSimulation.PlaceWater(tiles, liquids, heightGrid, rawHeightGrid,
                cornerHeights, width, height, baseWorldX, baseWorldY, seed, hasWorldRiver);
        }


        // Public: generate per-tile per-corner height map (4 sbytes per tile: NW,NE,SE,SW).
        // Ocean areas (below OceanThreshold) get negative heights representing depth below sea level.
        public static sbyte[] GenerateCornerHeightsAt(int mapX, int mapY, int width, int height,
            int seed = DefaultSeed, int maxHeight = MaxTerrainHeight)
        {
            int baseWorldX = mapX * width;
            int baseWorldY = mapY * height;

            // Corner grid is (width+1) x (height+1)
            int cw = width + 1;
            int ch = height + 1;
            var cornerMap = new double[cw * ch];

            // Step 1: Multi-scale height noise
            for (int cy = 0; cy < ch; cy++)
            {
                for (int cx = 0; cx < cw; cx++)
                {
                    int wx = baseWorldX + cx;
                    int wy = baseWorldY + cy;

                    // Three noise layers at different scales
                    double continent = FractalNoise2(wx, wy, ContinentFreq, 3, seed + 9999);
                    double hills = FractalNoise2(wx, wy, HillFreq, 3, seed + 55555);
                    double detail = FractalNoise2(wx, wy, DetailFreq, 2, seed + 77777);

                    // Weighted blend, map from [-1,1] to [0,1]
                    double h = continent * ContinentWeight
                             + hills * HillWeight
                             + detail * DetailWeight;
                    double h01 = Math.Clamp(h * 0.5 + 0.5, 0.0, 1.0);

                    // Boost height in mountain ridge areas for consistent elevated terrain
                    double ridge = RidgeNoise2(wx, wy, MountainRidgeFreq, 3, seed + 11111);
                    double mask0 = ContinentMask(wx, wy, seed);
                    double ridgeInterior = Math.Clamp((mask0 - 0.15) / 0.35, 0.0, 1.0);
                    h01 = Math.Clamp(h01 + ridge * ridgeInterior * 0.35, 0.0, 1.0);

                    cornerMap[cy * cw + cx] = h01;
                }
            }

            // Step 2: Hydraulic erosion
            SimulateErosion(cornerMap, cw, ch, seed + mapX * 7919 + mapY * 6271);

            // Step 3: Gaussian smoothing to round off sharp edges
            for (int pass = 0; pass < SmoothPasses; pass++)
                GaussianBlur3x3(cornerMap, cw, ch);

            // Step 3b: Continent mask — shape terrain into a single landmass surrounded by ocean.
            // Uses a hard ocean cut at mask < 0.1 (matching preview) and gentle coastal scaling.
            for (int cy = 0; cy < ch; cy++)
            {
                for (int cx = 0; cx < cw; cx++)
                {
                    int wx = baseWorldX + cx;
                    int wy = baseWorldY + cy;
                    double mask = ContinentMask(wx, wy, seed);
                    if (mask < 0.1)
                        cornerMap[cy * cw + cx] = 0.0;  // ocean floor
                    else
                    {
                        // Remap mask [0.1, 1.0] → [0.3, 1.0] so coastal land stays elevated
                        double coastal = 0.3 + 0.7 * ((mask - 0.1) / 0.9);
                        cornerMap[cy * cw + cx] *= coastal;
                    }
                }
            }

            // Step 4: Terrace and pack into per-tile corner sbytes
            // Ocean threshold: corners below this get negative heights
            const double OceanThreshold = 0.08;
            var heights = new sbyte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double hNW = cornerMap[y * cw + x];
                    double hNE = cornerMap[y * cw + (x + 1)];
                    double hSE = cornerMap[(y + 1) * cw + (x + 1)];
                    double hSW = cornerMap[(y + 1) * cw + x];

                    var idx = (y * width + x) * 4;
                    heights[idx + 0] = TerraceCorner(hNW, maxHeight, TerraceSteps, OceanThreshold);
                    heights[idx + 1] = TerraceCorner(hNE, maxHeight, TerraceSteps, OceanThreshold);
                    heights[idx + 2] = TerraceCorner(hSE, maxHeight, TerraceSteps, OceanThreshold);
                    heights[idx + 3] = TerraceCorner(hSW, maxHeight, TerraceSteps, OceanThreshold);
                }
            }

            return heights;
        }

        // 3x3 Gaussian blur (approximate: [1,2,1] kernel, normalized)
        private static void GaussianBlur3x3(double[] map, int w, int h)
        {
            var tmp = new double[map.Length];
            Array.Copy(map, tmp, map.Length);

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    // 3x3 Gaussian kernel weights: corners=1, edges=2, center=4, total=16
                    double sum =
                        tmp[(y - 1) * w + (x - 1)] * 1 + tmp[(y - 1) * w + x] * 2 + tmp[(y - 1) * w + (x + 1)] * 1 +
                        tmp[y * w + (x - 1)] * 2       + tmp[y * w + x] * 4       + tmp[y * w + (x + 1)] * 2 +
                        tmp[(y + 1) * w + (x - 1)] * 1 + tmp[(y + 1) * w + x] * 2 + tmp[(y + 1) * w + (x + 1)] * 1;
                    map[y * w + x] = sum / 16.0;
                }
            }
        }

        // Hydraulic erosion: simulate water droplets carving channels
        private static void SimulateErosion(double[] map, int w, int h, int seed)
        {
            var rng = new Random(seed);

            for (int d = 0; d < ErosionDroplets; d++)
            {
                double px = rng.NextDouble() * (w - 3) + 1;
                double py = rng.NextDouble() * (h - 3) + 1;
                double dirX = 0, dirY = 0;
                double speed = 0;
                double water = 1.0;
                double sediment = 0;

                for (int step = 0; step < ErosionMaxSteps; step++)
                {
                    int xi = (int)px;
                    int yi = (int)py;
                    if (xi < 1 || xi >= w - 2 || yi < 1 || yi >= h - 2) break;

                    double xf = px - xi;
                    double yf = py - yi;

                    double h00 = map[yi * w + xi];
                    double h10 = map[yi * w + xi + 1];
                    double h01 = map[(yi + 1) * w + xi];
                    double h11 = map[(yi + 1) * w + xi + 1];

                    double gx = (h10 - h00) * (1 - yf) + (h11 - h01) * yf;
                    double gy = (h01 - h00) * (1 - xf) + (h11 - h10) * xf;

                    dirX = dirX * ErosionInertia - gx * (1 - ErosionInertia);
                    dirY = dirY * ErosionInertia - gy * (1 - ErosionInertia);

                    double len = Math.Sqrt(dirX * dirX + dirY * dirY);
                    if (len < 1e-10)
                    {
                        double angle = rng.NextDouble() * Math.PI * 2;
                        dirX = Math.Cos(angle);
                        dirY = Math.Sin(angle);
                        len = 1;
                    }
                    dirX /= len;
                    dirY /= len;

                    double newPx = px + dirX;
                    double newPy = py + dirY;

                    int nxi = (int)newPx;
                    int nyi = (int)newPy;
                    if (nxi < 0 || nxi >= w - 1 || nyi < 0 || nyi >= h - 1) break;

                    double nxf = newPx - nxi;
                    double nyf = newPy - nyi;
                    double newH = map[nyi * w + nxi] * (1 - nxf) * (1 - nyf)
                                + map[nyi * w + nxi + 1] * nxf * (1 - nyf)
                                + map[(nyi + 1) * w + nxi] * (1 - nxf) * nyf
                                + map[(nyi + 1) * w + nxi + 1] * nxf * nyf;

                    double oldH = h00 * (1 - xf) * (1 - yf) + h10 * xf * (1 - yf)
                                + h01 * (1 - xf) * yf + h11 * xf * yf;
                    double heightDiff = newH - oldH;

                    double slope = Math.Max(-heightDiff, 0.005);
                    double cap = slope * speed * water * ErosionCapacity;

                    if (sediment > cap || heightDiff > 0)
                    {
                        double deposit = (heightDiff > 0)
                            ? Math.Min(sediment, heightDiff)
                            : (sediment - cap) * ErosionDeposition;
                        sediment -= deposit;
                        map[yi * w + xi] += deposit * (1 - xf) * (1 - yf);
                        map[yi * w + xi + 1] += deposit * xf * (1 - yf);
                        map[(yi + 1) * w + xi] += deposit * (1 - xf) * yf;
                        map[(yi + 1) * w + xi + 1] += deposit * xf * yf;
                    }
                    else
                    {
                        double erode = Math.Min((cap - sediment) * ErosionStrength, -heightDiff);
                        sediment += erode;
                        map[yi * w + xi] -= erode * (1 - xf) * (1 - yf);
                        map[yi * w + xi + 1] -= erode * xf * (1 - yf);
                        map[(yi + 1) * w + xi] -= erode * (1 - xf) * yf;
                        map[(yi + 1) * w + xi + 1] -= erode * xf * yf;
                    }

                    speed = Math.Sqrt(Math.Max(0, speed * speed - heightDiff));
                    water *= (1 - ErosionEvapRate);
                    px = newPx;
                    py = newPy;
                    if (water < 0.01) break;
                }
            }

            for (int i = 0; i < map.Length; i++)
                map[i] = Math.Clamp(map[i], 0.0, 1.0);
        }

        // Continent mask: shapes the world into a single landmass surrounded by ocean.
        // Returns 1.0 at continent interior and 0.0 in deep ocean.
        // Uses a radial gradient from world center with large-scale noise perturbation
        // to create an irregular, natural-looking coastline.
        public static double ContinentMask(int worldX, int worldY, int seed)
        {
            double totalW = WorldMapsX * DefaultChunkSize;
            double totalH = WorldMapsY * DefaultChunkSize;

            // Normalize position to [-0.5, 0.5] from world center
            double nx = worldX / totalW - 0.5;
            double ny = worldY / totalH - 0.5;

            // Distance from center [0, ~0.707]
            double dist = Math.Sqrt(nx * nx + ny * ny);

            // Large-scale noise warps the coastline for irregular, natural shapes
            // λ=8000 tiles (~62 chunks) for broad bays/peninsulas
            // λ=2000 tiles (~15 chunks) for medium coastal features
            double warp = FractalNoise2(worldX, worldY, 1.0 / 8000.0, 3, seed + 88888) * 0.15
                        + FractalNoise2(worldX, worldY, 1.0 / 2000.0, 2, seed + 99999) * 0.05;
            double warpedDist = dist + warp;

            // Smoothstep falloff: full land within landRadius, full ocean beyond oceanRadius
            const double landRadius = 0.38;
            const double oceanRadius = 0.44;
            double t = (warpedDist - landRadius) / (oceanRadius - landRadius);
            t = Math.Clamp(t, 0.0, 1.0);

            return 1.0 - t * t * (3.0 - 2.0 * t); // 1 = land, 0 = ocean
        }

        // Multi-octave fractal noise at a specified base frequency
        public static double FractalNoise2(int x, int y, double baseFreq, int octaves, int seed)
        {
            double total = 0.0, amp = 1.0, freq = 1.0, max = 0.0;
            for (int i = 0; i < octaves; i++)
            {
                total += ValueNoise(x * baseFreq * freq, y * baseFreq * freq, seed + i * 1000) * amp;
                max += amp;
                amp *= 0.5;
                freq *= 2.0;
            }
            return total / max;
        }

        // Multi-octave ridge noise: creates sharp mountain-chain features.
        // Uses 1 - |noise| which peaks where noise crosses zero, forming ridges.
        public static double RidgeNoise2(int x, int y, double baseFreq, int octaves, int seed)
        {
            double total = 0.0, amp = 1.0, freq = 1.0, max = 0.0;
            for (int i = 0; i < octaves; i++)
            {
                double n = ValueNoise(x * baseFreq * freq, y * baseFreq * freq, seed + i * 1000);
                total += (1.0 - Math.Abs(n * 2.0)) * amp;
                max += amp;
                amp *= 0.45;  // faster dropoff for sharper ridges
                freq *= 2.2;  // non-power-of-2 avoids axis alignment
            }
            return total / max;
        }

        // Terrace a corner height: above ocean threshold → positive (0..maxHeight),
        // at or below ocean threshold → 0 (sea level). Ocean floor depth is not stored in corner
        // heights — it's encoded in the Liquid depth field.
        private static sbyte TerraceCorner(double n01, int maxHeight, int steps, double oceanThreshold)
        {
            if (n01 <= oceanThreshold) return 0; // at or below sea level
            // Map the above-ocean range to [0,1] for terracing
            double normalized = (n01 - oceanThreshold) / (1.0 - oceanThreshold);
            int h = Terrace(normalized, maxHeight, steps);
            // Land must be at least height 1 so it sits above sea level
            return (sbyte)Math.Max(1, h);
        }

        // Terrace: map continuous [0,1] to discrete plateau height levels.
        private static int Terrace(double n01, int maxHeight, int steps)
        {
            n01 = Math.Clamp(n01, 0.0, 1.0);
            int step = (int)Math.Floor(n01 * steps);
            if (step >= steps) step = steps - 1;
            int h = (int)Math.Round((double)step / (steps - 1) * maxHeight);
            return Math.Clamp(h, 0, maxHeight);
        }

        // Original fractal noise for tile type sampling (continent-scale biomes)
        private static double FractalNoise(int x, int y, int seed)
        {
            double total = 0.0, amp = 1.0, freq = 1.0, max = 0.0;
            for (int i = 0; i < 4; i++)
            {
                total += ValueNoise(x * BaseFrequency * freq, y * BaseFrequency * freq, seed + i * 1000) * amp;
                max += amp;
                amp *= 0.5;
                freq *= 2.0;
            }
            return total / max;
        }

        // Value noise via hashing corners and bilinear interpolation. Input coords are doubles.
        private static double ValueNoise(double x, double y, int seed)
        {
            int xi = (int)Math.Floor(x);
            int yi = (int)Math.Floor(y);
            double xf = x - xi;
            double yf = y - yi;

            double v00 = Hash01(xi, yi, seed) * 2.0 - 1.0;
            double v10 = Hash01(xi + 1, yi, seed) * 2.0 - 1.0;
            double v01 = Hash01(xi, yi + 1, seed) * 2.0 - 1.0;
            double v11 = Hash01(xi + 1, yi + 1, seed) * 2.0 - 1.0;

            double u = Fade(xf);
            double v = Fade(yf);

            double ix0 = Lerp(v00, v10, u);
            double ix1 = Lerp(v01, v11, u);
            double f = Lerp(ix0, ix1, v);
            return f;
        }

        // Smoothstep
        private static double Fade(double t) => t * t * (3 - 2 * t);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        // Deterministic hash to [0,1) based on ints
        private static double Hash01(int x, int y, int seed)
        {
            // Use 64-bit mix - ensure all multiplications are done on unsigned 64-bit to avoid ambiguity
            ulong ux = (ulong)(uint)x;
            ulong uy = (ulong)(uint)y;
            ulong useed = (ulong)(uint)seed;
            ulong mix = (ux * 0x9E3779B185EBCA87UL) ^ (uy << 32) ^ (useed * 0x9E3779B97F4A7C15UL);
            ulong z = XorShift64(mix);
            // Convert to [0,1)
            return (z & 0xFFFFFFFFFFFFUL) / (double)(1UL << 48);
        }

        // Simple xorshift-like mix for deterministic hashing
        private static ulong XorShift64(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z = z ^ (z >> 31);
            return z;
        }

        // =========================================================
        //  WORLD-LEVEL RIVER GENERATION
        //  Computes a drainage network at coarse (preview) resolution.
        //  Returns a byte[] where value > 0 indicates a river cell
        //  (value = capped flow accumulation for width/intensity).
        // =========================================================

        /// <summary>
        /// Generate a world-level river network at the given grid resolution.
        /// Uses priority-flood → steepest-descent flow → flow accumulation.
        /// </summary>
        public static byte[] GenerateWorldRivers(int gridW, int gridH, int seed)
        {
            int worldW = WorldMapsX * DefaultChunkSize;
            int worldH = WorldMapsY * DefaultChunkSize;

            // Step 1: Build coarse elevation grid
            var elevation = new double[gridH * gridW];
            for (int gy = 0; gy < gridH; gy++)
            {
                for (int gx = 0; gx < gridW; gx++)
                {
                    int wx = (int)(((gx + 0.5) / gridW) * worldW);
                    int wy = (int)(((gy + 0.5) / gridH) * worldH);
                    double mask = ContinentMask(wx, wy, seed);
                    if (mask < 0.1) { elevation[gy * gridW + gx] = 0; continue; }
                    double ridge = RidgeNoise2(wx, wy, MountainRidgeFreq, 3, seed + 11111);
                    double interiorR = Math.Clamp((mask - 0.15) / 0.35, 0.0, 1.0);
                    double broad = FractalNoise2(wx, wy, BroadElevFreq, 2, seed + 22222) * 0.5 + 0.5;
                    double coastal = 0.3 + 0.7 * ((mask - 0.1) / 0.9);
                    elevation[gy * gridW + gx] = (broad * 0.5 + ridge * interiorR * 0.5) * coastal;
                }
            }

            // Step 2: Priority-flood to resolve depressions
            var filled = new double[gridH * gridW];
            Array.Copy(elevation, filled, elevation.Length);
            var pq = new PriorityQueue<int, double>();
            var visited = new bool[gridH * gridW];

            for (int gy = 0; gy < gridH; gy++)
            {
                for (int gx = 0; gx < gridW; gx++)
                {
                    int idx = gy * gridW + gx;
                    if (elevation[idx] <= 0 || gx == 0 || gy == 0 || gx == gridW - 1 || gy == gridH - 1)
                    {
                        pq.Enqueue(idx, filled[idx]);
                        visited[idx] = true;
                    }
                }
            }

            while (pq.Count > 0)
            {
                int idx = pq.Dequeue();
                int gy = idx / gridW;
                int gx = idx % gridW;
                double h = filled[idx];

                for (int dy2 = -1; dy2 <= 1; dy2++)
                {
                    for (int dx2 = -1; dx2 <= 1; dx2++)
                    {
                        if (dx2 == 0 && dy2 == 0) continue;
                        int nx = gx + dx2, ny = gy + dy2;
                        if (nx < 0 || ny < 0 || nx >= gridW || ny >= gridH) continue;
                        int ni = ny * gridW + nx;
                        if (visited[ni]) continue;
                        visited[ni] = true;
                        filled[ni] = Math.Max(elevation[ni], h + 0.0001);
                        pq.Enqueue(ni, filled[ni]);
                    }
                }
            }

            // Step 3: Flow directions on filled surface (steepest descent, 8-connected)
            var flowDir = new int[gridH * gridW];
            for (int i = 0; i < flowDir.Length; i++) flowDir[i] = -1;

            for (int gy = 0; gy < gridH; gy++)
            {
                for (int gx = 0; gx < gridW; gx++)
                {
                    int idx = gy * gridW + gx;
                    if (elevation[idx] <= 0) continue;
                    double bestDrop = 0;
                    int bestTarget = -1;
                    for (int dy2 = -1; dy2 <= 1; dy2++)
                    {
                        for (int dx2 = -1; dx2 <= 1; dx2++)
                        {
                            if (dx2 == 0 && dy2 == 0) continue;
                            int nx = gx + dx2, ny = gy + dy2;
                            if (nx < 0 || ny < 0 || nx >= gridW || ny >= gridH)
                            {
                                if (filled[idx] > bestDrop) { bestDrop = filled[idx]; bestTarget = -2; }
                                continue;
                            }
                            double d = filled[idx] - filled[ny * gridW + nx];
                            if (d > bestDrop) { bestDrop = d; bestTarget = ny * gridW + nx; }
                        }
                    }
                    flowDir[idx] = bestTarget;
                }
            }

            // Step 4: Flow accumulation via topological sort (highest first)
            var indices = new int[gridH * gridW];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            Array.Sort(indices, (a, b) => filled[b].CompareTo(filled[a]));

            var accumulation = new double[gridH * gridW];
            for (int i = 0; i < accumulation.Length; i++) accumulation[i] = 1.0;

            foreach (int idx in indices)
            {
                if (elevation[idx] <= 0) continue;
                int target = flowDir[idx];
                if (target >= 0) accumulation[target] += accumulation[idx];
            }

            // Step 5: Mark river cells (accumulation above threshold, on land)
            const int riverThresh = 15;
            var rivers = new byte[gridH * gridW];
            for (int i = 0; i < rivers.Length; i++)
            {
                if (elevation[i] > 0 && accumulation[i] >= riverThresh)
                    rivers[i] = (byte)Math.Min(255, (int)accumulation[i]);
            }
            return rivers;
        }

        /// <summary>
        /// Check if a chunk at (mapX, mapY) should have rivers.
        /// Uses the same coarse elevation + flow logic as GenerateWorldRivers
        /// but only checks this one chunk and its immediate surroundings.
        /// </summary>
        public static bool HasWorldRiver(int mapX, int mapY, int seed)
        {
            // Quick check: ocean chunks don't have rivers
            int cx = mapX * DefaultChunkSize + DefaultChunkSize / 2;
            int cy = mapY * DefaultChunkSize + DefaultChunkSize / 2;
            double mask = ContinentMask(cx, cy, seed);
            if (mask < 0.1) return false;

            // Sample a small region around this chunk at coarse resolution
            // and run a mini flow-accumulation to see if enough drainage passes through
            const int radius = 12; // check 12 chunks in each direction
            int gx0 = Math.Max(0, mapX - radius);
            int gy0 = Math.Max(0, mapY - radius);
            int gx1 = Math.Min(WorldMapsX - 1, mapX + radius);
            int gy1 = Math.Min(WorldMapsY - 1, mapY + radius);
            int gw = gx1 - gx0 + 1;
            int gh = gy1 - gy0 + 1;

            var elev = new double[gh * gw];
            for (int ry = 0; ry < gh; ry++)
            {
                for (int rx = 0; rx < gw; rx++)
                {
                    int mx = gx0 + rx;
                    int my = gy0 + ry;
                    int wx = mx * DefaultChunkSize + DefaultChunkSize / 2;
                    int wy = my * DefaultChunkSize + DefaultChunkSize / 2;
                    double m = ContinentMask(wx, wy, seed);
                    if (m < 0.1) { elev[ry * gw + rx] = 0; continue; }
                    double ridge = RidgeNoise2(wx, wy, MountainRidgeFreq, 3, seed + 11111);
                    double interiorR = Math.Clamp((m - 0.15) / 0.35, 0.0, 1.0);
                    double broad = FractalNoise2(wx, wy, BroadElevFreq, 2, seed + 22222) * 0.5 + 0.5;
                    double c = 0.3 + 0.7 * ((m - 0.1) / 0.9);
                    elev[ry * gw + rx] = (broad * 0.5 + ridge * interiorR * 0.5) * c;
                }
            }

            // Flow directions (steepest descent)
            var flowDir = new int[gh * gw];
            for (int i = 0; i < flowDir.Length; i++) flowDir[i] = -1;
            for (int ry = 0; ry < gh; ry++)
            {
                for (int rx = 0; rx < gw; rx++)
                {
                    int idx = ry * gw + rx;
                    if (elev[idx] <= 0) continue;
                    double bestDrop = 0;
                    int bestTarget = -1;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx2 = rx + dx, ny2 = ry + dy;
                            if (nx2 < 0 || ny2 < 0 || nx2 >= gw || ny2 >= gh) continue;
                            double d = elev[idx] - elev[ny2 * gw + nx2];
                            if (d > bestDrop) { bestDrop = d; bestTarget = ny2 * gw + nx2; }
                        }
                    }
                    flowDir[idx] = bestTarget;
                }
            }

            // Flow accumulation (topo sort, highest first)
            var indices = new int[gh * gw];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            Array.Sort(indices, (a, b) => elev[b].CompareTo(elev[a]));

            var accum = new double[gh * gw];
            for (int i = 0; i < accum.Length; i++) accum[i] = 1.0;
            foreach (int idx in indices)
            {
                if (elev[idx] <= 0) continue;
                int target = flowDir[idx];
                if (target >= 0) accum[target] += accum[idx];
            }

            // Check if the target chunk has enough flow
            int localX = mapX - gx0;
            int localY = mapY - gy0;
            int targetIdx = localY * gw + localX;
            return accum[targetIdx] >= 15;
        }

        /// <summary>
        /// Apply world-level river influence to a chunk's height grids.
        /// Creates a gentle depression along the world river's flow direction
        /// so the chunk's water simulation produces correctly-oriented rivers.
        /// </summary>
        private static void ApplyWorldRiverBias(double[] heightGrid, double[] rawHeightGrid,
            int width, int height, int mapX, int mapY, int seed)
        {
            // Reuse the coarse elevation helper from HasWorldRiver
            double CoarseElev(int mx, int my)
            {
                if (mx < 0 || my < 0 || mx >= WorldMapsX || my >= WorldMapsY) return 0;
                int wx = mx * DefaultChunkSize + DefaultChunkSize / 2;
                int wy = my * DefaultChunkSize + DefaultChunkSize / 2;
                double mask = ContinentMask(wx, wy, seed);
                if (mask < 0.1) return 0;
                double ridge = RidgeNoise2(wx, wy, MountainRidgeFreq, 3, seed + 11111);
                double interiorR = Math.Clamp((mask - 0.15) / 0.35, 0.0, 1.0);
                double broad = FractalNoise2(wx, wy, BroadElevFreq, 2, seed + 22222) * 0.5 + 0.5;
                double c = 0.3 + 0.7 * ((mask - 0.1) / 0.9);
                return (broad * 0.5 + ridge * interiorR * 0.5) * c;
            }

            double centerElev = CoarseElev(mapX, mapY);
            if (centerElev <= 0.05) return;

            // Find steepest descent (flow direction at world scale)
            int bestDx = 0, bestDy = 0;
            double bestDrop = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    double drop = centerElev - CoarseElev(mapX + dx, mapY + dy);
                    if (drop > bestDrop) { bestDrop = drop; bestDx = dx; bestDy = dy; }
                }
            }
            if (bestDrop < 0.01) return;

            // Count upstream cells (rough upstream drainage area)
            int upstreamCount = 0;
            for (int r = 1; r <= 4; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        if (CoarseElev(mapX + dx, mapY + dy) > centerElev) upstreamCount++;
                    }
            if (upstreamCount < 4) return;

            // Carve gentle depression along flow direction
            double strength = Math.Min(0.03, upstreamCount * 0.0015);
            double len = Math.Sqrt(bestDx * bestDx + bestDy * bestDy);
            double rdx = bestDx / len;
            double rdy = bestDy / len;
            int baseWorldX = mapX * width;
            int baseWorldY = mapY * height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double nx = (x + 0.5) / width - 0.5;
                    double ny = (y + 0.5) / height - 0.5;
                    double perp = nx * (-rdy) + ny * rdx;
                    // Meander using noise
                    int wx = baseWorldX + x;
                    int wy = baseWorldY + y;
                    double meander = FractalNoise2(wx, wy, 1.0 / 60.0, 2, seed + 90000) * 0.04;
                    perp += meander;
                    double riverW = 0.04;
                    double depression = strength * Math.Exp(-(perp * perp) / (2 * riverW * riverW));
                    int idx = y * width + x;
                    rawHeightGrid[idx] = Math.Max(0, rawHeightGrid[idx] - depression);
                    heightGrid[idx] = Math.Max(0, heightGrid[idx] - depression);
                }
            }
        }
    }
}
