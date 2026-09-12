using System;
using UnityEngine;
using UnityEngine.UIElements;
using TogetherWeFall.Loot;

namespace TogetherWeFall.UI
{
    /// <summary>
    /// The box beside the cursor that says what the thing under it is.
    ///
    /// A plain class rather than part of either panel, because both of them
    /// want it: the bag and the vendor's stock are the same question asked in
    /// two rooms. It owns one element and no game state at all — every hover
    /// asks its callback again, which asks the databases. A cached description
    /// would be a second answer to "what is this" that nothing invalidates when
    /// a gem moves.
    ///
    /// One box per panel rather than one per element: it is a property of the
    /// pointer, not of what is under it, and sixty items in a bag would
    /// otherwise be sixty hidden boxes waiting to be shown.
    ///
    /// The rarity colours arrive as a function because the two panels do not
    /// agree about them, and a tooltip that used its own third palette would be
    /// a name in a colour the item beside it is not.
    /// </summary>
    public sealed class TooltipView
    {
        /// <summary>
        /// How far from the cursor the box sits, so it never draws under the
        /// pointer itself.
        /// </summary>
        private const float Offset = 16f;

        /// <summary>
        /// How wide it may get. Wide enough for "anything it kills bursts for
        /// 60% of the damage" on two lines, narrow enough not to cover the bag
        /// it is describing.
        /// </summary>
        private const float MaxWidth = 260f;

        private readonly VisualElement _screen;
        private readonly Func<ItemRarity, Color> _rarityColour;

        private readonly VisualElement _box;
        private readonly Label _title;
        private readonly Label _kind;
        private readonly Label _stats;
        private readonly Label _compare;
        private readonly Label _detail;
        private readonly Label _hint;

        /// <summary>Where the cursor was when the box was asked for.</summary>
        private Vector2 _anchor;

        /// <summary>
        /// The size it had when it was last placed.
        ///
        /// Kept because placing it moves it, moving it raises another geometry
        /// event, and a placement that ran on every one of those would be a
        /// loop that never settles.
        /// </summary>
        private Vector2 _size;

        /// <summary>
        /// Builds the box into the given full-screen wrapper. Add it AFTER the
        /// panel: a tooltip behind the thing it describes helps nobody.
        /// </summary>
        public TooltipView(VisualElement screen, Func<ItemRarity, Color> rarityColour)
        {
            _screen = screen;
            _rarityColour = rarityColour;

            _box = new VisualElement();
            _box.style.position = Position.Absolute;
            _box.style.display = DisplayStyle.None;
            _box.style.maxWidth = MaxWidth;
            _box.style.paddingLeft = 8f;
            _box.style.paddingRight = 8f;
            _box.style.paddingTop = 6f;
            _box.style.paddingBottom = 7f;
            _box.style.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 0.97f);
            _box.style.borderTopWidth = 1f;
            _box.style.borderBottomWidth = 1f;
            _box.style.borderLeftWidth = 1f;
            _box.style.borderRightWidth = 1f;
            _box.style.borderTopColor = new Color(0.30f, 0.33f, 0.40f);
            _box.style.borderBottomColor = new Color(0.30f, 0.33f, 0.40f);
            _box.style.borderLeftColor = new Color(0.30f, 0.33f, 0.40f);
            _box.style.borderRightColor = new Color(0.30f, 0.33f, 0.40f);

            // It follows the pointer, so it must never be what the pointer
            // finds: picking it would mean entering it, which means leaving the
            // item, which means hiding it, which means entering the item again.
            _box.pickingMode = PickingMode.Ignore;

            _title = MakeLabel(14, new Color(0.88f, 0.89f, 0.92f));
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;

            _kind = MakeLabel(11, new Color(0.55f, 0.60f, 0.68f));
            _kind.style.marginBottom = 4f;

            _stats = MakeLabel(12, new Color(0.62f, 0.78f, 0.88f));
            _stats.style.marginBottom = 4f;

            // The comparison in its own colour, because it is the one part that
            // is not about this item at all — it is about the difference.
            _compare = MakeLabel(11, new Color(0.86f, 0.78f, 0.48f));
            _compare.style.marginBottom = 4f;

            _detail = MakeLabel(11, new Color(0.78f, 0.79f, 0.82f));

            _hint = MakeLabel(10, new Color(0.48f, 0.50f, 0.56f));
            _hint.style.marginTop = 4f;
            _hint.style.unityFontStyleAndWeight = FontStyle.Italic;

            // Its size is only known once it has been laid out with this
            // hover's text in it, and where it goes depends on its size.
            _box.RegisterCallback<GeometryChangedEvent>(OnResized);

            _screen.Add(_box);
        }

        /// <summary>
        /// Makes an element describe itself while the pointer is over it.
        ///
        /// The description arrives as a callback rather than as text, so it is
        /// read at the moment of the hover. Text captured when the element was
        /// built would be a snapshot, and a panel is rebuilt far less often
        /// than the world changes.
        /// </summary>
        public void Attach(VisualElement element, Func<ItemTooltip.Text> describe)
        {
            element.RegisterCallback<PointerEnterEvent>(evt => Show(describe(), evt.position));
            element.RegisterCallback<PointerLeaveEvent>(evt => Hide());
        }

        public void Show(in ItemTooltip.Text text, Vector2 pointer)
        {
            if (text.IsEmpty)
            {
                Hide();
                return;
            }

            _title.text = text.Title;
            _title.style.color = _rarityColour(text.Rarity);

            SetLine(_kind, text.Kind);
            SetLine(_stats, text.Stats);
            SetLine(_compare, text.Compare);
            SetLine(_detail, text.Detail);
            SetLine(_hint, text.Hint);

            _anchor = _screen.WorldToLocal(pointer);
            _size = Vector2.zero;

            _box.style.display = DisplayStyle.Flex;
            _box.BringToFront();

            Place();
        }

        public void Hide() => _box.style.display = DisplayStyle.None;

        private Label MakeLabel(int fontSize, Color colour)
        {
            var label = new Label(string.Empty);
            label.style.fontSize = fontSize;
            label.style.color = colour;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.pickingMode = PickingMode.Ignore;

            _box.Add(label);
            return label;
        }

        private static void SetLine(Label label, string text)
        {
            label.text = text;
            label.style.display =
                string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// Puts the box to the right of the cursor, and somewhere else when
        /// there is no room on the right.
        ///
        /// Clamped against the screen rather than against the panel: the panel
        /// is centred and the cursor is often near its edge, so a box that only
        /// ever fitted inside the panel would be one that covered the bag.
        /// </summary>
        private void Place()
        {
            float width = _box.resolvedStyle.width;
            float height = _box.resolvedStyle.height;
            float screenWidth = _screen.resolvedStyle.width;
            float screenHeight = _screen.resolvedStyle.height;

            float x = _anchor.x + Offset;

            if (screenWidth > 0f && x + width > screenWidth)
                x = Mathf.Max(0f, _anchor.x - Offset - width);

            float y = _anchor.y;

            if (screenHeight > 0f && y + height > screenHeight)
                y = Mathf.Max(0f, screenHeight - height);

            _box.style.left = x;
            _box.style.top = y;
        }

        /// <summary>
        /// Places it again once it has been laid out at its new size.
        ///
        /// Guarded on the size actually changing, because placing it moves it
        /// and moving it raises this event again.
        /// </summary>
        private void OnResized(GeometryChangedEvent evt)
        {
            var size = new Vector2(evt.newRect.width, evt.newRect.height);

            if (size == _size)
                return;

            _size = size;
            Place();
        }
    }
}
