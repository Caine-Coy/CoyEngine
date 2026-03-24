using System;
using System.Diagnostics;

namespace CoyEngine.Core
{
    public class FPSCounter
    {
        private float _smoothedFps;
        private bool _hasInitial;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        /// <summary>
        /// Instantaneous FPS measured in the last frame (1/dt)
        /// </summary>
        public float FPS { get; private set; }

        /// <summary>
        /// Smoothed FPS using exponential smoothing
        /// </summary>
        public float SmoothedFPS => _smoothedFps;

        /// <summary>
        /// Smoothing factor [0..1] where closer to 1 keeps history longer (default 0.9)
        /// </summary>
        public float Smoothing { get; set; } = 0.9f;

        /// <summary>
        /// Update using the provided dt (for testing or when IsFixedTimeStep is false).
        /// </summary>
        public void Update(float dt)
        {
            if (dt <= 0f) return;
            FPS = 1f / dt;

            if (!_hasInitial)
            {
                _smoothedFps = FPS;
                _hasInitial = true;
                return;
            }

            _smoothedFps = _smoothedFps * Smoothing + FPS * (1f - Smoothing);
        }

        /// <summary>
        /// Update using real wall-clock time via internal Stopwatch.
        /// Call this instead of Update(dt) when IsFixedTimeStep is true,
        /// since gameTime.ElapsedGameTime will always report the fixed step.
        /// </summary>
        public void UpdateRealTime()
        {
            if (!_stopwatch.IsRunning)
            {
                _stopwatch.Start();
                return;
            }

            float realDt = (float)_stopwatch.Elapsed.TotalSeconds;
            _stopwatch.Restart();

            if (realDt <= 0f) return;
            Update(realDt);
        }

        public void Reset()
        {
            _hasInitial = false;
            _smoothedFps = 0f;
            FPS = 0f;
            _stopwatch.Reset();
        }
    }
}