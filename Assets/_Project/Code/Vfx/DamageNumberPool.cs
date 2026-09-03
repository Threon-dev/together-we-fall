using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TogetherWeFall.Vfx
{
    /// <summary>
    /// A fixed set of floating damage numbers, reused forever.
    ///
    /// UI Toolkit labels rather than world-space text. Numbers over a crowd are
    /// read, not inhabited: they should face the player, stay legible at any
    /// camera distance, and never be occluded by the body they belong to. That
    /// is a screen-space problem, and the project already has a runtime panel.
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

        private readonly List<Label> _labels = new List<Label>();
        private readonly List<Number> _numbers = new List<Number>();

        private VisualElement _root;
        private Camera _camera;
        private int _baseFontSize;
        private int _next;

        public bool IsReady => _root != null;

        public void Initialize(int size, int fontSize, VisualElement parent, Camera camera)
        {
            _camera = camera;
            _baseFontSize = fontSize;

            _root = new VisualElement();
            _root.style.position = Position.Absolute;
            _root.style.left = 0f;
            _root.style.top = 0f;
            _root.style.right = 0f;
            _root.style.bottom = 0f;

            // The layer must never eat a click. The left mouse button is a cast.
            _root.pickingMode = PickingMode.Ignore;

            parent.Add(_root);

            for (int i = 0; i < size; i++)
            {
                Label label = CreateLabel(fontSize);
                _root.Add(label);

                _labels.Add(label);
                _numbers.Add(Number.Idle);
            }
        }

        public void Add(Vector3 worldPosition, float amount, Color color, float seconds, float rise,
            float scale)
        {
            if (!IsReady)
                return;

            int index = Take();
            Label label = _labels[index];

            // Allocated once per number rather than per frame: the text never
            // changes while it is on screen.
            label.text = Mathf.Max(1f, amount).ToString("0");
            label.style.color = color;
            label.style.fontSize = Mathf.RoundToInt(_baseFontSize * scale);

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

            label.visible = true;
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
                    _labels[i].visible = false;
                    _numbers[i] = Number.Idle;
                    continue;
                }

                Place(_labels[i], number);
                _numbers[i] = number;
            }
        }

        private void Place(Label label, in Number number)
        {
            // Zero at birth, one at the end.
            float age = 1f - Mathf.Clamp01(number.Remaining / Mathf.Max(0.01f, number.Duration));

            Vector3 world = number.World + Vector3.up * (number.Rise * age);
            Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(
                _root.panel, world, _camera);

            label.style.left = point.x + number.Drift * age;
            label.style.top = point.y;

            // Held solid for the first half, then let go. Fading from the first
            // frame makes a number look faint rather than fleeting.
            label.style.opacity = 1f - Mathf.Clamp01((age - 0.5f) * 2f);
        }

        public void Clear()
        {
            for (int i = 0; i < _numbers.Count; i++)
            {
                _labels[i].visible = false;
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

        private static Label CreateLabel(int fontSize)
        {
            var label = new Label
            {
                pickingMode = PickingMode.Ignore,
                visible = false
            };

            label.style.position = Position.Absolute;
            label.style.fontSize = fontSize;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;

            // Centred on the point rather than starting at it, so the number
            // sits over the body instead of beside it.
            label.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));

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
