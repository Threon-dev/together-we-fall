using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TogetherWeFall.UI
{
    /// <summary>
    /// The handful of things uGUI makes verbose that UI Toolkit made a one-liner.
    ///
    /// The screens here are built from code, not from prefabs — that did not
    /// change when the panels moved off UI Toolkit. What changed is that a
    /// `VisualElement` with four style writes became a GameObject, a
    /// RectTransform, an Image and a raft of anchor arithmetic. This file is
    /// that arithmetic, once, so the screens stay a list of what they look like
    /// rather than a list of how uGUI wants to hear it.
    ///
    /// Deliberately NOT a widget library: no themes, no base class, no
    /// controls. Every method here exists because two or more screens needed
    /// the same six lines. When a screen needs something once, it writes it
    /// itself.
    /// </summary>
    public static class Ugui
    {
        /// <summary>
        /// Rounded and ringed sprites, generated once and kept.
        ///
        /// uGUI has no border-radius — a rounded box is a nine-sliced sprite
        /// and nothing else. Generating them beats shipping PNGs: the radii are
        /// decided in the same file as the layout, and a sprite nobody asks for
        /// is never made.
        /// </summary>
        private static readonly Dictionary<int, Sprite> Sprites = new Dictionary<int, Sprite>();

        // ─────────────────────────────────────────────────────────────────
        // Elements
        // ─────────────────────────────────────────────────────────────────

        /// <summary>An empty box: a rect and nothing drawn. The container.</summary>
        public static RectTransform Node(Transform parent, string name)
        {
            var node = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, worldPositionStays: false);

            // Stretched by default, which is what a container usually wants and
            // what UI Toolkit would have done with a `VisualElement` that had no
            // size on it. Callers that want otherwise call Place.
            Place(rect, left: 0f, right: 0f, top: 0f, bottom: 0f);

            return rect;
        }

        /// <summary>
        /// A filled box. `radius` above zero rounds the corners; a radius of
        /// half the box's width makes a circle.
        /// </summary>
        public static Image Box(
            Transform parent, string name, Color colour, float radius = 0f, bool picks = true)
        {
            RectTransform rect = Node(parent, name);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = colour;

            // The stand-in for `pickingMode = Ignore` on one element. A child that
            // picks stops its parent from ever seeing the pointer, so decoration
            // — grid squares, a ghost, a highlight — has to say so.
            image.raycastTarget = picks;

            // Even a square box gets a sprite rather than uGUI's implicit white
            // quad, because Image.Type.Filled — how every drain, sweep and level
            // in this game is drawn — needs one to map. A generated four-pixel
            // square costs nothing and means no caller has to remember this.
            image.sprite = Rounded(Mathf.RoundToInt(radius), 0f);
            image.pixelsPerUnitMultiplier = 1f;

            if (radius > 0f)
                image.type = Image.Type.Sliced;

            return image;
        }

        /// <summary>
        /// A ring around the box, drawn over whatever is inside it.
        ///
        /// A child rather than a property, because uGUI has no border either.
        /// Call it LAST: it draws in sibling order, and a fill added after it
        /// covers it.
        ///
        /// Returns the image so a caller that re-colours its border — the skill
        /// bar does, per binding — can do it without finding the child again.
        /// </summary>
        public static Image Border(RectTransform rect, Color colour, float width, float radius = 0f)
        {
            Image image = Box(rect, "Border", colour, picks: false);

            image.sprite = Rounded(Mathf.RoundToInt(radius), width);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;

            // A frame is not a row of content. Without this a layout group on the
            // box would lay the border out beside its children as if it were one
            // — which looks exactly like the border having vanished.
            image.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            return image;
        }

        /// <summary>One line of text. Stretched to the parent unless placed.</summary>
        public static TextMeshProUGUI Text(
            Transform parent,
            string name,
            float size,
            Color colour,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            bool bold = false,
            bool wrap = false)
        {
            RectTransform rect = Node(parent, name);

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = colour;
            text.alignment = alignment;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.raycastTarget = false;

            // Off by default: almost every label in this game is one short line
            // in a box sized for it, and a wrap there means a word vanished
            // below the box rather than a line broke nicely. The ones that do
            // wrap — notes, tooltips, an item's name in its own square — ask.
            text.textWrappingMode = wrap
                ? TextWrappingModes.Normal
                : TextWrappingModes.NoWrap;

            return text;
        }

        /// <summary>
        /// A clickable box with a label in it, sized to the words.
        ///
        /// uGUI has a Button component but no button: what it draws is whatever
        /// Image and text somebody parents to it. This is that, once.
        /// </summary>
        public static UnityEngine.UI.Button Button(
            Transform parent, string name, string label, Action onClick)
        {
            Image background = Box(parent, name, new Color(0.16f, 0.18f, 0.22f), radius: 4f);
            Border(background.rectTransform, new Color(0.34f, 0.37f, 0.44f), 1f, radius: 4f);

            // Qualified, because this method is called Button too and the compiler
            // would otherwise have to guess which one a bare `Button` means.
            var button = background.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => onClick());

            TextMeshProUGUI text = Text(
                background.rectTransform, "Label", 12f, new Color(0.86f, 0.88f, 0.92f));
            text.text = label;

            // The button is as big as its label plus a margin, which is the one
            // thing UI Toolkit did for free and uGUI needs three components for.
            Row(background.rectTransform, padding: new RectOffset(10, 10, 4, 5));
            Fit(background.rectTransform);

            return button;
        }

        /// <summary>
        /// Dims a subtree, the way `style.opacity` did. One CanvasGroup, made the
        /// first time something is faded and reused after.
        /// </summary>
        public static void Fade(RectTransform rect, float alpha)
        {
            var group = rect.GetComponent<CanvasGroup>();

            if (group == null)
                group = rect.gameObject.AddComponent<CanvasGroup>();

            group.alpha = alpha;
        }

        /// <summary>
        /// Registers a pointer callback, the `RegisterCallback&lt;T&gt;` of uGUI.
        ///
        /// Through the built-in EventTrigger rather than a component per kind of
        /// element: it already carries every handler interface uGUI has, and the
        /// panels only ever want "tell me when this is pressed or hovered".
        /// </summary>
        public static void On(
            RectTransform rect, EventTriggerType type, Action<PointerEventData> handler)
        {
            var trigger = rect.GetComponent<EventTrigger>();

            if (trigger == null)
                trigger = rect.gameObject.AddComponent<EventTrigger>();

            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => handler(data as PointerEventData));
            trigger.triggers.Add(entry);
        }

        /// <summary>
        /// Destroys everything inside, now rather than at the end of the frame.
        ///
        /// `Destroy` is deferred, so a panel that clears and refills itself in
        /// one call would leave the old children in place for a frame — and a
        /// layout group would lay both sets out. Unparenting first is what makes
        /// the clear immediate as far as everything else is concerned.
        /// </summary>
        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                child.SetParent(null, worldPositionStays: false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Placement
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Absolute placement, written as the four edges UI Toolkit's
        /// `position: absolute` took, plus an explicit size for the axes that
        /// are pinned on one side only.
        ///
        /// The point of the signature is that a port reads like the thing it
        /// replaced: `left = 0, right = 0, bottom = 12` was three style writes
        /// and is one call here, instead of five lines of anchor, pivot,
        /// anchoredPosition and sizeDelta that are wrong in a way nobody spots
        /// by reading.
        ///
        /// Per axis: both edges given stretches between them; one edge plus a
        /// size pins to that edge; a size alone centres. NaN — the default —
        /// means "not given".
        /// </summary>
        public static void Place(
            RectTransform rect,
            float left = float.NaN,
            float right = float.NaN,
            float top = float.NaN,
            float bottom = float.NaN,
            float width = float.NaN,
            float height = float.NaN)
        {
            Axis(left, right, width, out float minX, out float maxX,
                out float pivotX, out float posX, out float sizeX);

            // Flipped on purpose: uGUI's y grows upward, so the anchor-zero edge
            // is the bottom one. Writing `top:` and getting a distance from the
            // bottom is the single easiest mistake to make in this file.
            Axis(bottom, top, height, out float minY, out float maxY,
                out float pivotY, out float posY, out float sizeY);

            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.pivot = new Vector2(pivotX, pivotY);
            rect.anchoredPosition = new Vector2(posX, posY);
            rect.sizeDelta = new Vector2(sizeX, sizeY);
        }

        /// <summary>
        /// Places a child by its top-left corner, in pixels from the parent's
        /// top-left, with y growing DOWNWARD.
        ///
        /// The whole inventory thinks in those coordinates — a grid of cells, an
        /// item at (2,3), a ghost under the cursor — and so did the UI Toolkit
        /// code it came from. Rather than flip every one of those sums into
        /// uGUI's y-up, the flip happens once, here.
        /// </summary>
        public static void TopLeft(
            RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Where a screen point falls inside a rect, in the same top-left,
        /// y-down pixels <see cref="TopLeft"/> takes. The `WorldToLocal` of uGUI.
        ///
        /// Pivot-independent on purpose: half the rects in these panels are
        /// centred by a layout group and the other half are pinned by a corner,
        /// and a conversion that only worked for one of those would be wrong
        /// exactly where a drag lands.
        /// </summary>
        public static Vector2 Point(RectTransform rect, Vector2 screenPoint)
        {
            // Null camera: these are screen-space-overlay canvases, where screen
            // pixels are the canvas's own coordinate system scaled by the scaler.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rect, screenPoint, null, out Vector2 local);

            Rect bounds = rect.rect;
            return new Vector2(local.x - bounds.xMin, bounds.yMax - local.y);
        }

        /// <summary>Whether a screen point is inside a rect. `worldBound.Contains`.</summary>
        public static bool Contains(RectTransform rect, Vector2 screenPoint) =>
            RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);

        // ─────────────────────────────────────────────────────────────────
        // Layout
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Stacks the children downward, each as wide as the column and as tall
        /// as it needs — which is what a flexbox column did by default.
        /// </summary>
        public static VerticalLayoutGroup Column(
            RectTransform rect, float spacing = 0f, RectOffset padding = null)
        {
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            Configure(group, spacing, padding);
            return group;
        }

        /// <summary>Lays the children out left to right. A flexbox row.</summary>
        public static HorizontalLayoutGroup Row(
            RectTransform rect, float spacing = 0f, RectOffset padding = null)
        {
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            Configure(group, spacing, padding);
            return group;
        }

        private static void Configure(
            HorizontalOrVerticalLayoutGroup group, float spacing, RectOffset padding)
        {
            group.spacing = spacing;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;

            if (padding != null)
                group.padding = padding;
        }

        /// <summary>
        /// Makes a container as big as what is inside it — the auto-sizing a
        /// flexbox element had without being asked.
        ///
        /// It only works when every child reports a size. Text reports its own;
        /// a plain box does not and needs <see cref="Size"/>, or the group
        /// collapses it to nothing.
        /// </summary>
        public static void Fit(RectTransform rect, bool horizontal = true, bool vertical = true)
        {
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();

            fitter.horizontalFit = horizontal
                ? ContentSizeFitter.FitMode.PreferredSize
                : ContentSizeFitter.FitMode.Unconstrained;

            fitter.verticalFit = vertical
                ? ContentSizeFitter.FitMode.PreferredSize
                : ContentSizeFitter.FitMode.Unconstrained;
        }

        /// <summary>
        /// Tells a layout group how big this child is, and sizes it that way when
        /// no group is listening. NaN leaves an axis alone.
        ///
        /// `grow` is flex-grow: above zero the child takes a share of whatever
        /// room is left over.
        /// </summary>
        public static LayoutElement Size(
            RectTransform rect,
            float width = float.NaN,
            float height = float.NaN,
            float grow = 0f)
        {
            var element = rect.GetComponent<LayoutElement>();

            if (element == null)
                element = rect.gameObject.AddComponent<LayoutElement>();

            if (!float.IsNaN(width))
            {
                element.preferredWidth = width;
                element.minWidth = width;
                rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            }

            if (!float.IsNaN(height))
            {
                element.preferredHeight = height;
                element.minHeight = height;
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            }

            element.flexibleWidth = grow;
            return element;
        }

        /// <summary>
        /// One axis of <see cref="Place"/>. `low` is the edge at anchor zero —
        /// left, or bottom.
        /// </summary>
        private static void Axis(
            float low, float high, float length,
            out float anchorMin, out float anchorMax,
            out float pivot, out float position, out float size)
        {
            bool hasLow = !float.IsNaN(low);
            bool hasHigh = !float.IsNaN(high);
            float span = float.IsNaN(length) ? 0f : length;

            if (hasLow && hasHigh)
            {
                // Stretched. sizeDelta is then an inset rather than a size, which
                // is why both numbers come out negative.
                anchorMin = 0f;
                anchorMax = 1f;
                pivot = 0.5f;
                position = (low - high) * 0.5f;
                size = -(low + high);
                return;
            }

            if (hasLow)
            {
                anchorMin = anchorMax = 0f;
                pivot = 0f;
                position = low;
                size = span;
                return;
            }

            if (hasHigh)
            {
                anchorMin = anchorMax = 1f;
                pivot = 1f;
                position = -high;
                size = span;
                return;
            }

            anchorMin = anchorMax = 0.5f;
            pivot = 0.5f;
            position = 0f;
            size = span;
        }

        // ─────────────────────────────────────────────────────────────────
        // Sprites
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// A nine-sliced rounded rectangle: solid when `border` is zero, a ring
        /// of that many pixels when it is not.
        ///
        /// Generated at the smallest size the slicing needs — two pixels of
        /// middle plus a corner on each side — because the middle is stretched
        /// to whatever box it lands in and everything that matters is in the
        /// corners. A 96-pixel orb costs a 98-pixel texture and nothing else
        /// does.
        /// </summary>
        public static Sprite Rounded(int radius, float border)
        {
            int width = Mathf.Max(0, Mathf.CeilToInt(border));
            int key = radius * 1000 + width;

            if (Sprites.TryGetValue(key, out Sprite cached) && cached != null)
                return cached;

            // The corner slice has to hold both the curve and the ring, or
            // nine-slicing would stretch part of one of them.
            int inset = Mathf.Max(Mathf.Max(radius, width), 1);
            int size = inset * 2 + 2;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = $"Ugui.Rounded({radius},{width})",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float outer = Distance(x + 0.5f, y + 0.5f, size, radius);

                    // One pixel of coverage either side of the edge. Without it a
                    // circle at this size reads as a polygon.
                    float alpha = Mathf.Clamp01(0.5f - outer);

                    if (width > 0)
                        alpha -= Mathf.Clamp01(0.5f - (outer + width));

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f,
                extrude: 0,
                // FullRect, not Tight: a sliced sprite needs all nine pieces to
                // exist, and Tight throws away the transparent middle of a ring.
                meshType: SpriteMeshType.FullRect,
                border: new Vector4(inset, inset, inset, inset));

            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.DontSave;

            Sprites[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Signed distance from a point to the edge of a rounded square:
        /// negative inside, zero on the edge. The usual formula — what makes it
        /// worth having is that one number gives both the shape and, offset by
        /// the border width, the ring inside it.
        /// </summary>
        private static float Distance(float x, float y, float size, float radius)
        {
            float half = size * 0.5f;
            float corner = Mathf.Min(radius, half);

            float dx = Mathf.Abs(x - half) - (half - corner);
            float dy = Mathf.Abs(y - half) - (half - corner);

            float outX = Mathf.Max(dx, 0f);
            float outY = Mathf.Max(dy, 0f);

            return Mathf.Sqrt(outX * outX + outY * outY)
                   + Mathf.Min(Mathf.Max(dx, dy), 0f)
                   - corner;
        }
    }
}
