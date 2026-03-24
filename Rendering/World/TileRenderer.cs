using CoyEngine.Core;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CoyEngine;

#nullable enable

namespace CoyEngine.Rendering.World
{
    // Simple renderer for map tiles. Draws the tile atlas tinted by tile type color.
    public class TileRenderer : IRenderer
    {
        private GameMap _map;
        private readonly Texture2D _tileTexture;
        private readonly Dictionary<TileType, Color> _tileColors;
        private readonly int _tileW;
        private readonly int _tileH;
        private readonly Camera _camera;
        private readonly Texture2D _rockTexture;
        private BasicEffect? _wallEffect;
        private BasicEffect? _stoneEffect; // textured effect for cliff walls

        // Reusable vertex buffers to avoid per-frame List + ToArray allocations.
        // These grow as needed and are never shrunk.
        private VertexPositionColor[] _terrainBuf = new VertexPositionColor[4096];
        private int _terrainCount;
        private VertexPositionColorTexture[] _wallBuf = new VertexPositionColorTexture[4096];
        private int _wallCount;
        private VertexPositionColor[] _liquidBuf = new VertexPositionColor[4096];
        private int _liquidCount;
        private VertexPositionColor[] _propBuf = new VertexPositionColor[4096];
        private int _propCount;

        // Prop vertex cache: props are deterministic per tile, so we cache
        // world-space vertices and only regenerate when visible range or LOD changes.
        private VertexPositionColor[] _propCache = new VertexPositionColor[4096];
        private int _propCacheCount;
        private int _propCacheStartX = int.MinValue, _propCacheEndX = int.MinValue;
        private int _propCacheStartY = int.MinValue, _propCacheEndY = int.MinValue;
        private int _propCacheLodTier = -1;
        // Terrain writes depth unconditionally (no test). This ensures the most
        // recently drawn row's terrain always wins, preserving painter's algorithm,
        // while still recording Z for water occlusion.
        private static readonly DepthStencilState DepthWriteAlways = new DepthStencilState
        {
            DepthBufferEnable = true,
            DepthBufferWriteEnable = true,
            DepthBufferFunction = CompareFunction.Always
        };
        // Water reads depth but doesn't write. Terrain pixels that are closer
        // (higher elevation) block the water behind them, per pixel.
        private static readonly DepthStencilState DepthReadOnly = new DepthStencilState
        {
            DepthBufferEnable = true,
            DepthBufferWriteEnable = false,
            DepthBufferFunction = CompareFunction.LessEqual
        };
        // Props read AND write depth so they mutually occlude with dogs.
        private static readonly DepthStencilState DepthReadWrite = new DepthStencilState
        {
            DepthBufferEnable = true,
            DepthBufferWriteEnable = true,
            DepthBufferFunction = CompareFunction.LessEqual
        };

        public GameMap Map { get => _map; set => _map = value ?? throw new ArgumentNullException(nameof(value)); }

