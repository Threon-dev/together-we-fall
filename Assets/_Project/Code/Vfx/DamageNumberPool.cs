using System.Collections.Generic;
using TMPro;
using UnityEngine;
using TogetherWeFall.UI;

namespace TogetherWeFall.Vfx
{
    /// <summary>
    /// A fixed set of floating damage numbers, reused forever.
    ///
    /// Canvas labels rather than world-space text. Numbers over a crowd are
    /// read, not inhabited: they should face the player, stay legible at any
    /// camera distance, and never be occluded by the body they belong to. That
    /// is a screen-space problem, and the project already has a canvas.
    ///
    /// Nothing is created after Initialize. When every label is busy the oldest
    /// is taken back, because a crowd dying is precisely the moment an
    /// allocation turns into a visible hitch.
    ///
    /// Everything is in unscaled time so the numbers keep rising through a
    /// hit-stop — the freeze is there to let the player look at exactly this.
    /// </summary>
    public sealed class DamageNumberPool
    {
        /// <summary>Sideways drift, in panel pixels, so stacked hits separate.</summary>
        private const float DriftPixels = 34f;

        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        private readonly List<Number> _numbers = new List<Number>();

        private RectTransform _root;
        private Camera _camera;
        private int _baseFontSize;
        private int _next;

        public bool IsReady => _root != null;

        /// <summary>
        /// The layer never eats a click — the left mouse button is a cast — and
        /// nothing here has to say so, because the canvas it is built on carries
        /// no raycaster at all. See the scene builder.
        /// </summary>
        public void Initialize(int size, int fontSize, RectTransform parent, Camera camera)
        {
            _camera = camera;
            _baseFontSize = fontSize;

            _root = Ugui.Node(parent, "DamageNumbers");

            for (int i = 0; i < size; i++)
            {
                _labels.Add(CreateLabel(_root, fontSize));
                _numbers.Add(Number.Idle);
            }
        }

        public void Add(Vector3 worldPosition, float amount, Color color, float seconds, float rise,
            float scale)
        {
            if (!IsReady)
                return;

            int index = Take();
            TextMeshProUGUI label = _labels[index];

            // Allocated once per number rather than per frame: the text never
            // changes while it is on screen.
            label.text = Mathf.Max(1f, amount).ToString("0");
            label.color = color;
            label.fontSize = Mathf.RoundToInt(_baseFontSize * scale);

            _numbers[index] = new Number
            {
                Remaining = seconds,
                Duration = seconds,
                World = worldPosition,
                Rise = rise,

                // Alternating rather than random: two hits in the same frame go
                // opposite ways, which is exactly when they would overlap.
                Drift = (index % 2 == 0) ? DriftPixels : -DriftPixels
            };

            label.gameObject.SetActive(true);
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (!IsReady)
                return;

            for (int i = 0; i < _numbers.Count; i++)
            {
                Number number = _numbers[i];
                if (number.Remaining <= 0f)
                    continue;

                number.Remaining -= unscaledDeltaTime;

                if (number.Remaining <= 0f)
                {
                    _labels[i].gameObject.SetActive(false);
                    _numbers[i] = Number.Idle;
                    continue;
                }

                Place(_labels[i], number);
                _numbers[i] = number;
            }
        }

        private void Place(TextMeshProUGUI label, in Number number)
        {
            // Zero at birth, one at the end.
            float age = 1f - Mathf.Clamp01(number.Remaining / Mathf.Max(0.01f, number.Duration));

            Vector3 world = number.World + Vector3.up * (number.Rise * age);

            // Through the screen and back into canvas pixels, which is what the
            // panel utility used to do in one call. The camera is the rig's own,
            // so a number lands where the body is on screen even while the
            // camera is being shaken.
            Vector2 point = Ugui.Point(_root, _camera.WorldToScreenPoint(world));

            label.rectTransform.anchoredPosition =
                new Vector2(point.x + number.Drift * age, -point.y);

            // Held solid for the first half, then let go. Fading from the first
            // frame makes a number look faint rather than fleeting.
            label.alpha = 1f - Mathf.Clamp01((age - 0.5f) * 2f);
        }

        public void Clear()
        {
            for (int i = 0; i < _numbers.Count; i++)
            {
                _labels[i].gameObject.SetActive(false);
                _numbers[i] = Number.Idle;
            }
        }

        private int Take()
        {
            for (int i = 0; i < _numbers.Count; i++)
            {
                int index = (_next + i) % _numbers.Count;
                if (_numbers[index].Remaining <= 0f)
                {
                    _next = (index + 1) % _numbers.Count;
                    return index;
                }
            }

            int oldest = _next;
            _next = (_next + 1) % _numbers.Count;
            return oldest;
        }

        /// <summary>
        /// One label, parked and hidden until something is hit.
        ///
        /// Pinned to the layer's top-left corner with a centre pivot, so writing
        /// a position is one assignment and the box is centred on the point
        /// rather than starting at it — which is what the half-size translate it
        /// replaces was for. Fixed at eighty by twenty-four: a damage number is
        /// four digits at most, and a box that never resizes is one the layout
        /// system never looks at again.
        /// </summary>
        private static TextMeshProUGUI CreateLabel(RectTransform parent, int fontSize)
        {
            TextMeshProUGUI label = Ugui.Text(
                parent, "Number", fontSize, Color.white, bold: true);

            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(80f, 24f);

            label.gameObject.SetActive(false);
            return label;
        }

        /// <summary>One live number. Remaining at zero means the slot is free.</summary>
        private struct Number
        {
            public float Remaining;
            public float Duration;
            public Vector3 World;
            public float Rise;
            public float Drift;

            public static Number Idle => default;
        }
    }
}
