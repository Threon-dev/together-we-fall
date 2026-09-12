using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;
using TogetherWeFall.Inventory;
using TogetherWeFall.Lobby;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;
using TogetherWeFall.Skills;

namespace TogetherWeFall.UI
{
    /// <summary>
    /// Everything the lobby puts on screen: the prompt beside an NPC, the shop,
    /// the forge, the portal and the damage meter.
    ///
    /// One panel class for four services rather than four, because they are the
    /// same panel with different contents — a frame, a heading, a grid or a list,
    /// and a message line for the refusals. Four classes would have been four
    /// copies of the part that reads a character out of ECS and four copies of
    /// the grid drawing, which is exactly the part that must not drift.
    ///
    /// It owns no game state. Every refresh reads the world — containers, cells,
    /// the balance, the meter — and draws what it found; every click appends a
    /// request and waits to be told what happened. The one thing it does own is
    /// which window is open, and that is client state in the same way the
    /// inventory panel's visibility is: in coop two players can be in the same
    /// shop, because nothing about being in one is written down anywhere they
    /// share.
    ///
    /// ponytail: buying and selling are a double-click, not a drag between two
    /// grids. The drag machinery in InventoryUI is four hundred lines of pointer
    /// capture, ghosts and rotation, and a shop needs none of it — an item goes
    /// wherever it fits and the player rearranges afterwards. Add the drag when
    /// placing a bought item precisely starts to matter.
    /// </summary>
    public sealed class LobbyUI : MonoBehaviour
    {
        private const float CellGap = 2f;
        private const float MinCellSize = 12f;
        private const float MaxCellSize = 34f;

        /// <summary>How much of the screen the panel may take.</summary>
        private const float ScreenFraction = 0.9f;

        /// <summary>Chrome around the grids: padding, headings, the message line.</summary>
        private const float HorizontalChrome = 64f;
        private const float VerticalChrome = 190f;

        /// <summary>Seconds between meter redraws. Numbers, not a game loop.</summary>
        private const float MeterInterval = 0.2f;

        /// <summary>Bars in the little damage-over-time strip.</summary>
        private const int MeterBuckets = 24;

        /// <summary>How close to a dummy the meter shows itself.</summary>
        private const float MeterRange = 14f;

        [SerializeField] private Canvas _canvas;

        private PlayerInputReader _input;
        private Transform _player;
        private int _playerId;

        private EntityManager _entityManager;
        private EntityQuery _characterQuery;
        private EntityQuery _itemDatabaseQuery;
        private EntityQuery _npcQuery;
        private EntityQuery _meterQuery;
        private EntityQuery _skillDatabaseQuery;
        private EntityQuery _itemSetDatabaseQuery;
        private bool _hasWorld;

        private RectTransform _screen;
        private RectTransform _panel;
        private TextMeshProUGUI _heading;
        private TextMeshProUGUI _message;
        private RectTransform _body;
        private TextMeshProUGUI _prompt;
        private RectTransform _meterPanel;
        private RectTransform _meterRows;
        private RectTransform _meterGraph;

        /// <summary>What says what a thing is while the pointer is over it.</summary>
        private TooltipView _tooltips;

        private NpcServiceType _session = NpcServiceType.None;
        private Entity _npc = Entity.Null;

        /// <summary>What the forge is pointed at. Null when nothing is chosen.</summary>
        private Entity _craftTarget = Entity.Null;

        private int _craftSocket = 1;

        private int _lastSignature = int.MinValue;
        private float _meterTimer;
        private float _messageTimer;

        private readonly List<RectTransform> _meterBars = new List<RectTransform>();

        /// <summary>
        /// Reused between redraws. A panel that allocates an array four times a
        /// second is a panel that shows up in a profile of a game it is only
        /// describing.
        /// </summary>
        private readonly float[] _buckets = new float[MeterBuckets];

        /// <summary>
        /// True while a service window is open, so the action publisher stops
        /// sending casts and interactions. Clicking "buy" is a left click and so
        /// is the first skill.
        /// </summary>
        public bool IsCapturingInput => _session != NpcServiceType.None;

        public void Initialize(PlayerInputReader input, Transform player, int playerId)
        {
            _input = input;
            _player = player;
            _playerId = playerId;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError(
                    $"[{nameof(LobbyUI)}] ECS world is unavailable — the lobby panels have " +
                    "nothing to read.", this);
                return;
            }

            _entityManager = world.EntityManager;

            _characterQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerCharacter>(),
                ComponentType.ReadOnly<CarriedBag>());