        public TileRenderer(GameMap map, Texture2D tileTexture, Dictionary<TileType, Color> tileColors, int tileW, int tileH, Camera camera)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _tileTexture = tileTexture ?? throw new ArgumentNullException(nameof(tileTexture));
            _tileColors = tileColors ?? throw new ArgumentNullException(nameof(tileColors));
            _tileW = tileW;
            _tileH = tileH;
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));

            var gd = _tileTexture.GraphicsDevice;
            int rockSize = 64;
            _rockTexture = new Texture2D(gd, rockSize, rockSize);
            var data = new Color[rockSize * rockSize];
            var rng = new System.Random(123456);

            for (int y = 0; y < rockSize; y++)
            {
                for (int x = 0; x < rockSize; x++)
                {
                    float val = (float)rng.NextDouble();
                    int gray = (int)(100 + val * 40);
                    gray = MathHelper.Clamp(gray, 0, 255);
                    data[y * rockSize + x] = new Color(gray, gray, gray);
                }
            }

            var blur = new Color[rockSize * rockSize];
            for (int y = 0; y < rockSize; y++)
            {
                for (int x = 0; x < rockSize; x++)
                {
                    int r = 0, c = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = (x + dx + rockSize) % rockSize;
                            int ny = (y + dy + rockSize) % rockSize;
                            r += data[ny * rockSize + nx].R;
                            c++;
                        }
                    }
                    byte b = (byte)(r / c);
                    blur[y * rockSize + x] = new Color(b, b, b);
                }
            }
            _rockTexture.SetData(blur);
        }

        public void Update(float dt)
        {
            // No per-frame state to keep for tiles currently
        }

        // Ensure a VertexPositionColor buffer has room for 'needed' more entries
        private void EnsureTerrainCapacity(int needed)
        {
            int required = _terrainCount + needed;
            if (required <= _terrainBuf.Length) return;
            int newSize = Math.Max(_terrainBuf.Length * 2, required);
            Array.Resize(ref _terrainBuf, newSize);
        }

        private void EnsureWallCapacity(int needed)
        {
            int required = _wallCount + needed;
            if (required <= _wallBuf.Length) return;
            int newSize = Math.Max(_wallBuf.Length * 2, required);
            Array.Resize(ref _wallBuf, newSize);
        }

        private void EnsureLiquidCapacity(int needed)
        {
            int required = _liquidCount + needed;
            if (required <= _liquidBuf.Length) return;
            int newSize = Math.Max(_liquidBuf.Length * 2, required);
            Array.Resize(ref _liquidBuf, newSize);
        }

        private void EnsurePropCapacity(int needed)
        {
            int required = _propCount + needed;
            if (required <= _propBuf.Length) return;
            int newSize = Math.Max(_propBuf.Length * 2, required);
            Array.Resize(ref _propBuf, newSize);
        }

        public void Draw(GameTime gameTime, RenderContext ctx)
        {
            if (_map == null || _map.Width == 0 || _map.Height == 0)
            {
                return;
            }

            var cam = _camera.GetViewMatrix();
            int elevPx = 12; // pixels per height unit
            int slopeLimit = 1; // max height diff between corners within a tile's surface
            int earthDepth = 30; // how deep edge cliffs extend below ground (in height units)
            Color southWallTint = new Color(180, 180, 180);
            Color eastWallTint = new Color(220, 220, 220);

            // Earth-core warmth: returns a tint blended from stone gray → warm brown → deep orange
            // based on how close the tile is to the map edge. 't' is 0 (interior) to 1 (map edge).
            Color WarmTint(Color baseTint, float edgeFactor)
            {
                if (edgeFactor <= 0f) return baseTint;
                edgeFactor = MathHelper.Clamp(edgeFactor, 0f, 1f);
                // Blend: gray → warm brown (0.5) → deep orange-red (1.0)
                Color warm1 = new Color(160, 120, 80);   // warm brown
                Color warm2 = new Color(180, 90, 40);     // deep orange
                Color target = (edgeFactor < 0.5f)
                    ? Color.Lerp(baseTint, warm1, edgeFactor * 2f)
                    : Color.Lerp(warm1, warm2, (edgeFactor - 0.5f) * 2f);
                return target;
            }

            // How close a tile is to the map edge (south/east). Returns 0..1.
            float EdgeFactor(int x, int y)
            {
                int edgeDist = 12; // tiles from edge where warmth starts
                int distE = _map.Width - 1 - x;
                int distS = _map.Height - 1 - y;
                int distW = x;
                int distN = y;
                int minDist = Math.Min(Math.Min(distE, distS), Math.Min(distW, distN));
                if (minDist >= edgeDist) return 0f;
                return 1f - (float)minDist / edgeDist;
            }

            Vector2 CornerTop(int isoX, int isoY, int h) => new Vector2(isoX + _tileW * 0.5f, isoY - h * elevPx);
            Vector2 CornerRight(int isoX, int isoY, int h) => new Vector2(isoX + _tileW, isoY + _tileH * 0.5f - h * elevPx);
            Vector2 CornerBottom(int isoX, int isoY, int h) => new Vector2(isoX + _tileW * 0.5f, isoY + _tileH - h * elevPx);
            Vector2 CornerLeft(int isoX, int isoY, int h) => new Vector2(isoX + 0, isoY + _tileH * 0.5f - h * elevPx);

            // Float-height variants for liquid surface positioning
            Vector2 CornerTopF(int isoX, int isoY, float h) => new Vector2(isoX + _tileW * 0.5f, isoY - h * elevPx);
            Vector2 CornerRightF(int isoX, int isoY, float h) => new Vector2(isoX + _tileW, isoY + _tileH * 0.5f - h * elevPx);
            Vector2 CornerBottomF(int isoX, int isoY, float h) => new Vector2(isoX + _tileW * 0.5f, isoY + _tileH - h * elevPx);
            Vector2 CornerLeftF(int isoX, int isoY, float h) => new Vector2(isoX + 0, isoY + _tileH * 0.5f - h * elevPx);

            // Map a world-space Y coordinate to a Z-buffer depth value.
            // In isometric view, larger world Y = closer to camera = smaller Z.
            // This correctly handles occlusion across tiles at different elevations.
            const float DepthScale = 1.0f / 4000f;
            float DepthFromWorldY(float worldY) => MathHelper.Clamp(1.0f - (worldY + 200f) * DepthScale, 0.01f, 0.99f);

            // Legacy height-to-Z (used only during vertex build; overridden in draw passes)
            float HeightToZ(float h) => 1.0f - (h + 1f) * 0.1f;

            // Deterministic hash for per-tile variation (0..1)
            float Hash01(int x, int y)
            {
                unchecked
                {
                    uint h = (uint)(x * 374761393 + y * 668265263);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    return (h & 0xFFFFFF) / (float)0x1000000;
                }
            }

            // Per-tile biome color with ±7% brightness noise.
            // Returns A=0 for Empty tiles (used as skip sentinel in averaging).
            Color TintedColor(int tx, int ty)
            {
                var t = _map.GetTile(tx, ty);
                if (t.Type == TileType.Empty) return new Color(0, 0, 0, 0);
                Color bc = _tileColors.TryGetValue(t.Type, out var c) ? c : Color.Magenta;
                float noise = Hash01(tx * 53 + 97, ty * 79 + 31);
                float tint = 0.93f + noise * 0.14f;
                return new Color(
                    (int)MathHelper.Clamp(bc.R * tint, 0, 255),
                    (int)MathHelper.Clamp(bc.G * tint, 0, 255),
                    (int)MathHelper.Clamp(bc.B * tint, 0, 255), 255);
            }

            // Average up to 4 colors, skipping Empty (A=0) tiles.
            Color AvgColors(Color a, Color b, Color c, Color d)
            {
                int r = 0, g = 0, bl = 0, n = 0;
                if (a.A > 0) { r += a.R; g += a.G; bl += a.B; n++; }
                if (b.A > 0) { r += b.R; g += b.G; bl += b.B; n++; }
                if (c.A > 0) { r += c.R; g += c.G; bl += c.B; n++; }
                if (d.A > 0) { r += d.R; g += d.G; bl += d.B; n++; }
                return n > 0 ? new Color(r / n, g / n, bl / n, 255) : Color.Magenta;
            }

            // Compute per-corner blended colors for smooth biome transitions.
            // Each corner is shared by 4 tiles; averaging their colors produces
            // a smooth gradient that the GPU interpolates across the tile surface.
            void GetCornerColors(int tx, int ty, out Color cNW, out Color cNE,
                                 out Color cSE, out Color cSW)
            {
                Color cc  = TintedColor(tx, ty);
                Color tN  = TintedColor(tx, ty - 1);
                Color tS  = TintedColor(tx, ty + 1);
                Color tE  = TintedColor(tx + 1, ty);
                Color tW  = TintedColor(tx - 1, ty);
                Color tNW = TintedColor(tx - 1, ty - 1);
                Color tNE = TintedColor(tx + 1, ty - 1);
                Color tSE = TintedColor(tx + 1, ty + 1);
                Color tSW = TintedColor(tx - 1, ty + 1);

                cNW = AvgColors(cc, tN, tW, tNW);
                cNE = AvgColors(cc, tN, tE, tNE);
                cSE = AvgColors(cc, tS, tE, tSE);
                cSW = AvgColors(cc, tS, tW, tSW);
            }

            // Compute display heights for a tile: clamp so no corner exceeds min + slopeLimit
            void DisplayHeights(Tile t, out int dNW, out int dNE, out int dSE, out int dSW)
            {
                int mn = Math.Min(Math.Min(t.CornerNW, t.CornerNE), Math.Min(t.CornerSE, t.CornerSW));
                int cap = mn + slopeLimit;
                dNW = Math.Min(t.CornerNW, cap);
                dNE = Math.Min(t.CornerNE, cap);
                dSE = Math.Min(t.CornerSE, cap);
                dSW = Math.Min(t.CornerSW, cap);
            }

            // Lazy-init BasicEffects
            var gd = _tileTexture.GraphicsDevice;
            if (_wallEffect == null)
            {
                _wallEffect = new BasicEffect(gd)
                {
                    TextureEnabled = false,
                    VertexColorEnabled = true,
                    World = Matrix.Identity,
                    View = Matrix.Identity
                };
            }
            if (_stoneEffect == null)
            {
                _stoneEffect = new BasicEffect(gd)
                {
                    TextureEnabled = true,
                    VertexColorEnabled = true,
                    Texture = _rockTexture,
                    World = Matrix.Identity,
                    View = Matrix.Identity
                };
            }
            var viewport = gd.Viewport;
            // MonoGame's ortho maps Z via: z_clip = z * 1/(near-far) + near/(near-far)
            // With near=0, far=-2 this gives z_clip = z * 0.5 — mapping positive
            // vertex Z values (from HeightToZ) into depth range [0,1].
            // Using far=-2 (negative!) is the key: MonoGame/XNA uses right-handed
            // coords where -Z is into the screen, so a negative far plane puts
            // positive Z values *inside* the frustum. SpriteEffect does the same
            // trick with far=-1.
            _wallEffect.Projection = Matrix.CreateOrthographicOffCenter(
                0, viewport.Width, viewport.Height, 0, 0, -2);
            _stoneEffect.Projection = Matrix.CreateOrthographicOffCenter(
                0, viewport.Width, viewport.Height, 0, 0, -2);

            // Helper: check if a neighbor tile is dry land (not empty, no liquid)
            bool NeighborIsDryLand(int x, int y, int dx, int dy)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= _map.Width || ny < 0 || ny >= _map.Height) return false;
                var t = _map.GetTile(nx, ny);
                if (t.Type == TileType.Empty) return false;
                return !_map.GetLiquid(nx, ny).HasLiquid;
            }

            // ============================================================
            // DEFERRED RENDERING: collect ALL geometry across all rows,
            // then draw terrain+cliffs (building complete depth buffer),
            // then draw ALL water in a single final pass against that
            // complete depth map. This prevents row-ordering artifacts
            // where background water leaks in front of foreground terrain.
            // ============================================================
            _terrainCount = 0;
            _wallCount = 0;
            _liquidCount = 0;
            _propCount = 0;

            // Check prop cache validity — skip regeneration if tile range & LOD unchanged
            bool propCacheHit = false;

            // Zoom-based LOD: skip expensive sub-pixel details when zoomed out
            bool drawFoam = _camera.Zoom >= 0.35f;
            bool drawCliffWalls = _camera.Zoom >= 0.12f;

            // Prop LOD tiers: reduce tree/grass count and complexity at lower zoom
            //   Tier 3 (zoom >= 0.9): full detail — 6-9 trees, 10-15 grass
            //   Tier 2 (zoom >= 0.55): reduced — 3-5 trees, no grass
            //   Tier 1 (zoom >= 0.25): sparse — 1-2 trees (single triangle), no grass
            //   Tier 0 (zoom < 0.25): skip individual vegetation entirely
            int propLodTier;
            if (_camera.Zoom >= 0.9f) propLodTier = 3;
            else if (_camera.Zoom >= 0.55f) propLodTier = 2;
            else if (_camera.Zoom >= 0.25f) propLodTier = 1;
            else propLodTier = 0;

            // ============================================================
            // VIEW FRUSTUM CULLING: Only iterate tiles visible on screen.
            // Convert screen corners to world space, then to grid coords.
            // ============================================================
            var invView = Matrix.Invert(cam);
            // Screen corners → world space
            var worldTL = Vector2.Transform(Vector2.Zero, invView);
            var worldBR = Vector2.Transform(new Vector2(viewport.Width, viewport.Height), invView);

            // Iso-to-grid inverse: gx = wx/tileW + wy/tileH, gy = wy/tileH - wx/tileW
            float invTW = 1f / _tileW;
            float invTH = 1f / _tileH;
            float IsoToGridX(float wx, float wy) => wx * invTW + wy * invTH;
            float IsoToGridY(float wx, float wy) => wy * invTH - wx * invTW;

            // Compute grid-space bounding box from all 4 screen corners
            var worldTR = Vector2.Transform(new Vector2(viewport.Width, 0), invView);
            var worldBL = Vector2.Transform(new Vector2(0, viewport.Height), invView);

            float g0x = IsoToGridX(worldTL.X, worldTL.Y);
            float g0y = IsoToGridY(worldTL.X, worldTL.Y);
            float g1x = IsoToGridX(worldTR.X, worldTR.Y);
            float g1y = IsoToGridY(worldTR.X, worldTR.Y);
            float g2x = IsoToGridX(worldBR.X, worldBR.Y);
            float g2y = IsoToGridY(worldBR.X, worldBR.Y);
            float g3x = IsoToGridX(worldBL.X, worldBL.Y);
            float g3y = IsoToGridY(worldBL.X, worldBL.Y);

            float gMinX = MathF.Min(MathF.Min(g0x, g1x), MathF.Min(g2x, g3x));
            float gMaxX = MathF.Max(MathF.Max(g0x, g1x), MathF.Max(g2x, g3x));
            float gMinY = MathF.Min(MathF.Min(g0y, g1y), MathF.Min(g2y, g3y));
            float gMaxY = MathF.Max(MathF.Max(g0y, g1y), MathF.Max(g2y, g3y));

            // Pad generously for elevation offsets and cliff walls
            const int cullPad = 6;
            int startX = Math.Max(0, (int)MathF.Floor(gMinX) - cullPad);
            int endX   = Math.Min(_map.Width - 1, (int)MathF.Ceiling(gMaxX) + cullPad);
            int startY = Math.Max(0, (int)MathF.Floor(gMinY) - cullPad);
            int endY   = Math.Min(_map.Height - 1, (int)MathF.Ceiling(gMaxY) + cullPad);

            // Prop cache: if visible tile range and LOD tier match, reuse cached vertices
            propCacheHit = (propLodTier == _propCacheLodTier
                && startX == _propCacheStartX && endX == _propCacheEndX
                && startY == _propCacheStartY && endY == _propCacheEndY);

            // RCT-style cliff rendering:
            //   1. Each tile's surface is clamped so corners differ by at most slopeLimit.
            //      e.g. tile (6,6,0,6) → min=0, cap=1 → display (1,1,0,1)
            //   2. Each tile clamps INDEPENDENTLY based on its own min corner.
            //      So a flat tile (6,6,6,6) displays at (6,6,6,6),
            //      while its neighbor (6,0,0,0) displays at (1,0,0,0).
            //   3. The GAP between adjacent tiles' display heights = cliff wall.
            //      Tile A south edge dSW=6 vs Tile B north edge dNW=1 → 5-unit cliff!
            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    var tile = _map.GetTile(x, y);
                    if (tile.Type == TileType.Empty) continue;

                    var isoPos = GameMap.GridToIso(x, y, _tileW, _tileH);
                    GetCornerColors(x, y, out Color colNW, out Color colNE,
                                    out Color colSE, out Color colSW);

                    DisplayHeights(tile, out int dNW, out int dNE, out int dSE, out int dSW);

                    // --- Check for liquid early so we can adjust terrain geometry ---
                    var liquid = _map.GetLiquid(x, y);
                    float waterH = 0f, waterZ = 0f;
                    if (liquid.HasLiquid)
                    {
                        if (liquid.Type == LiquidType.SaltWater)
                        {
                            const float seaLevel = 0.65f;
                            waterH = seaLevel;
                            waterZ = HeightToZ(seaLevel);
                        }
                        else
                        {
                            int minCorner = Math.Min(Math.Min(dNW, dNE), Math.Min(dSE, dSW));
                            waterH = minCorner + 0.65f;
                            waterZ = HeightToZ(waterH);
                        }
                    }

                    // --- Tile surface at display (clamped) heights ---
                    // For water tiles on slopes: clamp corners that are below
                    // water level UP to water level (RCT2 approach). This creates
                    // a flat shelf at the waterline that transitions naturally to
                    // the slope above. The flat water quad then covers the shelf
                    // exactly — no Z-buffer gaps or coverage issues.
                    float tNW = dNW, tNE = dNE, tSE = dSE, tSW = dSW;
                    if (liquid.HasLiquid)
                    {
                        tNW = Math.Max(tNW, waterH);
                        tNE = Math.Max(tNE, waterH);
                        tSE = Math.Max(tSE, waterH);
                        tSW = Math.Max(tSW, waterH);
                    }
                    var ptNW = CornerTopF(isoPos.x, isoPos.y, tNW);
                    var ptNE = CornerRightF(isoPos.x, isoPos.y, tNE);
                    var ptSE = CornerBottomF(isoPos.x, isoPos.y, tSE);
                    var ptSW = CornerLeftF(isoPos.x, isoPos.y, tSW);
                    // Depth values use ORIGINAL heights, not clamped ones.
                    // The flat shelf at waterH should record the original low-terrain
                    // depth so water (at waterZ) easily passes the depth test there.
                    float zNW = HeightToZ(dNW), zNE = HeightToZ(dNE);
                    float zSE = HeightToZ(dSE), zSW = HeightToZ(dSW);
                    AddQuadVerts(ptNW, ptNE, ptSE, ptSW,
                        colNW, colNE, colSE, colSW,
                        (int)MathF.Round(tNW), (int)MathF.Round(tNE),
                        (int)MathF.Round(tSE), (int)MathF.Round(tSW),
                        zNW, zNE, zSE, zSW);

                    // --- Liquid overlay ---
                    if (liquid.HasLiquid)
                    {
                        Color waterColor = liquid.Type == LiquidType.SaltWater
                            ? new Color(30, 80, 180, 140)     // ocean blue, semi-transparent
                            : new Color(60, 130, 210, 120);   // fresh water blue

                        // Use the SAME positions as the clamped terrain quad.
                        // The shelf portion is flat at waterH (terrain was clamped up),
                        // and the slope portion extends above. The Z-buffer uses
                        // original unclamped heights, so water (at waterZ) passes on
                        // the shelf (original terrain Z > waterZ) and fails on the
                        // slope (original terrain Z < waterZ). Same center-fan
                        // triangulation = identical triangle edges = no gaps.
                        AddFlatQuadVerts4(ptNW, ptNE, ptSE, ptSW, waterColor, waterZ);

                        // --- Phase 3: Shore foam / bright edge at water-land boundary ---
                        // Skip foam when zoomed far out (sub-pixel detail)
                        if (drawFoam)
                        {
                        Color foamColor = liquid.Type == LiquidType.SaltWater
                            ? new Color(180, 210, 240, 160)
                            : new Color(150, 200, 230, 140);
                        float foamZ = waterZ - 0.001f; // slightly closer to camera than water

                        // Foam positions at flat water level for consistent appearance
                        var wNW = CornerTopF(isoPos.x, isoPos.y, waterH);
                        var wNE = CornerRightF(isoPos.x, isoPos.y, waterH);
                        var wSE = CornerBottomF(isoPos.x, isoPos.y, waterH);
                        var wSW = CornerLeftF(isoPos.x, isoPos.y, waterH);

                        // Foam is a thin band along the water edge, inset toward center
                        const float inset = 0.20f;
                        var ctr = new Vector2(
                            (wNW.X + wNE.X + wSE.X + wSW.X) * 0.25f,
                            (wNW.Y + wNE.Y + wSE.Y + wSW.Y) * 0.25f);

                        Vector2 Inset(Vector2 edge) => Vector2.Lerp(edge, ctr, inset);

                        // North edge (NW→NE): neighbor at (x, y-1)
                        if (NeighborIsDryLand(x, y, 0, -1))
                            AddFlatQuadVerts(wNW, wNE, Inset(wNE), Inset(wNW), foamColor, foamZ);
                        // East edge (NE→SE): neighbor at (x+1, y)
                        if (NeighborIsDryLand(x, y, 1, 0))
                            AddFlatQuadVerts(wNE, wSE, Inset(wSE), Inset(wNE), foamColor, foamZ);
                        // South edge (SE→SW): neighbor at (x, y+1)
                        if (NeighborIsDryLand(x, y, 0, 1))
                            AddFlatQuadVerts(wSE, wSW, Inset(wSW), Inset(wSE), foamColor, foamZ);
                        // West edge (SW→NW): neighbor at (x-1, y)
                        if (NeighborIsDryLand(x, y, -1, 0))
                            AddFlatQuadVerts(wSW, wNW, Inset(wNW), Inset(wSW), foamColor, foamZ);
                        }
                    }

                    // --- Vegetation props (forest/grass) ---
                    // Skip prop generation entirely when using cached data
                    if (!propCacheHit && propLodTier > 0)
                    {
                    var veg = _map.GetVegetation(x, y);
                    if (veg != VegetationType.None && !liquid.HasLiquid)
                    {
                        float centerH = (tNW + tNE + tSE + tSW) * 0.25f;
                        float halfW = _tileW * 0.5f;
                        float halfH = _tileH * 0.5f;
                        float rand = Hash01(x, y);

                        if (veg == VegetationType.Forest)
                        {
                            var trunkColor = new Color(90, 60, 35, 255);

                            // LOD-scaled tree count:
                            //   Tier 3: 6-9 trees (full)   Tier 2: 3-5   Tier 1: 1-2
                            int treeCount = propLodTier switch
                            {
                                3 => 6 + (int)(rand * 4f),
                                2 => 3 + (int)(rand * 3f),
                                _ => 1 + (int)(rand * 1.5f),
                            };
                            for (int i = 0; i < treeCount; i++)
                            {
                                float treeRand = Hash01(x * 31 + i * 13, y * 37 - i * 17);
                                float angle = treeRand * MathF.Tau;
                                float radial = (0.10f + 0.25f * treeRand) * _tileW;
                                float offsetY = (Hash01(x - i * 9, y + i * 11) - 0.5f) * _tileH * 0.18f;

                                // Spatial offset from tile center (flat screen space)
                                float dx = MathF.Cos(angle) * radial;
                                float dySpatial = MathF.Sin(angle) * radial * 0.45f + offsetY;

                                // Convert to tile-local UV [0..1] and interpolate terrain height
                                float u = Math.Clamp(0.5f + dx / _tileW + dySpatial / _tileH, 0.02f, 0.98f);
                                float v = Math.Clamp(0.5f - dx / _tileW + dySpatial / _tileH, 0.02f, 0.98f);
                                float localH = (1 - u) * (1 - v) * tNW + u * (1 - v) * tNE
                                             + (1 - u) * v * tSW + u * v * tSE;

                                var treeCenter = new Vector2(
                                    isoPos.x + halfW + dx,
                                    isoPos.y + halfH + dySpatial - localH * elevPx);

                                float size = 0.50f + 0.38f * Hash01(x + i * 5, y - i * 7);
                                float treeZ = DepthFromWorldY(treeCenter.Y) - 0.001f;

                                float tone = Hash01(x * 7 + i * 3, y * 11 - i * 5);
                                var leafColor = new Color(
                                    (int)MathHelper.Lerp(24f, 42f, tone),
                                    (int)MathHelper.Lerp(92f, 132f, tone),
                                    (int)MathHelper.Lerp(42f, 64f, tone),
                                    (int)MathHelper.Lerp(228f, 236f, tone));

                                if (propLodTier >= 2)
                                {
                                    // Full detail: trunk quad + canopy quad (12 verts)
                                    float trunkW = _tileW * 0.022f * size;
                                    float trunkH = _tileH * 0.34f * size;
                                    float canopyW = _tileW * 0.10f * size;
                                    float canopyH = _tileH * 0.30f * size;

                                    var t0 = new Vector2(treeCenter.X - trunkW * 0.5f, treeCenter.Y);
                                    var t1 = new Vector2(treeCenter.X + trunkW * 0.5f, treeCenter.Y);
                                    var t2 = new Vector2(treeCenter.X + trunkW * 0.5f, treeCenter.Y - trunkH);
                                    var t3 = new Vector2(treeCenter.X - trunkW * 0.5f, treeCenter.Y - trunkH);
                                    AddPropQuad(t0, t1, t2, t3, trunkColor, treeZ);

                                    var c0 = new Vector2(treeCenter.X - canopyW, treeCenter.Y - trunkH * 0.82f);
                                    var c1 = new Vector2(treeCenter.X + canopyW, treeCenter.Y - trunkH * 0.82f);
                                    var c2 = new Vector2(treeCenter.X + canopyW * 0.7f, treeCenter.Y - trunkH - canopyH);
                                    var c3 = new Vector2(treeCenter.X - canopyW * 0.7f, treeCenter.Y - trunkH - canopyH);
                                    AddPropQuad(c0, c1, c2, c3, leafColor, treeZ);
                                }
                                else
                                {
                                    // Simplified: single triangle tree (3 verts)
                                    float treeH = _tileH * 0.50f * size;
                                    float treeW = _tileW * 0.08f * size;
                                    var t0 = new Vector2(treeCenter.X - treeW, treeCenter.Y);
                                    var t1 = new Vector2(treeCenter.X + treeW, treeCenter.Y);
                                    var t2 = new Vector2(treeCenter.X, treeCenter.Y - treeH);
                                    AddPropTri(t0, t1, t2, leafColor, treeZ);
                                }
                            }
                        }
                        else if (veg == VegetationType.Grass && propLodTier >= 3)
                        {
                            // Grass only at full detail (tier 3) — invisible when zoomed out
                            int grassCount = 10 + (int)(rand * 6f);
                            for (int i = 0; i < grassCount; i++)
                            {
                                float grassRand = Hash01(x * 29 + i * 7, y * 41 - i * 13);
                                float angle = grassRand * MathF.Tau;
                                float radial = (0.08f + 0.28f * grassRand) * _tileW;
                                float offsetY = (Hash01(x - i * 11, y + i * 19) - 0.5f) * _tileH * 0.24f;

                                // Spatial offset from tile center (flat screen space)
                                float dx = MathF.Cos(angle) * radial;
                                float dySpatial = MathF.Sin(angle) * radial * 0.5f + offsetY;

                                // Convert to tile-local UV and interpolate terrain height
                                float u = Math.Clamp(0.5f + dx / _tileW + dySpatial / _tileH, 0.02f, 0.98f);
                                float v = Math.Clamp(0.5f - dx / _tileW + dySpatial / _tileH, 0.02f, 0.98f);
                                float localH = (1 - u) * (1 - v) * tNW + u * (1 - v) * tNE
                                             + (1 - u) * v * tSW + u * v * tSE;

                                var tuftCenter = new Vector2(
                                    isoPos.x + halfW + dx,
                                    isoPos.y + halfH + dySpatial - localH * elevPx);

                                float size = 0.60f + 0.45f * Hash01(x + i * 3, y - i * 11);
                                float gW = _tileW * 0.045f * size;
                                float gH = _tileH * 0.24f * size;

                                float tuftZ = DepthFromWorldY(tuftCenter.Y) - 0.001f;

                                float tone = Hash01(x * 13 + i * 17, y * 23 - i * 29);
                                var grassColor = new Color(
                                    (int)MathHelper.Lerp(42f, 68f, tone),
                                    (int)MathHelper.Lerp(130f, 160f, tone),
                                    (int)MathHelper.Lerp(50f, 75f, tone),
                                    (int)MathHelper.Lerp(200f, 220f, tone));

                                var t0 = new Vector2(tuftCenter.X - gW, tuftCenter.Y);
                                var t1 = new Vector2(tuftCenter.X + gW, tuftCenter.Y);
                                var t2 = new Vector2(tuftCenter.X, tuftCenter.Y - gH);
                                AddPropTri(t0, t1, t2, grassColor, tuftZ);
                            }
                        }
                    }
                    } // end propCacheHit/propLodTier guard

                    // --- South-facing cliff wall (inter-tile gap) ---
                    // Compare this tile's south display edge with south neighbor's north display edge.
                    // Use clamped heights (tSW/tSE) for the top edge so cliffs fill
                    // the gap created by water-level terrain clamping.
                    if (drawCliffWalls)
                    {
                        float baseSW = 0, baseSE = 0;
                        bool isEdge = (y + 1 >= _map.Height);
                        if (!isEdge)
                        {
                            var s = _map.GetTile(x, y + 1);
                            if (s.Type != TileType.Empty)
                            {
                                DisplayHeights(s, out int snNW, out int snNE, out _, out _);
                                baseSW = snNW;
                                baseSE = snNE;
                                var sLiq = _map.GetLiquid(x, y + 1);
                                if (sLiq.HasLiquid)
                                {
                                    float sWaterH = (sLiq.Type == LiquidType.SaltWater) ? 0.65f
                                        : Math.Min(Math.Min(snNW, snNE), Math.Min(snNW, snNE)) + 0.65f;
                                    baseSW = Math.Max(baseSW, sWaterH);
                                    baseSE = Math.Max(baseSE, sWaterH);
                                }
                            }
                        }
                        else
                        {
                            baseSW = -earthDepth;
                            baseSE = -earthDepth;
                        }
                        if (tSW > baseSW || tSE > baseSE)
                        {
                            var topL = CornerLeftF(isoPos.x, isoPos.y, tSW);
                            var topR = CornerBottomF(isoPos.x, isoPos.y, tSE);
                            var botL = CornerLeftF(isoPos.x, isoPos.y, baseSW);
                            var botR = CornerBottomF(isoPos.x, isoPos.y, baseSE);
                            float maxDrop = Math.Max(tSW - baseSW, tSE - baseSE);
                            float cliffZL = HeightToZ(tSW);
                            float cliffZR = HeightToZ(tSE);
                            if (isEdge && _map.GetLiquid(x, y).HasLiquid)
                            {
                                var liq = _map.GetLiquid(x, y);
                                float wH = (liq.Type == LiquidType.SaltWater) ? 0.65f
                                    : Math.Min(Math.Min(dNW, dNE), Math.Min(dSE, dSW)) + 0.65f;
                                float wZ = HeightToZ(wH);

                                var rockTopL = CornerLeftF(isoPos.x, isoPos.y, dSW);
                                var rockTopR = CornerBottomF(isoPos.x, isoPos.y, dSE);
                                float rockMaxDrop = Math.Max(dSW - baseSW, dSE - baseSE);
                                Color topTintR = WarmTint(southWallTint, 0.3f);
                                Color botTintR = WarmTint(southWallTint, 1.0f);
                                AddGradientTexturedQuadVerts(rockTopL, rockTopR, botR, botL, topTintR, botTintR, rockMaxDrop * 0.5f,
                                    HeightToZ(dSW), HeightToZ(dSE), HeightToZ(dSE), HeightToZ(dSW));

                                var waterL = CornerLeftF(isoPos.x, isoPos.y, wH);
                                var waterR = CornerBottomF(isoPos.x, isoPos.y, wH);
                                Color surfWater = (liq.Type == LiquidType.SaltWater)
                                    ? new Color(30, 80, 180, 200)
                                    : new Color(60, 130, 210, 180);
                                AddFlatQuadVerts(waterL, waterR, rockTopR, rockTopL, surfWater, wZ);
                            }
                            else if (isEdge)
                            {
                                Color topTint = WarmTint(southWallTint, 0.3f);
                                Color botTint = WarmTint(southWallTint, 1.0f);
                                AddGradientTexturedQuadVerts(topL, topR, botR, botL, topTint, botTint, maxDrop * 0.5f,
                                    cliffZL, cliffZR, cliffZR, cliffZL);
                            }
                            else
                            {
                                Color wallTint = WarmTint(southWallTint, EdgeFactor(x, y));
                                AddTexturedQuadVerts(topL, topR, botR, botL, wallTint, maxDrop * 0.5f,
                                    cliffZL, cliffZR, cliffZR, cliffZL);
                            }
                        }
                    }

                    // --- East-facing cliff wall (inter-tile gap) ---
                    // Compare this tile's east display edge with east neighbor's west display edge.
                    if (drawCliffWalls)
                    {
                        float baseNE = 0, baseSE = 0;
                        bool isEdgeE = (x + 1 >= _map.Width);
                        if (!isEdgeE)
                        {
                            var e = _map.GetTile(x + 1, y);
                            if (e.Type != TileType.Empty)
                            {
                                DisplayHeights(e, out int enNW, out _, out _, out int enSW);
                                baseNE = enNW;
                                baseSE = enSW;
                                var eLiq = _map.GetLiquid(x + 1, y);
                                if (eLiq.HasLiquid)
                                {
                                    float eWaterH = (eLiq.Type == LiquidType.SaltWater) ? 0.65f
                                        : Math.Min(Math.Min(enNW, enSW), Math.Min(enNW, enSW)) + 0.65f;
                                    baseNE = Math.Max(baseNE, eWaterH);
                                    baseSE = Math.Max(baseSE, eWaterH);
                                }
                            }
                        }
                        else
                        {
                            baseNE = -earthDepth;
                            baseSE = -earthDepth;
                        }
                        if (tNE > baseNE || tSE > baseSE)
                        {
                            var topT = CornerRightF(isoPos.x, isoPos.y, tNE);
                            var topB = CornerBottomF(isoPos.x, isoPos.y, tSE);
                            var botT = CornerRightF(isoPos.x, isoPos.y, baseNE);
                            var botB = CornerBottomF(isoPos.x, isoPos.y, baseSE);
                            float maxDrop = Math.Max(tNE - baseNE, tSE - baseSE);
                            float cliffZT = HeightToZ(tNE);
                            float cliffZB = HeightToZ(tSE);
                            if (isEdgeE && _map.GetLiquid(x, y).HasLiquid)
                            {
                                var liq = _map.GetLiquid(x, y);
                                float wH = (liq.Type == LiquidType.SaltWater) ? 0.65f
                                    : Math.Min(Math.Min(dNW, dNE), Math.Min(dSE, dSW)) + 0.65f;
                                float wZ = HeightToZ(wH);

                                var rockTopT = CornerRightF(isoPos.x, isoPos.y, dNE);
                                var rockTopB = CornerBottomF(isoPos.x, isoPos.y, dSE);
                                float rockMaxDrop = Math.Max(dNE - baseNE, dSE - baseSE);
                                Color topTintR = WarmTint(eastWallTint, 0.3f);
                                Color botTintR = WarmTint(eastWallTint, 1.0f);
                                AddGradientTexturedQuadVerts(rockTopT, rockTopB, botB, botT, topTintR, botTintR, rockMaxDrop * 0.5f,
                                    HeightToZ(dNE), HeightToZ(dSE), HeightToZ(dSE), HeightToZ(dNE));

                                var waterT = CornerRightF(isoPos.x, isoPos.y, wH);
                                var waterB = CornerBottomF(isoPos.x, isoPos.y, wH);
                                Color surfWater = (liq.Type == LiquidType.SaltWater)
                                    ? new Color(30, 80, 180, 200)
                                    : new Color(60, 130, 210, 180);
                                AddFlatQuadVerts(waterT, waterB, rockTopB, rockTopT, surfWater, wZ);
                            }
                            else if (isEdgeE)
                            {
                                Color topTint = WarmTint(eastWallTint, 0.3f);
                                Color botTint = WarmTint(eastWallTint, 1.0f);
                                AddGradientTexturedQuadVerts(topT, topB, botB, botT, topTint, botTint, maxDrop * 0.5f,
                                    cliffZT, cliffZB, cliffZB, cliffZT);
                            }
                            else
                            {
                                Color wallTint = WarmTint(eastWallTint, EdgeFactor(x, y));
                                AddTexturedQuadVerts(topT, topB, botB, botT, wallTint, maxDrop * 0.5f,
                                    cliffZT, cliffZB, cliffZB, cliffZT);
                            }
                        }
                    }
                }
            }

            // ============================================================
            // PROP CACHE: save generated props, or restore from cache
            // ============================================================
            if (propCacheHit)
            {
                // Restore cached world-space prop vertices
                EnsurePropCapacity(_propCacheCount - _propCount);
                Array.Copy(_propCache, _propBuf, _propCacheCount);
                _propCount = _propCacheCount;
            }
            else if (_propCount > 0)
            {
                // Save newly generated props to cache
                if (_propCache.Length < _propCount)
                    _propCache = new VertexPositionColor[_propCount * 2];
                Array.Copy(_propBuf, _propCache, _propCount);
                _propCacheCount = _propCount;
                _propCacheStartX = startX; _propCacheEndX = endX;
                _propCacheStartY = startY; _propCacheEndY = endY;
                _propCacheLodTier = propLodTier;
            }
            else
            {
                // No props generated — mark cache as valid for this range
                _propCacheCount = 0;
                _propCacheStartX = startX; _propCacheEndX = endX;
                _propCacheStartY = startY; _propCacheEndY = endY;
                _propCacheLodTier = propLodTier;
            }

            // ============================================================
            // DRAW PASS 1: All terrain surfaces — visible draw + depth write
            // ============================================================
            // Max primitives per DrawUserPrimitives call (MonoGame limit ~65535 verts)
            const int MaxVertsPerBatch = 65535;

            if (_terrainCount > 0)
            {
                var prevBlend = gd.BlendState;
                var prevDepthState = gd.DepthStencilState;
                var prevRaster = gd.RasterizerState;

                gd.BlendState = BlendState.AlphaBlend;
                gd.DepthStencilState = DepthStencilState.None;
                gd.RasterizerState = RasterizerState.CullNone;

                // Transform world → screen in-place for the visible pass,
                // then build a separate depth array for the depth-write pass.
                // Depth Z uses world-Y position for correct isometric occlusion.
                var depthVerts = new VertexPositionColor[_terrainCount];
                for (int i = 0; i < _terrainCount; i++)
                {
                    var worldPos = new Vector2(_terrainBuf[i].Position.X, _terrainBuf[i].Position.Y);
                    var screenPos = Vector2.Transform(worldPos, cam);
                    float depthZ = DepthFromWorldY(worldPos.Y);
                    Color origColor = _terrainBuf[i].Color;
                    _terrainBuf[i].Position = new Vector3(screenPos, 0);

                    if (DebugSettings.ShowDepth)
                    {
                        byte gray = (byte)MathHelper.Clamp(255 * depthZ, 0, 255);
                        depthVerts[i] = new VertexPositionColor(
                            new Vector3(screenPos, depthZ),
                            new Color((int)gray, (int)gray, (int)gray, 255));
                    }
                    else
                    {
                        depthVerts[i] = new VertexPositionColor(
                            new Vector3(screenPos, depthZ),
                            origColor);
                    }
                }

                // Draw terrain (visible, no depth) — batched
                foreach (var pass in _wallEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    for (int off = 0; off < _terrainCount; )
                    {
                        int batch = Math.Min(_terrainCount - off, MaxVertsPerBatch);
                        batch = batch / 3 * 3; // align to triangles
                        if (batch <= 0) break;
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList, _terrainBuf, off, batch / 3);
                        off += batch;
                    }
                }

                // Redraw terrain at real Z to populate depth buffer.
                gd.DepthStencilState = DepthWriteAlways;
                foreach (var pass in _wallEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    for (int off = 0; off < _terrainCount; )
                    {
                        int batch = Math.Min(_terrainCount - off, MaxVertsPerBatch);
                        batch = batch / 3 * 3;
                        if (batch <= 0) break;
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList, depthVerts, off, batch / 3);
                        off += batch;
                    }
                }

                gd.BlendState = prevBlend;
                gd.DepthStencilState = prevDepthState;
                gd.RasterizerState = prevRaster;
            }

            // ============================================================
            // DRAW PASS 2: All cliff walls — visible draw + depth write
            // ============================================================
            if (_wallCount > 0)
            {
                var prevBlend = gd.BlendState;
                var prevDepthState = gd.DepthStencilState;
                var prevRaster = gd.RasterizerState;
                var prevSampler = gd.SamplerStates[0];

                gd.BlendState = BlendState.AlphaBlend;
                gd.DepthStencilState = DepthWriteAlways;
                gd.RasterizerState = RasterizerState.CullNone;
                gd.SamplerStates[0] = SamplerState.LinearWrap;

                for (int i = 0; i < _wallCount; i++)
                {
                    var worldPos = new Vector2(_wallBuf[i].Position.X, _wallBuf[i].Position.Y);
                    var screenPos = Vector2.Transform(worldPos, cam);
                    _wallBuf[i].Position = new Vector3(screenPos, DepthFromWorldY(worldPos.Y));
                }

                foreach (var pass in _stoneEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    for (int off = 0; off < _wallCount; )
                    {
                        int batch = Math.Min(_wallCount - off, MaxVertsPerBatch);
                        batch = batch / 3 * 3;
                        if (batch <= 0) break;
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList, _wallBuf, off, batch / 3);
                        off += batch;
                    }
                }

                gd.BlendState = prevBlend;
                gd.DepthStencilState = prevDepthState;
                gd.RasterizerState = prevRaster;
                gd.SamplerStates[0] = prevSampler;
            }

            // ============================================================
            // DRAW PASS 2.5: Vegetation props — depth read+write
            // ============================================================
            if (_propCount > 0)
            {
                var prevBlend = gd.BlendState;
                var prevDepthState = gd.DepthStencilState;
                var prevRaster = gd.RasterizerState;

                gd.BlendState = BlendState.AlphaBlend;
                gd.DepthStencilState = DepthReadWrite;
                gd.RasterizerState = RasterizerState.CullNone;

                for (int i = 0; i < _propCount; i++)
                {
                    var worldPos = new Vector2(_propBuf[i].Position.X, _propBuf[i].Position.Y);
                    var screenPos = Vector2.Transform(worldPos, cam);
                    // Depth Z was already computed per-prop during build (base Y)
                    _propBuf[i].Position = new Vector3(screenPos, _propBuf[i].Position.Z);
                }

                foreach (var pass in _wallEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    for (int off = 0; off < _propCount; )
                    {
                        int batch = Math.Min(_propCount - off, MaxVertsPerBatch);
                        batch = batch / 3 * 3;
                        if (batch <= 0) break;
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList, _propBuf, off, batch / 3);
                        off += batch;
                    }
                }

                gd.BlendState = prevBlend;
                gd.DepthStencilState = prevDepthState;
                gd.RasterizerState = prevRaster;
            }

            // ============================================================
            // DRAW PASS 3: All liquid overlays — depth TEST only (no write)
            // ============================================================
            if (_liquidCount > 0)
            {
                var prevBlend = gd.BlendState;
                var prevDepthState = gd.DepthStencilState;
                var prevRaster = gd.RasterizerState;

                gd.BlendState = BlendState.AlphaBlend;
                gd.DepthStencilState = DepthReadOnly;
                gd.RasterizerState = RasterizerState.CullNone;

                for (int i = 0; i < _liquidCount; i++)
                {
                    var worldPos = new Vector2(_liquidBuf[i].Position.X, _liquidBuf[i].Position.Y);
                    var screenPos = Vector2.Transform(worldPos, cam);
                    _liquidBuf[i].Position = new Vector3(screenPos, DepthFromWorldY(worldPos.Y) - 0.0005f);
                }

                foreach (var pass in _wallEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    for (int off = 0; off < _liquidCount; )
                    {
                        int batch = Math.Min(_liquidCount - off, MaxVertsPerBatch);
                        batch = batch / 3 * 3;
                        if (batch <= 0) break;
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList, _liquidBuf, off, batch / 3);
                        off += batch;
                    }
                }

                gd.BlendState = prevBlend;
                gd.DepthStencilState = prevDepthState;
                gd.RasterizerState = prevRaster;
            }

            // --- Debug grid overlay: thin lines along tile edges ---
            // Fade out grid when zoomed far away to reduce visual clutter
            float gridAlpha = MathHelper.Clamp((_camera.Zoom - 0.4f) / (1.5f - 0.4f), 0f, 1f);
            if (DebugSettings.ShowGrid && gridAlpha > 0.01f)
            {
                var gridVerts = new List<VertexPositionColor>();
                byte alphaVal = (byte)(20 * gridAlpha);
                Color gridColor = new Color(90,70,40, (int)alphaVal);
                float lineThickness = 1.0f;

                for (int y = startY; y <= endY; y++)
                {
                    for (int x = startX; x <= endX; x++)
                    {
                        var tile = _map.GetTile(x, y);
                        if (tile.Type == TileType.Empty) continue;

                        var isoPos = GameMap.GridToIso(x, y, _tileW, _tileH);
                        DisplayHeights(tile, out int dNW, out int dNE, out int dSE, out int dSW);

                        // Grid on tile surface (at display/clamped heights)
                        var ptNW = CornerTop(isoPos.x, isoPos.y, dNW);
                        var ptNE = CornerRight(isoPos.x, isoPos.y, dNE);
                        var ptSE = CornerBottom(isoPos.x, isoPos.y, dSE);
                        var ptSW = CornerLeft(isoPos.x, isoPos.y, dSW);

                        AddLineVerts(gridVerts, ptNW, ptNE, lineThickness, gridColor);
                        AddLineVerts(gridVerts, ptNE, ptSE, lineThickness, gridColor);
                        AddLineVerts(gridVerts, ptSE, ptSW, lineThickness, gridColor);
                        AddLineVerts(gridVerts, ptSW, ptNW, lineThickness, gridColor);

                        // Vertical cliff edge lines on south face
                        {
                            int baseSW = 0, baseSE = 0;
                            if (y + 1 < _map.Height)
                            {
                                var s = _map.GetTile(x, y + 1);
                                if (s.Type != TileType.Empty)
                                {
                                    DisplayHeights(s, out int snNW, out int snNE, out _, out _);
                                    baseSW = snNW;
                                    baseSE = snNE;
                                }
                            }
                            if (dSW > baseSW || dSE > baseSE)
                            {
                                // Vertical lines at corners
                                if (dSW > baseSW)
                                    AddLineVerts(gridVerts, CornerLeft(isoPos.x, isoPos.y, dSW),
                                        CornerLeft(isoPos.x, isoPos.y, baseSW), lineThickness, gridColor);
                                if (dSE > baseSE)
                                    AddLineVerts(gridVerts, CornerBottom(isoPos.x, isoPos.y, dSE),
                                        CornerBottom(isoPos.x, isoPos.y, baseSE), lineThickness, gridColor);
                                // Bottom edge of cliff
                                var botL = CornerLeft(isoPos.x, isoPos.y, baseSW);
                                var botR = CornerBottom(isoPos.x, isoPos.y, baseSE);
                                AddLineVerts(gridVerts, botL, botR, lineThickness, gridColor);
                            }
                        }

                        // Vertical cliff edge lines on east face
                        {
                            int baseNE = 0, baseSE = 0;
                            if (x + 1 < _map.Width)
                            {
                                var e = _map.GetTile(x + 1, y);
                                if (e.Type != TileType.Empty)
                                {
                                    DisplayHeights(e, out int enNW, out _, out _, out int enSW);
                                    baseNE = enNW;
                                    baseSE = enSW;
                                }
                            }
                            if (dNE > baseNE || dSE > baseSE)
                            {
                                if (dNE > baseNE)
                                    AddLineVerts(gridVerts, CornerRight(isoPos.x, isoPos.y, dNE),
                                        CornerRight(isoPos.x, isoPos.y, baseNE), lineThickness, gridColor);
                                if (dSE > baseSE)
                                    AddLineVerts(gridVerts, CornerBottom(isoPos.x, isoPos.y, dSE),
                                        CornerBottom(isoPos.x, isoPos.y, baseSE), lineThickness, gridColor);
                                var botT = CornerRight(isoPos.x, isoPos.y, baseNE);
                                var botB = CornerBottom(isoPos.x, isoPos.y, baseSE);
                                AddLineVerts(gridVerts, botT, botB, lineThickness, gridColor);
                            }
                        }
                    }
                }

                if (gridVerts.Count > 0)
                {
                    var prevBlend = gd.BlendState;
                    var prevDepthState = gd.DepthStencilState;
                    var prevRaster = gd.RasterizerState;

                    gd.BlendState = BlendState.AlphaBlend;
                    gd.DepthStencilState = DepthStencilState.None;
                    gd.RasterizerState = RasterizerState.CullNone;

                    var verts = gridVerts.ToArray();
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var worldPos = new Vector2(verts[i].Position.X, verts[i].Position.Y);
                        var screenPos = Vector2.Transform(worldPos, cam);
                        verts[i].Position = new Vector3(screenPos, 0);
                    }

                    foreach (var pass in _wallEffect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3);
                    }

                    gd.BlendState = prevBlend;
                    gd.DepthStencilState = prevDepthState;
                    gd.RasterizerState = prevRaster;
                }
            }

            if (DebugSettings.ShowHeights)
            {
                var font = ctx.DebugFont as SpriteFont;
                var tiny = ctx.TinyFont;

                ctx.BeginWorld(_camera.GetViewMatrix());

                for (int y = startY; y <= endY; y++)
                {
                    for (int x = startX; x <= endX; x++)
                    {
                        var t = _map.GetTile(x, y);
                        if (t.Type == TileType.Empty) continue;

                        var isoPos = GameMap.GridToIso(x, y, _tileW, _tileH);

                        // Display heights (clamped) for this tile
                        DisplayHeights(t, out int dNW, out int dNE, out int dSE, out int dSW);
                        int avgH = (dNW + dNE + dSE + dSW) / 4;

                        // Show single height number at tile center
                        var center = new Vector2(
                            isoPos.x + _tileW * 0.5f,
                            isoPos.y + _tileH * 0.5f - avgH * elevPx);

                        // Build label: if all corners same, just show the number;
                        // if they differ, show NW/NE/SE/SW
                        string label;
                        if (dNW == dNE && dNE == dSE && dSE == dSW)
                            label = dNW.ToString();
                        else
                            label = $"{dNW},{dNE},{dSE},{dSW}";

                        // Center the text
                        Vector2 textSize = Vector2.Zero;
                        if (font != null)
                            textSize = font.MeasureString(label);
                        else if (tiny != null)
                            textSize = tiny.MeasureString(label) * 1f;
                        var drawPos = center - textSize * 0.5f;

                        if (font != null)
                            ctx.SpriteBatch.DrawString(font, label, drawPos, Color.Yellow);
                        else if (tiny != null)
                            tiny.DrawString(ctx.SpriteBatch, label, drawPos, Color.Yellow, 1f);
                    }
                }

                ctx.End();
            }
        }
        private void AddQuadVerts(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3,
            Color cNW, Color cNE, Color cSE, Color cSW,
            int hNW = 0, int hNE = 0, int hSE = 0, int hSW = 0,
            float zNW = 0, float zNE = 0, float zSE = 0, float zSW = 0)
        {
            EnsureTerrainCapacity(12);
            // Four triangles from center point to eliminate diagonal crease on sloped tiles.
            // v0=NW(top), v1=NE(right), v2=SE(bottom), v3=SW(left)
            float hCenter = (hNW + hNE + hSE + hSW) * 0.25f;
            float zCenter = (zNW + zNE + zSE + zSW) * 0.25f;
            var center = new Vector2(
                (v0.X + v1.X + v2.X + v3.X) * 0.25f,
                (v0.Y + v1.Y + v2.Y + v3.Y) * 0.25f);

            // Center color = average of the 4 corner colors
            Color cCenter = new Color(
                (cNW.R + cNE.R + cSE.R + cSW.R) / 4,
                (cNW.G + cNE.G + cSE.G + cSW.G) / 4,
                (cNW.B + cNE.B + cSE.B + cSW.B) / 4, 255);

            const float shadeStr = 0.18f;

            float nSlopeX = (hNE - hNW);
            float nSlopeY = ((hNW + hNE) * 0.5f - hCenter);
            float nShade = 1.0f - nSlopeX * shadeStr * 0.5f + nSlopeY * shadeStr;

            float eSlopeX = ((hNE + hSE) * 0.5f - hCenter);
            float eSlopeY = (hSE - hNE);
            float eShade = 1.0f - eSlopeX * shadeStr - eSlopeY * shadeStr * 0.5f;

            float sSlopeX = (hSE - hSW);
            float sSlopeY = ((hSE + hSW) * 0.5f - hCenter);
            float sShade = 1.0f - sSlopeX * shadeStr * 0.5f - sSlopeY * shadeStr;

            float wSlopeX = ((hSW + hNW) * 0.5f - hCenter);
            float wSlopeY = (hNW - hSW);
            float wShade = 1.0f + wSlopeX * shadeStr + wSlopeY * shadeStr * 0.5f;

            // Apply directional shade to a corner color
            Color Shade(Color col, float s)
            {
                s = MathHelper.Clamp(s, 0.6f, 1.2f);
                return new Color(
                    (int)MathHelper.Clamp(col.R * s, 0, 255),
                    (int)MathHelper.Clamp(col.G * s, 0, 255),
                    (int)MathHelper.Clamp(col.B * s, 0, 255),
                    col.A);
            }

            nShade = MathHelper.Clamp(nShade, 0.6f, 1.2f);
            eShade = MathHelper.Clamp(eShade, 0.6f, 1.2f);
            sShade = MathHelper.Clamp(sShade, 0.6f, 1.2f);
            wShade = MathHelper.Clamp(wShade, 0.6f, 1.2f);

            // Per-corner, per-face shaded colors. Each corner appears in two
            // adjacent faces; we use the face's shade for directional lighting
            // while the corner's color carries the biome blend.
            var vc = new Vector3(center, zCenter);

            // N face: NW, NE, center
            _terrainBuf[_terrainCount++] = new VertexPositionColor(new Vector3(v0, zNW), Shade(cNW, nShade));
            _terrainBuf[_terrainCount++] = new VertexPositionColor(new Vector3(v1, zNE), Shade(cNE, nShade));
            _terrainBuf[_terrainCount++] = new VertexPositionColor(vc, Shade(cCenter, nShade));
            // E face: NE, SE, center
            _terrainBuf[_terrainCount++] = new VertexPositionColor(new Vector3(v1, zNE), Shade(cNE, eShade));
            _terrainBuf[_terrainCount++] = new VertexPositionColor(new Vector3(v2, zSE), Shade(cSE, eShade));
            _terrainBuf[_terrainCount++] = new VertexPositionColor(vc, Shade(cCenter, eShade));
            // S face: SE, SW, center
            _terrainBuf[_terrainCount++] = new VertexPositionColor(new Vector3(v2, zSE), Shade(cSE, sShade));
            _terrainBuf[_terrainCount++] = new VertexPositionColor(new Vector3(v3, zSW), Shade(cSW, sShade));
            _terrainBuf[_terrainCount++] = new VertexPositionColor(vc, Shade(cCenter, sShade));
            // W face: SW, NW, center
            _terrainBuf[_terrainCount++] = new VertexPositionColor(new Vector3(v3, zSW), Shade(cSW, wShade));
            _terrainBuf[_terrainCount++] = new VertexPositionColor(new Vector3(v0, zNW), Shade(cNW, wShade));
            _terrainBuf[_terrainCount++] = new VertexPositionColor(vc, Shade(cCenter, wShade));
        }

        private void AddFlatQuadVerts(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3, Color color, float z = 0f)
        {
            EnsureLiquidCapacity(6);
            _liquidBuf[_liquidCount++] = new VertexPositionColor(new Vector3(v0, z), color);
            _liquidBuf[_liquidCount++] = new VertexPositionColor(new Vector3(v1, z), color);
            _liquidBuf[_liquidCount++] = new VertexPositionColor(new Vector3(v2, z), color);
            _liquidBuf[_liquidCount++] = new VertexPositionColor(new Vector3(v0, z), color);
            _liquidBuf[_liquidCount++] = new VertexPositionColor(new Vector3(v2, z), color);
            _liquidBuf[_liquidCount++] = new VertexPositionColor(new Vector3(v3, z), color);
        }

        private void AddFlatQuadVerts4(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3, Color color, float z)
        {
            EnsureLiquidCapacity(12);
            var c = new Vector3(
                (v0.X + v1.X + v2.X + v3.X) * 0.25f,
                (v0.Y + v1.Y + v2.Y + v3.Y) * 0.25f,
                z);

            var p0 = new VertexPositionColor(new Vector3(v0, z), color);
            var p1 = new VertexPositionColor(new Vector3(v1, z), color);
            var p2 = new VertexPositionColor(new Vector3(v2, z), color);
            var p3 = new VertexPositionColor(new Vector3(v3, z), color);
            var pc = new VertexPositionColor(c, color);

            _liquidBuf[_liquidCount++] = p0; _liquidBuf[_liquidCount++] = p1; _liquidBuf[_liquidCount++] = pc;
            _liquidBuf[_liquidCount++] = p1; _liquidBuf[_liquidCount++] = p2; _liquidBuf[_liquidCount++] = pc;
            _liquidBuf[_liquidCount++] = p2; _liquidBuf[_liquidCount++] = p3; _liquidBuf[_liquidCount++] = pc;
            _liquidBuf[_liquidCount++] = p3; _liquidBuf[_liquidCount++] = p0; _liquidBuf[_liquidCount++] = pc;
        }

        private void AddTexturedQuadVerts(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3, Color tint, float vScale,
            float z0 = 0f, float z1 = 0f, float z2 = 0f, float z3 = 0f)
        {
            EnsureWallCapacity(6);
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v0, z0), tint, new Vector2(0, 0));
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v1, z1), tint, new Vector2(1, 0));
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v2, z2), tint, new Vector2(1, vScale));
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v0, z0), tint, new Vector2(0, 0));
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v2, z2), tint, new Vector2(1, vScale));
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v3, z3), tint, new Vector2(0, vScale));
        }

        private void AddGradientTexturedQuadVerts(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3, Color topTint, Color botTint, float vScale,
            float z0 = 0f, float z1 = 0f, float z2 = 0f, float z3 = 0f)
        {
            EnsureWallCapacity(6);
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v0, z0), topTint, new Vector2(0, 0));
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v1, z1), topTint, new Vector2(1, 0));
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v2, z2), botTint, new Vector2(1, vScale));
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v0, z0), topTint, new Vector2(0, 0));
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v2, z2), botTint, new Vector2(1, vScale));
            _wallBuf[_wallCount++] = new VertexPositionColorTexture(new Vector3(v3, z3), botTint, new Vector2(0, vScale));
        }

        private void AddPropQuad(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3, Color color, float z)
        {
            EnsurePropCapacity(6);
            _propBuf[_propCount++] = new VertexPositionColor(new Vector3(v0, z), color);
            _propBuf[_propCount++] = new VertexPositionColor(new Vector3(v1, z), color);
            _propBuf[_propCount++] = new VertexPositionColor(new Vector3(v2, z), color);
            _propBuf[_propCount++] = new VertexPositionColor(new Vector3(v0, z), color);
            _propBuf[_propCount++] = new VertexPositionColor(new Vector3(v2, z), color);
            _propBuf[_propCount++] = new VertexPositionColor(new Vector3(v3, z), color);
        }

        private void AddPropTri(Vector2 v0, Vector2 v1, Vector2 v2, Color color, float z)
        {
            EnsurePropCapacity(3);
            _propBuf[_propCount++] = new VertexPositionColor(new Vector3(v0, z), color);
            _propBuf[_propCount++] = new VertexPositionColor(new Vector3(v1, z), color);
            _propBuf[_propCount++] = new VertexPositionColor(new Vector3(v2, z), color);
        }

        private void AddLineVerts(List<VertexPositionColor> verts, Vector2 a, Vector2 b, float thickness, Color color)
        {
            // Build a thin quad along the line segment a→b
            var dir = b - a;
            if (dir.LengthSquared() < 0.001f) return;
            dir.Normalize();
            var perp = new Vector2(-dir.Y, dir.X) * (thickness * 0.5f);

            var v0 = a + perp;
            var v1 = a - perp;
            var v2 = b - perp;
            var v3 = b + perp;

            verts.Add(new VertexPositionColor(new Vector3(v0, 0), color));
            verts.Add(new VertexPositionColor(new Vector3(v1, 0), color));
            verts.Add(new VertexPositionColor(new Vector3(v2, 0), color));
            verts.Add(new VertexPositionColor(new Vector3(v0, 0), color));
            verts.Add(new VertexPositionColor(new Vector3(v2, 0), color));
            verts.Add(new VertexPositionColor(new Vector3(v3, 0), color));
        }
    }
}
