using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using TogetherWeFall.Equipment;
using TogetherWeFall.Inventory;
using TogetherWeFall.Lobby;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;
using TogetherWeFall.Skills;

namespace TogetherWeFall.UI
{
    /// <summary>
    /// The inventory and character sheet panel: a grid of cells, items that
    /// cover several of them, and drag and drop between the grid and the
    /// equipment slots.
    ///
    /// It owns no game state whatsoever. Every time it draws, it reads the
    /// player character out of ECS — stats, slots, the bag container and its
    /// cells — and renders what it found. Dropping an item does not move
    /// anything either; it appends an InventoryPlacementRequest and waits to be
    /// told what happened, exactly like a client would across a wire. The panel
    /// cannot show an item somewhere the host refused to put it, because it has
    /// nowhere to remember such a thing.
    ///
    /// The green and red squares under a dragged item are the one place this
    /// file duplicates a rule, and it does not: the prediction calls the same
    /// GridFit the host calls, read-only. A prediction that disagreed with the
    /// answer would be worse than no prediction at all.
    ///
    /// Rebuilt only when the data behind it changes, tracked by the stat version
    /// and the contents of the cell buffer — and never while a drag is in
    /// flight, because rebuilding would destroy the element under the cursor.
    /// </summary>
    public sealed class InventoryUI : MonoBehaviour
    {
        private const float CellGap = 2f;

        /// <summary>
        /// The smallest cell that is still comfortable to read and click.
        ///
        /// A target, not a floor. A floor that wins over fitting on screen is
        /// the bug it was meant to prevent, wearing a different hat: the cells
        /// stay a comfortable size and the bottom rows go past the bottom of the
        /// monitor. So when both cannot be had, fitting wins and this becomes a
        /// warning instead.
        /// </summary>
        private const float ComfortableCellSize = 22f;

        /// <summary>
        /// The hard floor. Below this the grid is not a grid any more, and the
        /// answer is a smaller bag rather than a smaller cell.
        /// </summary>
        private const float MinCellSize = 12f;

        /// <summary>
        /// The floor for a slot box, lowered when the sockets and the hotkeys
        /// arrived.
        ///
        /// Ten slot boxes, four stat rows and a row of hotkeys do not fit into
        /// the 338 logical pixels a 32:9 screen offers at 24 pixels a box. The
        /// same rule as the cells: fitting beats comfort, because a comfortable
        /// box below the bottom edge is not comfortable, it is gone.
        /// </summary>
        private const float MinSlotBoxHeightFloor = 18f;

        /// <summary>
        /// Above this a small bag on a large screen turns into a wall of tiles.
        /// </summary>
        private const float MaxCellSize = 52f;

        /// <summary>Panel padding, left plus right.</summary>
        private const float HorizontalChrome = 24f;

        /// <summary>
        /// Everything above and below the grid inside the panel: padding, the
        /// "Bag" and "Sockets" headings, two rows of socket cells and the
        /// message line. Measured rather than derived because the alternative is
        /// asking the layout engine mid-layout.
        /// </summary>
        private const float VerticalChrome = 147f;

        private const float ColumnGap = 16f;

        /// <summary>
        /// How tall one equipment slot box may be, and how short it may get.
        ///
        /// Adaptive for the same reason the cells are: ten slots two to a line
        /// is five rows, and five rows at a fixed height is the tallest thing in
        /// the panel. On an ultrawide, where the whole screen is 338 logical
        /// pixels tall, a fixed 46 would put the belt slot past the bottom edge
        /// — which is exactly the failure the grid already had to stop having.
        /// </summary>
        private const float MaxSlotBoxHeight = 46f;
        private const float MinSlotBoxHeight = 24f;

        /// <summary>
        /// Everything in the sidebar that is not a slot box: panel padding,
        /// three headings, the section margins, the four rows of stats and the
        /// row of hotkeys.
        /// </summary>
        private const float SidebarChrome = 225f;

        /// <summary>Ten slots, two to a line.</summary>
        private const int SlotRows = (EquipmentSlots.Count + 1) / 2;

        /// <summary>
        /// How many squares are stacked down each side of the character.
        ///
        /// Five and five, weapons included, which is exactly the ten slots a
        /// character has — and five rows is what the sidebar's height budget
        /// was already measured for when they were a two-column list.
        /// </summary>
        private const int DollRails = 5;

        /// <summary>Space between the squares, and between them and the portrait.</summary>
        private const float DollGap = 4f;

        /// <summary>Side of one socket cell, and the gap between linked ones.</summary>
        private const float SocketCellSize = 20f;

        private const float LinkBarWidth = 6f;

        /// <summary>Height of one hotkey box on the skill bar.</summary>
        private const float BarBoxHeight = 34f;

        /// <summary>How many hotkeys there are. SkillLoadoutSystem hands out these.</summary>
        private const int BarSlotCount = 4;

        /// <summary>
        /// Height of one line in the character column.
        ///
        /// A number rather than "whatever the text needs", because the two-per
        /// -line flow has to know where the next line starts before the text has
        /// been measured.
        /// </summary>
        private const float StatRowHeight = 17f;

        /// <summary>
        /// How much of the screen the panel may take. Not all of it: a panel
        /// flush against the edges reads as a broken full-screen mode rather
        /// than as a window.
        /// </summary>
        private const float ScreenFraction = 0.9f;

        [SerializeField] private Canvas _canvas;

        [Tooltip("Width of the character column beside the bag. The bag takes " +
                 "whatever is left, so this is the one number that decides how " +
                 "the two share a landscape screen.")]
        [SerializeField, Range(180, 420)] private int _sidebarWidth = 280;

        [Tooltip("Optional. The camera that draws the character between the " +
                 "equipment squares. Without it the frame is simply empty and " +
                 "everything else works.")]
        [SerializeField] private CharacterPortrait _portrait;

        [Tooltip("Item art by item id. Written by the scene build from every item " +
                 "that has an icon; an item missing here is drawn by its name.")]
        [SerializeField] private ItemIcon[] _icons = System.Array.Empty<ItemIcon>();

        /// <summary>
        /// Id and sprite rather than the ItemDefinition itself: a definition
        /// references its skills, and a skill its effect prefabs, so listing the
        /// definitions would load every one of those with the scene.
        /// </summary>
        [System.Serializable]
        public struct ItemIcon
        {
            public int ItemId;
            public Sprite Sprite;
        }

        private readonly Dictionary<int, Sprite> _iconsById = new Dictionary<int, Sprite>();

        private PlayerInputReader _input;
        private EntityManager _entityManager;
        private EntityQuery _characterQuery;
        private EntityQuery _itemDatabaseQuery;
        private EntityQuery _skillDatabaseQuery;
        private EntityQuery _itemSetDatabaseQuery;

        private int _playerId;
        private bool _hasWorld;
        private bool _visible;
        private int _lastSignature;
        private int _builtWidth;
        private int _builtHeight;
        private float _builtCellSize;
        private float _cellSize = MaxCellSize;
        private float _slotBoxHeight = MaxSlotBoxHeight;
        private Vector2 _lastRootSize;

        private RectTransform _screen;
        private RectTransform _panel;
        private RectTransform _sidebar;
        private RectTransform _bagColumn;
        private RectTransform _dollRoot;
        private RectTransform _socketList;
        private RectTransform _barList;
        private RectTransform _statsList;
        private RectTransform _slotList;
        private RectTransform _gridRoot;
        private RectTransform _cellLayer;
        private RectTransform _itemLayer;
        private Image _highlight;
        private Image _ghost;
        private TextMeshProUGUI _ghostLabel;
        private TextMeshProUGUI _message;

        /// <summary>
        /// The ghost's frame, which is a child rather than a style in uGUI — so
        /// re-colouring it for a different item means replacing it.
        /// </summary>
        private Image _ghostBorder;
        private Image _ghostIcon;

        /// <summary>
        /// Where the next stat row goes, while the stats are being filled.
        ///
        /// uGUI has no flex-wrap, so the two-per-line the sidebar wants is a
        /// cursor rather than a style. Fields rather than locals because the rows
        /// are added by four different methods — the purse, the keystone, the
        /// sets and the stats themselves — and they all share one flow.
        /// </summary>
        private float _statsY;
        private bool _statsRight;

        /// <summary>
        /// The frame the character is drawn in, and the image inside it.
        ///
        /// Built once and only ever moved, unlike the squares around them: the
        /// portrait is a texture that does not change when a ring does, and
        /// throwing a RawImage away on every refresh would mean rebinding the
        /// render target sixty times a bag.
        /// </summary>
        private RectTransform _portraitFrame;
        private RawImage _portraitImage;

        /// <summary>What says what a thing is while the pointer is over it.</summary>
        private TooltipView _tooltips;

        private readonly List<SlotTarget> _slotTargets = new List<SlotTarget>();
        private readonly List<SocketTarget> _socketTargets = new List<SocketTarget>();
        private readonly List<BarTarget> _barTargets = new List<BarTarget>();

        /// <summary>
        /// Reused across rebuilds. A multi-cell item appears in the cell buffer
        /// once per cell it covers, and this is what turns those back into one
        /// element each.
        /// </summary>
        private readonly HashSet<Entity> _seen = new HashSet<Entity>();

        private Drag _drag;

        /// <summary>
        /// Whether the panel is currently taking the player's clicks.
        ///
        /// Read by the bootstrap and handed to PlayerActionPublisher, so that
        /// dragging an item does not also fire the skill bound to the same
        /// button. The panel does not reach into the publisher itself — one
        /// direction, one wiring point, no new bridge.
        /// </summary>
        public bool IsCapturingInput => _visible;

        public void Initialize(PlayerInputReader input, int playerId)
        {
            _input = input;
            _playerId = playerId;
            _lastSignature = int.MinValue;

            _iconsById.Clear();
            foreach (ItemIcon icon in _icons)
            {
                if (icon.Sprite != null)
                    _iconsById[icon.ItemId] = icon.Sprite;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError(
                    $"[{nameof(InventoryUI)}] ECS world is unavailable — the inventory panel " +
                    "has nothing to read.", this);
                return;
            }

            _entityManager = world.EntityManager;

            _characterQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerCharacter>(),
                ComponentType.ReadOnly<PlayerStats>(),
                ComponentType.ReadOnly<CarriedBag>());

            _itemDatabaseQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ItemDatabase>());

