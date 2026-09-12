using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
        /// How wide it is. Wide enough for "anything it kills bursts for 60% of
        /// the damage" on two lines, narrow enough not to cover the bag it is
        /// describing.
        ///
        /// Fixed rather than a ceiling, which is what it was under UI Toolkit:
        /// uGUI has no max-width, and a shrink-to-fit box would mean measuring
        /// the longest line by hand to get the same answer. Every tooltip in the
        /// genre is a fixed column anyway.
        /// </summary>
        private const float Width = 260f;

        private readonly RectTransform _screen;
        private readonly Func<ItemRarity, Color> _rarityColour;

        private readonly RectTransform _box;
        private readonly TextMeshProUGUI _title;
        private readonly TextMeshProUGUI _kind;
        private readonly TextMeshProUGUI _stats;
        private readonly TextMeshProUGUI _compare;
        private readonly TextMeshProUGUI _detail;
        private readonly TextMeshProUGUI _hint;

        /// <summary>Where the cursor was when the box was asked for.</summary>
        private Vector2 _anchor;

        /// <summary>
        /// Builds the box into the given full-screen wrapper. Add it AFTER the
        /// panel: a tooltip behind the thing it describes helps nobody.
        /// </summary>
        public TooltipView(RectTransform screen, Func<ItemRarity, Color> rarityColour)
        {
            _screen = screen;
            _rarityColour = rarityColour;

            // It follows the pointer, so it must never be what the pointer
            // finds: picking it would mean entering it, which means leaving the
            // item, which means hiding it, which means entering the item again.
            Image background = Ugui.Box(
                screen, "Tooltip", new Color(0.04f, 0.05f, 0.07f, 0.97f), picks: false);

            _box = background.rectTransform;

            // A column that grows downward from a fixed width. The height is
            // whatever the lines add up to, which is the one measurement uGUI
            // will do for us.
            Ugui.Column(_box, spacing: 3f, padding: new RectOffset(8, 8, 6, 7));
            Ugui.Fit(_box, horizontal: false);
            Ugui.Size(_box, width: Width);

            _title = MakeLabel(14f, new Color(0.88f, 0.89f, 0.92f), bold: true);
            _kind = MakeLabel(11f, new Color(0.55f, 0.60f, 0.68f));
            _stats = MakeLabel(12f, new Color(0.62f, 0.78f, 0.88f));

            // The comparison in its own colour, because it is the one part that
            // is not about this item at all — it is about the difference.
            _compare = MakeLabel(11f, new Color(0.86f, 0.78f, 0.48f));

            _detail = MakeLabel(11f, new Color(0.78f, 0.79f, 0.82f));
            _hint = MakeLabel(10f, new Color(0.48f, 0.50f, 0.56f), italic: true);

            Ugui.Border(_box, new Color(0.30f, 0.33f, 0.40f), 1f);

            _box.gameObject.SetActive(false);
        }

        /// <summary>
        /// Makes an element describe itself while the pointer is over it.
        ///
        /// The description arrives as a callback rather than as text, so it is
        /// read at the moment of the hover. Text captured when the element was
        /// built would be a snapshot, and a panel is rebuilt far less often
        /// than the world changes.
        /// </summary>
        public void Attach(RectTransform element, Func<ItemTooltip.Text> describe)
        {
            Ugui.On(element, EventTriggerType.PointerEnter,
                data => Show(describe(), data.position));

            Ugui.On(element, EventTriggerType.PointerExit, _ => Hide());
        }

        public void Show(in ItemTooltip.Text text, Vector2 pointer)
        {
            if (text.IsEmpty)
            {
                Hide();
                return;
            }

            _title.text = text.Title;
            _title.color = _rarityColour(text.Rarity);

            SetLine(_kind, text.Kind);
            SetLine(_stats, text.Stats);
            SetLine(_compare, text.Compare);
            SetLine(_detail, text.Detail);
            SetLine(_hint, text.Hint);

            _anchor = Ugui.Point(_screen, pointer);

            _box.gameObject.SetActive(true);
            _box.SetAsLastSibling();

            // Measured now rather than at the end of the frame. The height
            // depends on this hover's text and the placement depends on the
            // height, so a box placed before the rebuild would be placed from
            // the previous item's size — which is what the geometry-event dance
            // this replaces was for.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_box);

            Place();
        }

        public void Hide() => _box.gameObject.SetActive(false);

        private TextMeshProUGUI MakeLabel(
            float fontSize, Color colour, bool bold = false, bool italic = false)
        {
            TextMeshProUGUI label = Ugui.Text(
                _box, "Line", fontSize, colour, TextAlignmentOptions.TopLeft, bold, wrap: true);

            if (italic)
                label.fontStyle = FontStyles.Italic;

            return label;
        }

        private static void SetLine(TextMeshProUGUI label, string text)
        {
            label.text = text;
            label.gameObject.SetActive(!string.IsNullOrEmpty(text));
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
            float height = _box.rect.height;
            float screenWidth = _screen.rect.width;
            float screenHeight = _screen.rect.height;

            float x = _anchor.x + Offset;

            if (screenWidth > 0f && x + Width > screenWidth)
                x = Mathf.Max(0f, _anchor.x - Offset - Width);

            float y = _anchor.y;

            if (screenHeight > 0f && y + height > screenHeight)
                y = Mathf.Max(0f, screenHeight - height);

            Ugui.TopLeft(_box, x, y, Width, height);
        }
    }
}
