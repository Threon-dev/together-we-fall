using System.Collections.Generic;
using UnityEngine;

namespace TogetherWeFall.Vfx
{
    /// <summary>
    /// A fixed set of line renderers, reused forever.
    ///
    /// Both effects this project draws are lines: a chain link is two points,
    /// and a blast is a ring of them. One pool serves both, which is why there
    /// is no second pool and no second lifetime to get wrong.
    ///
    /// Nothing is ever created after Initialize. When every line is busy the
    /// oldest one is taken back rather than a new one made — under a chain
    /// reaction that is the difference between dropping a frame and dropping the
    /// oldest flash nobody was looking at any more. Effects that fire dozens of
    /// times a second are exactly where an allocation becomes a hitch.
    ///
    /// Everything runs on unscaled time, so the lines keep animating through a
    /// hit-stop. Freezing the effect that caused the freeze would be an odd way
    /// to sell it.
    /// </summary>
    public sealed class VfxLinePool
    {
        /// <summary>Points around a blast ring. Enough to read as a circle at any size.</summary>
        private const int RingSegments = 28;

        private readonly List<LineRenderer> _renderers = new List<LineRenderer>();
        private readonly List<Line> _lines = new List<Line>();

        private MaterialPropertyBlock _properties;
        private Vector3[] _ringPoints;
        private int _next;

        public void Initialize(int size, Material material, Transform parent)
        {
            _properties = new MaterialPropertyBlock();
            _ringPoints = new Vector3[RingSegments + 1];

            for (int i = 0; i < size; i++)
                _renderers.Add(CreateRenderer(i, material, parent));

            for (int i = 0; i < size; i++)
                _lines.Add(Line.Idle);
        }

        /// <summary>A straight line between two points, fading out by thinning.</summary>
        public void AddLink(Vector3 from, Vector3 to, Color color, float seconds, float width)
        {
            int index = Take();
            LineRenderer renderer = _renderers[index];

            renderer.positionCount = 2;
            renderer.SetPosition(0, from);
            renderer.SetPosition(1, to);

            Apply(renderer, color);

            _lines[index] = new Line
            {
                Remaining = seconds,
                Duration = seconds,
                Width = width,
                Radius = 0f
            };

            renderer.enabled = true;
        }

        /// <summary>A ring that grows to the blast radius while thinning away.</summary>
        public void AddRing(Vector3 center, float radius, Color color, float seconds, float width)
        {
            int index = Take();
            LineRenderer renderer = _renderers[index];

            renderer.positionCount = _ringPoints.Length;
            Apply(renderer, color);

            _lines[index] = new Line
            {
                Remaining = seconds,
                Duration = seconds,
                Width = width,
                Radius = radius,
                Center = center
            };

            WriteRing(renderer, center, 0f);
            renderer.enabled = true;
        }

        /// <summary>
        /// Ages every live line. Width carries the fade rather than alpha: the
        /// line material may or may not be transparent depending on which shader
        /// the project ends up with, and a line thinning to nothing reads the
        /// same either way.
        /// </summary>
        public void Tick(float unscaledDeltaTime)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                Line line = _lines[i];
                if (line.Remaining <= 0f)
                    continue;

                line.Remaining -= unscaledDeltaTime;

                if (line.Remaining <= 0f)
                {
                    _renderers[i].enabled = false;
                    _lines[i] = Line.Idle;
                    continue;
                }

                // One at birth, zero at the end.
                float life = Mathf.Clamp01(line.Remaining / Mathf.Max(0.01f, line.Duration));

                _renderers[i].widthMultiplier = line.Width * life;

                if (line.Radius > 0f)
                {
                    // Grows as it fades, so the ring is widest at the moment it
                    // disappears — a shockwave leaving, not a bubble popping.
                    WriteRing(_renderers[i], line.Center, line.Radius * (1f - life));
                }

                _lines[i] = line;
            }
        }

        /// <summary>Stops everything at once. For a scene teardown, not for gameplay.</summary>
        public void Clear()
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                _renderers[i].enabled = false;
                _lines[i] = Line.Idle;
            }
        }

        private int Take()
        {
            // An idle one if there is one.
            for (int i = 0; i < _lines.Count; i++)
            {
                int index = (_next + i) % _lines.Count;
                if (_lines[index].Remaining <= 0f)
                {
                    _next = (index + 1) % _lines.Count;
                    return index;
                }
            }

            // Everything is busy: take the one that has been going longest.
            // Growing the pool here would mean an allocation in the middle of
            // the busiest frame of the fight.
            int oldest = _next;
            _next = (_next + 1) % _lines.Count;
            return oldest;
        }

        private void Apply(LineRenderer renderer, Color color)
        {
            renderer.startColor = color;
            renderer.endColor = color;

            // Set through a property block as well: whether the vertex colours
            // above do anything depends on the shader the line material ended up
            // with, and one of the two paths will tint it.
            _properties.SetColor(BaseColorId, color);
            _properties.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_properties);
        }

        private void WriteRing(LineRenderer renderer, Vector3 center, float radius)
        {
            for (int i = 0; i < _ringPoints.Length; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;

                _ringPoints[i] = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y,
                    center.z + Mathf.Sin(angle) * radius);
            }

            renderer.SetPositions(_ringPoints);
        }

        private static LineRenderer CreateRenderer(int index, Material material, Transform parent)
        {
            var lineObject = new GameObject($"VfxLine_{index}");
            lineObject.transform.SetParent(parent, worldPositionStays: false);

            LineRenderer renderer = lineObject.AddComponent<LineRenderer>();
            renderer.sharedMaterial = material;
            renderer.useWorldSpace = true;
            renderer.alignment = LineAlignment.View;
            renderer.numCapVertices = 0;
            renderer.numCornerVertices = 0;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;

            return renderer;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>One live line. Remaining at zero means the slot is free.</summary>
        private struct Line
        {
            public float Remaining;
            public float Duration;
            public float Width;
            public float Radius;
            public Vector3 Center;

            public static Line Idle => default;
        }
    }
}