            // Needed to say what a socket casts. The panel never decides with
            // it — it asks the same GemSockets the host asks.
            _skillDatabaseQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SkillDatabase>());

            // Optional, like the skill database: a scene baked before sets
            // existed draws exactly what it drew before.
            _itemSetDatabaseQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ItemSetDatabase>());

            _hasWorld = true;
        }

        private void Update()
        {
            if (!_hasWorld || _input == null || !EnsureTree())
                return;

            if (_input.WasInventoryTogglePressed())
                Toggle();

            if (!_visible)
                return;

            WatchScreenSize();

            // Driven from here rather than from pointer events on the dragged
            // element, which is what UI Toolkit's pointer capture was for. uGUI
            // has its own drag protocol, but it only starts firing after the
            // cursor has travelled a few pixels — so a slow, short drag would
            // silently do nothing. A position read every frame has no threshold.
            if (_drag.Active)
                StepDrag();

            // Never while a drag is in flight: a rebuild destroys the element
            // the pointer is holding.
            if (!_drag.Active)
                RefreshIfChanged();
        }

        private void Toggle()
        {
            _visible = !_visible;
            _screen.gameObject.SetActive(_visible);

            // The portrait camera costs a render pass, so it runs only while
            // somebody is looking at it — and the render target is made on the
            // first look rather than on the first frame of the scene, which is
            // why the texture is hung here instead of when the frame was built.
            if (_portrait != null)
            {
                _portrait.SetShown(_visible);

                if (_visible && _portraitImage != null)
                    _portraitImage.texture = _portrait.Texture;
            }

            if (!_visible)
                CancelDrag();

            // Force a rebuild on the next visible frame: the data may well have
            // moved on while the panel was closed.
            _lastSignature = int.MinValue;
        }

        // ─────────────────────────────────────────────────────────────────
        // Tree
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the panel the first time it is needed.
        ///
        /// Lazily rather than in Initialize because the bootstrap that calls
        /// Initialize deliberately runs before every other component in the
        /// scene.
        /// </summary>
        private bool EnsureTree()
        {
            if (_panel != null)
                return true;

            if (_canvas == null)
                return false;

            // A full-screen wrapper that draws nothing, so it cannot be what the
            // pointer finds. Only the panel inside it picks.
            _screen = Ugui.Node((RectTransform)_canvas.transform, "Inventory");

            _panel = Ugui.Box(
                _screen, "Panel", new Color(0.06f, 0.07f, 0.09f, 0.95f)).rectTransform;

            // Centred by its anchors and as big as its contents. That is what
            // the flexbox centring it replaces was for; uGUI has no
            // justify-content, but the middle of the parent is one anchor.
            Ugui.Place(_panel);
            Ugui.Row(_panel, spacing: ColumnGap, padding: new RectOffset(12, 12, 10, 12));
            Ugui.Fit(_panel);

            // Two columns, because the screen is wider than it is tall. Stacked
            // vertically the panel is the sum of its parts; side by side it is
            // the taller of them, and that is the difference between fitting on
            // a 16:9 screen and not.
            _sidebar = Ugui.Node(_panel, "Sidebar");
            Ugui.Column(_sidebar, spacing: 4f);
            Ugui.Fit(_sidebar, horizontal: false);
            Ugui.Size(_sidebar, width: _sidebarWidth);

            _bagColumn = Ugui.Node(_panel, "Bag");
            Ugui.Column(_bagColumn, spacing: 4f);
            Ugui.Fit(_bagColumn);

            MakeHeading(_sidebar, "Character");

            _dollRoot = MakeSection(_sidebar);
            BuildPortraitFrame();

            // Over the frame, so a square on the rail is never hidden by it, and
            // a separate node so that clearing the squares cannot take the
            // portrait with them.
            _slotList = Ugui.Node(_dollRoot, "Slots");

            // Under the character, not above them: the sheet is what the gear
            // around the portrait adds up to, and a column of numbers between
            // the title and the doll would separate the two things that explain
            // each other. No heading of its own — a row saying "Strength 12"
            // does not need to be told it is a stat.
            _statsList = MakeSection(_sidebar);

            // The hotkeys go in the sidebar and the sockets under the bag, so
            // the two new sections land in different columns. Stacking both on
            // one would put the panel back over the edge of a short screen,
            // which is the failure this layout already had to stop having once.
            MakeHeading(_sidebar, "Skill bar");
            _barList = MakeSection(_sidebar);
            Ugui.Row(_barList, spacing: 3f);
            Ugui.Size(_barList, height: BarBoxHeight);

            MakeHeading(_bagColumn, "Bag");

            _gridRoot = Ugui.Node(_bagColumn, "Grid");

            // The background must not swallow pointer events: the item layer
            // above it is what a drag talks to.
            _cellLayer = Ugui.Node(_gridRoot, "Cells");

            // Between the two layers, deliberately: over the empty squares so it
            // can be seen, under the items so one never hides the square it is
            // about to land on. Being a sibling of the cell layer rather than a
            // child of it is also what keeps a grid rebuild from destroying it.
            _highlight = Ugui.Box(_gridRoot, "Highlight", Color.clear, picks: false);
            _highlight.gameObject.SetActive(false);

            _itemLayer = Ugui.Node(_gridRoot, "Items");

            MakeHeading(_bagColumn, "Sockets");
            _socketList = MakeSection(_bagColumn);
            Ugui.Column(_socketList, spacing: 4f);
            Ugui.Fit(_socketList, horizontal: false);

            _message = Ugui.Text(
                _bagColumn, "Message", 12f, new Color(0.72f, 0.55f, 0.45f),
                TextAlignmentOptions.TopLeft, wrap: true);

            // What follows the cursor during a drag.
            //
            // A separate element rather than moving the item itself, because a
            // drag can start in either column: an equipment slot lives inside
            // the sidebar and moving it would be clipped by that column's own
            // layout. One ghost serves both, and the thing being dragged just
            // dims in place.
            //
            // On the wrapper rather than inside the panel, and last, so it draws
            // over both columns and no layout group can take hold of it.
            _ghost = Ugui.Box(_screen, "Ghost", Color.clear, picks: false);
            _ghost.gameObject.SetActive(false);

            _ghostLabel = Ugui.Text(
                _ghost.rectTransform, "Name", 11f, Color.white, wrap: true);

            _ghostIcon = MakeIcon(_ghost.rectTransform, ItemIconInset);

            // Made once and re-coloured per item. A frame is a child in uGUI, and
            // replacing it every time something is picked up would leave the old
            // one drawing for the rest of the frame.
            _ghostBorder = Ugui.Border(_ghost.rectTransform, Color.white, 1f);

            Ugui.Fade(_ghost.rectTransform, 0.85f);

            // After the panel, so it draws over it. A tooltip behind the thing
            // it describes is the one placement that helps nobody.
            _tooltips = new TooltipView(_screen, RarityColour);

            _screen.gameObject.SetActive(false);
            return true;
        }

        /// <summary>
        /// The window the character is drawn in.
        ///
        /// The frame exists whether or not anything fills it: a panel with a
        /// hole where the character should be reads as broken, and a dark
        /// bordered box reads as a portrait nobody has taken yet.
        /// </summary>
        private void BuildPortraitFrame()
        {
            _portraitFrame = Ugui.Box(
                _dollRoot, "Portrait", new Color(0.09f, 0.10f, 0.13f, 1f),
                picks: false).rectTransform;

            if (_portrait != null)
            {
                RectTransform image = Ugui.Node(_portraitFrame, "Character");
                _portraitImage = image.gameObject.AddComponent<RawImage>();
                _portraitImage.raycastTarget = false;
            }

            Ugui.Border(_portraitFrame, new Color(0.20f, 0.22f, 0.27f), 1f);
        }

        /// <summary>
        /// Redraws the panel when the window changes size.
        ///
        /// Polled once a frame while the panel is open rather than waiting for a
        /// layout event: the canvas rect is one field, the comparison is two
        /// floats, and a geometry callback that fires for the panel's own
        /// contents was a rebuild that caused another one.
        /// </summary>
        private void WatchScreenSize()
        {
            Vector2 size = ((RectTransform)_canvas.transform).rect.size;

            if (size == _lastRootSize)
                return;

            _lastRootSize = size;

            // Not just the signature: the cells themselves have to be redrawn at
            // the new size, and BuildGrid only does that when it sees a change.
            _builtCellSize = 0f;
            _lastSignature = int.MinValue;
        }

        // ─────────────────────────────────────────────────────────────────
        // Tooltip
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Makes an element describe itself while the pointer is over it.
        ///
        /// A wrapper around TooltipView rather than a call straight to it,
        /// because of the one thing this panel knows and the box does not: a
        /// tooltip following a dragged item would sit on top of the very
        /// squares that say where it may land.
        /// </summary>
        private void AttachTooltip(
            RectTransform element, System.Func<ItemTooltip.Text> describe)
            => _tooltips.Attach(element, () => _drag.Active ? default : describe());

        /// <summary>
        /// What an item in the bag or in a socket says.
        ///
        /// Compared against what is worn only when it is not itself the worn
        /// thing: an item's difference from itself is a list of zeroes, and the
        /// panel would print "the same numbers" under every equipment slot.
        /// </summary>
        private ItemTooltip.Text DescribeItem(int itemId, bool compare)
        {
            if (!TryGetItems(out ItemDatabase items))
                return default;

            TryGetSkills(out SkillDatabase skills);

            TryGetSets(out ItemSetDatabase sets);

            ItemTooltip.TryDescribeItem(
                items, skills, itemId, compare ? WornRivalOf(items, itemId) : 0,
                out ItemTooltip.Text text,
                sets: sets, equippedFromSet: EquippedFromSet(sets, itemId));

            return text;
        }

        /// <summary>
        /// The same, for a gem that is already in a hole — so the tooltip can
        /// say whether it is doing anything THERE.
        ///
        /// A separate entry point rather than an argument on the one above,
        /// because "which skill is this supporting" is a question only a
        /// socketed gem has an answer to, and every other caller would have to
        /// pass nothing.
        /// </summary>
        private ItemTooltip.Text DescribeSocketedGem(
            int itemId, Entity gear, int linkGroup, int socketIndex)
        {
            if (!TryGetItems(out ItemDatabase items) || !TryGetSkills(out SkillDatabase skills))
                return DescribeItem(itemId, false);

            var supported = default(SkillShape);

            if (GemSockets.TryResolveGroupSkill(
                    _entityManager, items, skills, gear, linkGroup, out int skillIndex))
            {
                supported = skills.ShapeOf(skillIndex);
            }

            TryGetSets(out ItemSetDatabase sets);

            ItemTooltip.TryDescribeItem(
                items, skills, itemId, 0, out ItemTooltip.Text text, supported,
                GemSockets.GatherSupports(_entityManager, items, gear, linkGroup),
                GemSockets.IsPassiveActive(_entityManager, items, gear, socketIndex),
                GemSockets.HasPassiveActive(_entityManager, items, gear, linkGroup),
                sets, EquippedFromSet(sets, itemId));

            return text;
        }

        /// <summary>
        /// Whether a support gem in this hole can do anything at all.
        ///
        /// Asked by the socket cell, which dims it when the answer is no.
        /// Exactly the same two questions the tooltip spells out, through
        /// exactly the same tables — the cell is the glance and the tooltip is
        /// the sentence, and they must never disagree.
        /// </summary>
        private bool SupportIsLive(
            in SkillModifierBlob support, Entity gear, int linkGroup, ItemDatabase items)
        {
            if (!TryGetSkills(out SkillDatabase skills))
                return true;

            if (GemSockets.TryResolveGroupSkill(
                    _entityManager, items, skills, gear, linkGroup, out int skillIndex) &&
                !SkillModifiers.AppliesTo(support.Kind, skills.ShapeOf(skillIndex)))
            {
                return false;
            }

            // A trigger with nothing behind it to cast. The first active in the
            // group keeps the key, so a trigger gem beside a lone skill fires
            // into an empty group — which used to be the whole feature and is
            // now the one way to socket it wrong.
            if (SkillModifiers.IsAutomatic(support.Kind))
                return GemSockets.HasPassiveActive(_entityManager, items, gear, linkGroup);

            if (!SkillModifiers.HasCompanion(support.Kind))
                return true;

            return GemSockets.GroupHas(
                GemSockets.GatherSupports(_entityManager, items, gear, linkGroup),
                SkillModifiers.NeedsCompanion(support.Kind));
        }

        /// <summary>
        /// What wearing this item would cost the player, by id, or zero.
        ///
        /// The rule itself lives in EquipmentSlots, beside the mask it reads.
        /// This is only the part that knows whose character is being looked at.
        /// </summary>
        private int WornRivalOf(ItemDatabase items, int itemId)
        {
            if (!TryGetCharacter(out Entity character, out _) ||
                !_entityManager.HasBuffer<EquippedItem>(character))
            {
                return 0;
            }

            return EquipmentSlots.WornRivalOf(
                _entityManager.GetBuffer<EquippedItem>(character, isReadOnly: true),
                items, itemId);
        }

        /// <summary>What a welded socket or a bound hotkey says, by skill id.</summary>
        private ItemTooltip.Text DescribeSkillId(int skillId)
        {
            if (!TryGetSkills(out SkillDatabase skills))
                return default;

            ItemTooltip.TryDescribeSkill(
                skills, skills.IndexOf(skillId), out ItemTooltip.Text text);

            return text;
        }

        /// <summary>The same, for the one caller that already holds an index.</summary>
        private ItemTooltip.Text DescribeSkillIndex(int skillIndex)
        {
            if (!TryGetSkills(out SkillDatabase skills))
                return default;

            ItemTooltip.TryDescribeSkill(skills, skillIndex, out ItemTooltip.Text text);
            return text;
        }

        private void HideTooltip() => _tooltips?.Hide();

        /// <summary>
        /// Draws the empty grid. Only when its shape changes — the cells behind
        /// the items never move, and rebuilding sixty elements every time
        /// something is picked up is the cheapest way to make a profile
        /// unreadable.
        /// </summary>
        private void BuildGrid(in InventoryGridComponent grid)
        {
            RecomputeCellSize(grid);

            if (_builtWidth == grid.Width &&
                _builtHeight == grid.Height &&
                Mathf.Approximately(_builtCellSize, _cellSize))
            {
                return;
            }

            _builtWidth = grid.Width;
            _builtHeight = grid.Height;
            _builtCellSize = _cellSize;

            float width = grid.Width * _cellSize;
            float height = grid.Height * _cellSize;

            // The one size in the panel that is arithmetic rather than content,
            // so it is the one a layout group has to be told: a group asks every
            // child how big it is and collapses the ones with no answer.
            Ugui.Size(_gridRoot, width, height);
            Ugui.TopLeft(_cellLayer, 0f, 0f, width, height);
            Ugui.TopLeft(_itemLayer, 0f, 0f, width, height);

            Ugui.Clear(_cellLayer);

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    Image cell = Ugui.Box(
                        _cellLayer, "Cell", new Color(0.12f, 0.13f, 0.16f, 1f), picks: false);

                    Ugui.TopLeft(
                        cell.rectTransform,
                        x * _cellSize, y * _cellSize,
                        _cellSize - CellGap, _cellSize - CellGap);
                }
            }
        }

        /// <summary>
        /// Picks a cell size that fits the bag into what is left of the screen.
        ///
        /// The panel settings scale the whole UI to a 1200x800 reference and
        /// match on width, which means the height a 16:9 screen actually offers
        /// is 1200 / (16/9) = 675, not 800 — and on an ultrawide it is 514. A
        /// fixed cell size is a bet that the bag is small enough for whatever
        /// the player's aspect ratio turns out to be, and it is a bet that loses
        /// quietly, by drawing the bottom row off the screen.
        ///
        /// So it is measured instead: whichever of width and height runs out
        /// first decides, clamped so the cells stay clickable at one end and
        /// stop growing at the other.
        /// </summary>
        private void RecomputeCellSize(in InventoryGridComponent grid)
        {
            if (grid.Width <= 0 || grid.Height <= 0)
                return;

            Rect root = _canvas != null
                ? ((RectTransform)_canvas.transform).rect
                : new Rect(0f, 0f, float.NaN, float.NaN);

            float rootWidth = root.width;
            float rootHeight = root.height;

            // Before the first layout the root has no size yet. Start at the
            // largest cell; the geometry callback will correct it on the frame
            // the real size arrives.
            if (float.IsNaN(rootWidth) || float.IsNaN(rootHeight) ||
                rootWidth < 1f || rootHeight < 1f)
            {
                _cellSize = MaxCellSize;
                return;
            }

            float availableWidth =
                rootWidth * ScreenFraction - _sidebarWidth - ColumnGap - HorizontalChrome;
            float availableHeight = rootHeight * ScreenFraction - VerticalChrome;

            // The sidebar has its own budget and its own scarce direction. The
            // bag column shrinking does not help it: they sit side by side, and
            // the panel is as tall as the taller of the two.
            _slotBoxHeight = Mathf.Clamp(
                Mathf.Floor((rootHeight * ScreenFraction - SidebarChrome) / SlotRows) - 4f,
                MinSlotBoxHeightFloor,
                MaxSlotBoxHeight);

            float byWidth = availableWidth / grid.Width;
            float byHeight = availableHeight / grid.Height;

            // Floored, so a row of cells is never a fraction wider than the box
            // that is supposed to hold it.
            _cellSize = Mathf.Clamp(
                Mathf.Floor(Mathf.Min(byWidth, byHeight)), MinCellSize, MaxCellSize);

            // Said once per size change rather than per frame, because this is
            // a configuration problem — a bag too big for the screen it is being
            // played on — and the fix is a smaller bag, not a smaller cell.
            if (_cellSize < ComfortableCellSize && !Mathf.Approximately(_builtCellSize, _cellSize))
            {
                Debug.LogWarning(
                    $"[{nameof(InventoryUI)}] A {grid.Width}x{grid.Height} bag only fits this " +
                    $"screen at {_cellSize:0} pixels per cell. Lower the bag size in " +
                    "CharacterConfig, or the items will be hard to read.", this);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Reading ECS
        // ─────────────────────────────────────────────────────────────────

        private void RefreshIfChanged()
        {
            if (!TryGetCharacter(out Entity character, out Entity bag))
            {
                ShowMessage("No character yet.");
                return;
            }

            if (bag == Entity.Null || !_entityManager.HasComponent<InventoryCell>(bag))
            {
                ShowMessage("No bag yet.");
                return;
            }

            PlayerStats stats = _entityManager.GetComponentData<PlayerStats>(character);
            InventoryGridComponent grid =
                _entityManager.GetComponentData<InventoryGridComponent>(bag);
            DynamicBuffer<EquippedItem> slots =
                _entityManager.GetBuffer<EquippedItem>(character, isReadOnly: true);
            DynamicBuffer<InventoryCell> cells =
                _entityManager.GetBuffer<InventoryCell>(bag, isReadOnly: true);

            ReportRefusals(character);
            ReportEquipRefusals(character);
            ReportSocketRefusals(character);

            // The purse is in the signature because it is on the panel now and
            // nothing else in here changes when a coin is picked up: without it
            // the gold row would be correct only until the first pickup.
            int signature = Signature(stats, slots, cells) * 31 +
                            SocketSignature(character, slots) * 31 +
                            Currency.Balance(_entityManager, character);
            if (signature == _lastSignature)
                return;

            _lastSignature = signature;

            if (_itemDatabaseQuery.IsEmptyIgnoreFilter)
            {
                ShowMessage("No item database baked.");
                return;
            }

            ItemDatabase items = _itemDatabaseQuery.GetSingleton<ItemDatabase>();

            // Everything the pointer could be over is about to be thrown away,
            // and a destroyed element raises no leave event — so the box would
            // hang there describing an item that is no longer under the cursor.
            HideTooltip();

            BuildGrid(grid);
            RebuildStats(character, stats);
            RebuildSlots(slots, items);
            RebuildItems(bag, cells, items);
            RebuildSockets(slots, items);
            RebuildBar(character, items);
        }

        /// <summary>
        /// Drains the result queue and says why the last move did not happen.
        ///
        /// The results are the whole point of the request being a request. A
        /// refused placement leaves the world untouched, so without this the
        /// player would see an item spring back and be told nothing about why.
        /// </summary>
        private void ReportRefusals(Entity character)
        {
            DynamicBuffer<InventoryPlacementResult> results =
                _entityManager.GetBuffer<InventoryPlacementResult>(character);

            if (results.Length == 0)
                return;

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].Succeeded)
                    continue;

                ShowMessage(DescribeRefusal(results[i].Status));
                break;
            }

            results.Clear();
        }

        /// <summary>
        /// The same, for equipment. Two queues because two systems answer, and
        /// a shared one would make "who said no" a question again.
        /// </summary>
        private void ReportEquipRefusals(Entity character)
        {
            DynamicBuffer<EquipResult> results =
                _entityManager.GetBuffer<EquipResult>(character);

            if (results.Length == 0)
                return;

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].Succeeded)
                    continue;

                ShowMessage(DescribeEquipRefusal(results[i].Status));
                break;
            }

            results.Clear();
        }

        /// <summary>The same again for sockets. Three queues, three answerers.</summary>
        private void ReportSocketRefusals(Entity character)
        {
            DynamicBuffer<SocketResult> results =
                _entityManager.GetBuffer<SocketResult>(character);

            if (results.Length == 0)
                return;

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].Succeeded)
                    continue;

                ShowMessage(DescribeSocketRefusal(results[i].Status));
                break;
            }

            results.Clear();
        }

        private static string DescribeSocketRefusal(SocketStatus status)
        {
            switch (status)
            {
                case SocketStatus.RejectedSocketFull:
                    return "That socket already has a gem in it.";
                case SocketStatus.RejectedSocketEmpty:
                    return "That socket is empty.";
                case SocketStatus.RejectedNotAGem:
                    return "That is not a gem.";
                case SocketStatus.RejectedBagFull:
                    return "No room in the bag for that gem.";
                case SocketStatus.RejectedNotActive:
                    return "A support gem cannot be cast on its own.";
                case SocketStatus.RejectedNotCarried:
                    return "That is not yours to socket.";
                case SocketStatus.RejectedWelded:
                    return "That is the weapon's own attack. It does not come out.";
                case SocketStatus.RejectedPassive:
                    return "A trigger gem is linked to it, so it casts itself. " +
                           "Take the trigger out to put it on a key.";
                default:
                    return "The change was refused.";
            }
        }

        private static string DescribeEquipRefusal(EquipStatus status)
        {
            switch (status)
            {
                case EquipStatus.RejectedNotCarried:
                    return "That item is not in your bag.";
                case EquipStatus.RejectedWrongSlot:
                    return "That does not go in that slot.";
                case EquipStatus.RejectedOffHandBlocked:
                    return "Your weapon needs both hands.";
                case EquipStatus.RejectedBagFull:
                    return "No room in the bag for what that would take off.";
                case EquipStatus.RejectedEmptySlot:
                    return "That slot is empty.";
                case EquipStatus.RejectedNoBag:
                    return "You have no bag.";
                default:
                    return "The change was refused.";
            }
        }

        private static string DescribeRefusal(InventoryPlacementStatus status)
        {
            switch (status)
            {
                case InventoryPlacementStatus.RejectedOccupied:
                    return "Something is already there.";
                case InventoryPlacementStatus.RejectedOutOfBounds:
                    return "That does not fit inside the bag.";
                case InventoryPlacementStatus.RejectedNoRoom:
                    return "No room in the bag.";
                case InventoryPlacementStatus.RejectedCannotRotate:
                    return "That item cannot be turned.";
                default:
                    return "The move was refused.";
            }
        }

        private void RebuildStats(Entity character, in PlayerStats stats)
        {
            Ugui.Clear(_statsList);

            _statsY = 0f;
            _statsRight = false;

            AddGoldRow(character);
            AddKeystoneRow(character);
            AddSetRow(character);

            for (int s = 0; s < StatBlock.StatCount; s++)
            {
                var stat = (StatKind)s;

                // Two to a line. Eight of them in a single column is the tallest
                // thing in the panel, and height is the scarce direction.
                FlowStatRow(
                    MakeRow(_statsList, stat.ToString(), $"{stats.Final.Get(stat):0.##}"),
                    half: true);
            }

            // The list is absolutely positioned inside, so nothing else can work
            // out how tall it ended up.
            Ugui.Size(_statsList, height: _statsY + (_statsRight ? StatRowHeight : 0f));
        }

        /// <summary>
        /// Puts one row on the current line, or starts a new one.
        ///
        /// The wrap UI Toolkit did with percentage widths and flex-wrap, spelled
        /// out: half-width rows pair up, full-width rows always take a line of
        /// their own, and a full-width row after a lone half-width one starts
        /// below it rather than beside it.
        /// </summary>
        private void FlowStatRow(RectTransform row, bool half)
        {
            float full = _sidebarWidth;
            float halfWidth = (full - 4f) * 0.5f;

            if (!half)
            {
                if (_statsRight)
                {
                    _statsY += StatRowHeight;
                    _statsRight = false;
                }

                Ugui.TopLeft(row, 0f, _statsY, full, StatRowHeight);
                _statsY += StatRowHeight;
                return;
            }

            Ugui.TopLeft(
                row, _statsRight ? halfWidth + 4f : 0f, _statsY, halfWidth, StatRowHeight);

            if (_statsRight)
                _statsY += StatRowHeight;

            _statsRight = !_statsRight;
        }

        /// <summary>
        /// What is in the purse.
        ///
        /// Money stopped being an item in the bag, so without this line there is
        /// nowhere outside a shop to see how much of it there is — and the
        /// dungeon, where it is picked up, is the one place with no shop in it.
        ///
        /// Above the stats and drawn even at zero, unlike the keystone row: an
        /// empty purse is a number a player wants, and "you have nothing" is
        /// what an empty stat line is for.
        /// </summary>
        private void AddGoldRow(Entity character)
        {
            FlowStatRow(
                MakeRow(_statsList, "Gold", Currency.Balance(_entityManager, character).ToString()),
                half: false);
        }

        /// <summary>
        /// Says which rule of the game this character is currently breaking, and
        /// says when a second item is being ignored.
        ///
        /// Drawn above the stats rather than among them because it is not a
        /// number and does not add up with anything. A character with no keystone
        /// gets no row at all: an empty label saying "Keystone: None" is a line
        /// of nothing on the shortest column of a panel that already struggles
        /// to fit on an ultrawide.
        ///
        /// The conflict note is the whole of the promised warning, and
        /// deliberately no more than that. Which of two keystones is in force is
        /// a rule the player can read here; what they would do together is a
        /// design conversation, not a tooltip.
        /// </summary>
        /// <summary>
        /// How many pieces of this item's set are worn, counted off the
        /// character the panel is looking at.
        ///
        /// The counting itself lives in ItemSets, beside the rule the host
        /// evaluates: a panel with its own count would eventually be a panel
        /// with a different count.
        /// </summary>
        private int EquippedFromSet(ItemSetDatabase sets, int itemId)
        {
            if (!TryGetCharacter(out Entity character, out _))
                return 0;

            return ItemSets.EquippedCount(_entityManager, character, sets, itemId);
        }

        /// <summary>
        /// Says which set bonuses are in force, so the player can see one
        /// working rather than infer it from tooltips.
        ///
        /// Beside the keystone row and drawn the same way, because it is the
        /// same kind of fact: not a number, does not add up with anything, and
        /// absent for a character wearing no set — a row reading "Set: none" is
        /// a line of nothing on the shortest column of the panel.
        ///
        /// A set with pieces but no step reached gets a row as well, saying so:
        /// "one of four, no bonus yet" is the one thing worth knowing while a
        /// set is still being assembled.
        /// </summary>
        private void AddSetRow(Entity character)
        {
            if (!_entityManager.HasBuffer<ActiveSetBonusStatus>(character) ||
                !TryGetSets(out ItemSetDatabase sets))
            {
                return;
            }

            DynamicBuffer<ActiveSetBonusStatus> active =
                _entityManager.GetBuffer<ActiveSetBonusStatus>(character, isReadOnly: true);

            for (int i = 0; i < active.Length; i++)
            {
                ActiveSetBonusStatus status = active[i];

                if (status.SetIndex < 0 || status.SetIndex >= sets.SetCount)
                    continue;

                // By reference, like everywhere else a set blob is read.
                ref ItemSetBlob set = ref sets.Value.Value.Sets[status.SetIndex];

                string detail = status.IsActive
                    ? $"{status.CurrentEquippedCount}/{set.MemberItemIds.Length} · " +
                      $"{status.HighestActiveThreshold}-piece bonus"
                    : $"{status.CurrentEquippedCount}/{set.MemberItemIds.Length} · " +
                      "no bonus yet";

                FlowStatRow(MakeRow(_statsList, set.SetName.ToString(), detail), half: false);
            }
        }

        private void AddKeystoneRow(Entity character)
        {
            if (!_entityManager.HasComponent<KeystoneComponent>(character))
                return;

            KeystoneComponent keystone =
                _entityManager.GetComponentData<KeystoneComponent>(character);

            if (keystone.Effect == KeystoneEffect.None)
                return;

            string detail = keystone.Ignored > 0
                ? $"{keystone.Effect} (+{keystone.Ignored} ignored)"
                : keystone.Effect.ToString();

            FlowStatRow(MakeRow(_statsList, "Keystone", detail), half: false);
        }

        /// <summary>
        /// Draws the equipment slots as squares around the character.
        ///
        /// A square is a target you can drop onto and a handle you can drag
        /// from, which a row of text with a button beside it is not — and once
        /// they are squares, where each one sits can say what it is. A helmet
        /// above a chest above a belt is a sentence the player reads without
        /// being told; ten labelled boxes in a two-column list is a form.
        ///
        /// The character in the middle is the whole point of the arrangement:
        /// every square hangs on one side of it or the other, weapons too, so
        /// nothing sits between the portrait and the top of the panel.
        /// </summary>
        private void RebuildSlots(DynamicBuffer<EquippedItem> slots, ItemDatabase items)
        {
            Ugui.Clear(_slotList);
            _slotTargets.Clear();

            // Asked once for the whole rebuild. It is a property of the main
            // hand, not of the off hand, so asking per slot would be asking the
            // same question ten times.
            bool offHandBlocked = EquipmentSlots.IsOffHandBlocked(slots, items);

            // Square, not a wide box: the size the sidebar's height budget
            // already worked out for one row of slots is also how wide a rail
            // may be, because two rails and a portrait have to fit across it.
            float square = _slotBoxHeight;
            float step = square + DollGap;
            float rails = DollRails * step - DollGap;

            float frameWidth = _sidebarWidth - 2f * (square + DollGap);
            Ugui.TopLeft(_portraitFrame, square + DollGap, 0f, frameWidth, rails);

            if (_portrait != null && rails > 1f)
                _portrait.SetAspect(frameWidth / rails);

            for (int i = 0; i < slots.Length; i++)
            {
                EquippedItem slot = slots[i];
                bool blocked = slot.Slot == EquipmentSlot.OffHand && offHandBlocked;

                SlotBox box = MakeSlotBox(slot, items, blocked);
                Vector2 corner = DollCorner(slot.Slot, square, step);

                Ugui.TopLeft(box.Rect, corner.x, corner.y, square, square);

                _slotTargets.Add(new SlotTarget
                {
                    Element = box.Rect,
                    Border = box.Border,
                    Slot = slot.Slot,
                    Blocked = blocked,
                    DefaultBorder = box.BorderColour
                });
            }

            Ugui.Size(_dollRoot, height: rails);
        }

        /// <summary>
        /// Where one slot's square goes, in pixels from the block's top-left.
        ///
        /// The table below is the layout; this is only arithmetic on it.
        /// </summary>
        private Vector2 DollCorner(EquipmentSlot slot, float square, float step)
        {
            SlotPlace place = Doll[(int)slot];
            float left = place.Rail == 0 ? 0f : _sidebarWidth - square;
            return new Vector2(left, place.Index * step);
        }

        /// <summary>
        /// Which rail each slot hangs on, in the buffer's own order.
        ///
        /// The weapons open both rails, main hand left and off hand right, at
        /// either side of the character rather than above it — they are the two
        /// the player changes most and the two a build is named after. Under
        /// them the body reads top to bottom on the left — head, chest, hands —
        /// and the small things on the right: neck, waist, feet. The rings close
        /// both rails on the same line, one either side, so the pair reads as a
        /// pair rather than as two entries in a list.
        /// </summary>
        private static readonly SlotPlace[] Doll =
        {
            new SlotPlace(0, 0), // MainHand
            new SlotPlace(1, 0), // OffHand
            new SlotPlace(0, 1), // Helmet
            new SlotPlace(0, 2), // Chest
            new SlotPlace(0, 3), // Gloves
            new SlotPlace(1, 3), // Boots
            new SlotPlace(0, 4), // Ring1
            new SlotPlace(1, 4), // Ring2
            new SlotPlace(1, 1), // Amulet
            new SlotPlace(1, 2)  // Belt
        };

        /// <summary>One square's place: which rail, and how far down it.</summary>
        private readonly struct SlotPlace
        {
            /// <summary>Nought is the left rail, one the right.</summary>
            public readonly int Rail;
            public readonly int Index;

            public SlotPlace(int rail, int index)
            {
                Rail = rail;
                Index = index;
            }
        }

        /// <summary>
        /// What an empty square says.
        ///
        /// Short enough to fit one, which the enum's own names are not: "MainHand"
        /// in forty pixels is two lines of nothing.
        /// </summary>
        private static string ShortSlotName(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.MainHand: return "Main";
                case EquipmentSlot.OffHand: return "Off";
                case EquipmentSlot.Helmet: return "Helm";
                case EquipmentSlot.Chest: return "Chest";
                case EquipmentSlot.Gloves: return "Glove";
                case EquipmentSlot.Boots: return "Boots";
                case EquipmentSlot.Ring1: return "Ring";
                case EquipmentSlot.Ring2: return "Ring";
                case EquipmentSlot.Amulet: return "Amul";
                default: return "Belt";
            }
        }

        /// <summary>Enough of a name to recognise it in a square. The tooltip has the rest.</summary>
        private static string Shorten(string name) =>
            string.IsNullOrEmpty(name) || name.Length <= 12 ? name : name.Substring(0, 12);

        /// <summary>What MakeSlotBox hands back: the box, and the frame to recolour.</summary>
        private struct SlotBox
        {
            public RectTransform Rect;
            public Image Border;
            public Color BorderColour;
        }

        private SlotBox MakeSlotBox(in EquippedItem slot, ItemDatabase items, bool blocked)
        {
            Color background = blocked
                ? new Color(0.10f, 0.09f, 0.09f, 1f)
                : !slot.HasItem
                    ? new Color(0.10f, 0.11f, 0.14f, 1f)
                    : Tint(RarityOf(items, slot.ItemId));

            Color border = blocked
                ? new Color(0.32f, 0.24f, 0.24f)
                : !slot.HasItem
                    ? new Color(0.20f, 0.22f, 0.27f)
                    : RarityColour(RarityOf(items, slot.ItemId));

            Color valueColour = blocked
                ? new Color(0.55f, 0.45f, 0.42f)
                : !slot.HasItem
                    ? new Color(0.45f, 0.47f, 0.52f)
                    : border;

            Image box = Ugui.Box(_slotList, slot.Slot.ToString(), background);

            // One line, not two. A square forty pixels across has room for what
            // is in it OR what belongs in it, and which of those the player
            // wants is decided by whether the square is empty. The full answer
            // is a hover away, and always was.
            TextMeshProUGUI name = Ugui.Text(
                box.rectTransform, "Label", 9f, valueColour, wrap: true);

            name.text = blocked ? "2H" : slot.HasItem
                ? Shorten(NameOf(items, slot.ItemId))
                : ShortSlotName(slot.Slot);

            Ugui.Place(name.rectTransform, left: 2f, right: 2f, top: 2f, bottom: 2f);

            var result = new SlotBox
            {
                Rect = box.rectTransform,
                Border = Ugui.Border(box.rectTransform, border, 1f),
                BorderColour = border
            };

            if (blocked || !slot.HasItem)
                return result;

            EquippedItem captured = slot;

            Ugui.On(box.rectTransform, EventTriggerType.PointerDown,
                data => BeginSlotDrag(data, box.rectTransform, captured, items));

            // Double click takes it off, mirroring the double click that puts it
            // on. No target, so the host finds the room.
            Ugui.On(box.rectTransform, EventTriggerType.PointerClick, data =>
            {
                if (data.clickCount >= 2)
                    SendUnequip(captured.Slot, false, 0, 0, false);
            });

            AttachTooltip(box.rectTransform, () => DescribeItem(captured.ItemId, false));

            return result;
        }

        /// <summary>
        /// Draws one element per item, sized to the cells it covers.
        ///
        /// The distinct items are found by walking the cell buffer rather than
        /// with a query, because a multi-cell item appears in the buffer several
        /// times and the buffer is sixty entries. A query per frame to learn
        /// what is in one bag would cost more than the whole panel.
        /// </summary>
        private void RebuildItems(
            Entity bag,
            DynamicBuffer<InventoryCell> cells,
            ItemDatabase items)
        {
            Ugui.Clear(_itemLayer);

            _seen.Clear();

            for (int i = 0; i < cells.Length; i++)
            {
                Entity item = cells[i].OccupyingItem;

                if (item == Entity.Null || !_seen.Add(item))
                    continue;

                if (!_entityManager.Exists(item) ||
                    !_entityManager.HasComponent<ItemGridPlacement>(item))
                {
                    continue;
                }

                ItemGridPlacement placement =
                    _entityManager.GetComponentData<ItemGridPlacement>(item);
                ItemInstance instance = _entityManager.GetComponentData<ItemInstance>(item);

                if (!GridFit.TryGetFootprint(
                        items, instance.ItemId, placement.IsRotated,
                        out int width, out int height))
                {
                    continue;
                }

                MakeItem(
                    bag, item, instance, placement, width, height,
                    NameOf(items, instance.ItemId), items);
            }
        }

        private void MakeItem(
            Entity bag,
            Entity item,
            in ItemInstance instance,
            in ItemGridPlacement placement,
            int width,
            int height,
            string label,
            ItemDatabase items)
        {
            Color border = RarityColour(instance.Rarity);

            Image element = Ugui.Box(_itemLayer, label, Tint(instance.Rarity));

            Ugui.TopLeft(
                element.rectTransform,
                placement.OriginX * _cellSize,
                placement.OriginY * _cellSize,
                width * _cellSize - CellGap,
                height * _cellSize - CellGap);

            // The art when there is some, the name otherwise — the tooltip says
            // the name either way.
            if (AddIcon(element.rectTransform, instance.ItemId, ItemIconInset) == null)
            {
                TextMeshProUGUI text = Ugui.Text(
                    element.rectTransform, "Name", 11f, border, wrap: true);
                text.text = label;
            }

            Ugui.Border(element.rectTransform, border, 1f);

            Entity capturedBag = bag;
            Entity capturedItem = item;
            int capturedId = instance.ItemId;
            ItemGridPlacement capturedPlacement = placement;

            ItemDatabase capturedItems = items;

            Ugui.On(element.rectTransform, EventTriggerType.PointerDown, data => BeginDrag(
                data, element.rectTransform, capturedBag, capturedItem, capturedId,
                capturedItems, capturedPlacement));

            AttachTooltip(element.rectTransform, () => DescribeItem(capturedId, true));

            // Double click equips, the way it does in every game this one is
            // trying to feel like. No slot travels with it, so the host picks —
            // an empty allowed slot first, which is what makes double-clicking
            // a second ring fill the other hand instead of replacing the first.
            Ugui.On(element.rectTransform, EventTriggerType.PointerClick, data =>
            {
                if (data.clickCount >= 2)
                    SendEquipAuto(capturedItem);
            });
        }

        /// <summary>Inset of an icon inside a bag item, clear of its frame.</summary>
        private const float ItemIconInset = 3f;

        /// <summary>The item's icon inside a box, or null when it has none.</summary>
        private Image AddIcon(RectTransform parent, int itemId, float inset)
        {
            if (!_iconsById.TryGetValue(itemId, out Sprite sprite))
                return null;

            Image icon = MakeIcon(parent, inset);
            icon.sprite = sprite;
            return icon;
        }

        private static Image MakeIcon(RectTransform parent, float inset)
        {
            RectTransform rect = Ugui.Node(parent, "Icon");
            Ugui.Place(rect, left: inset, right: inset, top: inset, bottom: inset);

            Image image = rect.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        // ─────────────────────────────────────────────────────────────────
        // Drag and drop
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Starts a drag from an item lying in the bag.
        /// </summary>
        private void BeginDrag(
            PointerEventData data,
            RectTransform element,
            Entity bag,
            Entity item,
            int itemId,
            ItemDatabase items,
            in ItemGridPlacement placement)
        {
            if (data.button != PointerEventData.InputButton.Left || _drag.Active)
                return;

            StartDrag(element, items, itemId, item, bag, placement.IsRotated);

            _drag.Corner = new Vector2(
                placement.OriginX * _cellSize, placement.OriginY * _cellSize);
            _drag.TargetX = placement.OriginX;
            _drag.TargetY = placement.OriginY;

            // Grabbed where the player actually took hold of it, so the square
            // it lands on is the one they are looking at.
            _drag.GrabOffset = Ugui.Point(element, data.position);
        }

        /// <summary>
        /// Starts a drag from an equipment slot.
        ///
        /// The same drag, with a source that is a slot rather than a square. The
        /// ghost is centred on the cursor because a slot box is not the shape of
        /// the item inside it, so there is no meaningful place on the item to
        /// have grabbed.
        /// </summary>
        private void BeginSlotDrag(
            PointerEventData data, RectTransform box, EquippedItem slot, ItemDatabase items)
        {
            if (data.button != PointerEventData.InputButton.Left || _drag.Active || !slot.HasItem)
                return;

            if (!TryGetCharacter(out _, out Entity bag))
                return;

            StartDrag(box, items, slot.ItemId, slot.Item, bag, false);

            _drag.Source2 = DragSource.EquipmentSlot;
            _drag.SourceSlot = slot.Slot;

            if (GridFit.TryGetFootprint(items, slot.ItemId, false, out int width, out int height))
                _drag.GrabOffset = new Vector2(width * _cellSize, height * _cellSize) * 0.5f;
        }

        /// <summary>
        /// Everything the entry points share: what is being dragged, and the
        /// ghost that shows it.
        ///
        /// No pointer capture any more. From here on the drag is stepped from
        /// Update, reading the pointer out of the input reader — so nothing
        /// depends on events continuing to reach the element that was pressed,
        /// which is what capture bought under UI Toolkit.
        /// </summary>
        private void StartDrag(
            RectTransform source,
            ItemDatabase items,
            int itemId,
            Entity item,
            Entity bag,
            bool rotated)
        {
            _drag = new Drag
            {
                Active = true,
                Source = source,
                Bag = bag,
                Item = item,
                ItemId = itemId,
                AllowedSlots = AllowedSlotsOf(items, itemId),
                Rotated = rotated,

                // Asked once here rather than at every hover: a pointer move
                // touches every socket cell on screen, and none of them should
                // be doing a database lookup to decide what colour to be.
                IsGem = GemKindOf(items, itemId) != GemKind.None,
                IsActiveGem = GemKindOf(items, itemId) == GemKind.Active
            };

            Ugui.Fade(source, 0.35f);

            // The pointer is about to stop being over what it grabbed, and a
            // tooltip left behind would describe the hole the item came out of.
            HideTooltip();

            ShapeGhost(items);

            _ghost.gameObject.SetActive(true);
            _ghost.rectTransform.SetAsLastSibling();

            _highlight.gameObject.SetActive(true);
        }

        /// <summary>Sizes and colours the ghost for what is currently being dragged.</summary>
        private void ShapeGhost(ItemDatabase items)
        {
            if (!GridFit.TryGetFootprint(
                    items, _drag.ItemId, _drag.Rotated, out int width, out int height))
            {
                return;
            }

            ItemRarity rarity = RarityOf(items, _drag.ItemId);
            Color border = RarityColour(rarity);

            _drag.GhostSize = new Vector2(
                width * _cellSize - CellGap, height * _cellSize - CellGap);

            _ghost.color = Tint(rarity);
            _ghostBorder.color = border;

            bool hasIcon = _iconsById.TryGetValue(_drag.ItemId, out Sprite sprite);
            _ghostIcon.sprite = sprite;
            _ghostIcon.enabled = hasIcon;

            _ghostLabel.text = hasIcon ? string.Empty : NameOf(items, _drag.ItemId);
            _ghostLabel.color = border;
        }

        /// <summary>
        /// One frame of a drag in flight: where the ghost goes, what it would
        /// land on, and the two buttons that end or turn it.
        ///
        /// All of it read from the input reader rather than from pointer events,
        /// which is what replaced the capture. The release is checked last, so a
        /// drop uses the position the ghost was actually drawn at this frame.
        /// </summary>
        private void StepDrag()
        {
            Vector2 pointer = _input.PointerPosition;

            MoveGhost(pointer);
            UpdateTargets(pointer);

            // The other mouse button, while this one is held. Free, because
            // casting is suppressed for as long as the panel is open.
            if (_input.WasCastPressed(1))
                OnDragRotate();

            if (_input.WasCastReleased(0))
                OnDragEnd(pointer);
        }

        private void MoveGhost(Vector2 pointer)
        {
            Vector2 corner = Ugui.Point(_screen, pointer) - _drag.GrabOffset;

            Ugui.TopLeft(
                _ghost.rectTransform, corner.x, corner.y,
                _drag.GhostSize.x, _drag.GhostSize.y);
        }

        /// <summary>
        /// Right button while dragging turns the item on its side.
        ///
        /// Not a key, deliberately. The button is read from the same input reader
        /// the game reads, so it arrives wherever the cursor happens to be; a
        /// keyboard shortcut would depend on who holds focus, which is one more
        /// thing that can quietly not be true. Casting is suppressed while the
        /// panel is open, so the button is free.
        /// </summary>
        private void OnDragRotate()
        {
            if (!TryGetItems(out ItemDatabase items) || !GridFit.CanRotate(items, _drag.ItemId))
            {
                ShowMessage("That item cannot be turned.");
                return;
            }

            _drag.Rotated = !_drag.Rotated;

            ShapeGhost(items);
            UpdateHighlight(_drag.Corner);
        }

        /// <summary>
        /// Colours everything the drop could land on: the square in the grid,
        /// and the equipment slot under the cursor.
        ///
        /// Both are predictions and neither is acted on. The grid one calls the
        /// same GridFit the host calls; the slot one reads the same allowed mask
        /// the host checks. A prediction computed a second way would eventually
        /// disagree with the answer, and a green box followed by a refusal is
        /// worse than no box at all.
        /// </summary>
        private void UpdateTargets(Vector2 pointer)
        {
            Vector2 corner = Ugui.Point(_gridRoot, pointer) - _drag.GrabOffset;

            _drag.Corner = corner;
            UpdateHighlight(corner);

            for (int i = 0; i < _slotTargets.Count; i++)
            {
                SlotTarget target = _slotTargets[i];
                target.Border.color = TargetColour(
                    target.DefaultBorder, Ugui.Contains(target.Element, pointer), SlotAccepts(target));
            }

            for (int i = 0; i < _socketTargets.Count; i++)
            {
                SocketTarget target = _socketTargets[i];
                target.Border.color = TargetColour(
                    target.DefaultBorder, Ugui.Contains(target.Element, pointer), SocketAccepts(target));
            }

            for (int i = 0; i < _barTargets.Count; i++)
            {
                BarTarget target = _barTargets[i];
                target.Border.color = TargetColour(
                    target.DefaultBorder, Ugui.Contains(target.Element, pointer), BarAccepts());
            }
        }

        /// <summary>
        /// A target's frame during a drag. Under the pointer it answers green or
        /// red; everywhere else, every place the thing could go is lit from the
        /// moment it is picked up, so the player sees where to take it before
        /// aiming at anything.
        /// </summary>
        private static Color TargetColour(Color idle, bool hovered, bool accepts)
        {
            if (hovered)
                return accepts ? new Color(0.35f, 0.80f, 0.40f) : new Color(0.85f, 0.30f, 0.30f);

            return accepts ? new Color(0.95f, 0.78f, 0.30f) : idle;
        }

        /// <summary>
        /// A gem out of the bag, into a hole that is free. A gem already in a
        /// socket moves by coming out first, which is what the host would say too.
        /// </summary>
        private bool SocketAccepts(in SocketTarget target) =>
            _drag.Source2 == DragSource.Bag && _drag.IsGem && target.IsEmpty;

        /// <summary>
        /// Only an active gem, and only one already in a socket: a hotkey points
        /// at a hole, so there has to be a hole.
        /// </summary>
        private bool BarAccepts() =>
            _drag.Source2 == DragSource.Socket && _drag.IsActiveGem;

        /// <summary>
        /// Whether dropping what is being dragged onto this slot could work.
        ///
        /// Could, not will: the host still decides, and it knows things this
        /// does not — whether the bag has room for what the slot would give up,
        /// most of all. This answers the question the player can see the answer
        /// to, which is whether the item belongs there at all.
        /// </summary>
        private bool SlotAccepts(in SlotTarget target)
        {
            if (target.Blocked)
                return false;

            // Out of a socket goes to the bag or a key, never a slot — the drop
            // ignores slots for it. Its mask would lie, too: a welded attack's
            // drag carries the weapon's id, and the weapon fits the main hand.
            if (_drag.Source2 == DragSource.Socket)
                return false;

            if (_drag.Source2 == DragSource.EquipmentSlot && target.Slot == _drag.SourceSlot)
                return false;

            return EquipmentSlots.Accepts(_drag.AllowedSlots, target.Slot);
        }

        private void UpdateHighlight(Vector2 corner)
        {
            if (!TryGetCharacter(out _, out Entity bag) ||
                bag == Entity.Null ||
                !TryGetItems(out ItemDatabase items))
            {
                return;
            }

            InventoryGridComponent grid =
                _entityManager.GetComponentData<InventoryGridComponent>(bag);
            DynamicBuffer<InventoryCell> cells =
                _entityManager.GetBuffer<InventoryCell>(bag, isReadOnly: true);

            if (!GridFit.TryGetFootprint(
                    items, _drag.ItemId, _drag.Rotated, out int width, out int height))
            {
                return;
            }

            int x = Mathf.RoundToInt(corner.x / _cellSize);
            int y = Mathf.RoundToInt(corner.y / _cellSize);

            _drag.TargetX = x;
            _drag.TargetY = y;

            // Shown, never acted on. The request goes out whatever colour this
            // is: a client that refused to ask because it predicted a no would
            // be a client whose bugs look like the host's.
            bool fits = GridFit.Fits(cells, grid, x, y, width, height, _drag.Item);

            Ugui.TopLeft(
                _highlight.rectTransform,
                Mathf.Clamp(x, 0, Mathf.Max(0, grid.Width - 1)) * _cellSize,
                Mathf.Clamp(y, 0, Mathf.Max(0, grid.Height - 1)) * _cellSize,
                width * _cellSize - CellGap,
                height * _cellSize - CellGap);

            _highlight.color = fits
                ? new Color(0.30f, 0.75f, 0.35f, 0.35f)
                : new Color(0.85f, 0.25f, 0.25f, 0.35f);
        }

        /// <summary>
        /// Four possible drops, and the source decides which two are on offer.
        ///
        /// From the bag onto a slot is an equip; from a slot onto another slot
        /// is a move that never touches the bag; from a slot onto the grid is an
        /// unequip that lands where the player pointed; and from the bag onto
        /// the grid is an ordinary placement.
        /// </summary>
        private void OnDragEnd(Vector2 pointer)
        {
            Entity item = _drag.Item;
            Entity bag = _drag.Bag;
            DragSource source = _drag.Source2;
            EquipmentSlot sourceSlot = _drag.SourceSlot;
            bool rotated = _drag.Rotated;
            int targetX = _drag.TargetX;
            int targetY = _drag.TargetY;

            Entity sourceGear = _drag.SourceGear;
            int sourceSocket = _drag.SourceSocket;
            bool isActiveGem = _drag.IsActiveGem;

            bool overGrid = Ugui.Contains(_gridRoot, pointer);
            EquipmentSlot slot = default;
            bool overSlot = TryFindSlotUnder(pointer, ref slot);

            bool overSocket = TryFindSocketUnder(
                pointer, out Entity socketGear, out int socketIndex);
            bool overBar = TryFindBarUnder(pointer, out int barIndex);

            CancelDrag();

            if (overSocket)
            {
                if (source == DragSource.Bag)
                    SendSocketInsert(socketGear, item, socketIndex);
                else
                    ShowMessage("Take the gem out before moving it to another socket.");

                return;
            }

            if (overBar)
            {
                // The passive check is the panel agreeing with the host rather
                // than deciding anything: SocketSystem refuses it too. Saying
                // it here is what makes the answer arrive beside the cursor
                // instead of one frame later.
                if (source == DragSource.Socket && isActiveGem &&
                    TryGetItems(out ItemDatabase barItems) &&
                    GemSockets.IsPassiveActive(
                        _entityManager, barItems, sourceGear, sourceSocket))
                {
                    ShowMessage(
                        "A trigger gem is linked to it, so it casts itself rather than " +
                        "answering a key.");
                }
                else if (source == DragSource.Socket && isActiveGem)
                {
                    SendBindBar(sourceGear, sourceSocket, barIndex);
                }
                else
                {
                    ShowMessage("Only an active gem in a socket can go on a hotkey.");
                }

                return;
            }

            if (source == DragSource.Socket)
            {
                // Anywhere else means out. The host finds it room, or refuses
                // and leaves the gem where it is.
                if (overGrid)
                    SendSocketRemove(sourceGear, sourceSocket);

                return;
            }

            if (overSlot)
            {
                if (source == DragSource.EquipmentSlot)
                {
                    if (slot != sourceSlot)
                        SendSwapSlots(sourceSlot, slot);

                    return;
                }

                // The slot the player dropped on is the slot they meant. The
                // host checks it against the item's allowed mask rather than
                // ignoring it, which is what makes a ring in either hand
                // possible without making any item in any slot possible.
                SendEquipToSlot(item, slot);
                return;
            }

            if (!overGrid)
                return;

            if (source == DragSource.EquipmentSlot)
            {
                SendUnequip(sourceSlot, true, targetX, targetY, rotated);
                return;
            }

            SendPlacement(bag, item, targetX, targetY, rotated);
        }

        /// <summary>
        /// Ends the drag and lets the next refresh redraw from ECS.
        ///
        /// Nothing is restored by hand: dropping the signature forces a rebuild,
        /// so what the player sees comes from the host either way — whether the
        /// move was accepted or not.
        /// </summary>
        private void CancelDrag()
        {
            if (!_drag.Active)
                return;

            if (_drag.Source != null)
                Ugui.Fade(_drag.Source, 1f);

            for (int i = 0; i < _slotTargets.Count; i++)
                _slotTargets[i].Border.color = _slotTargets[i].DefaultBorder;

            for (int i = 0; i < _socketTargets.Count; i++)
                _socketTargets[i].Border.color = _socketTargets[i].DefaultBorder;

            for (int i = 0; i < _barTargets.Count; i++)
                _barTargets[i].Border.color = _barTargets[i].DefaultBorder;

            _ghost.gameObject.SetActive(false);
            _highlight.gameObject.SetActive(false);

            _drag = default;
            _lastSignature = int.MinValue;
        }

        private bool TryFindSocketUnder(Vector2 position, out Entity gear, out int socketIndex)
        {
            gear = Entity.Null;
            socketIndex = 0;

            for (int i = 0; i < _socketTargets.Count; i++)
            {
                if (!Ugui.Contains(_socketTargets[i].Element, position))
                    continue;

                gear = _socketTargets[i].Gear;
                socketIndex = _socketTargets[i].SocketIndex;
                return true;
            }

            return false;
        }

        private bool TryFindBarUnder(Vector2 position, out int barIndex)
        {
            barIndex = 0;

            for (int i = 0; i < _barTargets.Count; i++)
            {
                if (!Ugui.Contains(_barTargets[i].Element, position))
                    continue;

                barIndex = _barTargets[i].BarSlotIndex;
                return true;
            }

            return false;
        }

        private bool TryFindSlotUnder(Vector2 position, ref EquipmentSlot slot)
        {
            for (int i = 0; i < _slotTargets.Count; i++)
            {
                if (!Ugui.Contains(_slotTargets[i].Element, position))
                    continue;

                slot = _slotTargets[i].Slot;
                return true;
            }

            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // Sockets and the skill bar
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Draws the holes in everything currently worn.
        ///
        /// Only gear that has any: nine of the ten slots have no sockets today,
        /// and a row of empty labels for each would be a list of nothing. Linked
        /// sockets are joined by a bar between them, which is the whole reason
        /// the cells are drawn in a row rather than in a grid — a link is a
        /// relationship between neighbours, and neighbours are what a row has.
        /// </summary>
        private void RebuildSockets(DynamicBuffer<EquippedItem> slots, ItemDatabase items)
        {
            Ugui.Clear(_socketList);
            _socketTargets.Clear();

            for (int i = 0; i < slots.Length; i++)
            {
                Entity gear = slots[i].Item;

                if (!slots[i].HasItem || !_entityManager.HasBuffer<GearSocket>(gear))
                    continue;

                DynamicBuffer<GearSocket> sockets =
                    _entityManager.GetBuffer<GearSocket>(gear, isReadOnly: true);

                if (sockets.Length == 0)
                    continue;

                MakeSocketRow(gear, sockets, items, NameOf(items, slots[i].ItemId));
            }

            if (_socketList.childCount == 0)
            {
                Ugui.Size(
                    MakeRow(_socketList, "Nothing worn has sockets", string.Empty),
                    height: StatRowHeight);
            }
        }

        private void MakeSocketRow(
            Entity gear, DynamicBuffer<GearSocket> sockets, ItemDatabase items, string label)
        {
            RectTransform row = Ugui.Node(_socketList, label);
            Ugui.Size(row, height: SocketCellSize);

            // The cells and the links between them have different heights and
            // both are meant to sit on the row's centre line, so the row spaces
            // them without touching how tall they are.
            HorizontalLayoutGroup group = Ugui.Row(row, spacing: 1f);
            group.childControlHeight = false;
            group.childAlignment = TextAnchor.MiddleLeft;

            TextMeshProUGUI name = Ugui.Text(
                row, "Gear", 11f, new Color(0.66f, 0.68f, 0.72f), TextAlignmentOptions.Left);
            name.text = label;
            Ugui.Size(name.rectTransform, width: 96f, height: SocketCellSize);

            for (int i = 0; i < sockets.Length; i++)
            {
                // The bar goes BEFORE the cell it links backwards to, so the
                // row reads left to right the way the link does.
                if (i > 0)
                    MakeLink(row, sockets[i - 1].LinkGroup == sockets[i].LinkGroup);

                MakeSocketCell(row, gear, sockets[i], items);
            }
        }

        /// <summary>
        /// The bar between two neighbouring sockets, drawn only when they share
        /// a group. An unlinked pair still gets the element, transparent, so the
        /// cells stay on the same pitch whether they are joined or not.
        /// </summary>
        private static void MakeLink(RectTransform parent, bool linked)
        {
            Image link = Ugui.Box(parent, "Link", linked
                ? new Color(0.75f, 0.70f, 0.42f)
                : new Color(0f, 0f, 0f, 0f), picks: false);

            Ugui.Size(link.rectTransform, width: LinkBarWidth, height: 3f);
        }

        private void MakeSocketCell(
            RectTransform parent, Entity gear, GearSocket socket, ItemDatabase items)
        {
            Image cell = Ugui.Box(parent, $"Socket{socket.SocketIndex}", Color.clear);
            Ugui.Size(cell.rectTransform, width: SocketCellSize, height: SocketCellSize);

            Color border = new Color(0.30f, 0.32f, 0.38f);

            TextMeshProUGUI mark = Ugui.Text(
                cell.rectTransform, "Mark", 10f, new Color(0.82f, 0.84f, 0.88f));

            if (socket.IsWelded)
            {
                // The weapon's own attack. Its own colour rather than a rarity,
                // because it has no gem to take a rarity from — and a letter of
                // its own, because "A" would promise a gem the player could pull
                // out and put somewhere else.
                border = new Color(0.62f, 0.58f, 0.42f);
                cell.color = new Color(0.20f, 0.18f, 0.12f, 1f);

                mark.text = "W";
                mark.color = border;

                int capturedSocket = socket.SocketIndex;
                ItemDatabase capturedDatabase = items;
                Entity capturedWeapon = gear;

                Ugui.On(cell.rectTransform, EventTriggerType.PointerDown, data => BeginWeldedDrag(
                    data, cell.rectTransform, capturedWeapon, capturedSocket, capturedDatabase));

                // A welded skill has no gem to describe, so the tooltip comes
                // from the skill database instead. Same box, same words.
                int capturedSkill = socket.WeldedSkillId;
                AttachTooltip(cell.rectTransform, () => DescribeSkillId(capturedSkill));
            }
            else if (!socket.IsEmpty &&
                GemSockets.TryDescribeGem(
                    _entityManager, items, socket.InsertedGem,
                    out GemKind kind, out _, out SkillModifierBlob support, out _, out _))
            {
                int itemId = _entityManager.GetComponentData<ItemInstance>(socket.InsertedGem).ItemId;
                ItemRarity rarity = RarityOf(items, itemId);

                border = RarityColour(rarity);
                cell.color = Tint(rarity);

                Image icon = AddIcon(cell.rectTransform, itemId, 1f);

                if (icon != null)
                {
                    // Under the letter, which moves to the corner: the art says
                    // which gem, the letter still says what kind.
                    icon.transform.SetAsFirstSibling();
                    Ugui.Place(mark.rectTransform, right: 0f, bottom: 0f, width: 9f, height: 11f);
                }

                // One letter, because a socket cell is twenty pixels across
                // and that is what fits. An active gem says so; a support says
                // WHEN it acts, which is the thing a player has to know to
                // arrange them — a fork that splits on impact and a multicast
                // that fires three at once are both "support" and behave
                // nothing alike.
                // An active that a trigger owns is not the same thing as one on
                // a key, and the cell has room for exactly one letter to say
                // which: P for a passive, because the player needs to know
                // before they try to drag it onto a hotkey and are told no.
                bool passive = kind == GemKind.Active &&
                               GemSockets.IsPassiveActive(
                                   _entityManager, items, gear, socket.SocketIndex);

                mark.text = kind == GemKind.Active
                    ? passive ? "P" : "A"
                    : SkillModifiers.Marker(SkillModifiers.PhaseOf(support.Kind));
                mark.color = border;

                // A support that cannot act on what it is linked to is drawn
                // grey and marked with a dash instead of its phase.
                //
                // The socket still looks full, because it IS full — what the
                // cell stops claiming is that the gem is doing something. It is
                // the smallest honest version of the feedback: the glance says
                // "this hole is wasted", and the tooltip says why.
                if (kind == GemKind.Support &&
                    !SupportIsLive(support, gear, socket.LinkGroup, items))
                {
                    border = new Color(0.34f, 0.34f, 0.36f);
                    cell.color = new Color(0.11f, 0.11f, 0.12f, 1f);

                    mark.text = "–";
                    mark.color = border;

                    if (icon != null)
                        icon.color = new Color(1f, 1f, 1f, 0.3f);
                }

                Entity capturedGem = socket.InsertedGem;
                int capturedIndex = socket.SocketIndex;
                ItemDatabase capturedItems = items;
                Entity capturedGear = gear;

                Ugui.On(cell.rectTransform, EventTriggerType.PointerDown, data => BeginSocketDrag(
                    data, cell.rectTransform, capturedGear, capturedIndex, capturedGem,
                    capturedItems));

                // The letter in the cell says which kind of gem it is; the
                // tooltip is where the rest of the sentence lives — including
                // what this gem is doing for the skill it happens to be linked
                // to, which is a question only a socketed gem can answer.
                int capturedGroup = socket.LinkGroup;
                AttachTooltip(cell.rectTransform, () =>
                    DescribeSocketedGem(itemId, capturedGear, capturedGroup, capturedIndex));
            }
            else
            {
                cell.color = new Color(0.09f, 0.10f, 0.13f, 1f);
            }

            _socketTargets.Add(new SocketTarget
            {
                Element = cell.rectTransform,
                Border = Ugui.Border(cell.rectTransform, border, 1f),
                Gear = gear,
                SocketIndex = socket.SocketIndex,
                IsEmpty = socket.IsEmpty,
                DefaultBorder = border
            });
        }

        /// <summary>
        /// Draws the hotkeys and what each one currently casts.
        ///
        /// The name is derived through the same GemSockets the cast system uses,
        /// so a key bound to a socket somebody emptied reads as empty here for
        /// exactly the reason it casts nothing there.
        /// </summary>
        private void RebuildBar(Entity character, ItemDatabase items)
        {
            Ugui.Clear(_barList);
            _barTargets.Clear();

            if (_skillDatabaseQuery.IsEmptyIgnoreFilter)
                return;

            SkillDatabase skills = _skillDatabaseQuery.GetSingleton<SkillDatabase>();
            DynamicBuffer<SkillSlot> bar =
                _entityManager.GetBuffer<SkillSlot>(character, isReadOnly: true);

            // What is worn, so a key pointing at a sword now sitting in the bag
            // reads as empty here for exactly the reason it casts nothing there.
            DynamicBuffer<EquippedItem> worn =
                _entityManager.GetBuffer<EquippedItem>(character, isReadOnly: true);

            for (int i = 0; i < bar.Length && i < BarSlotCount; i++)
                MakeBarBox(i, bar[i], worn, items, skills);
        }

        private void MakeBarBox(
            int index,
            in SkillSlot slot,
            DynamicBuffer<EquippedItem> worn,
            ItemDatabase items,
            SkillDatabase skills)
        {
            Image box = Ugui.Box(_barList, $"Key{index}", new Color(0.10f, 0.11f, 0.14f, 1f));
            Ugui.Size(box.rectTransform, height: BarBoxHeight, grow: 1f);

            Color border = new Color(0.24f, 0.26f, 0.32f);
            string text = HotkeyName(index);

            if (GemSockets.IsWorn(worn, slot.Gear) &&
                GemSockets.TryResolveActive(
                    _entityManager, items, skills, slot.Gear, slot.SocketIndex,
                    out int skillIndex, out int linkGroup))
            {
                border = new Color(0.45f, 0.62f, 0.85f);
                text = $"{HotkeyName(index)}  {skills.NameOf(skillIndex)}";

                // A key bound to a socket that has since become a passive — a
                // trigger gem was linked to it after the binding was made. The
                // binding is refused now, but one made before the gem arrived
                // is still there and silently does nothing, so the bar says so.
                //
                // Asked of the same GemSockets the cast system asks, so the
                // panel cannot claim a key works when the host has already
                // decided it does not.
                if (GemSockets.IsPassiveActive(
                        _entityManager, items, slot.Gear, slot.SocketIndex))
                {
                    FixedList512Bytes<SkillModifierBlob> supports =
                        GemSockets.GatherSupports(_entityManager, items, slot.Gear, linkGroup);

                    GemSockets.TryGetTrigger(supports, out SkillModifierBlob trigger);

                    border = new Color(0.85f, 0.66f, 0.38f);
                    text = $"{HotkeyName(index)}  {skills.NameOf(skillIndex)}\n" +
                           $"auto: {trigger.TriggerCondition}";
                }

                int capturedSkill = skillIndex;
                AttachTooltip(box.rectTransform, () => DescribeSkillIndex(capturedSkill));
            }

            TextMeshProUGUI label = Ugui.Text(
                box.rectTransform, "Label", 10f, border, wrap: true);
            label.text = text;

            int captured = index;
            Ugui.On(box.rectTransform, EventTriggerType.PointerClick, data =>
            {
                if (data.clickCount >= 2)
                    SendClearBar(captured);
            });

            _barTargets.Add(new BarTarget
            {
                Element = box.rectTransform,
                Border = Ugui.Border(box.rectTransform, border, 1f),
                BarSlotIndex = index,
                DefaultBorder = border
            });
        }

        /// <summary>
        /// What the player actually presses.
        ///
        /// Asked of the reader that binds the keys rather than answered here.
        /// It used to be a copy of that switch, which is fine right up until a
        /// key moves and one of the two panels keeps advertising the old one.
        /// </summary>
        private static string HotkeyName(int index) => PlayerInputReader.CastSlotName(index);

        /// <summary>Starts a drag from a gem sitting in a socket.</summary>
        /// <summary>
        /// Drags a weapon's built-in attack, which exists as an id in a socket
        /// and not as a gem anywhere.
        ///
        /// It is worth being draggable for one reason: the hotkey. Equipping
        /// arms the first key automatically, but a player who binds something
        /// else there would otherwise have no way back to their own weapon's
        /// attack short of taking the weapon off and putting it on again.
        ///
        /// Dropped anywhere but a hotkey it goes nowhere — the host refuses to
        /// remove a welded socket, and says so.
        /// </summary>
        private void BeginWeldedDrag(
            PointerEventData data,
            RectTransform cell,
            Entity gear,
            int socketIndex,
            ItemDatabase items)
        {
            if (data.button != PointerEventData.InputButton.Left || _drag.Active)
                return;

            if (!TryGetCharacter(out _, out Entity bag))
                return;

            if (!_entityManager.HasComponent<ItemInstance>(gear))
                return;

            // The ghost is shaped from the weapon, because the thing being
            // dragged has no shape of its own. What matters is that something
            // follows the pointer at all.
            int gearId = _entityManager.GetComponentData<ItemInstance>(gear).ItemId;

            StartDrag(cell, items, gearId, Entity.Null, bag, false);

            // Set after the fact, because both are read off the item id and the
            // id here belongs to the sword rather than to the attack in it. What
            // is being dragged behaves as an active gem in a socket, which is
            // exactly what the hotkey drop is looking for.
            _drag.IsGem = true;
            _drag.IsActiveGem = true;

            _drag.Source2 = DragSource.Socket;
            _drag.SourceGear = gear;
            _drag.SourceSocket = socketIndex;
        }

        private void BeginSocketDrag(
            PointerEventData data,
            RectTransform cell,
            Entity gear,
            int socketIndex,
            Entity gem,
            ItemDatabase items)
        {
            if (data.button != PointerEventData.InputButton.Left || _drag.Active)
                return;

            if (!TryGetCharacter(out _, out Entity bag))
                return;

            int itemId = _entityManager.GetComponentData<ItemInstance>(gem).ItemId;

            StartDrag(cell, items, itemId, gem, bag, false);

            _drag.Source2 = DragSource.Socket;
            _drag.SourceGear = gear;
            _drag.SourceSocket = socketIndex;

            if (GridFit.TryGetFootprint(items, itemId, false, out int width, out int height))
                _drag.GrabOffset = new Vector2(width * _cellSize, height * _cellSize) * 0.5f;
        }

        // ─────────────────────────────────────────────────────────────────
        // Requests. Nothing below this line changes anything — it asks.
        // ─────────────────────────────────────────────────────────────────

        private void SendPlacement(Entity bag, Entity item, int x, int y, bool rotated)
        {
            if (!TryGetCharacter(out Entity character, out _))
                return;

            _entityManager.GetBuffer<InventoryPlacementRequest>(character).Add(
                new InventoryPlacementRequest
                {
                    Container = bag,
                    Item = item,
                    Mode = InventoryPlacementMode.Exact,
                    OriginX = x,
                    OriginY = y,
                    Rotated = rotated
                });

            _lastSignature = int.MinValue;
        }

        private void SendEquip(EquipRequest request)
        {
            if (!TryGetCharacter(out Entity character, out _))
                return;

            _entityManager.GetBuffer<EquipRequest>(character).Add(request);

            // Nothing is drawn differently yet. The next refresh shows whatever
            // the host decided, which may be nothing at all.
            _lastSignature = int.MinValue;
        }

        private void SendEquipToSlot(Entity item, EquipmentSlot slot)
        {
            SendEquip(new EquipRequest
            {
                Kind = EquipRequestKind.ToSlot,
                Item = item,
                Slot = slot
            });
        }

        private void SendEquipAuto(Entity item)
        {
            SendEquip(new EquipRequest
            {
                Kind = EquipRequestKind.Auto,
                Item = item
            });
        }

        private void SendUnequip(
            EquipmentSlot slot, bool useTarget, int x, int y, bool rotated)
        {
            SendEquip(new EquipRequest
            {
                Kind = EquipRequestKind.Unequip,
                Slot = slot,
                UseTarget = useTarget,
                TargetX = x,
                TargetY = y,
                Rotated = rotated
            });
        }

        private void SendSwapSlots(EquipmentSlot from, EquipmentSlot to)
        {
            SendEquip(new EquipRequest
            {
                Kind = EquipRequestKind.SwapSlots,
                Slot = from,
                OtherSlot = to
            });
        }

        private void SendSocket(SocketRequest request)
        {
            if (!TryGetCharacter(out Entity character, out _))
                return;

            _entityManager.GetBuffer<SocketRequest>(character).Add(request);
            _lastSignature = int.MinValue;
        }

        private void SendSocketInsert(Entity gear, Entity gem, int socketIndex)
        {
            SendSocket(new SocketRequest
            {
                Kind = SocketRequestKind.Insert,
                Gear = gear,
                Gem = gem,
                SocketIndex = socketIndex
            });
        }

        private void SendSocketRemove(Entity gear, int socketIndex)
        {
            SendSocket(new SocketRequest
            {
                Kind = SocketRequestKind.Remove,
                Gear = gear,
                SocketIndex = socketIndex
            });
        }

        private void SendBindBar(Entity gear, int socketIndex, int barSlotIndex)
        {
            SendSocket(new SocketRequest
            {
                Kind = SocketRequestKind.BindBar,
                Gear = gear,
                SocketIndex = socketIndex,
                BarSlotIndex = barSlotIndex
            });
        }

        private void SendClearBar(int barSlotIndex)
        {
            SendSocket(new SocketRequest
            {
                Kind = SocketRequestKind.ClearBar,
                BarSlotIndex = barSlotIndex
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // Lookups
        // ─────────────────────────────────────────────────────────────────

        private bool TryGetCharacter(out Entity character, out Entity bag)
        {
            character = Entity.Null;
            bag = Entity.Null;

            if (_characterQuery.IsEmptyIgnoreFilter)
                return false;

            using NativeArray<Entity> entities = _characterQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PlayerCharacter> characters =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);
            using NativeArray<CarriedBag> bags =
                _characterQuery.ToComponentDataArray<CarriedBag>(Allocator.Temp);

            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i].PlayerId != _playerId)
                    continue;

                character = entities[i];
                bag = bags[i].Container;
                return true;
            }

            return false;
        }

        private bool TryGetItems(out ItemDatabase items)
        {
            items = default;

            if (_itemDatabaseQuery.IsEmptyIgnoreFilter)
                return false;

            items = _itemDatabaseQuery.GetSingleton<ItemDatabase>();
            return true;
        }

        private bool TryGetSkills(out SkillDatabase skills)
        {
            skills = default;

            if (_skillDatabaseQuery.IsEmptyIgnoreFilter)
                return false;

            skills = _skillDatabaseQuery.GetSingleton<SkillDatabase>();
            return true;
        }

        private bool TryGetSets(out ItemSetDatabase sets)
        {
            sets = default;

            if (_itemSetDatabaseQuery.IsEmptyIgnoreFilter)
                return false;

            sets = _itemSetDatabaseQuery.GetSingleton<ItemSetDatabase>();
            return true;
        }

        /// <summary>
        /// Which slots this item may go in.
        ///
        /// The same mask the host checks a request against, read straight off
        /// the blob. The panel colours a slot with it; it never decides with it.
        /// </summary>
        /// <summary>What kind of gem an item is, or None. Read straight off the blob.</summary>
        private static GemKind GemKindOf(ItemDatabase items, int itemId)
        {
            int index = items.IndexOf(itemId);
            if (index < 0)
                return GemKind.None;

            // By reference: ItemBlob carries a BlobArray, and copying it would
            // leave the affixes pointing at nothing.
            ref ItemBlob item = ref items.Value.Value.Items[index];
            return item.GemKind;
        }

        private static ushort AllowedSlotsOf(ItemDatabase items, int itemId)
        {
            int index = items.IndexOf(itemId);
            if (index < 0)
                return EquipmentSlots.None;

            // By reference: ItemBlob carries a BlobArray, and copying it would
            // leave the affixes pointing at nothing.
            ref ItemBlob item = ref items.Value.Value.Items[index];
            return item.AllowedSlots;
        }

        /// <summary>
        /// What quality an item is, by id.
        ///
        /// Items in the bag carry an ItemInstance to read this from; items in a
        /// slot are known only by id, so this asks the database instead.
        /// </summary>
        private static ItemRarity RarityOf(ItemDatabase items, int itemId)
        {
            int index = items.IndexOf(itemId);
            if (index < 0)
                return ItemRarity.Common;

            ref ItemBlob item = ref items.Value.Value.Items[index];
            return item.Rarity;
        }

        private static string NameOf(ItemDatabase items, int itemId)
        {
            int index = items.IndexOf(itemId);
            if (index < 0)
                return $"Unknown item {itemId}";

            // By reference: ItemBlob carries a BlobArray, and copying it would
            // leave the affixes pointing at nothing.
            ref ItemBlob item = ref items.Value.Value.Items[index];
            return item.Name.ToString();
        }

        /// <summary>
        /// What the panel is showing, in one number.
        ///
        /// The cell buffer rather than a list of items: it covers where things
        /// are as well as what they are, which is the whole point of a grid.
        /// Version covers every stat and slot change.
        /// </summary>
        private static int Signature(
            in PlayerStats stats,
            DynamicBuffer<EquippedItem> slots,
            DynamicBuffer<InventoryCell> cells)
        {
            unchecked
            {
                int hash = stats.Version * 31 + cells.Length;

                for (int i = 0; i < slots.Length; i++)
                    hash = hash * 31 + slots[i].ItemId + slots[i].Item.Index * 7;

                for (int i = 0; i < cells.Length; i++)
                    hash = hash * 31 + cells[i].OccupyingItem.Index;

                return hash;
            }
        }

        /// <summary>
        /// What the sockets and the hotkeys are showing, in one number.
        ///
        /// Separate from the main signature because it needs an EntityManager to
        /// reach the socket buffers, and the main one is deliberately static —
        /// a function of exactly what is handed to it.
        /// </summary>
        private int SocketSignature(Entity character, DynamicBuffer<EquippedItem> slots)
        {
            unchecked
            {
                int hash = 17;

                for (int i = 0; i < slots.Length; i++)
                {
                    Entity gear = slots[i].Item;

                    if (!slots[i].HasItem || !_entityManager.HasBuffer<GearSocket>(gear))
                        continue;

                    DynamicBuffer<GearSocket> sockets =
                        _entityManager.GetBuffer<GearSocket>(gear, isReadOnly: true);

                    for (int s = 0; s < sockets.Length; s++)
                        hash = hash * 31 + sockets[s].InsertedGem.Index;
                }

                DynamicBuffer<SkillSlot> bar =
                    _entityManager.GetBuffer<SkillSlot>(character, isReadOnly: true);

                for (int i = 0; i < bar.Length; i++)
                    hash = hash * 31 + bar[i].Gear.Index * 7 + bar[i].SocketIndex;

                return hash;
            }
        }

        private void ShowMessage(string message)
        {
            if (_message != null)
                _message.text = message;
        }

        // ─────────────────────────────────────────────────────────────────
        // Element construction. Styles are inline so the panel does not depend
        // on a stylesheet that may or may not have been imported.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// A block inside a column.
        ///
        /// Deliberately without a LayoutElement of its own: a LayoutElement
        /// OUTRANKS a layout group when the parent asks how tall a child is, so
        /// one left at zero would collapse a section that measures itself. The
        /// sections whose rows are placed by hand call Size when they know.
        /// </summary>
        private static RectTransform MakeSection(RectTransform parent) =>
            Ugui.Node(parent, "Section");

        private static void MakeHeading(RectTransform parent, string text)
        {
            TextMeshProUGUI heading = Ugui.Text(
                parent, "Heading", 15f, new Color(0.62f, 0.72f, 0.85f),
                TextAlignmentOptions.TopLeft, bold: true);

            heading.text = text;
            Ugui.Size(heading.rectTransform, height: 19f);
        }

        /// <summary>
        /// A name on the left and a value on the right. The name takes the slack,
        /// which is how uGUI spells space-between.
        /// </summary>
        private static RectTransform MakeRow(RectTransform parent, string label, string detail)
        {
            RectTransform row = Ugui.Node(parent, label);
            Ugui.Row(row, spacing: 6f);

            TextMeshProUGUI name = Ugui.Text(
                row, "Name", 13f, new Color(0.88f, 0.89f, 0.92f), TextAlignmentOptions.Left);
            name.text = label;
            Ugui.Size(name.rectTransform, grow: 1f);

            if (!string.IsNullOrEmpty(detail))
            {
                TextMeshProUGUI value = Ugui.Text(
                    row, "Value", 13f, new Color(0.66f, 0.68f, 0.72f), TextAlignmentOptions.Right);
                value.text = detail;
            }

            return row;
        }

        private static Color RarityColour(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon: return new Color(0.35f, 0.80f, 0.40f);
                case ItemRarity.Rare: return new Color(0.30f, 0.55f, 0.95f);
                case ItemRarity.Epic: return new Color(0.65f, 0.35f, 0.90f);
                case ItemRarity.Legendary: return new Color(0.95f, 0.62f, 0.18f);
                case ItemRarity.Mythic: return new Color(0.95f, 0.25f, 0.35f);
                default: return new Color(0.88f, 0.89f, 0.92f);
            }
        }

        /// <summary>The same colour, dimmed, so the label on top stays readable.</summary>
        private static Color Tint(ItemRarity rarity)
        {
            Color colour = RarityColour(rarity);
            return new Color(colour.r * 0.28f, colour.g * 0.28f, colour.b * 0.28f, 0.95f);
        }

        private void OnDestroy()
        {
            if (_portrait != null)
                _portrait.SetShown(false);

            if (!_hasWorld)
                return;

            _characterQuery.Dispose();
            _itemDatabaseQuery.Dispose();
            _skillDatabaseQuery.Dispose();
            _itemSetDatabaseQuery.Dispose();
        }

        /// <summary>One equipment row, and which slot dropping on it means.</summary>
        /// <summary>Where a drag began. Three places, four meanings on drop.</summary>
        private enum DragSource : byte
        {
            Bag = 0,
            EquipmentSlot = 1,
            Socket = 2
        }

        /// <summary>One socket cell, and the hole it stands for.</summary>
        private struct SocketTarget
        {
            public RectTransform Element;

            /// <summary>
            /// The frame to recolour while a drag hovers it. A border is a child
            /// in uGUI, so the target has to hold on to it — finding it again by
            /// name on every pointer move would be a search per socket per frame.
            /// </summary>
            public Image Border;

            public Entity Gear;
            public int SocketIndex;
            public bool IsEmpty;
            public Color DefaultBorder;
        }

        /// <summary>One hotkey box.</summary>
        private struct BarTarget
        {
            public RectTransform Element;
            public Image Border;
            public int BarSlotIndex;
            public Color DefaultBorder;
        }

        private struct SlotTarget
        {
            public RectTransform Element;
            public Image Border;
            public EquipmentSlot Slot;

            /// <summary>Unusable because the main hand needs both hands.</summary>
            public bool Blocked;

            /// <summary>
            /// The border it wears when nothing is hovering it.
            ///
            /// Remembered rather than recomputed, so that clearing a hover is a
            /// copy instead of a second place that has to agree with
            /// MakeSlotBox about what an empty slot looks like.
            /// </summary>
            public Color DefaultBorder;
        }

        /// <summary>
        /// Everything about a drag in flight.
        ///
        /// A struct rather than half a dozen fields on the panel, so that ending
        /// a drag is one assignment and there is no way to leave three of six
        /// pieces of state behind.
        /// </summary>
        private struct Drag
        {
            public bool Active;

            /// <summary>
            /// What was picked up. Not the thing that moves — the ghost does
            /// that — but the thing that dims in place until the button comes up,
            /// and the thing to undim when it does.
            /// </summary>
            public RectTransform Source;

            /// <summary>
            /// Where this started. It decides what a drop means on every target,
            /// which is why it is remembered rather than worked out at the end.
            /// </summary>
            public DragSource Source2;

            public EquipmentSlot SourceSlot;

            /// <summary>The gear whose socket this came out of. Socket drags only.</summary>
            public Entity SourceGear;
            public int SourceSocket;

            /// <summary>Whether what is being dragged is a gem at all.</summary>
            public bool IsGem;

            /// <summary>Whether the gem being dragged can be bound to a hotkey.</summary>
            public bool IsActiveGem;

            public Entity Bag;
            public Entity Item;
            public int ItemId;

            /// <summary>Read once at the start, so the hover check is a mask test.</summary>
            public ushort AllowedSlots;

            public bool Rotated;
            public Vector2 GrabOffset;

            /// <summary>
            /// How big the ghost is, worked out when the item was picked up or
            /// turned. Kept because moving it writes its size as well as its
            /// position, and reading that back off the rect every frame would be
            /// asking uGUI what we just told it.
            /// </summary>
            public Vector2 GhostSize;

            /// <summary>Top-left of the ghost, in grid-local pixels.</summary>
            public Vector2 Corner;

            public int TargetX;
            public int TargetY;
        }
    }
}
