using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.UI;

namespace TogetherWeFall.Vfx
{
    /// <summary>
    /// A fixed set of markers floating over whatever is afflicted.
    ///
    /// The same shape as DamageNumberPool and for the same reasons: canvas
    /// labels rather than world-space text, because a marker is read rather than
    /// inhabited, and nothing is created after Initialize.
    ///
    /// The difference is what it is showing. A damage number is an event and
    /// lives for half a second; a status is a LEVEL, and the marker has to be
    /// there for as long as the status is. So this is rebuilt every frame from
    /// whatever the simulation currently says, rather than accumulating: the
    /// presenter hands over the afflicted bodies in whatever order it found
    /// them, and every label not claimed this frame is hidden. That is a few
    /// dozen writes on a frame where a crowd is burning, and it means a
    /// status that ends in any way at all — expired, consumed, the body killed,
    /// the body returned to the pool — takes its marker with it without anything
    /// having to say so.
    ///
    /// Budgeted, like everything else that a wave can produce hundreds of. A
    /// hundred burning enemies are a hundred markers nobody can read; the
    /// presenter picks which ones to hand over and this simply stops when it
    /// runs out of labels.
    /// </summary>
    public sealed class StatusIconPool
    {
        /// <summary>Panel pixels above the body's own point, so it clears the damage numbers.</summary>
        private const float LiftPixels = 26f;

        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        private readonly StringBuilder _text = new StringBuilder(32);

        private RectTransform _root;
        private Camera _camera;
        private int _used;

        public bool IsReady => _root != null;

        /// <summary>How many bodies can be marked at once. The presenter's budget.</summary>
        public int Capacity => _labels.Count;

        /// <summary>
        /// The layer never eats a click — the left mouse button is a cast — and
        /// nothing here has to say so, because the canvas it is built on carries
        /// no raycaster at all. See the scene builder.
        /// </summary>
        public void Initialize(int size, int fontSize, RectTransform parent, Camera camera)
        {
            _camera = camera;

            _root = Ugui.Node(parent, "StatusIcons");

            for (int i = 0; i < size; i++)
                _labels.Add(CreateLabel(_root, fontSize));
        }

        /// <summary>Starts a frame. Everything not added before Finish is hidden.</summary>
        public void Begin()
        {
            _used = 0;
        }

        /// <summary>
        /// Marks one body with everything on it.
        ///
        /// The mask is turned into words here rather than by the caller, because
        /// what a status is called is a fact about the status — StatusEffects
        /// owns it, and the alternative is a second table of names beside the
        /// drawing code.
        /// </summary>
        public void Add(Vector3 worldPosition, uint icons, Color tint)
        {
            if (!IsReady || icons == 0 || _used >= _labels.Count)
                return;

            _text.Clear();

            for (int i = 1; i < StatusMask.Count; i++)
            {
                var type = (StatusEffectType)i;
                if (!StatusMask.Has(icons, type))
                    continue;

                if (_text.Length > 0)
                    _text.Append(' ');

                _text.Append(StatusEffects.GlyphOf(type));
            }

            if (_text.Length == 0)
                return;

            TextMeshProUGUI label = _labels[_used++];

            label.text = _text.ToString();
            label.color = tint;

            Vector2 point = Ugui.Point(_root, _camera.WorldToScreenPoint(worldPosition));

            // Minus the lift in the panel's y-down pixels is plus it in uGUI's,
            // which is the whole of the difference between the two.
            label.rectTransform.anchoredPosition = new Vector2(point.x, -point.y + LiftPixels);
            label.gameObject.SetActive(true);
        }

        /// <summary>Hides every label no body claimed this frame.</summary>
        public void Finish()
        {
            for (int i = _used; i < _labels.Count; i++)
            {
                if (_labels[i].gameObject.activeSelf)
                    _labels[i].gameObject.SetActive(false);
            }
        }

        public void Clear()
        {
            _used = 0;
            Finish();
        }

        /// <summary>
        /// One marker, built exactly the way a damage number is: pinned to the
        /// layer's corner, centred on its own point, a fixed box the layout
        /// system need never measure.
        /// </summary>
        private static TextMeshProUGUI CreateLabel(RectTransform parent, int fontSize)
        {
            TextMeshProUGUI label = Ugui.Text(
                parent, "Status", fontSize, Color.white, bold: true);

            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(96f, 22f);

            label.gameObject.SetActive(false);
            return label;
        }
    }
}
