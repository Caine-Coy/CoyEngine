using System;
using System.Collections.Generic;

namespace CoyEngine
{
    /// <summary>
    /// Hydrological water placement inspired by RollerCoaster Tycoon 2 and SimCity 4.
    ///
    /// Design principles:
    ///   - Water exists at discrete height levels matching terrain terracing (RCT2 style)
    ///   - Lakes fill basins to their spill elevation — perfectly flat surfaces
    ///   - Rivers trace steepest-descent paths from highland springs to ocean/lakes
    ///   - Ocean flood-fills from world edges at sea level (height 0)
    ///   - All water is deterministic and baked at generation time (no runtime simulation)
    ///
    /// Algorithm (run during world generation):
    ///   1. Build a flow-direction grid: each cell points to its steepest downhill neighbor
    ///   2. Resolve flats/depressions using priority-flood (breach-fill hybrid)
    ///   3. Accumulate flow: count how many upstream cells drain through each cell
    ///   4. Rivers form where flow accumulation exceeds a threshold
    ///   5. Lakes form at depressions that cannot drain — filled to their pour point
    ///   6. Ocean flood-fills from edges at height 0
    /// </summary>
    public static class WaterSimulation
    {
        // --- Configuration ---
        // Flow accumulation threshold for a cell to become a river
        private const int RiverThreshold = 80;
        // Minimum river flow accumulation for "wide" rivers (depth 4 vs 3)
        private const int WideRiverThreshold = 200;
        // Maximum lake size (tiles) before we stop expanding
        private const int MaxLakeSize = 60;
        // Minimum depression depth (terrain units) for a lake to form
        private const double MinLakeDepth = 1.5;

        // 8-connected neighbors: N, NE, E, SE, S, SW, W, NW
        private static readonly (int dx, int dy)[] Neighbors8 =
        {
            (0, -1), (1, -1), (1, 0), (1, 1),
            (0, 1), (-1, 1), (-1, 0), (-1, -1)
        };

        // 4-connected neighbors: N, E, S, W
        private static readonly (int dx, int dy)[] Neighbors4 =
        {
            (0, -1), (1, 0), (0, 1), (-1, 0)
        };

        /// <summary>
        /// Run the complete hydrological water placement pipeline.
        /// Modifies tiles[], liquids[], and cornerHeights[] in place.
        ///
        /// Uses the "terraced" heightGrid (average of 4 corners per tile) for
        /// flow routing, and the continuous rawHeightGrid for fine-grained slope
        /// decisions. Corner heights are carved for rivers and lakes.
        /// </summary>
        public static void PlaceWater(
            TileType[] tiles,
            Liquid[] liquids,
            double[] heightGrid,
            double[] rawHeightGrid,
            sbyte[] cornerHeights,
            int width, int height,
            int baseWorldX, int baseWorldY,
            int seed,
            bool hasWorldRiver = true)
        {
            // ============================================================
            // Step 1: Ocean flood-fill from world edges
            // ============================================================
            var oceanTiles = new HashSet<int>();
            PlaceOcean(tiles, liquids, cornerHeights, rawHeightGrid, width, height,
                       baseWorldX, baseWorldY, oceanTiles);

            // Steps 2-8 only run if this chunk has a world-level river tag.
            // Chunks without rivers only get ocean placement.
            if (!hasWorldRiver) return;

            // ============================================================
            // Step 2: Priority-flood to resolve depressions
            //         Produces a "filled" height grid where all cells drain
            //         to the edge or to ocean tiles (no local minima except ocean).
            // ============================================================
            var filledHeight = PriorityFlood(heightGrid, width, height, oceanTiles);

            // ============================================================
            // Step 3: Compute flow directions on the filled surface
            //         Each cell points to its steepest downhill neighbor.
            // ============================================================
            var flowDir = ComputeFlowDirections(filledHeight, width, height);

            // ============================================================
            // Step 4: Accumulate upstream drainage area per cell
            // ============================================================
            var flowAccum = AccumulateFlow(flowDir, width, height);

            // ============================================================
            // Step 5: Identify lake depressions
            //         Where filledHeight > heightGrid, the depression is flooded.
            //         Group connected depression cells into lake bodies.
            // ============================================================
            var lakeTiles = new HashSet<int>();
            var wetlandTiles = new HashSet<int>();
            IdentifyLakes(heightGrid, filledHeight, width, height, oceanTiles,
                          lakeTiles, wetlandTiles, seed);

            // ============================================================
            // Step 6: Identify river cells from flow accumulation
            // ============================================================
            var riverTiles = new HashSet<int>();
            IdentifyRivers(flowAccum, heightGrid, width, height, oceanTiles,
                           lakeTiles, riverTiles);

            // ============================================================
            // Step 7: Carve terrain for rivers and lakes
            // ============================================================
            CarveRivers(riverTiles, cornerHeights, width, height, oceanTiles, lakeTiles);
            CarveLakes(lakeTiles, wetlandTiles, filledHeight, heightGrid,
                       cornerHeights, width, height);

            // ============================================================
            // Step 8: Apply liquid overlays and terrain substrates
            // ============================================================
            ApplyLiquids(tiles, liquids, riverTiles, lakeTiles, wetlandTiles,
                         oceanTiles, flowDir, flowAccum, rawHeightGrid, width, height);
        }