            _itemDatabaseQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ItemDatabase>());

            _npcQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<NpcService>(),
                ComponentType.ReadOnly<Interaction.InteractionRadius>(),
                ComponentType.ReadOnly<LocalTransform>());

            _meterQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<DamageMeter>(),
                ComponentType.ReadOnly<LocalTransform>());

            // For the tooltips: half of what a vendor sells is a gem, and a gem
            // is a skill nobody can read off the tile.
            _skillDatabaseQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SkillDatabase>());

            // For the tooltips as well: the shop is where the piece missing from
            // a set is actually bought, so it is the one place "3 of 4 worn" has
            // to be readable.
            _itemSetDatabaseQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ItemSetDatabase>());

            _hasWorld = true;
        }

        private void Update()
        {
            if (!_hasWorld || !EnsureTree())
                return;

            if (_input != null && _input.WasCancelPressed())
                CloseSession();

            DrainSessions();
            RefreshPrompt();

            if (_session != NpcServiceType.None)
                RefreshIfChanged();

            RefreshMeter();
            ExpireMessage();
        }

        // ─────────────────────────────────────────────────────────────────
        // Sessions
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens whatever the host says this player just walked up to.
        ///
        /// Drained rather than read, because the queue is the announcement: a
        /// session that was opened and closed in the same frame is one the
        /// player pressed the key for, and leaving it in the buffer would open
        /// the panel again on the next.
        /// </summary>
        private void DrainSessions()
        {
            if (!TryGetCharacter(out Entity character, out _))
                return;

            DynamicBuffer<NpcSessionOpened> sessions =
                _entityManager.GetBuffer<NpcSessionOpened>(character);

            if (sessions.Length == 0)
                return;

            NpcSessionOpened latest = sessions[sessions.Length - 1];
            sessions.Clear();

            // No "press E again to close": while a panel is open the action
            // publisher stops sending interactions at all, so a second press
            // never reaches the host to become a second session. Esc closes,
            // and it is the one key that cannot also mean something else.
            _session = latest.Type;
            _npc = latest.Npc;
            _craftTarget = Entity.Null;
            _craftSocket = 1;
            _lastSignature = int.MinValue;

            _screen.gameObject.SetActive(true);
        }

        private void CloseSession()
        {
            if (_session == NpcServiceType.None)
                return;

            _session = NpcServiceType.None;
            _npc = Entity.Null;
            _craftTarget = Entity.Null;
            _screen.gameObject.SetActive(false);
            _lastSignature = int.MinValue;
        }

        // ─────────────────────────────────────────────────────────────────
        // Prompt
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The "press E" hint over the nearest NPC in reach.
        ///
        /// It works out what is in reach with the same rule the host uses — the
        /// radius on the target, not on the player — but it does not decide
        /// anything: pressing the key still sends a position and lets
        /// InteractionResolveSystem pick. A prompt that disagreed with the
        /// resolver would be a prompt that lied, which is why it reads the same
        /// two numbers rather than a guess of its own.
        /// </summary>
        private void RefreshPrompt()
        {
            if (_session != NpcServiceType.None || _player == null)
            {
                _prompt.gameObject.SetActive(false);
                return;
            }

            NpcServiceType nearest = NearestService(out float _);

            if (nearest == NpcServiceType.None)
            {
                _prompt.gameObject.SetActive(false);
                return;
            }

            _prompt.text = $"E — {PromptFor(nearest)}";
            _prompt.gameObject.SetActive(true);
        }

        private NpcServiceType NearestService(out float distanceSq)
        {
            distanceSq = float.MaxValue;
            NpcServiceType best = NpcServiceType.None;

            if (_npcQuery.IsEmptyIgnoreFilter)
                return best;

            float3 position = _player.position;

            using NativeArray<NpcService> services =
                _npcQuery.ToComponentDataArray<NpcService>(Allocator.Temp);
            using NativeArray<Interaction.InteractionRadius> radii =
                _npcQuery.ToComponentDataArray<Interaction.InteractionRadius>(Allocator.Temp);
            using NativeArray<LocalTransform> transforms =
                _npcQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < services.Length; i++)
            {
                float radius = radii[i].Value;
                float d = math.distancesq(position, transforms[i].Position);

                if (d > radius * radius || d >= distanceSq)
                    continue;

                distanceSq = d;
                best = services[i].Type;
            }

            return best;
        }

        private static string PromptFor(NpcServiceType type)
        {
            switch (type)
            {
                case NpcServiceType.Vendor: return "Trade";
                case NpcServiceType.Crafting: return "Craft";
                case NpcServiceType.TrainingGround: return "Training";
                case NpcServiceType.DungeonPortal: return "Descend";
                default: return "Talk";
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Tree
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the frame the first time it is needed.
        ///
        /// Lazily rather than in Initialize, because the bootstrap that calls
        /// Initialize deliberately runs before everything else in the scene.
        /// </summary>
        private bool EnsureTree()
        {
            if (_panel != null)
                return true;

            if (_canvas == null)
                return false;

            var root = (RectTransform)_canvas.transform;

            _prompt = Ugui.Text(
                root, "Prompt", 18f, new Color(0.92f, 0.88f, 0.62f));
            Ugui.Place(_prompt.rectTransform, left: 0f, right: 0f, bottom: 96f, height: 26f);
            _prompt.gameObject.SetActive(false);

            BuildMeterPanel(root);

            // The wrapper covers the screen, so it must not be what the pointer
            // finds: a plain node draws nothing and so catches nothing. Only the
            // panel inside it picks.
            _screen = Ugui.Node(root, "Service");

            _panel = Ugui.Box(
                _screen, "Panel", new Color(0.06f, 0.07f, 0.09f, 0.96f)).rectTransform;

            // Centred by its anchors and as big as its contents, which is what
            // the flexbox centring it replaces was for. uGUI has no
            // justify-content, but it does have "middle of the parent".
            Ugui.Place(_panel);
            Ugui.Column(_panel, spacing: 2f, padding: new RectOffset(14, 14, 10, 12));
            Ugui.Fit(_panel);

            _heading = MakeHeading(_panel, "Lobby");

            _body = Ugui.Node(_panel, "Body");
            Ugui.Row(_body, spacing: 14f);

            _message = Ugui.Text(
                _panel, "Message", 12f, new Color(0.95f, 0.62f, 0.55f),
                TextAlignmentOptions.TopLeft);

            TextMeshProUGUI hint = Ugui.Text(
                _panel, "Hint", 11f, new Color(0.55f, 0.58f, 0.64f),
                TextAlignmentOptions.TopLeft);
            hint.text = "Esc — close";

            Ugui.Border(_panel, new Color(0.30f, 0.32f, 0.38f), 1f);

            _screen.gameObject.SetActive(false);

            // After the panel, so it draws over it. A tooltip behind the thing
            // it describes is the one placement that helps nobody.
            _tooltips = new TooltipView(_screen, RarityColour);

            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // Tooltips
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// What an item on a shelf, in the bag or in a socket says.
        ///
        /// The same describer the inventory panel uses, told the same two
        /// things: the item, and what the player is already wearing that it
        /// would replace. A shop that described a helmet differently from the
        /// bag it lands in would be the second answer this whole class exists
        /// to avoid.
        /// </summary>
        private ItemTooltip.Text DescribeItem(int itemId)
        {
            if (!TryGetItems(out ItemDatabase items))
                return default;

            TryGetSkills(out SkillDatabase skills);

            TryGetSets(out ItemSetDatabase sets);

            ItemTooltip.TryDescribeItem(
                items, skills, itemId, WornRivalOf(items, itemId), out ItemTooltip.Text text,
                sets: sets, equippedFromSet: EquippedFromSet(sets, itemId));

            return text;
        }

        /// <summary>What a welded socket says, by skill id.</summary>
        private ItemTooltip.Text DescribeSkillId(int skillId)
        {
            if (!TryGetSkills(out SkillDatabase skills))
                return default;

            ItemTooltip.TryDescribeSkill(
                skills, skills.IndexOf(skillId), out ItemTooltip.Text text);

            return text;
        }

        /// <summary>
        /// What buying this would replace, by id, or zero. The rule lives in
        /// EquipmentSlots; this is the part that knows whose character it is.
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

        private bool TryGetSets(out ItemSetDatabase sets)
        {
            sets = default;

            if (_itemSetDatabaseQuery.IsEmptyIgnoreFilter)
                return false;

            sets = _itemSetDatabaseQuery.GetSingleton<ItemSetDatabase>();
            return sets.IsCreated;
        }

        /// <summary>
        /// How many pieces of this item's set the shopper is wearing. Counted by
        /// ItemSets, like the inventory panel's copy of this question.
        /// </summary>
        private int EquippedFromSet(ItemSetDatabase sets, int itemId)
        {
            if (!TryGetCharacter(out Entity character, out _))
                return 0;

            return ItemSets.EquippedCount(_entityManager, character, sets, itemId);
        }

        private bool TryGetSkills(out SkillDatabase skills)
        {
            skills = default;

            if (_skillDatabaseQuery.IsEmptyIgnoreFilter)
                return false;

            skills = _skillDatabaseQuery.GetSingleton<SkillDatabase>();
            return skills.Value.IsCreated;
        }

        private void BuildMeterPanel(RectTransform root)
        {
            _meterPanel = Ugui.Box(
                root, "DamageMeter", new Color(0.05f, 0.06f, 0.08f, 0.86f)).rectTransform;

            // Pinned to the corner at a fixed width; only the height follows what
            // is in it.
            Ugui.Place(_meterPanel, right: 12f, top: 12f, width: 250f);
            Ugui.Column(_meterPanel, spacing: 4f, padding: new RectOffset(8, 8, 6, 8));
            Ugui.Fit(_meterPanel, horizontal: false);

            MakeHeading(_meterPanel, "Damage meter");

            _meterGraph = Ugui.Node(_meterPanel, "Graph");
            Ugui.Size(_meterGraph, height: 44f);

            // The bars keep their own heights — that is the whole graph — so the
            // row spaces them and aligns them to the bottom without touching how
            // tall each one is.
            HorizontalLayoutGroup graph = Ugui.Row(_meterGraph, spacing: 1f);
            graph.childControlHeight = false;
            graph.childAlignment = TextAnchor.LowerLeft;

            for (int i = 0; i < MeterBuckets; i++)
            {
                Image bar = Ugui.Box(
                    _meterGraph, $"Bucket{i}", new Color(0.45f, 0.72f, 0.95f), picks: false);

                Ugui.Size(bar.rectTransform, height: 1f, grow: 1f);
                _meterBars.Add(bar.rectTransform);
            }

            _meterRows = Ugui.Node(_meterPanel, "Rows");
            Ugui.Column(_meterRows, spacing: 2f);
            Ugui.Fit(_meterRows, horizontal: false);

            Ugui.Button(_meterPanel, "Reset", "Reset", RequestMeterReset);

            Ugui.Border(_meterPanel, new Color(0.26f, 0.28f, 0.34f), 1f);

            _meterPanel.gameObject.SetActive(false);
        }

        // ─────────────────────────────────────────────────────────────────
        // Refresh
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the open panel when what it shows has changed.
        ///
        /// A signature rather than a rebuild per frame: a rebuild destroys the
        /// button under the cursor, and a panel that rebuilds itself sixty times
        /// a second is a panel whose buttons only work by luck.
        /// </summary>
        private void RefreshIfChanged()
        {
            if (!TryGetCharacter(out Entity character, out Entity bag) ||
                !TryGetItems(out ItemDatabase items))
            {
                return;
            }

            if (_npc != Entity.Null && !_entityManager.Exists(_npc))
            {
                CloseSession();
                return;
            }

            ReportRefusals(character);

            int signature = Signature(character, bag, items);

            if (signature == _lastSignature)
                return;

            _lastSignature = signature;

            // Everything the pointer could be over is about to be thrown away,
            // and a destroyed element raises no leave event — so the box would
            // hang there describing a tile that is no longer under the cursor.
            _tooltips?.Hide();

            Ugui.Clear(_body);

            switch (_session)
            {
                case NpcServiceType.Vendor:
                    _heading.text = "Trader";
                    BuildVendor(items, character, bag);
                    break;

                case NpcServiceType.Crafting:
                    _heading.text = "Forge";
                    BuildCrafting(items, character, bag);
                    break;

                case NpcServiceType.DungeonPortal:
                    _heading.text = "Descent";
                    BuildPortal();
                    break;

                default:
                    _heading.text = "Training ground";
                    BuildTraining();
                    break;
            }
        }

        private int Signature(Entity character, Entity bag, ItemDatabase items)
        {
            unchecked
            {
                int hash = (int)_session * 397;
                hash = hash * 31 + _npc.Index;
                hash = hash * 31 + _craftTarget.Index;
                hash = hash * 31 + _craftSocket;
                hash = hash * 31 + ContainerSignature(bag);
                hash = hash * 31 + Currency.Balance(_entityManager, character);

                if (_session == NpcServiceType.Vendor && TryGetVendor(out VendorComponent vendor))
                    hash = hash * 31 + ContainerSignature(vendor.StockContainer);

                if (_session == NpcServiceType.DungeonPortal &&
                    _entityManager.HasBuffer<LobbyReadyPlayer>(_npc))
                {
                    hash = hash * 31 + _entityManager.GetBuffer<LobbyReadyPlayer>(_npc).Length;
                }

                if (_craftTarget != Entity.Null &&
                    _entityManager.Exists(_craftTarget) &&
                    _entityManager.HasBuffer<GearSocket>(_craftTarget))
                {
                    DynamicBuffer<GearSocket> sockets =
                        _entityManager.GetBuffer<GearSocket>(_craftTarget);

                    hash = hash * 31 + sockets.Length;

                    for (int i = 0; i < sockets.Length; i++)
                        hash = hash * 31 + sockets[i].LinkGroup * 7 + sockets[i].WeldedSkillId;
                }

                return hash;
            }
        }

        private int ContainerSignature(Entity container)
        {
            if (container == Entity.Null || !_entityManager.HasBuffer<InventoryCell>(container))
                return 0;

            DynamicBuffer<InventoryCell> cells = _entityManager.GetBuffer<InventoryCell>(container);

            unchecked
            {
                int hash = cells.Length;

                for (int i = 0; i < cells.Length; i++)
                    hash = hash * 31 + cells[i].OccupyingItem.Index;

                return hash;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Vendor
        // ─────────────────────────────────────────────────────────────────

        private void BuildVendor(ItemDatabase items, Entity character, Entity bag)
        {
            if (!TryGetVendor(out VendorComponent vendor) ||
                vendor.StockContainer == Entity.Null)
            {
                MakeNote(_body, "The shelves are empty.");
                return;
            }

            InventoryGridComponent shelf =
                _entityManager.GetComponentData<InventoryGridComponent>(vendor.StockContainer);
            InventoryGridComponent bagGrid =
                _entityManager.GetComponentData<InventoryGridComponent>(bag);

            float cell = CellSizeFor(
                shelf.Width + bagGrid.Width,
                math.max(shelf.Height, bagGrid.Height));

            RectTransform left = MakeColumn(_body, "For sale");
            BuildGrid(
                left, vendor.StockContainer, items, cell,
                item => Trade(item, VendorTransactionKind.Buy),
                item => PriceLabel(items, item, vendor, VendorTransactionKind.Buy));

            RectTransform right = MakeColumn(
                _body, $"Your bag — {Currency.Balance(_entityManager, character)} coin");
            BuildGrid(
                right, bag, items, cell,
                item => Trade(item, VendorTransactionKind.Sell),
                item => PriceLabel(items, item, vendor, VendorTransactionKind.Sell));

            // A third column of the body rather than a line under the bag, which
            // is where it sat before: the panel is wider than it is tall and this
            // is the one place with room to spare.
            MakeNote(
                _body,
                "Double-click an item on the left to buy it, or one in your bag to sell it.");
        }

        private string PriceLabel(
            ItemDatabase items, Entity item, in VendorComponent vendor, VendorTransactionKind kind)
        {
            int id = _entityManager.GetComponentData<ItemInstance>(item).ItemId;
            int index = items.IndexOf(id);

            if (index < 0)
                return string.Empty;

            ref ItemBlob blob = ref items.Value.Value.Items[index];

            // Currency is not merchandise, and saying so on the tile is cheaper
            // than letting somebody find out by clicking.
            if (kind == VendorTransactionKind.Sell && blob.CurrencyValue > 0)
                return string.Empty;

            float multiplier = kind == VendorTransactionKind.Buy
                ? vendor.BuyPriceMultiplier
                : vendor.SellPriceMultiplier;

            int basePrice = Currency.BasePrice(blob.Rarity);

            int price = kind == VendorTransactionKind.Buy
                ? math.max(1, (int)math.ceil(basePrice * multiplier))
                : math.max(1, (int)math.floor(basePrice * multiplier));

            return price.ToString();
        }

        private void Trade(Entity item, VendorTransactionKind kind)
        {
            if (!TryGetCharacter(out Entity character, out _) || _npc == Entity.Null)
                return;

            _entityManager.GetBuffer<VendorTransactionRequest>(character).Add(
                new VendorTransactionRequest
                {
                    Vendor = _npc,
                    Item = item,
                    Kind = kind
                });
        }

        private bool TryGetVendor(out VendorComponent vendor)
        {
            vendor = default;

            if (_npc == Entity.Null ||
                !_entityManager.Exists(_npc) ||
                !_entityManager.HasComponent<VendorComponent>(_npc))
            {
                return false;
            }

            vendor = _entityManager.GetComponentData<VendorComponent>(_npc);
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // Crafting
        // ─────────────────────────────────────────────────────────────────

        private void BuildCrafting(ItemDatabase items, Entity character, Entity bag)
        {
            if (!_entityManager.HasComponent<CraftingStation>(_npc))
            {
                MakeNote(_body, "The forge is cold.");
                return;
            }

            CraftingStation station = _entityManager.GetComponentData<CraftingStation>(_npc);

            InventoryGridComponent bagGrid =
                _entityManager.GetComponentData<InventoryGridComponent>(bag);

            float cell = CellSizeFor(bagGrid.Width + 6, bagGrid.Height);

            RectTransform left = MakeColumn(
                _body, $"Your bag — {Currency.Balance(_entityManager, character)} coin");

            BuildGrid(left, bag, items, cell, SelectCraftTarget, null);

            RectTransform right = MakeColumn(_body, "Work");

            if (_craftTarget == Entity.Null || !_entityManager.Exists(_craftTarget))
            {
                MakeNote(right, "Click an item in your bag to put it on the anvil.");
                return;
            }

            Ugui.Text(
                right, "Target", 13f, new Color(0.88f, 0.89f, 0.92f),
                TextAlignmentOptions.TopLeft).text = NameOf(items, _craftTarget);

            BuildSocketRow(right, _craftTarget);

            MakeAction(
                right,
                $"Add socket — {station.AddSocketCost}",
                () => Craft(CraftOperation.AddSocket, 0));

            MakeAction(
                right,
                $"Link socket {_craftSocket} to {_craftSocket - 1} — {station.LinkSocketCost}",
                () => Craft(CraftOperation.LinkSocket, _craftSocket));

            MakeAction(
                right,
                $"Reroll built-in skills — {station.RerollSkillsCost}",
                () => Craft(CraftOperation.RerollSkills, 0));

            MakeNote(
                right,
                "Affixes are authored on the item, not rolled per copy, so there is nothing " +
                "on this one to reroll. Sockets, links and a weapon's built-in attacks are " +
                "the parts that belong to this instance.");
        }

        /// <summary>
        /// The holes in the chosen item, drawn in a row so a link reads as a
        /// relationship between neighbours. Clicking one picks it as the socket
        /// to pull into the group on its left.
        /// </summary>
        private void BuildSocketRow(RectTransform parent, Entity item)
        {
            RectTransform row = Ugui.Node(parent, "Sockets");
            Ugui.Size(row, height: 24f);
            Ugui.Row(row, spacing: 1f);

            if (!_entityManager.HasBuffer<GearSocket>(item))
                return;

            DynamicBuffer<GearSocket> sockets = _entityManager.GetBuffer<GearSocket>(item);

            for (int i = 0; i < sockets.Length; i++)
            {
                GearSocket socket = sockets[i];

                Image box = Ugui.Box(row, $"Socket{i}", socket.IsWelded
                    ? new Color(0.42f, 0.34f, 0.16f)
                    : socket.InsertedGem != Entity.Null
                        ? new Color(0.20f, 0.34f, 0.44f)
                        : new Color(0.13f, 0.14f, 0.17f));

                Ugui.Size(box.rectTransform, width: 22f, height: 22f);

                Ugui.Text(
                    box.rectTransform, "Group", 11f,
                    new Color(0.82f, 0.84f, 0.88f)).text = socket.LinkGroup.ToString();

                Ugui.Border(box.rectTransform, i == _craftSocket
                    ? new Color(0.90f, 0.80f, 0.35f)
                    : new Color(0.26f, 0.28f, 0.34f), 1f);

                int index = i;
                Ugui.On(box.rectTransform, EventTriggerType.PointerDown, _ =>
                {
                    _craftSocket = index;
                    _lastSignature = int.MinValue;
                });

                // The forge is where a player decides which gem goes where, so
                // it is the last place a hole should be a number with no name.
                if (socket.IsWelded)
                {
                    int weldedSkill = socket.WeldedSkillId;
                    _tooltips.Attach(box.rectTransform, () => DescribeSkillId(weldedSkill));
                }
                else if (socket.InsertedGem != Entity.Null &&
                         _entityManager.HasComponent<ItemInstance>(socket.InsertedGem))
                {
                    int gemId =
                        _entityManager.GetComponentData<ItemInstance>(socket.InsertedGem).ItemId;

                    _tooltips.Attach(box.rectTransform, () => DescribeItem(gemId));
                }
            }
        }

        private void SelectCraftTarget(Entity item)
        {
            _craftTarget = item;
            _craftSocket = 1;
            _lastSignature = int.MinValue;
        }

        private void Craft(CraftOperation operation, int socketIndex)
        {
            if (!TryGetCharacter(out Entity character, out _) || _craftTarget == Entity.Null)
                return;

            _entityManager.GetBuffer<CraftRequest>(character).Add(new CraftRequest
            {
                Station = _npc,
                Item = _craftTarget,
                Operation = operation,
                SocketIndex = socketIndex
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // Portal and training
        // ─────────────────────────────────────────────────────────────────

        private void BuildPortal()
        {
            RectTransform column = MakeColumn(_body, "Ready to descend");

            int ready = 0;
            bool self = false;

            if (_entityManager.HasBuffer<LobbyReadyPlayer>(_npc))
            {
                DynamicBuffer<LobbyReadyPlayer> list =
                    _entityManager.GetBuffer<LobbyReadyPlayer>(_npc);

                ready = list.Length;

                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i].PlayerId == _playerId)
                        self = true;
                }
            }

            Ugui.Text(
                column, "Ready", 13f, new Color(0.88f, 0.89f, 0.92f),
                TextAlignmentOptions.TopLeft).text = $"{ready} of {PlayerCount()} ready";

            bool wantReady = !self;

            MakeAction(
                column,
                self ? "Not yet" : "I am ready",
                () => SetReady(wantReady));

            MakeNote(
                column,
                "The floor is generated from a seed when everyone has agreed. Nothing about " +
                "the dungeon travels — every client builds the same floor from the same number.");
        }

        private void BuildTraining()
        {
            RectTransform column = MakeColumn(_body, "Training ground");

            MakeNote(
                column,
                "Hit the dummies. The meter on the right splits what lands into what you " +
                "pressed, what your triggers cast and what your reactions did — which is the " +
                "only way to see whether a combination is working.");
        }

        private void SetReady(bool ready)
        {
            if (_npc == Entity.Null || !_entityManager.HasBuffer<PortalReadyRequest>(_npc))
                return;

            _entityManager.GetBuffer<PortalReadyRequest>(_npc).Add(new PortalReadyRequest
            {
                PlayerId = _playerId,
                Ready = ready
            });

            _lastSignature = int.MinValue;
        }

        private int PlayerCount()
        {
            int count = _characterQuery.CalculateEntityCount();
            return count > 0 ? count : 1;
        }

        // ─────────────────────────────────────────────────────────────────
        // Damage meter
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Shows itself near a metered target and hides again when the player
        /// walks away.
        ///
        /// No key, deliberately. The meter is only meaningful standing in front
        /// of a dummy, and a toggle for something that is either obviously
        /// wanted or obviously not is a keybinding nobody would remember.
        /// </summary>
        private void RefreshMeter()
        {
            _meterTimer -= Time.unscaledDeltaTime;
            if (_meterTimer > 0f)
                return;

            _meterTimer = MeterInterval;

            if (_player == null || !TryFindMeter(out Entity dummy))
            {
                _meterPanel.gameObject.SetActive(false);
                return;
            }

            _meterPanel.gameObject.SetActive(true);
            DrawMeter(dummy);
        }

        private bool TryFindMeter(out Entity dummy)
        {
            dummy = Entity.Null;

            if (_meterQuery.IsEmptyIgnoreFilter)
                return false;

            using NativeArray<Entity> entities = _meterQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<LocalTransform> transforms =
                _meterQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float3 position = _player.position;
            float bestSq = MeterRange * MeterRange;

            for (int i = 0; i < entities.Length; i++)
            {
                float d = math.distancesq(position, transforms[i].Position);

                if (d >= bestSq)
                    continue;

                bestSq = d;
                dummy = entities[i];
            }

            return dummy != Entity.Null;
        }

        private void DrawMeter(Entity dummy)
        {
            Ugui.Clear(_meterRows);

            DynamicBuffer<DamageMeterEntry> log =
                _entityManager.GetBuffer<DamageMeterEntry>(dummy);

            float window = math.max(0.1f, _entityManager.GetComponentData<DamageMeter>(dummy).Window);

            if (log.Length == 0)
            {
                ClearGraph();
                MakeNote(_meterRows, "Nothing has landed yet.");
                return;
            }

            float latest = log[log.Length - 1].Timestamp;
            float start = latest - window;

            float total = 0f;
            float direct = 0f;
            float trigger = 0f;
            float reaction = 0f;
            float mine = 0f;

            System.Array.Clear(_buckets, 0, _buckets.Length);

            for (int i = 0; i < log.Length; i++)
            {
                DamageMeterEntry entry = log[i];

                total += entry.Damage;

                if (entry.FromReaction)
                    reaction += entry.Damage;
                else if (entry.FromTrigger)
                    trigger += entry.Damage;
                else
                    direct += entry.Damage;

                if (entry.SourcePlayerId == _playerId)
                    mine += entry.Damage;

                int bucket = (int)((entry.Timestamp - start) / window * MeterBuckets);
                _buckets[math.clamp(bucket, 0, MeterBuckets - 1)] += entry.Damage;
            }

            DrawGraph();

            MakeStat(_meterRows, "DPS", (total / window).ToString("0.0"));
            MakeStat(_meterRows, "Yours", (mine / window).ToString("0.0"));
            MakeStat(_meterRows, "Pressed", Share(direct, total));
            MakeStat(_meterRows, "Triggered", Share(trigger, total));
            MakeStat(_meterRows, "Reactions", Share(reaction, total));
            MakeStat(_meterRows, "Hits", log.Length.ToString());
        }

        private static string Share(float part, float total) =>
            total <= 0f ? "—" : $"{part / total * 100f:0}%  ({part:0})";

        private void DrawGraph()
        {
            float peak = 0.0001f;

            for (int i = 0; i < _buckets.Length; i++)
                peak = math.max(peak, _buckets[i]);

            for (int i = 0; i < _meterBars.Count && i < _buckets.Length; i++)
                SetBarHeight(_meterBars[i], math.max(1f, _buckets[i] / peak * 42f));
        }

        private void ClearGraph()
        {
            for (int i = 0; i < _meterBars.Count; i++)
                SetBarHeight(_meterBars[i], 1f);
        }

        /// <summary>
        /// The bar's own height, written straight onto the rect.
        ///
        /// Not through the LayoutElement: the row deliberately does not control
        /// its children's height, so the rect is the one place the number lives.
        /// The width it leaves alone, because the row does own that.
        /// </summary>
        private static void SetBarHeight(RectTransform bar, float height) =>
            bar.sizeDelta = new Vector2(bar.sizeDelta.x, height);

        private void RequestMeterReset()
        {
            if (!TryFindMeter(out Entity dummy))
                return;

            _entityManager.SetComponentEnabled<DamageMeterReset>(dummy, true);
        }

        // ─────────────────────────────────────────────────────────────────
        // Grid drawing
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Draws one container: a layer of empty squares, and the items on top
        /// of it.
        ///
        /// Items are drawn from the cells but only at each one's recorded
        /// origin, because an item writes itself into every square it covers and
        /// drawing per cell would draw a two-by-three breastplate six times.
        /// </summary>
        private void BuildGrid(
            RectTransform parent,
            Entity container,
            ItemDatabase items,
            float cell,
            Action<Entity> onActivate,
            Func<Entity, string> badge)
        {
            RectTransform root = Ugui.Node(parent, "Grid");

            if (container == Entity.Null || !_entityManager.HasBuffer<InventoryCell>(container))
                return;

            InventoryGridComponent grid =
                _entityManager.GetComponentData<InventoryGridComponent>(container);
            DynamicBuffer<InventoryCell> cells = _entityManager.GetBuffer<InventoryCell>(container);

            float step = cell + CellGap;

            // The grid is the one child of a column whose size is arithmetic
            // rather than content, so it has to be told — a layout group asks
            // every child how big it is and collapses the ones that do not know.
            Ugui.Size(root, grid.Width * step, grid.Height * step);

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    Image square = Ugui.Box(
                        root, "Cell", new Color(0.12f, 0.13f, 0.16f), picks: false);

                    Ugui.TopLeft(square.rectTransform, x * step, y * step, cell, cell);
                }
            }

            for (int i = 0; i < cells.Length; i++)
            {
                Entity item = cells[i].OccupyingItem;

                if (item == Entity.Null ||
                    !_entityManager.HasComponent<ItemGridPlacement>(item) ||
                    !_entityManager.HasComponent<ItemInstance>(item))
                {
                    continue;
                }

                ItemGridPlacement placement =
                    _entityManager.GetComponentData<ItemGridPlacement>(item);

                if (grid.IndexOf(placement.OriginX, placement.OriginY) != i)
                    continue;

                int id = _entityManager.GetComponentData<ItemInstance>(item).ItemId;

                if (!GridFit.TryGetFootprint(
                        items, id, placement.IsRotated, out int width, out int height))
                {
                    continue;
                }

                MakeTile(
                    root, item, items, id, placement, width, height, cell, step,
                    onActivate, badge);
            }
        }

        private void MakeTile(
            RectTransform parent,
            Entity item,
            ItemDatabase items,
            int itemId,
            in ItemGridPlacement placement,
            int width,
            int height,
            float cell,
            float step,
            Action<Entity> onActivate,
            Func<Entity, string> badge)
        {
            ItemRarity rarity = RarityOf(items, itemId);

            Image tile = Ugui.Box(parent, "Item", Tint(rarity));

            Ugui.TopLeft(
                tile.rectTransform,
                placement.OriginX * step,
                placement.OriginY * step,
                width * cell + (width - 1) * CellGap,
                height * cell + (height - 1) * CellGap);

            TextMeshProUGUI label = Ugui.Text(
                tile.rectTransform, "Name", math.clamp(cell * 0.32f, 8f, 12f),
                new Color(0.88f, 0.89f, 0.92f), wrap: true);
            label.text = ShortName(NameOf(items, item));

            if (badge != null)
            {
                string text = badge(item);

                if (!string.IsNullOrEmpty(text))
                {
                    TextMeshProUGUI price = Ugui.Text(
                        tile.rectTransform, "Price", 10f, new Color(0.94f, 0.86f, 0.45f),
                        TextAlignmentOptions.Bottom);
                    price.text = text;
                }
            }

            Ugui.Border(tile.rectTransform, RarityColour(rarity), 1f);

            int capturedId = itemId;
            _tooltips.Attach(tile.rectTransform, () => DescribeItem(capturedId));

            if (onActivate == null)
                return;

            // A double-click to trade, a single one to select. Two meanings on
            // one tile rather than two panels, and the destructive one is the
            // one that needs saying twice.
            bool needsDouble = _session == NpcServiceType.Vendor;

            Ugui.On(tile.rectTransform, EventTriggerType.PointerDown, data =>
            {
                if (needsDouble && data.clickCount < 2)
                    return;

                onActivate(item);
            });
        }

        /// <summary>
        /// How big a cell may be so that the whole panel fits on this screen.
        ///
        /// Computed from the canvas's own rect rather than from the reference
        /// resolution: a scaler that matches on width gives a logical height of
        /// 1200/aspect, which is 675 on 16:9 and 338 on 32:9, and a layout built
        /// against 800 goes off the bottom of exactly the monitors people own.
        /// </summary>
        private float CellSizeFor(int columns, int rows)
        {
            Rect root = ((RectTransform)_canvas.transform).rect;

            float width = root.width;
            float height = root.height;

            if (width <= 1f || height <= 1f)
                return MaxCellSize;

            float availableWidth = width * ScreenFraction - HorizontalChrome;
            float availableHeight = height * ScreenFraction - VerticalChrome;

            float byWidth = (availableWidth - columns * CellGap) / math.max(1, columns);
            float byHeight = (availableHeight - rows * CellGap) / math.max(1, rows);

            return math.clamp(math.min(byWidth, byHeight), MinCellSize, MaxCellSize);
        }

        // ─────────────────────────────────────────────────────────────────
        // Results
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Drains the result queues and says what went wrong.
        ///
        /// A refusal leaves the world exactly as it was, so without this the
        /// player would double-click a sword and watch nothing happen. That is
        /// the same reason the result buffers exist at all.
        /// </summary>
        private void ReportRefusals(Entity character)
        {
            DynamicBuffer<VendorTransactionResult> trades =
                _entityManager.GetBuffer<VendorTransactionResult>(character);

            for (int i = 0; i < trades.Length; i++)
            {
                if (!trades[i].Succeeded)
                    ShowMessage(DescribeTrade(trades[i]));
            }

            trades.Clear();

            DynamicBuffer<CraftResult> crafts = _entityManager.GetBuffer<CraftResult>(character);

            for (int i = 0; i < crafts.Length; i++)
            {
                if (!crafts[i].Succeeded)
                    ShowMessage(DescribeCraft(crafts[i].Status));
            }

            crafts.Clear();
        }

        private static string DescribeTrade(in VendorTransactionResult result)
        {
            switch (result.Status)
            {
                case VendorTransactionStatus.RejectedTooPoor:
                    return $"Not enough coin — that costs {result.Price}.";
                case VendorTransactionStatus.RejectedNoRoom:
                    return "No room for that.";
                case VendorTransactionStatus.RejectedNotCarried:
                    return "You can only sell what is in your bag. Take it off first.";
                case VendorTransactionStatus.RejectedNotForSale:
                    return "Coin is not merchandise.";
                case VendorTransactionStatus.RejectedNotStocked:
                    return "That is not on the shelf any more.";
                default:
                    return "The trader refused.";
            }
        }

        private static string DescribeCraft(CraftStatus status)
        {
            switch (status)
            {
                case CraftStatus.RejectedTooPoor: return "Not enough coin.";
                case CraftStatus.RejectedNotCarried: return "That is not yours to work on.";
                case CraftStatus.RejectedSocketLimit: return "No room for another socket.";
                case CraftStatus.RejectedNoSuchSocket: return "No such socket.";
                case CraftStatus.RejectedAlreadyLinked: return "Those two are already linked.";
                case CraftStatus.RejectedNothingToReroll:
                    return "Nothing built into this one to reroll.";
                default: return "The forge refused.";
            }
        }

        private void ShowMessage(string message)
        {
            _message.text = message;
            _messageTimer = 4f;
        }

        private void ExpireMessage()
        {
            if (_messageTimer <= 0f)
                return;

            _messageTimer -= Time.unscaledDeltaTime;

            if (_messageTimer <= 0f)
                _message.text = string.Empty;
        }

        // ─────────────────────────────────────────────────────────────────
        // ECS reads
        // ─────────────────────────────────────────────────────────────────

        private bool TryGetCharacter(out Entity character, out Entity bag)
        {
            character = Entity.Null;
            bag = Entity.Null;

            if (_characterQuery.IsEmptyIgnoreFilter)
                return false;

            using NativeArray<Entity> entities = _characterQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PlayerCharacter> players =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);
            using NativeArray<CarriedBag> bags =
                _characterQuery.ToComponentDataArray<CarriedBag>(Allocator.Temp);

            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].PlayerId != _playerId)
                    continue;

                character = entities[i];
                bag = bags[i].Container;
                return bag != Entity.Null;
            }

            return false;
        }

        private bool TryGetItems(out ItemDatabase items)
        {
            items = default;

            if (_itemDatabaseQuery.IsEmptyIgnoreFilter)
                return false;

            items = _itemDatabaseQuery.GetSingleton<ItemDatabase>();
            return items.Value.IsCreated;
        }

        private string NameOf(ItemDatabase items, Entity item)
        {
            if (_entityManager.HasComponent<ItemDisplayName>(item))
                return _entityManager.GetComponentData<ItemDisplayName>(item).Value.ToString();

            int id = _entityManager.GetComponentData<ItemInstance>(item).ItemId;
            return Currency.NameOf(items, id).ToString();
        }

        private static ItemRarity RarityOf(ItemDatabase items, int itemId)
        {
            int index = items.IndexOf(itemId);

            if (index < 0)
                return ItemRarity.Common;

            ref ItemBlob item = ref items.Value.Value.Items[index];
            return item.Rarity;
        }

        /// <summary>Enough of a name to recognise it in a cell that is 20 pixels wide.</summary>
        private static string ShortName(string name) =>
            string.IsNullOrEmpty(name) || name.Length <= 10 ? name : name.Substring(0, 10);

        // ─────────────────────────────────────────────────────────────────
        // Small builders
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// One column of the body, as wide as the widest thing in it.
        ///
        /// The 420-pixel ceiling is gone: uGUI has no max-width, and the only
        /// thing that used to hit it was a note, which now takes the column's
        /// own width instead of setting it.
        /// </summary>
        private static RectTransform MakeColumn(RectTransform parent, string heading)
        {
            RectTransform column = Ugui.Node(parent, heading);
            Ugui.Column(column, spacing: 4f);
            Ugui.Fit(column);

            MakeHeading(column, heading);
            return column;
        }

        private static TextMeshProUGUI MakeHeading(RectTransform parent, string text)
        {
            TextMeshProUGUI label = Ugui.Text(
                parent, "Heading", 14f, new Color(0.86f, 0.88f, 0.92f),
                TextAlignmentOptions.TopLeft, bold: true);

            label.text = text;
            return label;
        }

        private static TextMeshProUGUI MakeNote(RectTransform parent, string text)
        {
            TextMeshProUGUI label = Ugui.Text(
                parent, "Note", 11f, new Color(0.58f, 0.61f, 0.67f),
                TextAlignmentOptions.TopLeft, wrap: true);

            label.text = text;

            // Wide enough to read, and the column is as wide as this unless
            // something in it is wider — a grid usually is.
            Ugui.Size(label.rectTransform, width: 320f);
            return label;
        }

        private static void MakeAction(RectTransform parent, string text, Action action) =>
            Ugui.Button(parent, "Action", text, action);

        /// <summary>
        /// A name on the left and a number on the right. The name takes the
        /// slack, which is how uGUI spells space-between.
        /// </summary>
        private static void MakeStat(RectTransform parent, string label, string value)
        {
            RectTransform row = Ugui.Node(parent, label);
            Ugui.Size(row, height: 15f);
            Ugui.Row(row);

            TextMeshProUGUI name = Ugui.Text(
                row, "Name", 11f, new Color(0.62f, 0.65f, 0.70f),
                TextAlignmentOptions.Left);
            name.text = label;
            Ugui.Size(name.rectTransform, grow: 1f);

            TextMeshProUGUI amount = Ugui.Text(
                row, "Value", 11f, new Color(0.90f, 0.92f, 0.96f),
                TextAlignmentOptions.Right);
            amount.text = value;
        }

        private static Color RarityColour(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon: return new Color(0.45f, 0.85f, 0.45f);
                case ItemRarity.Rare: return new Color(0.40f, 0.60f, 0.95f);
                case ItemRarity.Epic: return new Color(0.70f, 0.45f, 0.95f);
                case ItemRarity.Legendary: return new Color(0.95f, 0.65f, 0.25f);
                case ItemRarity.Mythic: return new Color(0.95f, 0.35f, 0.35f);
                default: return new Color(0.70f, 0.72f, 0.76f);
            }
        }

        private static Color Tint(ItemRarity rarity)
        {
            Color colour = RarityColour(rarity);
            return new Color(colour.r * 0.30f, colour.g * 0.30f, colour.b * 0.30f, 0.95f);
        }

        private void OnDestroy()
        {
            if (!_hasWorld)
                return;

            _characterQuery.Dispose();
            _itemDatabaseQuery.Dispose();
            _itemSetDatabaseQuery.Dispose();
            _npcQuery.Dispose();
            _meterQuery.Dispose();
            _skillDatabaseQuery.Dispose();
        }
    }
}
