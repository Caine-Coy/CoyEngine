using CoyEngine.Core;
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#nullable enable
namespace CoyEngine.UI.Components
{
    // A container that slides its single child between a hidden offset and its target position when Visible toggles.
    // Reusable for menus that should slide-in/out from a direction.
    public class SlidePanel : Container
    {
        // Offset to apply to child when hidden (relative to the SlidePanel.Position)
        public Vector2 HiddenOffset { get; set; }

        // Animation duration in seconds
        public float Duration { get; set; } = 0.25f;

        private float _progress = 0f; // 0 hidden, 1 visible
        private bool _prevVisible = false;

        // Local offset of the child relative to this Panel's Position. Initialized lazily because
        // object initializer may set SlidePanel.Position after construction.
        private Vector2 _baseChildLocal = Vector2.Zero;
        private bool _baseLocalInitialized = false;

        // Expose progress for debugging and tests
        public float Progress => _progress;

        public SlidePanel() { }

        public SlidePanel(UIComponent child, Vector2 hiddenOffset, float duration = 0.25f)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            HiddenOffset = hiddenOffset;
            Duration = duration;
            Add(child);

            // Do NOT capture base local here because object initializers may set the
            // SlidePanel.Position *after* constructor returns. We'll initialize lazily
            // during the first Update() call so positions are captured relative to the
            // final SlidePanel.Position.

            // Start hidden by default to ensure slides animate in when Visible flips true
            _progress = 0f;
            // Use the current Visible as the _prevVisible baseline so the first frame will detect changes
            _prevVisible = Visible;
        }

        public override void Update(float dt)
        {
            // If Visible changed since last frame, update the prev flag. When becoming visible,
            // avoid capturing the child's current position if it's currently at the hidden spot
            // (that would store the hidden offset as the base). Instead, if the child is at the
            // hidden position, use a default base of (0,0) so it slides into the panel.Position.
            if (Visible != _prevVisible)
            {
                // Ensure progress starts at the 'from' side of the animation when toggling visibility
                if (Visible) _progress = 0f; else _progress = 1f;

                if (!Visible && Children.Count > 0 && !_baseLocalInitialized)
                {
                    // Becoming hidden for the first time: capture the visible base local and move child to hidden spot
                    var c = Children[0];
                    _baseChildLocal = c.Position - this.Position;
                    _baseLocalInitialized = true;

                    // Place child at hidden offset now
                    c.Position = this.Position + _baseChildLocal + HiddenOffset;
                    c.Update(0f);
                    Bounds = new Rectangle((int)c.Position.X, (int)c.Position.Y, Math.Max(0, c.Bounds.Width), Math.Max(0, c.Bounds.Height));
                }
                else if (Visible && Children.Count > 0 && !_baseLocalInitialized)
                {
                    // Becoming visible for the first time but base not captured; capture local offset relative to panel
                    var c = Children[0];
                    _baseChildLocal = c.Position - this.Position;
                    _baseLocalInitialized = true;
                }
                else if (Visible && Children.Count > 0)
                {
                    // Normal visible transition: if the child is still at hidden, prefer baseLocal zero so it slides to panel.Position
                    var c = Children[0];
                    var expectedHiddenX = this.Position.X + HiddenOffset.X;
                    if (Math.Abs(c.Position.X - expectedHiddenX) < 1e-3f)
                    {
                        _baseChildLocal = Vector2.Zero;
                        _baseLocalInitialized = true;
                    }
                }

                _prevVisible = Visible;
            }

            // Determine target progress
            float target = Visible ? 1f : 0f;
            if (Math.Abs(_progress - target) > 1e-4f)
            {
                if (Duration <= 0f)
                    _progress = target;
                else
                {
                    float step = dt / Duration;
                    if (_progress < target) _progress = Math.Min(1f, _progress + step);
                    else _progress = Math.Max(0f, _progress - step);
                }
                UpdateChildPosition();
            }

            // Update children; make sure child only updates when partially or fully visible
            foreach (var c in Children)
            {
                c.Visible = _progress > 0f || Visible; // visible during animation or fully visible
                if (c.Visible)
                {
                    // Ensure local base offset is initialized relative to the panel's Position
                    if (!_baseLocalInitialized)
                    {
                        _baseChildLocal = c.Position - this.Position;
                        _baseLocalInitialized = true;
                    }

                    // Set child's position to the slided position (panel position + local offset + slide offset)
                    var eased = EaseOutCubic(_progress);
                    var offset = Vector2.Lerp(HiddenOffset, Vector2.Zero, eased);
                            c.Position = this.Position + _baseChildLocal + offset;

                    c.Update(dt);

                    // Mirror child bounds at panel level so debug visuals and hit-tests at top-level reflect the real position
                    Bounds = new Rectangle((int)c.Position.X, (int)c.Position.Y, Math.Max(0, c.Bounds.Width), Math.Max(0, c.Bounds.Height));
                }
                else
                {
                    // If child not visible, collapse our bounds
                    Bounds = new Rectangle(0, 0, 0, 0);
                }
            }

            // If we're fully visible but the child still resides at the hidden spot (possible earlier state),
            // reset base local to zero and snap the child into place so UI becomes visible.
            if (_progress >= 0.9999f && Children.Count > 0)
            {
                var c = Children[0];
                var expectedHiddenX = this.Position.X + HiddenOffset.X;
                if (Math.Abs(c.Position.X - expectedHiddenX) < 0.1f)
                {
                    _baseChildLocal = Vector2.Zero;
                    c.Position = this.Position + _baseChildLocal; // snap to panel position
                    c.Update(0f);
                    Bounds = new Rectangle((int)c.Position.X, (int)c.Position.Y, Math.Max(0, c.Bounds.Width), Math.Max(0, c.Bounds.Height));
                }
            }
        }

        private void UpdateChildPosition()
        {
            if (Children.Count == 0) return;
            var c = Children[0];

            if (!_baseLocalInitialized)
            {
                _baseChildLocal = c.Position - this.Position;
                _baseLocalInitialized = true;
            }

            // ease out cubic for a nicer feel
            float eased = EaseOutCubic(_progress);
            var offset = Vector2.Lerp(HiddenOffset, Vector2.Zero, eased);
            c.Position = this.Position + _baseChildLocal + offset;
            // Adjust bounds for hit testing
            c.Update(0f);
            // Mirror child bounds at panel level so debug visuals reflect position even before the next full Update
            Bounds = new Rectangle((int)c.Position.X, (int)c.Position.Y, Math.Max(0, c.Bounds.Width), Math.Max(0, c.Bounds.Height));
        }

        // Expose base local for diagnostics
        public Vector2 BaseLocal => _baseChildLocal;

        private static float EaseOutCubic(float t) => 1f - (float)Math.Pow(1 - t, 3);
    }
}