        // ================================================================
        //  OCEAN PLACEMENT — flood-fill from world edges
        // ================================================================

        /// <summary>
        /// Public entry point: place ocean only (no rivers/lakes).
        /// Used by plan-based generation where rivers come from the WorldPlan.
        /// </summary>
        public static void PlaceOceanOnly(
            TileType[] tiles, Liquid[] liquids, sbyte[] cornerHeights,
            double[] rawHeightGrid, int width, int height,
            int baseWorldX, int baseWorldY)
        {
            var oceanTiles = new HashSet<int>();
            PlaceOcean(tiles, liquids, cornerHeights, rawHeightGrid, width, height,
                       baseWorldX, baseWorldY, oceanTiles);
        }

        private static void PlaceOcean(
            TileType[] tiles, Liquid[] liquids, sbyte[] cornerHeights,
            double[] rawHeightGrid, int width, int height,
            int baseWorldX, int baseWorldY, HashSet<int> oceanTiles)
        {
            int totalW = WorldGenerator.WorldMapsX * width;
            int totalH = WorldGenerator.WorldMapsY * height;

            bool IsSeaLevel(int i)
            {
                int ci = i * 4;
                return cornerHeights[ci] == 0 && cornerHeights[ci + 1] == 0
                    && cornerHeights[ci + 2] == 0 && cornerHeights[ci + 3] == 0;
            }

            var queue = new Queue<int>();
            var visited = new bool[width * height];

            void MarkOcean(int idx)
            {
                tiles[idx] = rawHeightGrid[idx] < 0.03 ? TileType.Clay : TileType.Sand;
                byte depth = (byte)Math.Clamp((int)((0.08 - rawHeightGrid[idx]) * 50), 1, 255);
                liquids[idx] = new Liquid(LiquidType.SaltWater, depth, FlowDirection.Still, 0);
                oceanTiles.Add(idx);
                visited[idx] = true;
                queue.Enqueue(idx);
            }

            // Seed from world edges
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int wx = baseWorldX + x;
                    int wy = baseWorldY + y;
                    bool onWorldEdge = (wx == 0 || wy == 0 || wx >= totalW - 1 || wy >= totalH - 1);
                    if (!onWorldEdge) continue;
                    int idx = y * width + x;
                    if (IsSeaLevel(idx))
                        MarkOcean(idx);
                }
            }

            // Seed from chunk edges where neighbor chunk would have ocean
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool onChunkEdge = (x == 0 || y == 0 || x == width - 1 || y == height - 1);
                    if (!onChunkEdge) continue;
                    int idx = y * width + x;
                    if (visited[idx] || !IsSeaLevel(idx)) continue;

                    int wx = baseWorldX + x;
                    int wy = baseWorldY + y;
                    bool neighborOcean = false;

                    if (x == 0 && baseWorldX > 0)
                        neighborOcean = SampleContinentHeight(wx - 1, wy, width, height) < 0.08;
                    if (x == width - 1 && wx + 1 < totalW)
                        neighborOcean = neighborOcean || SampleContinentHeight(wx + 1, wy, width, height) < 0.08;
                    if (y == 0 && baseWorldY > 0)
                        neighborOcean = neighborOcean || SampleContinentHeight(wx, wy - 1, width, height) < 0.08;
                    if (y == height - 1 && wy + 1 < totalH)
                        neighborOcean = neighborOcean || SampleContinentHeight(wx, wy + 1, width, height) < 0.08;

