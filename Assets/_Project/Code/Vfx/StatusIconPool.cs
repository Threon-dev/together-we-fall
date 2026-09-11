using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Vfx
{
    /// <summary>
    /// A fixed set of markers floating over whatever is afflicted.
    ///
    /// The same shape as DamageNumberPool and for the same reasons: UI Toolkit
    /// labels rather than world-space text, because a marker is read rather than
    /// inhabited, and nothing is created after Initialize.
    ///
    /// The difference is what it is showing. A damage number is an event and
    /// lives for half a second; a status is a LEVEL, and the marker has to be
    /// there for as long as the status is. So this is rebuilt every frame from
    /// whatever the simulation currently says, rather than accumulating: the
    /// presenter hands over the afflicted bodies in whatever order it found
    /// them, and every label not claimed this frame is hidden. That is a few
    /// dozen style writes on a frame where a crowd is burning, and it means a
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

        private readonly List<Label> _labels = new List<Label>();
        private readonly StringBuilder _text = new StringBuilder(32);

        private VisualElement _root;
        private Camera _camera;
        private int _used;

        public bool IsReady => _root != null;

        /// <summary>How many bodies can be marked at once. The presenter's budget.</summary>
        public int Capacity => _labels.Count;

        public void Initialize(int size, int fontSize, VisualElement parent, Camera camera)
        {
            _camera = camera;

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
            }
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
        public void Add(Vector3 worldPosition, ushort icons, Color tint)
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

            Label label = _labels[_used++];

            label.text = _text.ToString();
            label.style.color = tint;

            Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(
                _root.panel, worldPosition, _camera);

            label.style.left = point.x;
            label.style.top = point.y - LiftPixels;
            label.visible = true;
        }

        /// <summary>Hides every label no body claimed this frame.</summary>
        public void Finish()
        {
            for (int i = _used; i < _labels.Count; i++)
            {
                if (_labels[i].visible)
                    _labels[i].visible = false;
            }
        }

        public void Clear()
        {
            _used = 0;
            Finish();
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

            // Centred on the point rather than starting at it, so the marker
            // sits over the body instead of beside it.
            label.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));

            return label;
        }
    }
}
