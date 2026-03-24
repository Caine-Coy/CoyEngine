namespace CoyEngine.Core
{
    /// <summary>
    /// Global debug rendering toggles for isometric visualization.
    /// Useful for debugging terrain, depth, and grid alignment.
    /// </summary>
    public static class DebugSettings
    {
        public static bool ShowHeights { get; set; } = false;
        public static bool ShowGrid { get; set; } = false;
        public static bool ShowDepth { get; set; } = false;
    }
}