                    if (neighborOcean)
                        MarkOcean(idx);
                }
            }

            // BFS flood-fill to connected sea-level tiles
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % width;
                int cy = cur / width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + Neighbors4[d].dx;
                    int ny = cy + Neighbors4[d].dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int ni = ny * width + nx;
                    if (visited[ni]) continue;
                    if (IsSeaLevel(ni))
                        MarkOcean(ni);
                }
            }

            // Coastal expansion: slope tiles adjacent to ocean with at least one sea-level corner
            bool HasSeaLevelCorner(int i)
            {
                int ci = i * 4;
                return cornerHeights[ci] == 0 || cornerHeights[ci + 1] == 0
                    || cornerHeights[ci + 2] == 0 || cornerHeights[ci + 3] == 0;
            }

            var coastalTiles = new List<int>();
            for (int i = 0; i < width * height; i++)
            {
                if (oceanTiles.Contains(i)) continue;
                if (!HasSeaLevelCorner(i)) continue;

                int cx2 = i % width;
                int cy2 = i / width;
                bool adjacentToOcean = false;
                for (int d = 0; d < 4 && !adjacentToOcean; d++)
                {
                    int nx = cx2 + Neighbors4[d].dx;
                    int ny = cy2 + Neighbors4[d].dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    if (oceanTiles.Contains(ny * width + nx))
                        adjacentToOcean = true;
                }
                if (adjacentToOcean)
                    coastalTiles.Add(i);
            }

            foreach (int idx in coastalTiles)
            {
                tiles[idx] = TileType.Sand;
                liquids[idx] = new Liquid(LiquidType.SaltWater, 1, FlowDirection.Still, 0);
                oceanTiles.Add(idx);
            }
        }

        /// <summary>
        /// Quick continent-scale height sample for cross-chunk ocean checks.
        /// Uses the actual ContinentMask to determine if a neighboring tile is ocean.
        /// </summary>
        private static double SampleContinentHeight(int wx, int wy, int chunkW, int chunkH)
        {
            int seed = WorldGenerator.DefaultSeed;
            double mask = WorldGenerator.ContinentMask(wx, wy, seed);

            // If continent mask is near 0, it's ocean — return a low height
            if (mask < 0.1) return 0.0;

            // Otherwise sample actual elevation scaled by mask
            const double ContinentFreq = 1.0 / 80.0;
            const double ContinentWeight = 0.55;
            double continent = FractalNoise2(wx, wy, ContinentFreq, 3, seed + 9999);
            double h = Math.Clamp(continent * ContinentWeight * 0.5 + 0.5, 0.0, 1.0);
            return h * mask;
        }

        // ================================================================
        //  PRIORITY-FLOOD — resolve depressions (breach/fill hybrid)
        // ================================================================

        /// <summary>
        /// Priority-flood algorithm (Wang and Liu 2006). Fills all depressions in the
        /// height grid so every cell has a monotonically non-increasing path to the
        /// boundary (or ocean). Returns the filled height grid.
        ///
        /// This is the key algorithm that makes SimCity 4-style drainage work:
        /// every cell gets a path to the ocean/edge, and depressions become lakes.
        /// </summary>
        private static double[] PriorityFlood(double[] heightGrid, int width, int height,
                                              HashSet<int> oceanTiles)
        {
            int n = width * height;
            var filled = new double[n];
            Array.Copy(heightGrid, filled, n);

            var inQueue = new bool[n];
            // Min-heap: (height, index)
            var pq = new SortedSet<(double h, int idx)>(
                Comparer<(double h, int idx)>.Create((a, b) =>
                {
                    int c = a.h.CompareTo(b.h);
                    return c != 0 ? c : a.idx.CompareTo(b.idx);
                }));

            // Seed the priority queue with boundary cells and ocean cells
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    bool isBoundary = (x == 0 || y == 0 || x == width - 1 || y == height - 1);
                    if (isBoundary || oceanTiles.Contains(idx))
                    {
                        pq.Add((filled[idx], idx));
                        inQueue[idx] = true;
                    }
                }
            }

            // Process cells from lowest to highest
            while (pq.Count > 0)
            {
                var (curH, curIdx) = pq.Min;
                pq.Remove(pq.Min);

                int cx = curIdx % width;
                int cy = curIdx / width;

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Neighbors8[d].dx;
                    int ny = cy + Neighbors8[d].dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int ni = ny * width + nx;
                    if (inQueue[ni]) continue;

                    inQueue[ni] = true;

                    // If neighbor is lower than current, it is in a depression — raise it
                    if (filled[ni] < curH)
                    {
                        // Add tiny epsilon to ensure strict drainage (no perfect flats)
                        filled[ni] = curH + 1e-7;
                    }

                    pq.Add((filled[ni], ni));
                }
            }

            return filled;
        }

        // ================================================================
        //  FLOW DIRECTIONS — steepest descent on filled surface
        // ================================================================

        /// <summary>
        /// Compute flow direction for each cell: index of the neighbor it drains to,
        /// or -1 for boundary/ocean cells that drain "off-map".
        /// Uses 8-connected steepest descent on the filled height surface.
        /// </summary>
        private static int[] ComputeFlowDirections(double[] filledHeight, int width, int height)
        {
            int n = width * height;
            var flowDir = new int[n];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    double curH = filledHeight[idx];

                    int bestNeighbor = -1;
                    double bestDrop = 0;

                    for (int d = 0; d < 8; d++)
                    {
                        int nx = x + Neighbors8[d].dx;
                        int ny = y + Neighbors8[d].dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                        {
                            // Off-map = infinite sink, always drains here
                            bestNeighbor = -1;
                            bestDrop = double.MaxValue;
                            continue;
                        }

                        int ni = ny * width + nx;
                        double drop = curH - filledHeight[ni];
                        // Diagonal distance correction
                        if (Math.Abs(Neighbors8[d].dx) + Math.Abs(Neighbors8[d].dy) == 2)
                            drop /= 1.414;

                        if (drop > bestDrop)
                        {
                            bestDrop = drop;
                            bestNeighbor = ni;
                        }
                    }

                    flowDir[idx] = bestNeighbor;
                }
            }

            return flowDir;
        }

        // ================================================================
        //  FLOW ACCUMULATION — count upstream drainage area
        // ================================================================

        /// <summary>
        /// Count how many upstream cells drain through each cell (including itself).
        /// Uses topological sort on the flow-direction graph.
        /// High values = rivers; low values = hilltops.
        /// </summary>
        private static int[] AccumulateFlow(int[] flowDir, int width, int height)
        {
            int n = width * height;
            var accum = new int[n];
            var inDegree = new int[n];

            // Count in-degree (how many cells flow INTO each cell)
            for (int i = 0; i < n; i++)
            {
                if (flowDir[i] >= 0 && flowDir[i] < n)
                    inDegree[flowDir[i]]++;
            }

            // Initialize accumulation to 1 (each cell counts itself)
            for (int i = 0; i < n; i++)
                accum[i] = 1;

            // Start with cells that have no upstream sources (hilltops)
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (inDegree[i] == 0)
                    queue.Enqueue(i);
            }

            // Propagate flow downstream
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int next = flowDir[cur];
                if (next < 0 || next >= n) continue;

                accum[next] += accum[cur];
                inDegree[next]--;
                if (inDegree[next] == 0)
                    queue.Enqueue(next);
            }

            return accum;
        }

        // ================================================================
        //  LAKE IDENTIFICATION — depression analysis
        // ================================================================

        /// <summary>
        /// Identify lakes: where filledHeight > heightGrid, terrain was depressed
        /// and the priority-flood filled it. These depressions become lakes.
        /// Lakes are grouped into connected components and sized-limited.
        /// Wetland fringe forms around lake edges.
        /// </summary>
        private static void IdentifyLakes(
            double[] heightGrid, double[] filledHeight,
            int width, int height, HashSet<int> oceanTiles,
            HashSet<int> lakeTiles, HashSet<int> wetlandTiles,
            int seed)
        {
            int n = width * height;

            // Find all depressed cells (where filling raised the surface)
            var depressed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (oceanTiles.Contains(i)) continue;
                double diff = filledHeight[i] - heightGrid[i];
                if (diff > MinLakeDepth)
                    depressed[i] = true;
            }

            // Group into connected components and apply size limits
            var visited = new bool[n];
            var componentQueue = new Queue<int>();
            var component = new List<int>();

            for (int i = 0; i < n; i++)
            {
                if (!depressed[i] || visited[i]) continue;

                component.Clear();
                componentQueue.Enqueue(i);
                visited[i] = true;

                while (componentQueue.Count > 0)
                {
                    int cur = componentQueue.Dequeue();
                    component.Add(cur);

                    int cx = cur % width;
                    int cy = cur / width;
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = cx + Neighbors8[d].dx;
                        int ny = cy + Neighbors8[d].dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        int ni = ny * width + nx;
                        if (visited[ni] || !depressed[ni]) continue;
                        visited[ni] = true;
                        componentQueue.Enqueue(ni);
                    }
                }

                // Only create lakes from reasonably-sized depressions
                if (component.Count > MaxLakeSize) continue;
                if (component.Count < 1) continue;

                foreach (int idx in component)
                    lakeTiles.Add(idx);

                // Create wetland fringe around lake
                var rng = new Random(seed ^ (component[0] * 31));
                foreach (int idx in component)
                {
                    int cx = idx % width;
                    int cy = idx / width;
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = cx + Neighbors8[d].dx;
                        int ny = cy + Neighbors8[d].dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        int ni = ny * width + nx;
                        if (lakeTiles.Contains(ni) || oceanTiles.Contains(ni)) continue;
                        if (wetlandTiles.Contains(ni)) continue;

                        // Wetland forms on gentle slopes near the lake
                        double diff = heightGrid[ni] - heightGrid[idx];
                        if (diff >= 0 && diff < 0.08 && rng.NextDouble() < 0.35)
                            wetlandTiles.Add(ni);
                    }
                }
            }
        }

        // ================================================================
        //  RIVER IDENTIFICATION — from flow accumulation
        // ================================================================

        /// <summary>
        /// Mark cells as river where flow accumulation exceeds threshold.
        /// Skip ocean and lake tiles (rivers terminate at those).
        /// </summary>
        private static void IdentifyRivers(
            int[] flowAccum, double[] heightGrid,
            int width, int height,
            HashSet<int> oceanTiles, HashSet<int> lakeTiles,
            HashSet<int> riverTiles)
        {
            for (int i = 0; i < width * height; i++)
            {
                if (oceanTiles.Contains(i)) continue;
                if (lakeTiles.Contains(i)) continue;
                if (flowAccum[i] >= RiverThreshold)
                {
                    // Skip very low elevations near ocean (would just be ocean expansion)
                    if (heightGrid[i] < 0.10) continue;
                    riverTiles.Add(i);
                }
            }
        }

        // ================================================================
        //  TERRAIN CARVING — rivers and lakes
        // ================================================================

        /// <summary>
        /// Carve river channels: lower the terrain beneath rivers so water sits
        /// in a visible depression. Rivers are 1 height unit below surrounding terrain.
        /// RCT2-style: the channel is carved into the terraced grid.
        /// </summary>
        private static void CarveRivers(
            HashSet<int> riverTiles, sbyte[] cornerHeights,
            int width, int height,
            HashSet<int> oceanTiles, HashSet<int> lakeTiles)
        {
            foreach (int idx in riverTiles)
            {
                int x = idx % width;
                int y = idx / width;

                // Find minimum corner height among non-river, non-lake neighbors
                int minNeighborCorner = int.MaxValue;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + Neighbors8[d].dx;
                    int ny = y + Neighbors8[d].dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int ni = ny * width + nx;
                    if (riverTiles.Contains(ni) || lakeTiles.Contains(ni) || oceanTiles.Contains(ni))
                        continue;

                    int nci = ni * 4;
                    int nMin = Math.Min(
                        Math.Min(cornerHeights[nci], cornerHeights[nci + 1]),
                        Math.Min(cornerHeights[nci + 2], cornerHeights[nci + 3]));
                    if (nMin < minNeighborCorner) minNeighborCorner = nMin;
                }

                if (minNeighborCorner == int.MaxValue)
                {
                    // All neighbors are water — use this tile's current min
                    int ci = idx * 4;
                    minNeighborCorner = Math.Min(
                        Math.Min(cornerHeights[ci], cornerHeights[ci + 1]),
                        Math.Min(cornerHeights[ci + 2], cornerHeights[ci + 3]));
                }

                // Set all 4 corners to 1 below the lowest neighbor, clamped to 0
                int target = Math.Max(0, minNeighborCorner - 1);
                int ci2 = idx * 4;
                cornerHeights[ci2]     = (sbyte)target;
                cornerHeights[ci2 + 1] = (sbyte)target;
                cornerHeights[ci2 + 2] = (sbyte)target;
                cornerHeights[ci2 + 3] = (sbyte)target;
            }
        }

        /// <summary>
        /// Carve lake basins: all tiles in a lake body are set to a uniform height
        /// (the spill level minus pond depth). Surrounding terrain is smoothed to
        /// slope gently into the lake (no abrupt cliffs). RCT2-style flat water.
        /// </summary>
        private static void CarveLakes(
            HashSet<int> lakeTiles, HashSet<int> wetlandTiles,
            double[] filledHeight, double[] heightGrid,
            sbyte[] cornerHeights, int width, int height)
        {
            const int PondDepth = 2;    // height units below surrounding terrain
            const int SmoothRadius = 3; // tiles around lake to smooth
            const int MaxTerrainHeight = 8;

            // Group into connected components
            var remaining = new HashSet<int>(lakeTiles);
            var queue = new Queue<int>();
            var component = new List<int>();

            while (remaining.Count > 0)
            {
                component.Clear();
                int start = -1;
                foreach (var v in remaining) { start = v; break; }
                if (start == -1) break;

                queue.Enqueue(start);
                remaining.Remove(start);
                component.Add(start);

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    int cx = cur % width;
                    int cy = cur / width;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = cx + dx;
                            int ny = cy + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            int ni = ny * width + nx;
                            if (remaining.Contains(ni))
                            {
                                remaining.Remove(ni);
                                queue.Enqueue(ni);
                                component.Add(ni);
                            }
                        }
                    }
                }

                // Find minimum corner height among non-lake neighbors
                int minNeighborCorner = int.MaxValue;
                foreach (int idx in component)
                {
                    int x = idx % width;
                    int y = idx / width;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            int ni = ny * width + nx;
                            if (lakeTiles.Contains(ni)) continue;
                            int nci = ni * 4;
                            int nMin = Math.Min(
                                Math.Min(cornerHeights[nci], cornerHeights[nci + 1]),
                                Math.Min(cornerHeights[nci + 2], cornerHeights[nci + 3]));
                            if (nMin < minNeighborCorner) minNeighborCorner = nMin;
                        }
                    }
                }

                if (minNeighborCorner == int.MaxValue)
                {
                    int curMin = int.MaxValue;
                    foreach (int idx in component)
                    {
                        int ci = idx * 4;
                        int cmin = Math.Min(
                            Math.Min(cornerHeights[ci], cornerHeights[ci + 1]),
                            Math.Min(cornerHeights[ci + 2], cornerHeights[ci + 3]));
                        if (cmin < curMin) curMin = cmin;
                    }
                    minNeighborCorner = curMin;
                }

                // Set lake bed to uniform height
                int target = Math.Max(0, minNeighborCorner - PondDepth);
                foreach (int idx in component)
                {
                    int ci = idx * 4;
                    cornerHeights[ci]     = (sbyte)target;
                    cornerHeights[ci + 1] = (sbyte)target;
                    cornerHeights[ci + 2] = (sbyte)target;
                    cornerHeights[ci + 3] = (sbyte)target;
                }

                // Smooth surrounding terrain into the lake
                var visitedRing = new HashSet<int>(component);
                var ringQueue = new Queue<(int idx, int dist)>();

                foreach (int idx in component)
                {
                    int cx = idx % width;
                    int cy = idx / width;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = cx + dx;
                            int ny = cy + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            int ni = ny * width + nx;
                            if (visitedRing.Contains(ni) || lakeTiles.Contains(ni)) continue;
                            visitedRing.Add(ni);
                            ringQueue.Enqueue((ni, 1));
                        }
                    }
                }

                while (ringQueue.Count > 0)
                {
                    var (ri, dist) = ringQueue.Dequeue();
                    if (dist > SmoothRadius) continue;
                    if (lakeTiles.Contains(ri)) continue;

                    int ci = ri * 4;
                    int curMin = Math.Min(
                        Math.Min(cornerHeights[ci], cornerHeights[ci + 1]),
                        Math.Min(cornerHeights[ci + 2], cornerHeights[ci + 3]));
                    int desiredMin = Math.Max(0, target + dist);
                    if (curMin > desiredMin)
                    {
                        int delta = curMin - desiredMin;
                        for (int k = 0; k < 4; k++)
                            cornerHeights[ci + k] = (sbyte)Math.Max(0, cornerHeights[ci + k] - delta);
                    }

                    if (dist < SmoothRadius)
                    {
                        int cx = ri % width;
                        int cy = ri / width;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = cx + dx;
                                int ny = cy + dy;
                                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                                int ni = ny * width + nx;
                                if (visitedRing.Contains(ni) || lakeTiles.Contains(ni)) continue;
                                visitedRing.Add(ni);
                                ringQueue.Enqueue((ni, dist + 1));
                            }
                        }
                    }
                }

                // Per-corner neighbor averaging to smooth slopes
                const int SlopeSmoothPasses = 3;
                const double SlopeBlend = 0.55;
                int totalTiles = width * height;
                var tempCorners = new sbyte[cornerHeights.Length];

                for (int pass = 0; pass < SlopeSmoothPasses; pass++)
                {
                    Array.Copy(cornerHeights, tempCorners, cornerHeights.Length);
                    for (int i = 0; i < totalTiles; i++)
                    {
                        if (lakeTiles.Contains(i)) continue;
                        int cx = i % width;
                        int cy = i / width;
                        int ci = i * 4;

                        for (int corner = 0; corner < 4; corner++)
                        {
                            int sum = 0, count = 0;
                            void AddNeighborCorner(int tx, int ty, int cornerIndex)
                            {
                                if (tx < 0 || tx >= width || ty < 0 || ty >= height) return;
                                int ti = ty * width + tx;
                                sum += tempCorners[ti * 4 + cornerIndex];
                                count++;
                            }

                            switch (corner)
                            {
                                case 0: // NW
                                    AddNeighborCorner(cx, cy, 0);
                                    AddNeighborCorner(cx - 1, cy, 1);
                                    AddNeighborCorner(cx, cy - 1, 3);
                                    AddNeighborCorner(cx - 1, cy - 1, 2);
                                    break;
                                case 1: // NE
                                    AddNeighborCorner(cx, cy, 1);
                                    AddNeighborCorner(cx + 1, cy, 0);
                                    AddNeighborCorner(cx + 1, cy - 1, 3);
                                    AddNeighborCorner(cx, cy - 1, 2);
                                    break;
                                case 2: // SE
                                    AddNeighborCorner(cx, cy, 2);
                                    AddNeighborCorner(cx + 1, cy, 3);
                                    AddNeighborCorner(cx + 1, cy + 1, 0);
                                    AddNeighborCorner(cx, cy + 1, 1);
                                    break;
                                case 3: // SW
                                    AddNeighborCorner(cx, cy, 3);
                                    AddNeighborCorner(cx - 1, cy, 2);
                                    AddNeighborCorner(cx - 1, cy + 1, 1);
                                    AddNeighborCorner(cx, cy + 1, 0);
                                    break;
                            }

                            if (count == 0) continue;
                            double avg = (double)sum / count;
                            double cur = cornerHeights[ci + corner];
                            double desired = cur * (1.0 - SlopeBlend) + avg * SlopeBlend;
                            if (desired > cur) desired = cur;
                            cornerHeights[ci + corner] = (sbyte)Math.Max(0, Math.Min((int)Math.Round(desired), MaxTerrainHeight));
                        }
                    }
                }
            }

            // Flatten wetland areas
            var remainingWet = new HashSet<int>(wetlandTiles);
            component.Clear();
            while (remainingWet.Count > 0)
            {
                component.Clear();
                int start = -1;
                foreach (var v in remainingWet) { start = v; break; }
                if (start == -1) break;

                queue.Enqueue(start);
                remainingWet.Remove(start);
                component.Add(start);

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    int cx = cur % width;
                    int cy = cur / width;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = cx + dx;
                            int ny = cy + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            int ni = ny * width + nx;
                            if (remainingWet.Contains(ni))
                            {
                                remainingWet.Remove(ni);
                                queue.Enqueue(ni);
                                component.Add(ni);
                            }
                        }
                    }
                }

                int minCorner = int.MaxValue;
                foreach (int idx in component)
                {
                    int x = idx % width;
                    int y = idx / width;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            int ni = ny * width + nx;
                            int nci = ni * 4;
                            int nMin = Math.Min(
                                Math.Min(cornerHeights[nci], cornerHeights[nci + 1]),
                                Math.Min(cornerHeights[nci + 2], cornerHeights[nci + 3]));
                            if (nMin < minCorner) minCorner = nMin;
                        }
                    }
                }

                if (minCorner == int.MaxValue) continue;
                int wetTarget = Math.Max(0, minCorner);
                foreach (int idx in component)
                {
                    int ci = idx * 4;
                    cornerHeights[ci]     = (sbyte)wetTarget;
                    cornerHeights[ci + 1] = (sbyte)wetTarget;
                    cornerHeights[ci + 2] = (sbyte)wetTarget;
                    cornerHeights[ci + 3] = (sbyte)wetTarget;
                }
            }

            // Cleanup: ensure no neighboring land tiles sit below lake level
            var raisedTiles = new HashSet<int>();
            foreach (int idx in lakeTiles)
            {
                int ci = idx * 4;
                int pondMin = Math.Min(
                    Math.Min(cornerHeights[ci], cornerHeights[ci + 1]),
                    Math.Min(cornerHeights[ci + 2], cornerHeights[ci + 3]));
                int desiredMin = Math.Min(MaxTerrainHeight, pondMin + 1);

                int cx = idx % width;
                int cy = idx / width;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx;
                        int ny = cy + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        int ni = ny * width + nx;
                        if (lakeTiles.Contains(ni)) continue;

                        int nci = ni * 4;
                        for (int k = 0; k < 4; k++)
                        {
                            if (cornerHeights[nci + k] < desiredMin)
                            {
                                cornerHeights[nci + k] = (sbyte)desiredMin;
                                raisedTiles.Add(ni);
                            }
                        }
                    }
                }
            }

            // Smooth raised tiles into surrounding terrain
            if (raisedTiles.Count > 0)
            {
                var smoothSet = new HashSet<int>(raisedTiles);
                const int CleanupRadius = 2;
                foreach (int r in raisedTiles)
                {
                    int rx = r % width;
                    int ry = r / width;
                    for (int dy = -CleanupRadius; dy <= CleanupRadius; dy++)
                    {
                        for (int dx = -CleanupRadius; dx <= CleanupRadius; dx++)
                        {
                            int nx = rx + dx;
                            int ny = ry + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            int ni = ny * width + nx;
                            if (!lakeTiles.Contains(ni)) smoothSet.Add(ni);
                        }
                    }
                }

                var temp = new sbyte[cornerHeights.Length];
                for (int pass = 0; pass < 2; pass++)
                {
                    Array.Copy(cornerHeights, temp, cornerHeights.Length);
                    foreach (int i in smoothSet)
                    {
                        if (lakeTiles.Contains(i)) continue;
                        int ci = i * 4;
                        int cx = i % width;
                        int cy = i / width;

                        for (int corner = 0; corner < 4; corner++)
                        {
                            int sum = 0, count = 0;
                            void AddN(int tx, int ty, int cIdx)
                            {
                                if (tx < 0 || tx >= width || ty < 0 || ty >= height) return;
                                sum += temp[(ty * width + tx) * 4 + cIdx];
                                count++;
                            }

                            switch (corner)
                            {
                                case 0: AddN(cx,cy,0); AddN(cx-1,cy,1); AddN(cx,cy-1,3); AddN(cx-1,cy-1,2); break;
                                case 1: AddN(cx,cy,1); AddN(cx+1,cy,0); AddN(cx+1,cy-1,3); AddN(cx,cy-1,2); break;
                                case 2: AddN(cx,cy,2); AddN(cx+1,cy,3); AddN(cx+1,cy+1,0); AddN(cx,cy+1,1); break;
                                case 3: AddN(cx,cy,3); AddN(cx-1,cy,2); AddN(cx-1,cy+1,1); AddN(cx,cy+1,0); break;
                            }

                            if (count == 0) continue;
                            double avg = (double)sum / count;
                            double cur = cornerHeights[ci + corner];
                            double desired = cur * 0.5 + avg * 0.5;
                            if (desired > cur) desired = cur;
                            int limited = Math.Max((int)Math.Round(desired), (int)Math.Round(cur) - 1);
                            cornerHeights[ci + corner] = (sbyte)Math.Max(0, Math.Min(limited, MaxTerrainHeight));
                        }
                    }
                }
            }
        }

        // ================================================================
        //  APPLY LIQUID OVERLAYS AND TERRAIN SUBSTRATES
        // ================================================================

        /// <summary>
        /// Set liquid type/depth and terrain substrate for all water tiles.
        /// Rivers get gravel substrate with flow direction.
        /// Lakes get dirt substrate, still water.
        /// Wetlands get dirt substrate, shallow water.
        /// </summary>
        private static void ApplyLiquids(
            TileType[] tiles, Liquid[] liquids,
            HashSet<int> riverTiles, HashSet<int> lakeTiles,
            HashSet<int> wetlandTiles, HashSet<int> oceanTiles,
            int[] flowDir, int[] flowAccum,
            double[] rawHeightGrid, int width, int height)
        {
            foreach (int idx in riverTiles)
            {
                if (oceanTiles.Contains(idx) || lakeTiles.Contains(idx)) continue;
                tiles[idx] = TileType.Gravel;

                // Compute flow direction from flow-direction grid
                int target = flowDir[idx];
                FlowDirection dir = FlowDirection.Still;
                if (target >= 0 && target < width * height)
                {
                    int dx = (target % width) - (idx % width);
                    int dy = (target / width) - (idx / width);
                    dir = DeltaToFlowDirection(dx, dy);
                }

                // Wider rivers get more depth
                byte depth = (byte)(flowAccum[idx] >= WideRiverThreshold ? 4 : 3);
                byte flowStr = (byte)Math.Clamp(flowAccum[idx] * 2, 10, 200);
                liquids[idx] = new Liquid(LiquidType.FreshWater, depth, dir, flowStr);
            }

            foreach (int idx in lakeTiles)
            {
                if (oceanTiles.Contains(idx)) continue;
                tiles[idx] = TileType.Dirt;
                liquids[idx] = new Liquid(LiquidType.FreshWater, 4, FlowDirection.Still, 0);
            }

            foreach (int idx in wetlandTiles)
            {
                if (oceanTiles.Contains(idx) || lakeTiles.Contains(idx) || riverTiles.Contains(idx))
                    continue;
                tiles[idx] = TileType.Dirt;
                liquids[idx] = new Liquid(LiquidType.FreshWater, 1, FlowDirection.Still, 0);
            }
        }

        // ================================================================
        //  UTILITY
        // ================================================================

        private static FlowDirection DeltaToFlowDirection(int dx, int dy)
        {
            if (dx == 0 && dy == -1) return FlowDirection.N;
            if (dx == 1 && dy == -1) return FlowDirection.NE;
            if (dx == 1 && dy == 0)  return FlowDirection.E;
            if (dx == 1 && dy == 1)  return FlowDirection.SE;
            if (dx == 0 && dy == 1)  return FlowDirection.S;
            if (dx == -1 && dy == 1) return FlowDirection.SW;
            if (dx == -1 && dy == 0) return FlowDirection.W;
            if (dx == -1 && dy == -1) return FlowDirection.NW;
            return FlowDirection.Still;
        }

        /// <summary>
        /// Multi-octave fractal noise for ocean placement decisions.
        /// Matches WorldGenerator's FractalNoise2 signature.
        /// </summary>
        private static double FractalNoise2(int x, int y, double baseFreq, int octaves, int seed)
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

            return Lerp(Lerp(v00, v10, u), Lerp(v01, v11, u), v);
        }

        private static double Fade(double t) => t * t * (3 - 2 * t);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static double Hash01(int x, int y, int seed)
        {
            ulong ux = (ulong)(uint)x;
            ulong uy = (ulong)(uint)y;
            ulong useed = (ulong)(uint)seed;
            ulong mix = (ux * 0x9E3779B185EBCA87UL) ^ (uy << 32) ^ (useed * 0x9E3779B97F4A7C15UL);
            ulong z = mix;
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z = z ^ (z >> 31);
            return (z & 0xFFFFFFFFFFFFUL) / (double)(1UL << 48);
        }
    }
}
