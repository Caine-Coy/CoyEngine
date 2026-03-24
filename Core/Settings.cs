using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics; 

namespace CoyEngine.Core
{
    public enum InputCoordinateMode
    {
        Auto = 0,
        WindowRelative = 1,
        RawMinusWindowPos = 2,
        BackbufferScaled = 3,
        CustomOffset = 4
    }

    public class Settings
    { 
        /// <summary>
        /// Frame cap in frames per second. If null or <= 0, the game will use platform defaults (no fixed timestep change).
        /// </summary>
        public int? FrameCap { get; set; } = 60;

        /// <summary>
        /// If true, enable vertical sync via GraphicsDeviceManager.SynchronizeWithVerticalRetrace
        /// </summary>
        public bool VSync { get; set; } = false;

        /// <summary>
        /// Server base URL for map requests (used by the main menu Join Server button).
        /// </summary>
        public string ServerUrl { get; set; } = "http://localhost:5000";

        /// <summary>
        /// Input coordinate mapping mode used to adjust raw mouse coordinates.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public InputCoordinateMode InputMode { get; set; } = InputCoordinateMode.Auto;

        /// <summary>
        /// Manual offset (x,y) applied when InputMode == CustomOffset.
        /// </summary>
        public int CustomOffsetX { get; set; } = 0;
        public int CustomOffsetY { get; set; } = 0;

        /// <summary>
        /// Load settings from a JSON file, returns defaults if file is missing or cannot be parsed.
        /// </summary>
        public static Settings LoadFromFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return new Settings();
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var s = JsonSerializer.Deserialize<Settings>(json, opts);
                return s ?? new Settings();
            }
            catch
            {
                return new Settings();
            }
        }

        /// <summary>
        /// Apply settings to the running game.
        /// - Sets IsFixedTimeStep and TargetElapsedTime when FrameCap is set.
        /// - Optionally updates GraphicsDeviceManager for VSync.
        /// </summary>
        public void ApplyToGame(Game game, GraphicsDeviceManager? gdm = null)
        {
            if (FrameCap.HasValue && FrameCap.Value > 0)
            {
                game.IsFixedTimeStep = true;
                game.TargetElapsedTime = TimeSpan.FromSeconds(1.0 / FrameCap.Value);
            }

            if (gdm != null)
            {
                gdm.SynchronizeWithVerticalRetrace = VSync;
                try { gdm.ApplyChanges(); } catch { /* ignore if not ready in tests */ }
            }
        }
    }
}