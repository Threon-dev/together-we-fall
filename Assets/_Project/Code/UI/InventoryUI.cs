using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;
using TogetherWeFall.Equipment;
using TogetherWeFall.Inventory;
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

        /// <summary>Side of one socket cell, and the gap between linked ones.</summary>
        private const float SocketCellSize = 20f;

        private const float LinkBarWidth = 6f;

        /// <summary>Height of one hotkey box on the skill bar.</summary>
        private const float BarBoxHeight = 34f;

        /// <summary>How many hotkeys there are. SkillLoadoutSystem hands out these.</summary>
        private const int BarSlotCount = 4;

        /// <summary>
        /// How much of the screen the panel may take. Not all of it: a panel
        /// flush against the edges reads as a broken full-screen mode rather
        /// than as a window.
        /// </summary>
        private const float ScreenFraction = 0.9f;

        [SerializeField] private UIDocument _document;

        [Tooltip("Width of the character column beside the bag. The bag takes " +
                 "whatever is left, so this is the one number that decides how " +
                 "the two share a landscape screen.")]
        [SerializeField, Range(180, 420)] private int _sidebarWidth = 280;

        private PlayerInputReader _input;
        private EntityManager _entityManager;
        private EntityQuery _characterQuery;
        private EntityQuery _itemDatabaseQuery;
        private EntityQuery _skillDatabaseQuery;

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

        private VisualElement _screen;
        private VisualElement _panel;
        private VisualElement _sidebar;
        private VisualElement _bagColumn;
        private VisualElement _socketList;
        private VisualElement _barList;
        private VisualElement _statsList;
        private VisualElement _slotList;
        private VisualElement _gridRoot;
        private VisualElement _cellLayer;
        private VisualElement _itemLayer;
        private VisualElement _highlight;
        private VisualElement _ghost;
        private Label _ghostLabel;
        private Label _message;

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

            _hasWorld = true;
        }

        private void Update()
        {
            if (!_hasWorld || _input == null || !EnsureTree())
                return;

            if (_input.WasInventoryTogglePressed())
                Toggle();

            // Never while a drag is in flight: a rebuild destroys the element
            // the pointer is holding, and the capture goes with it.
            if (_visible && !_drag.Active)
                RefreshIfChanged();
        }

        private void Toggle()
        {
            _visible = !_visible;
            _screen.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;

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
        /// Lazily rather than in Initialize because UIDocument populates its root
        /// in OnEnable, and the bootstrap that calls Initialize deliberately runs
        /// before every other component in the scene.
        /// </summary>
        private bool EnsureTree()
        {
            if (_panel != null)
                return true;

            if (_document == null)
                return false;

            VisualElement root = _document.rootVisualElement;
            if (root == null)
                return false;

            // A full-screen wrapper that centres the panel with flexbox rather
            // than with a percentage translate. Flexbox centring is the same in
            // every Unity version; percentage transforms are not.
            _screen = new VisualElement();
            _screen.style.position = Position.Absolute;
            _screen.style.left = 0f;
            _screen.style.top = 0f;
            _screen.style.right = 0f;
            _screen.style.bottom = 0f;
            _screen.style.justifyContent = Justify.Center;
            _screen.style.alignItems = Align.Center;
            _screen.style.display = DisplayStyle.None;

            // The wrapper covers the screen, so it must not be what the pointer
            // finds. Only the panel inside it picks.
            _screen.pickingMode = PickingMode.Ignore;

            _panel = new VisualElement();
            _panel.style.flexDirection = FlexDirection.Row;
            _panel.style.paddingLeft = 12f;
            _panel.style.paddingRight = 12f;
            _panel.style.paddingTop = 10f;
            _panel.style.paddingBottom = 12f;
            _panel.style.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 0.95f);

            // Two columns, because the screen is wider than it is tall. Stacked
            // vertically the panel is the sum of its parts; side by side it is
            // the taller of them, and that is the difference between fitting on
            // a 16:9 screen and not.
            _sidebar = new VisualElement();
            _sidebar.style.width = _sidebarWidth;
            _sidebar.style.flexShrink = 0f;
            _sidebar.style.marginRight = ColumnGap;
            _panel.Add(_sidebar);

            _bagColumn = new VisualElement();
            _panel.Add(_bagColumn);

            _sidebar.Add(MakeHeading("Character"));
            _statsList = MakeSection(_sidebar);

            // Two stats per line. Eight of them in a single column is the
            // tallest thing in the panel, and height is the scarce direction.
            _statsList.style.flexDirection = FlexDirection.Row;
            _statsList.style.flexWrap = Wrap.Wrap;

            _sidebar.Add(MakeHeading("Equipped"));
            _slotList = MakeSection(_sidebar);

            // The hotkeys go in the sidebar and the sockets under the bag, so
            // the two new sections land in different columns. Stacking both on
            // one would put the panel back over the edge of a short screen,
            // which is the failure this layout already had to stop having once.
            _sidebar.Add(MakeHeading("Skill bar"));
            _barList = MakeSection(_sidebar);
            _barList.style.flexDirection = FlexDirection.Row;

            _bagColumn.Add(MakeHeading("Bag"));

            _gridRoot = new VisualElement();
            _gridRoot.style.position = Position.Relative;
            _bagColumn.Add(_gridRoot);

            _cellLayer = new VisualElement();
            _cellLayer.style.position = Position.Absolute;
            _cellLayer.style.left = 0f;
            _cellLayer.style.top = 0f;

            // The background must not swallow pointer events: the item layer
            // above it is what a drag talks to.
            _cellLayer.pickingMode = PickingMode.Ignore;
            _gridRoot.Add(_cellLayer);

            _highlight = new VisualElement();
            _highlight.style.position = Position.Absolute;
            _highlight.style.display = DisplayStyle.None;
            _highlight.pickingMode = PickingMode.Ignore;
            _cellLayer.Add(_highlight);

            _itemLayer = new VisualElement();
            _itemLayer.style.position = Position.Absolute;
            _itemLayer.style.left = 0f;
            _itemLayer.style.top = 0f;
            _gridRoot.Add(_itemLayer);

            // What follows the cursor during a drag.
            //
            // A separate element rather than moving the item itself, because a
            // drag can start in either column: an equipment slot lives inside
            // the sidebar and moving it would clip against that column's bounds,
            // while reparenting it mid-drag would take the pointer capture with
            // it. One ghost serves both, and the thing being dragged just dims
            // in place.
            _ghost = new VisualElement();
            _ghost.style.position = Position.Absolute;
            _ghost.style.display = DisplayStyle.None;
            _ghost.style.justifyContent = Justify.Center;
            _ghost.style.alignItems = Align.Center;
            _ghost.style.borderTopWidth = 1f;
            _ghost.style.borderBottomWidth = 1f;
            _ghost.style.borderLeftWidth = 1f;
            _ghost.style.borderRightWidth = 1f;
            _ghost.style.opacity = 0.85f;
            _ghost.pickingMode = PickingMode.Ignore;

            _ghostLabel = new Label(string.Empty);
            _ghostLabel.style.fontSize = 11;
            _ghostLabel.style.whiteSpace = WhiteSpace.Normal;
            _ghostLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _ghostLabel.pickingMode = PickingMode.Ignore;
            _ghost.Add(_ghostLabel);

            _panel.Add(_ghost);

            _bagColumn.Add(MakeHeading("Sockets"));
            _socketList = MakeSection(_bagColumn);

            _message = new Label(string.Empty);
            _message.style.color = new Color(0.72f, 0.55f, 0.45f);
            _message.style.fontSize = 12;
            _message.style.marginTop = 6f;
            _message.style.whiteSpace = WhiteSpace.Normal;
            _bagColumn.Add(_message);

            _screen.Add(_panel);

            // After the panel, so it draws over it. A tooltip behind the thing
            // it describes is the one placement that helps nobody.
            _tooltips = new TooltipView(_screen, RarityColour);

            root.Add(_screen);

            // The panel is sized from the screen, so it has to be told when the
            // screen changes. A resolution change mid-session is rare; a first
            // layout that arrives after this method is not.
            root.RegisterCallback<GeometryChangedEvent>(OnRootResized);

            return true;
        }

        /// <summary>
        /// Forces a re-layout when the window changes size.
        ///
        /// Guarded on the size actually differing, so that geometry events
        /// caused by the panel's own contents cannot start a rebuild that
        /// causes another one.
        /// </summary>
        private void OnRootResized(GeometryChangedEvent evt)
        {
            var size = new Vector2(evt.newRect.width, evt.newRect.height);

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
            VisualElement element, System.Func<ItemTooltip.Text> describe)
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

            ItemTooltip.TryDescribeItem(
                items, skills, itemId, compare ? WornRivalOf(items, itemId) : 0,
                out ItemTooltip.Text text);

            return text;
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

            _cellLayer.Clear();
            _cellLayer.Add(_highlight);

            float width = grid.Width * _cellSize;
            float height = grid.Height * _cellSize;

            // No width is set on the panel at all any more. It is a row of two
            // columns and flexbox already knows how wide that is; a number here
            // could only ever disagree with the grid it is supposed to contain.
            _gridRoot.style.width = width;
            _gridRoot.style.height = height;
            _cellLayer.style.width = width;
            _cellLayer.style.height = height;
            _itemLayer.style.width = width;
            _itemLayer.style.height = height;

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var cell = new VisualElement();
                    cell.style.position = Position.Absolute;
                    cell.style.left = x * _cellSize;
                    cell.style.top = y * _cellSize;
                    cell.style.width = _cellSize - CellGap;
                    cell.style.height = _cellSize - CellGap;
                    cell.style.backgroundColor = new Color(0.12f, 0.13f, 0.16f, 1f);
                    cell.pickingMode = PickingMode.Ignore;

                    _cellLayer.Add(cell);
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

            VisualElement root = _document != null ? _document.rootVisualElement : null;

            float rootWidth = root != null ? root.resolvedStyle.width : float.NaN;
            float rootHeight = root != null ? root.resolvedStyle.height : float.NaN;

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

            int signature = Signature(stats, slots, cells) * 31 +
                            SocketSignature(character, slots);
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
            _statsList.Clear();

            AddKeystoneRow(character);

            for (int s = 0; s < StatBlock.StatCount; s++)
            {
                var stat = (StatKind)s;

                VisualElement row =
                    MakeRow(stat.ToString(), $"{stats.Final.Get(stat):0.##}", null);

                // Just under half, so two sit on a line with the wrap having
                // somewhere to round to.
                row.style.width = Length.Percent(48f);
                row.style.marginRight = Length.Percent(2f);

                _statsList.Add(row);
            }
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

            VisualElement row = MakeRow("Keystone", detail, null);
            row.style.width = Length.Percent(98f);

            _statsList.Add(row);
        }

        /// <summary>
        /// Draws the equipment slots as boxes rather than rows.
        ///
        /// A box is a target you can drop onto and a handle you can drag from,
        /// which a row of text with a button beside it is not. They are laid out
        /// two to a line for the same reason the stats are: height is the scarce
        /// direction on a landscape screen.
        /// </summary>
        private void RebuildSlots(DynamicBuffer<EquippedItem> slots, ItemDatabase items)
        {
            _slotList.Clear();
            _slotTargets.Clear();

            _slotList.style.flexDirection = FlexDirection.Row;
            _slotList.style.flexWrap = Wrap.Wrap;

            // Asked once for the whole rebuild. It is a property of the main
            // hand, not of the off hand, so asking per slot would be asking the
            // same question ten times.
            bool offHandBlocked = EquipmentSlots.IsOffHandBlocked(slots, items);

            for (int i = 0; i < slots.Length; i++)
            {
                EquippedItem slot = slots[i];
                bool blocked = slot.Slot == EquipmentSlot.OffHand && offHandBlocked;

                VisualElement box = MakeSlotBox(slot, items, blocked, out Color border);
                _slotList.Add(box);

                _slotTargets.Add(new SlotTarget
                {
                    Element = box,
                    Slot = slot.Slot,
                    Blocked = blocked,
                    DefaultBorder = border
                });
            }
        }

        private VisualElement MakeSlotBox(
            in EquippedItem slot, ItemDatabase items, bool blocked, out Color border)
        {
            var box = new VisualElement();
            box.style.width = Length.Percent(48f);
            box.style.marginRight = Length.Percent(2f);
            box.style.marginBottom = 4f;
            box.style.height = _slotBoxHeight;
            box.style.paddingLeft = 4f;
            box.style.paddingRight = 4f;
            box.style.justifyContent = Justify.Center;
            box.style.borderTopWidth = 1f;
            box.style.borderBottomWidth = 1f;
            box.style.borderLeftWidth = 1f;
            box.style.borderRightWidth = 1f;

            var name = new Label(slot.Slot.ToString());
            name.style.fontSize = 10;
            name.style.color = new Color(0.50f, 0.55f, 0.62f);
            name.pickingMode = PickingMode.Ignore;
            box.Add(name);

            var value = new Label(blocked ? "two-handed" : slot.HasItem
                ? NameOf(items, slot.ItemId)
                : "empty");
            value.style.fontSize = 11;
            value.style.whiteSpace = WhiteSpace.Normal;
            value.pickingMode = PickingMode.Ignore;
            box.Add(value);

            if (blocked)
            {
                border = new Color(0.32f, 0.24f, 0.24f);
                box.style.backgroundColor = new Color(0.10f, 0.09f, 0.09f, 1f);
                SetBorder(box, border);
                value.style.color = new Color(0.55f, 0.45f, 0.42f);
                return box;
            }

            if (!slot.HasItem)
            {
                border = new Color(0.20f, 0.22f, 0.27f);
                box.style.backgroundColor = new Color(0.10f, 0.11f, 0.14f, 1f);
                SetBorder(box, border);
                value.style.color = new Color(0.45f, 0.47f, 0.52f);
                return box;
            }

            ItemRarity rarity = RarityOf(items, slot.ItemId);
            border = RarityColour(rarity);

            box.style.backgroundColor = Tint(rarity);
            SetBorder(box, border);
            value.style.color = border;

            EquippedItem captured = slot;

            box.RegisterCallback<PointerDownEvent>(evt => BeginSlotDrag(evt, box, captured, items));
            box.RegisterCallback<PointerDownEvent>(OnDragRotate);
            box.RegisterCallback<PointerMoveEvent>(OnDragMove);
            box.RegisterCallback<PointerUpEvent>(OnDragEnd);

            // Double click takes it off, mirroring the double click that puts it
            // on. No target, so the host finds the room.
            box.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount >= 2)
                    SendUnequip(captured.Slot, false, 0, 0, false);
            });

            AttachTooltip(box, () => DescribeItem(captured.ItemId, false));

            return box;
        }

        private static void SetBorder(VisualElement element, Color colour)
        {
            element.style.borderTopColor = colour;
            element.style.borderBottomColor = colour;
            element.style.borderLeftColor = colour;
            element.style.borderRightColor = colour;
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
            _itemLayer.Clear();

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

                _itemLayer.Add(MakeItem(
                    bag, item, instance, placement, width, height,
                    NameOf(items, instance.ItemId), items));
            }
        }

        private VisualElement MakeItem(
            Entity bag,
            Entity item,
            in ItemInstance instance,
            in ItemGridPlacement placement,
            int width,
            int height,
            string label,
            ItemDatabase items)
        {
            var element = new VisualElement();
            element.style.position = Position.Absolute;
            element.style.left = placement.OriginX * _cellSize;
            element.style.top = placement.OriginY * _cellSize;
            element.style.width = width * _cellSize - CellGap;
            element.style.height = height * _cellSize - CellGap;
            element.style.backgroundColor = Tint(instance.Rarity);
            element.style.borderTopWidth = 1f;
            element.style.borderBottomWidth = 1f;
            element.style.borderLeftWidth = 1f;
            element.style.borderRightWidth = 1f;

            Color border = RarityColour(instance.Rarity);
            element.style.borderTopColor = border;
            element.style.borderBottomColor = border;
            element.style.borderLeftColor = border;
            element.style.borderRightColor = border;

            element.style.justifyContent = Justify.Center;
            element.style.alignItems = Align.Center;

            var text = new Label(label);
            text.style.color = border;
            text.style.fontSize = 11;
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            text.pickingMode = PickingMode.Ignore;
            element.Add(text);

            Entity capturedBag = bag;
            Entity capturedItem = item;
            int capturedId = instance.ItemId;
            ItemGridPlacement capturedPlacement = placement;

            ItemDatabase capturedItems = items;

            element.RegisterCallback<PointerDownEvent>(evt => BeginDrag(
                evt, element, capturedBag, capturedItem, capturedId,
                capturedItems, capturedPlacement));

            element.RegisterCallback<PointerDownEvent>(OnDragRotate);
            element.RegisterCallback<PointerMoveEvent>(OnDragMove);
            element.RegisterCallback<PointerUpEvent>(OnDragEnd);

            AttachTooltip(element, () => DescribeItem(capturedId, true));

            // Double click equips, the way it does in every game this one is
            // trying to feel like. No slot travels with it, so the host picks —
            // an empty allowed slot first, which is what makes double-clicking
            // a second ring fill the other hand instead of replacing the first.
            element.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount >= 2)
                    SendEquipAuto(capturedItem);
            });

            return element;
        }

        // ─────────────────────────────────────────────────────────────────
        // Drag and drop
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Starts a drag from an item lying in the bag.
        /// </summary>
        private void BeginDrag(
            PointerDownEvent evt,
            VisualElement element,
            Entity bag,
            Entity item,
            int itemId,
            ItemDatabase items,
            in ItemGridPlacement placement)
        {
            if (evt.button != 0 || _drag.Active)
                return;

            StartDrag(evt, element, items, itemId, item, bag, placement.IsRotated);

            _drag.Corner = new Vector2(
                placement.OriginX * _cellSize, placement.OriginY * _cellSize);
            _drag.TargetX = placement.OriginX;
            _drag.TargetY = placement.OriginY;

            // Grabbed where the player actually took hold of it, so the square
            // it lands on is the one they are looking at.
            _drag.GrabOffset = evt.localPosition;

            evt.StopPropagation();
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
            PointerDownEvent evt, VisualElement box, EquippedItem slot, ItemDatabase items)
        {
            if (evt.button != 0 || _drag.Active || !slot.HasItem)
                return;

            if (!TryGetCharacter(out _, out Entity bag))
                return;

            StartDrag(evt, box, items, slot.ItemId, slot.Item, bag, false);

            _drag.Source2 = DragSource.EquipmentSlot;
            _drag.SourceSlot = slot.Slot;

            if (GridFit.TryGetFootprint(items, slot.ItemId, false, out int width, out int height))
                _drag.GrabOffset = new Vector2(width * _cellSize, height * _cellSize) * 0.5f;

            evt.StopPropagation();
        }

        /// <summary>
        /// Everything the two entry points share: what is being dragged, the
        /// ghost that shows it, and the pointer capture that keeps the events
        /// coming to one element.
        /// </summary>
        private void StartDrag(
            PointerDownEvent evt,
            VisualElement source,
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
                PointerId = evt.pointerId,

                // Asked once here rather than at every hover: a pointer move
                // touches every socket cell on screen, and none of them should
                // be doing a database lookup to decide what colour to be.
                IsGem = GemKindOf(items, itemId) != GemKind.None,
                IsActiveGem = GemKindOf(items, itemId) == GemKind.Active
            };

            source.CapturePointer(evt.pointerId);
            source.style.opacity = 0.35f;

            // Pointer capture means the elements underneath stop seeing the
            // cursor, so the leave event that would normally hide this never
            // arrives.
            HideTooltip();

            ShapeGhost(items);

            _ghost.style.display = DisplayStyle.Flex;
            _ghost.BringToFront();

            _highlight.style.display = DisplayStyle.Flex;
            _highlight.BringToFront();
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

            _ghost.style.width = width * _cellSize - CellGap;
            _ghost.style.height = height * _cellSize - CellGap;
            _ghost.style.backgroundColor = Tint(rarity);
            SetBorder(_ghost, border);

            _ghostLabel.text = NameOf(items, _drag.ItemId);
            _ghostLabel.style.color = border;
        }

        private void OnDragMove(PointerMoveEvent evt)
        {
            if (!_drag.Active)
                return;

            MoveGhost(evt.position);
            UpdateTargets(evt.position);

            evt.StopPropagation();
        }

        private void MoveGhost(Vector2 pointer)
        {
            Vector2 corner = (Vector2)_panel.WorldToLocal(pointer) - _drag.GrabOffset;

            _ghost.style.left = corner.x;
            _ghost.style.top = corner.y;
        }

        /// <summary>
        /// Right button while dragging turns the item on its side.
        ///
        /// Not a key, deliberately. Pointer capture guarantees this event
        /// reaches the element being dragged; a keyboard shortcut in a runtime
        /// UI Toolkit panel depends on that panel holding focus, which is one
        /// more thing that can quietly not be true. Casting is suppressed while
        /// the panel is open, so the button is free.
        /// </summary>
        private void OnDragRotate(PointerDownEvent evt)
        {
            if (!_drag.Active || evt.button != 1)
                return;

            evt.StopPropagation();

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
            Vector2 corner = (Vector2)_gridRoot.WorldToLocal(pointer) - _drag.GrabOffset;

            _drag.Corner = corner;
            UpdateHighlight(corner);

            for (int i = 0; i < _slotTargets.Count; i++)
            {
                SlotTarget target = _slotTargets[i];
                Color colour = target.DefaultBorder;

                if (target.Element.worldBound.Contains(pointer))
                {
                    colour = SlotAccepts(target)
                        ? new Color(0.35f, 0.80f, 0.40f)
                        : new Color(0.85f, 0.30f, 0.30f);
                }

                SetBorder(target.Element, colour);
            }

            for (int i = 0; i < _socketTargets.Count; i++)
            {
                SocketTarget target = _socketTargets[i];
                Color colour = target.DefaultBorder;

                if (target.Element.worldBound.Contains(pointer))
                {
                    // A gem out of the bag, into a hole that is free. A gem
                    // already in a socket moves by coming out first, which is
                    // what the host would say too.
                    bool accepts = _drag.Source2 == DragSource.Bag &&
                                   _drag.IsGem && target.IsEmpty;

                    colour = accepts
                        ? new Color(0.35f, 0.80f, 0.40f)
                        : new Color(0.85f, 0.30f, 0.30f);
                }

                SetBorder(target.Element, colour);
            }

            for (int i = 0; i < _barTargets.Count; i++)
            {
                BarTarget target = _barTargets[i];
                Color colour = target.DefaultBorder;

                if (target.Element.worldBound.Contains(pointer))
                {
                    // Only an active gem, and only one already in a socket: a
                    // hotkey points at a hole, so there has to be a hole.
                    bool accepts = _drag.Source2 == DragSource.Socket && _drag.IsActiveGem;

                    colour = accepts
                        ? new Color(0.35f, 0.80f, 0.40f)
                        : new Color(0.85f, 0.30f, 0.30f);
                }

                SetBorder(target.Element, colour);
            }
        }

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

            _highlight.style.left = Mathf.Clamp(x, 0, Mathf.Max(0, grid.Width - 1)) * _cellSize;
            _highlight.style.top = Mathf.Clamp(y, 0, Mathf.Max(0, grid.Height - 1)) * _cellSize;
            _highlight.style.width = width * _cellSize - CellGap;
            _highlight.style.height = height * _cellSize - CellGap;
            _highlight.style.backgroundColor = fits
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
        private void OnDragEnd(PointerUpEvent evt)
        {
            if (!_drag.Active || evt.button != 0)
                return;

            evt.StopPropagation();

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

            bool overGrid = _gridRoot.worldBound.Contains(evt.position);
            EquipmentSlot slot = default;
            bool overSlot = TryFindSlotUnder(evt.position, ref slot);

            bool overSocket = TryFindSocketUnder(
                evt.position, out Entity socketGear, out int socketIndex);
            bool overBar = TryFindBarUnder(evt.position, out int barIndex);

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
                if (source == DragSource.Socket && isActiveGem)
                    SendBindBar(sourceGear, sourceSocket, barIndex);
                else
                    ShowMessage("Only an active gem in a socket can go on a hotkey.");

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
            {
                _drag.Source.ReleasePointer(_drag.PointerId);
                _drag.Source.style.opacity = 1f;
            }

            for (int i = 0; i < _slotTargets.Count; i++)
                SetBorder(_slotTargets[i].Element, _slotTargets[i].DefaultBorder);

            for (int i = 0; i < _socketTargets.Count; i++)
                SetBorder(_socketTargets[i].Element, _socketTargets[i].DefaultBorder);

            for (int i = 0; i < _barTargets.Count; i++)
                SetBorder(_barTargets[i].Element, _barTargets[i].DefaultBorder);

            _ghost.style.display = DisplayStyle.None;
            _highlight.style.display = DisplayStyle.None;

            _drag = default;
            _lastSignature = int.MinValue;
        }

        private bool TryFindSocketUnder(Vector2 position, out Entity gear, out int socketIndex)
        {
            gear = Entity.Null;
            socketIndex = 0;

            for (int i = 0; i < _socketTargets.Count; i++)
            {
                if (!_socketTargets[i].Element.worldBound.Contains(position))
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
                if (!_barTargets[i].Element.worldBound.Contains(position))
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
                if (!_slotTargets[i].Element.worldBound.Contains(position))
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
            _socketList.Clear();
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

                _socketList.Add(MakeSocketRow(gear, sockets, items, NameOf(items, slots[i].ItemId)));
            }

            if (_socketList.childCount == 0)
                _socketList.Add(MakeRow("Nothing worn has sockets", string.Empty, null));
        }

        private VisualElement MakeSocketRow(
            Entity gear, DynamicBuffer<GearSocket> sockets, ItemDatabase items, string label)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4f;

            var name = new Label(label);
            name.style.fontSize = 11;
            name.style.color = new Color(0.66f, 0.68f, 0.72f);
            name.style.width = 96f;
            name.pickingMode = PickingMode.Ignore;
            row.Add(name);

            for (int i = 0; i < sockets.Length; i++)
            {
                // The bar goes BEFORE the cell it links backwards to, so the
                // row reads left to right the way the link does.
                if (i > 0)
                    row.Add(MakeLink(sockets[i - 1].LinkGroup == sockets[i].LinkGroup));

                row.Add(MakeSocketCell(gear, sockets[i], items));
            }

            return row;
        }

        /// <summary>
        /// The bar between two neighbouring sockets, drawn only when they share
        /// a group. An unlinked pair still gets the element, transparent, so the
        /// cells stay on the same pitch whether they are joined or not.
        /// </summary>
        private static VisualElement MakeLink(bool linked)
        {
            var link = new VisualElement();
            link.style.width = LinkBarWidth;
            link.style.height = 3f;
            link.pickingMode = PickingMode.Ignore;

            link.style.backgroundColor = linked
                ? new Color(0.75f, 0.70f, 0.42f)
                : new Color(0f, 0f, 0f, 0f);

            return link;
        }

        private VisualElement MakeSocketCell(Entity gear, GearSocket socket, ItemDatabase items)
        {
            var cell = new VisualElement();
            cell.style.width = SocketCellSize;
            cell.style.height = SocketCellSize;
            cell.style.justifyContent = Justify.Center;
            cell.style.alignItems = Align.Center;
            cell.style.borderTopWidth = 1f;
            cell.style.borderBottomWidth = 1f;
            cell.style.borderLeftWidth = 1f;
            cell.style.borderRightWidth = 1f;

            Color border = new Color(0.30f, 0.32f, 0.38f);
            var mark = new Label(string.Empty);
            mark.style.fontSize = 10;
            mark.pickingMode = PickingMode.Ignore;

            if (socket.IsWelded)
            {
                // The weapon's own attack. Its own colour rather than a rarity,
                // because it has no gem to take a rarity from — and a letter of
                // its own, because "A" would promise a gem the player could pull
                // out and put somewhere else.
                border = new Color(0.62f, 0.58f, 0.42f);
                cell.style.backgroundColor = new Color(0.20f, 0.18f, 0.12f, 1f);

                mark.text = "W";
                mark.style.color = border;

                int capturedSocket = socket.SocketIndex;
                ItemDatabase capturedDatabase = items;
                Entity capturedWeapon = gear;

                cell.RegisterCallback<PointerDownEvent>(evt => BeginWeldedDrag(
                    evt, cell, capturedWeapon, capturedSocket, capturedDatabase));
                cell.RegisterCallback<PointerMoveEvent>(OnDragMove);
                cell.RegisterCallback<PointerUpEvent>(OnDragEnd);

                // A welded skill has no gem to describe, so the tooltip comes
                // from the skill database instead. Same box, same words.
                int capturedSkill = socket.WeldedSkillId;
                AttachTooltip(cell, () => DescribeSkillId(capturedSkill));
            }
            else if (!socket.IsEmpty &&
                GemSockets.TryDescribeGem(
                    _entityManager, items, socket.InsertedGem,
                    out GemKind kind, out _, out SkillModifierBlob support))
            {
                int itemId = _entityManager.GetComponentData<ItemInstance>(socket.InsertedGem).ItemId;
                ItemRarity rarity = RarityOf(items, itemId);

                border = RarityColour(rarity);
                cell.style.backgroundColor = Tint(rarity);

                // One letter, because a socket cell is twenty pixels across
                // and that is what fits. An active gem says so; a support says
                // WHEN it acts, which is the thing a player has to know to
                // arrange them — a fork that splits on impact and a multicast
                // that fires three at once are both "support" and behave
                // nothing alike.
                mark.text = kind == GemKind.Active
                    ? "A"
                    : SkillModifiers.Marker(SkillModifiers.PhaseOf(support.Kind));
                mark.style.color = border;

                Entity capturedGem = socket.InsertedGem;
                int capturedIndex = socket.SocketIndex;
                ItemDatabase capturedItems = items;
                Entity capturedGear = gear;

                cell.RegisterCallback<PointerDownEvent>(evt => BeginSocketDrag(
                    evt, cell, capturedGear, capturedIndex, capturedGem, capturedItems));
                cell.RegisterCallback<PointerMoveEvent>(OnDragMove);
                cell.RegisterCallback<PointerUpEvent>(OnDragEnd);

                // The letter in the cell says which kind of gem it is; the
                // tooltip is where the rest of the sentence lives.
                AttachTooltip(cell, () => DescribeItem(itemId, false));
            }
            else
            {
                cell.style.backgroundColor = new Color(0.09f, 0.10f, 0.13f, 1f);
            }

            SetBorder(cell, border);
            cell.Add(mark);

            _socketTargets.Add(new SocketTarget
            {
                Element = cell,
                Gear = gear,
                SocketIndex = socket.SocketIndex,
                IsEmpty = socket.IsEmpty,
                DefaultBorder = border
            });

            return cell;
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
            _barList.Clear();
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
                _barList.Add(MakeBarBox(i, bar[i], worn, items, skills));
        }

        private VisualElement MakeBarBox(
            int index,
            in SkillSlot slot,
            DynamicBuffer<EquippedItem> worn,
            ItemDatabase items,
            SkillDatabase skills)
        {
            var box = new VisualElement();
            box.style.flexGrow = 1f;
            box.style.height = BarBoxHeight;
            box.style.marginRight = 3f;
            box.style.justifyContent = Justify.Center;
            box.style.alignItems = Align.Center;
            box.style.borderTopWidth = 1f;
            box.style.borderBottomWidth = 1f;
            box.style.borderLeftWidth = 1f;
            box.style.borderRightWidth = 1f;

            Color border = new Color(0.24f, 0.26f, 0.32f);
            string text = HotkeyName(index);

            if (GemSockets.IsWorn(worn, slot.Gear) &&
                GemSockets.TryResolveActive(
                    _entityManager, items, skills, slot.Gear, slot.SocketIndex,
                    out int skillIndex, out int linkGroup))
            {
                border = new Color(0.45f, 0.62f, 0.85f);
                text = $"{HotkeyName(index)}  {skills.NameOf(skillIndex)}";

                // A trigger gem linked beside this active takes the key away,
                // and the player has to be able to see that before they press it
                // twenty times. Asked of the same GemSockets the cast system
                // asks, so the panel cannot claim a key works when the host has
                // already decided it does not.
                FixedList512Bytes<SkillModifierBlob> supports =
                    GemSockets.GatherSupports(_entityManager, items, slot.Gear, linkGroup);

                if (GemSockets.TryGetTrigger(supports, out SkillModifierBlob trigger))
                {
                    border = new Color(0.85f, 0.66f, 0.38f);
                    text = $"{HotkeyName(index)}  {skills.NameOf(skillIndex)}\n" +
                           $"auto: {trigger.TriggerCondition}";
                }

                int capturedSkill = skillIndex;
                AttachTooltip(box, () => DescribeSkillIndex(capturedSkill));
            }

            box.style.backgroundColor = new Color(0.10f, 0.11f, 0.14f, 1f);
            SetBorder(box, border);

            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.color = border;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.pickingMode = PickingMode.Ignore;
            box.Add(label);

            int captured = index;
            box.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount >= 2)
                    SendClearBar(captured);
            });

            _barTargets.Add(new BarTarget
            {
                Element = box,
                BarSlotIndex = index,
                DefaultBorder = border
            });

            return box;
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
            PointerDownEvent evt,
            VisualElement cell,
            Entity gear,
            int socketIndex,
            ItemDatabase items)
        {
            if (evt.button != 0 || _drag.Active)
                return;

            if (!TryGetCharacter(out _, out Entity bag))
                return;

            if (!_entityManager.HasComponent<ItemInstance>(gear))
                return;

            // The ghost is shaped from the weapon, because the thing being
            // dragged has no shape of its own. What matters is that something
            // follows the pointer at all.
            int gearId = _entityManager.GetComponentData<ItemInstance>(gear).ItemId;

            StartDrag(evt, cell, items, gearId, Entity.Null, bag, false);

            // Set after the fact, because both are read off the item id and the
            // id here belongs to the sword rather than to the attack in it. What
            // is being dragged behaves as an active gem in a socket, which is
            // exactly what the hotkey drop is looking for.
            _drag.IsGem = true;
            _drag.IsActiveGem = true;

            _drag.Source2 = DragSource.Socket;
            _drag.SourceGear = gear;
            _drag.SourceSocket = socketIndex;

            evt.StopPropagation();
        }

        private void BeginSocketDrag(
            PointerDownEvent evt,
            VisualElement cell,
            Entity gear,
            int socketIndex,
            Entity gem,
            ItemDatabase items)
        {
            if (evt.button != 0 || _drag.Active)
                return;

            if (!TryGetCharacter(out _, out Entity bag))
                return;

            int itemId = _entityManager.GetComponentData<ItemInstance>(gem).ItemId;

            StartDrag(evt, cell, items, itemId, gem, bag, false);

            _drag.Source2 = DragSource.Socket;
            _drag.SourceGear = gear;
            _drag.SourceSocket = socketIndex;

            if (GridFit.TryGetFootprint(items, itemId, false, out int width, out int height))
                _drag.GrabOffset = new Vector2(width * _cellSize, height * _cellSize) * 0.5f;

            evt.StopPropagation();
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

        private static VisualElement MakeSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.style.marginBottom = 8f;
            parent.Add(section);
            return section;
        }

        private static Label MakeHeading(string text)
        {
            var heading = new Label(text);
            heading.style.color = new Color(0.62f, 0.72f, 0.85f);
            heading.style.fontSize = 15;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginBottom = 4f;
            return heading;
        }

        private static VisualElement MakeRow(string label, string detail, Button action)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2f;

            var name = new Label(label);
            name.style.color = new Color(0.88f, 0.89f, 0.92f);
            name.style.fontSize = 13;
            name.style.flexGrow = 1f;
            row.Add(name);

            if (!string.IsNullOrEmpty(detail))
            {
                var value = new Label(detail);
                value.style.color = new Color(0.66f, 0.68f, 0.72f);
                value.style.fontSize = 13;
                value.style.marginRight = 6f;
                row.Add(value);
            }

            if (action != null)
                row.Add(action);

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
            if (!_hasWorld)
                return;

            _characterQuery.Dispose();
            _itemDatabaseQuery.Dispose();
            _skillDatabaseQuery.Dispose();
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
            public VisualElement Element;
            public Entity Gear;
            public int SocketIndex;
            public bool IsEmpty;
            public Color DefaultBorder;
        }

        /// <summary>One hotkey box.</summary>
        private struct BarTarget
        {
            public VisualElement Element;
            public int BarSlotIndex;
            public Color DefaultBorder;
        }

        private struct SlotTarget
        {
            public VisualElement Element;
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
            /// The element holding the pointer capture. Not the thing that
            /// moves — the ghost does that — but the thing every pointer event
            /// is routed to until the button comes up.
            /// </summary>
            public VisualElement Source;

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
            public int PointerId;
            public Vector2 GrabOffset;

            /// <summary>Top-left of the ghost, in grid-local pixels.</summary>
            public Vector2 Corner;

            public int TargetX;
            public int TargetY;
        }
    }
}
