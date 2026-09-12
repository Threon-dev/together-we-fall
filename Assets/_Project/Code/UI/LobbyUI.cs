using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;
using TogetherWeFall.Inventory;
using TogetherWeFall.Lobby;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;

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

        [SerializeField] private UIDocument _document;

        private PlayerInputReader _input;
        private Transform _player;
        private int _playerId;

        private EntityManager _entityManager;
        private EntityQuery _characterQuery;
        private EntityQuery _itemDatabaseQuery;
        private EntityQuery _npcQuery;
        private EntityQuery _meterQuery;
        private bool _hasWorld;

        private VisualElement _screen;
        private VisualElement _panel;
        private Label _heading;
        private Label _message;
        private VisualElement _body;
        private Label _prompt;
        private VisualElement _meterPanel;
        private VisualElement _meterRows;
        private VisualElement _meterGraph;

        private NpcServiceType _session = NpcServiceType.None;
        private Entity _npc = Entity.Null;

        /// <summary>What the forge is pointed at. Null when nothing is chosen.</summary>
        private Entity _craftTarget = Entity.Null;

        private int _craftSocket = 1;

        private int _lastSignature = int.MinValue;
        private float _meterTimer;
        private float _messageTimer;

        private readonly List<VisualElement> _meterBars = new List<VisualElement>();

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

            _screen.style.display = DisplayStyle.Flex;
        }

        private void CloseSession()
        {
            if (_session == NpcServiceType.None)
                return;

            _session = NpcServiceType.None;
            _npc = Entity.Null;
            _craftTarget = Entity.Null;
            _screen.style.display = DisplayStyle.None;
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
                _prompt.style.display = DisplayStyle.None;
                return;
            }

            NpcServiceType nearest = NearestService(out float _);

            if (nearest == NpcServiceType.None)
            {
                _prompt.style.display = DisplayStyle.None;
                return;
            }

            _prompt.text = $"E — {PromptFor(nearest)}";
            _prompt.style.display = DisplayStyle.Flex;
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
        /// Lazily rather than in Initialize, because UIDocument fills its root in
        /// OnEnable and the bootstrap that calls Initialize deliberately runs
        /// before everything else in the scene.
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

            _prompt = new Label { text = string.Empty };
            _prompt.style.position = Position.Absolute;
            _prompt.style.bottom = 96f;
            _prompt.style.left = 0f;
            _prompt.style.right = 0f;
            _prompt.style.unityTextAlign = TextAnchor.MiddleCenter;
            _prompt.style.fontSize = 18f;
            _prompt.style.color = new Color(0.92f, 0.88f, 0.62f);
            _prompt.style.display = DisplayStyle.None;
            _prompt.pickingMode = PickingMode.Ignore;
            root.Add(_prompt);

            BuildMeterPanel(root);

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
            _panel.style.paddingLeft = 14f;
            _panel.style.paddingRight = 14f;
            _panel.style.paddingTop = 10f;
            _panel.style.paddingBottom = 12f;
            _panel.style.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 0.96f);
            SetBorder(_panel, new Color(0.30f, 0.32f, 0.38f));

            _heading = MakeHeading("Lobby");
            _panel.Add(_heading);

            _body = new VisualElement();
            _body.style.flexDirection = FlexDirection.Row;
            _panel.Add(_body);

            _message = new Label { text = string.Empty };
            _message.style.marginTop = 6f;
            _message.style.fontSize = 12f;
            _message.style.color = new Color(0.95f, 0.62f, 0.55f);
            _panel.Add(_message);

            var hint = new Label { text = "Esc — close" };
            hint.style.marginTop = 2f;
            hint.style.fontSize = 11f;
            hint.style.color = new Color(0.55f, 0.58f, 0.64f);
            _panel.Add(hint);

            _screen.Add(_panel);
            root.Add(_screen);

            return true;
        }

        private void BuildMeterPanel(VisualElement root)
        {
            _meterPanel = new VisualElement();
            _meterPanel.style.position = Position.Absolute;
            _meterPanel.style.right = 12f;
            _meterPanel.style.top = 12f;
            _meterPanel.style.width = 250f;
            _meterPanel.style.paddingLeft = 8f;
            _meterPanel.style.paddingRight = 8f;
            _meterPanel.style.paddingTop = 6f;
            _meterPanel.style.paddingBottom = 8f;
            _meterPanel.style.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 0.86f);
            _meterPanel.style.display = DisplayStyle.None;
            SetBorder(_meterPanel, new Color(0.26f, 0.28f, 0.34f));

            _meterPanel.Add(MakeHeading("Damage meter"));

            _meterGraph = new VisualElement();
            _meterGraph.style.flexDirection = FlexDirection.Row;
            _meterGraph.style.alignItems = Align.FlexEnd;
            _meterGraph.style.height = 44f;
            _meterGraph.style.marginBottom = 4f;

            for (int i = 0; i < MeterBuckets; i++)
            {
                var bar = new VisualElement();
                bar.style.flexGrow = 1f;
                bar.style.marginRight = 1f;
                bar.style.height = 1f;
                bar.style.backgroundColor = new Color(0.45f, 0.72f, 0.95f);
                _meterGraph.Add(bar);
                _meterBars.Add(bar);
            }

            _meterPanel.Add(_meterGraph);

            _meterRows = new VisualElement();
            _meterPanel.Add(_meterRows);

            var reset = new Button(RequestMeterReset) { text = "Reset" };
            reset.style.marginTop = 6f;
            _meterPanel.Add(reset);

            root.Add(_meterPanel);
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

            int signature = Signature(bag, items);

            if (signature == _lastSignature)
                return;

            _lastSignature = signature;
            _body.Clear();

            switch (_session)
            {
                case NpcServiceType.Vendor:
                    _heading.text = "Trader";
                    BuildVendor(items, bag);
                    break;

                case NpcServiceType.Crafting:
                    _heading.text = "Forge";
                    BuildCrafting(items, bag);
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

        private int Signature(Entity bag, ItemDatabase items)
        {
            unchecked
            {
                int hash = (int)_session * 397;
                hash = hash * 31 + _npc.Index;
                hash = hash * 31 + _craftTarget.Index;
                hash = hash * 31 + _craftSocket;
                hash = hash * 31 + ContainerSignature(bag);
                hash = hash * 31 + Currency.Balance(_entityManager, items, bag);

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

        private void BuildVendor(ItemDatabase items, Entity bag)
        {
            if (!TryGetVendor(out VendorComponent vendor) ||
                vendor.StockContainer == Entity.Null)
            {
                _body.Add(new Label { text = "The shelves are empty." });
                return;
            }

            InventoryGridComponent shelf =
                _entityManager.GetComponentData<InventoryGridComponent>(vendor.StockContainer);
            InventoryGridComponent bagGrid =
                _entityManager.GetComponentData<InventoryGridComponent>(bag);

            float cell = CellSizeFor(
                shelf.Width + bagGrid.Width,
                math.max(shelf.Height, bagGrid.Height));

            VisualElement left = MakeColumn("For sale");
            left.Add(BuildGrid(
                vendor.StockContainer, items, cell,
                item => Trade(item, VendorTransactionKind.Buy),
                item => PriceLabel(items, item, vendor, VendorTransactionKind.Buy)));
            _body.Add(left);

            VisualElement right = MakeColumn(
                $"Your bag — {Currency.Balance(_entityManager, items, bag)} coin");
            right.Add(BuildGrid(
                bag, items, cell,
                item => Trade(item, VendorTransactionKind.Sell),
                item => PriceLabel(items, item, vendor, VendorTransactionKind.Sell)));
            _body.Add(right);

            _body.Add(MakeNote(
                "Double-click an item on the left to buy it, or one in your bag to sell it."));
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

        private void BuildCrafting(ItemDatabase items, Entity bag)
        {
            if (!_entityManager.HasComponent<CraftingStation>(_npc))
            {
                _body.Add(new Label { text = "The forge is cold." });
                return;
            }

            CraftingStation station = _entityManager.GetComponentData<CraftingStation>(_npc);

            InventoryGridComponent bagGrid =
                _entityManager.GetComponentData<InventoryGridComponent>(bag);

            float cell = CellSizeFor(bagGrid.Width + 6, bagGrid.Height);

            VisualElement left = MakeColumn(
                $"Your bag — {Currency.Balance(_entityManager, items, bag)} coin");

            left.Add(BuildGrid(bag, items, cell, SelectCraftTarget, null));
            _body.Add(left);

            VisualElement right = MakeColumn("Work");

            if (_craftTarget == Entity.Null || !_entityManager.Exists(_craftTarget))
            {
                right.Add(MakeNote("Click an item in your bag to put it on the anvil."));
                _body.Add(right);
                return;
            }

            right.Add(new Label { text = NameOf(items, _craftTarget) });
            right.Add(BuildSocketRow(_craftTarget));

            right.Add(MakeAction(
                $"Add socket — {station.AddSocketCost}",
                () => Craft(CraftOperation.AddSocket, 0)));

            right.Add(MakeAction(
                $"Link socket {_craftSocket} to {_craftSocket - 1} — {station.LinkSocketCost}",
                () => Craft(CraftOperation.LinkSocket, _craftSocket)));

            right.Add(MakeAction(
                $"Reroll built-in skills — {station.RerollSkillsCost}",
                () => Craft(CraftOperation.RerollSkills, 0)));

            right.Add(MakeNote(
                "Affixes are authored on the item, not rolled per copy, so there is nothing " +
                "on this one to reroll. Sockets, links and a weapon's built-in attacks are " +
                "the parts that belong to this instance."));

            _body.Add(right);
        }

        /// <summary>
        /// The holes in the chosen item, drawn in a row so a link reads as a
        /// relationship between neighbours. Clicking one picks it as the socket
        /// to pull into the group on its left.
        /// </summary>
        private VisualElement BuildSocketRow(Entity item)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 4f;
            row.style.marginBottom = 6f;
            row.style.flexWrap = Wrap.Wrap;

            if (!_entityManager.HasBuffer<GearSocket>(item))
                return row;

            DynamicBuffer<GearSocket> sockets = _entityManager.GetBuffer<GearSocket>(item);

            for (int i = 0; i < sockets.Length; i++)
            {
                GearSocket socket = sockets[i];

                var box = new Label { text = socket.LinkGroup.ToString() };
                box.style.width = 22f;
                box.style.height = 22f;
                box.style.marginRight = 1f;
                box.style.unityTextAlign = TextAnchor.MiddleCenter;
                box.style.fontSize = 11f;

                box.style.backgroundColor = socket.IsWelded
                    ? new Color(0.42f, 0.34f, 0.16f)
                    : socket.InsertedGem != Entity.Null
                        ? new Color(0.20f, 0.34f, 0.44f)
                        : new Color(0.13f, 0.14f, 0.17f);

                SetBorder(box, i == _craftSocket
                    ? new Color(0.90f, 0.80f, 0.35f)
                    : new Color(0.26f, 0.28f, 0.34f));

                int index = i;
                box.RegisterCallback<PointerDownEvent>(_ =>
                {
                    _craftSocket = index;
                    _lastSignature = int.MinValue;
                });

                row.Add(box);
            }

            return row;
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
            var column = MakeColumn("Ready to descend");

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

            column.Add(new Label { text = $"{ready} of {PlayerCount()} ready" });

            bool wantReady = !self;

            column.Add(MakeAction(
                self ? "Not yet" : "I am ready",
                () => SetReady(wantReady)));

            column.Add(MakeNote(
                "The floor is generated from a seed when everyone has agreed. Nothing about " +
                "the dungeon travels — every client builds the same floor from the same number."));

            _body.Add(column);
        }

        private void BuildTraining()
        {
            VisualElement column = MakeColumn("Training ground");

            column.Add(MakeNote(
                "Hit the dummies. The meter on the right splits what lands into what you " +
                "pressed, what your triggers cast and what your reactions did — which is the " +
                "only way to see whether a combination is working."));

            _body.Add(column);
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
                _meterPanel.style.display = DisplayStyle.None;
                return;
            }

            _meterPanel.style.display = DisplayStyle.Flex;
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
            _meterRows.Clear();

            DynamicBuffer<DamageMeterEntry> log =
                _entityManager.GetBuffer<DamageMeterEntry>(dummy);

            float window = math.max(0.1f, _entityManager.GetComponentData<DamageMeter>(dummy).Window);

            if (log.Length == 0)
            {
                ClearGraph();
                _meterRows.Add(MakeNote("Nothing has landed yet."));
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

            _meterRows.Add(MakeStat("DPS", (total / window).ToString("0.0")));
            _meterRows.Add(MakeStat("Yours", (mine / window).ToString("0.0")));
            _meterRows.Add(MakeStat("Pressed", Share(direct, total)));
            _meterRows.Add(MakeStat("Triggered", Share(trigger, total)));
            _meterRows.Add(MakeStat("Reactions", Share(reaction, total)));
            _meterRows.Add(MakeStat("Hits", log.Length.ToString()));
        }

        private static string Share(float part, float total) =>
            total <= 0f ? "—" : $"{part / total * 100f:0}%  ({part:0})";

        private void DrawGraph()
        {
            float peak = 0.0001f;

            for (int i = 0; i < _buckets.Length; i++)
                peak = math.max(peak, _buckets[i]);

            for (int i = 0; i < _meterBars.Count && i < _buckets.Length; i++)
                _meterBars[i].style.height = math.max(1f, _buckets[i] / peak * 42f);
        }

        private void ClearGraph()
        {
            for (int i = 0; i < _meterBars.Count; i++)
                _meterBars[i].style.height = 1f;
        }

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
        private VisualElement BuildGrid(
            Entity container,
            ItemDatabase items,
            float cell,
            Action<Entity> onActivate,
            Func<Entity, string> badge)
        {
            var root = new VisualElement();
            root.style.position = Position.Relative;

            if (container == Entity.Null || !_entityManager.HasBuffer<InventoryCell>(container))
                return root;

            InventoryGridComponent grid =
                _entityManager.GetComponentData<InventoryGridComponent>(container);
            DynamicBuffer<InventoryCell> cells = _entityManager.GetBuffer<InventoryCell>(container);

            float step = cell + CellGap;
            root.style.width = grid.Width * step;
            root.style.height = grid.Height * step;

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var square = new VisualElement();
                    square.style.position = Position.Absolute;
                    square.style.left = x * step;
                    square.style.top = y * step;
                    square.style.width = cell;
                    square.style.height = cell;
                    square.style.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
                    square.pickingMode = PickingMode.Ignore;
                    root.Add(square);
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

                root.Add(MakeTile(
                    item, items, id, placement, width, height, cell, step, onActivate, badge));
            }

            return root;
        }

        private VisualElement MakeTile(
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
            var tile = new VisualElement();
            tile.style.position = Position.Absolute;
            tile.style.left = placement.OriginX * step;
            tile.style.top = placement.OriginY * step;
            tile.style.width = width * cell + (width - 1) * CellGap;
            tile.style.height = height * cell + (height - 1) * CellGap;
            tile.style.justifyContent = Justify.Center;
            tile.style.alignItems = Align.Center;

            ItemRarity rarity = RarityOf(items, itemId);
            tile.style.backgroundColor = Tint(rarity);
            SetBorder(tile, RarityColour(rarity));

            var label = new Label { text = ShortName(NameOf(items, item)) };
            label.style.fontSize = math.clamp(cell * 0.32f, 8f, 12f);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.pickingMode = PickingMode.Ignore;
            tile.Add(label);

            if (badge != null)
            {
                string text = badge(item);

                if (!string.IsNullOrEmpty(text))
                {
                    var price = new Label { text = text };
                    price.style.fontSize = 10f;
                    price.style.color = new Color(0.94f, 0.86f, 0.45f);
                    price.pickingMode = PickingMode.Ignore;
                    tile.Add(price);
                }
            }

            if (onActivate == null)
                return tile;

            // A double-click to trade, a single one to select. Two meanings on
            // one tile rather than two panels, and the destructive one is the
            // one that needs saying twice.
            bool needsDouble = _session == NpcServiceType.Vendor;

            tile.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (needsDouble && evt.clickCount < 2)
                    return;

                onActivate(item);
            });

            return tile;
        }

        /// <summary>
        /// How big a cell may be so that the whole panel fits on this screen.
        ///
        /// Computed from the resolved size of the root rather than from the
        /// reference resolution: panel settings that match on width give a
        /// logical height of 1200/aspect, which is 675 on 16:9 and 338 on 32:9,
        /// and a layout built against 800 goes off the bottom of exactly the
        /// monitors people own.
        /// </summary>
        private float CellSizeFor(int columns, int rows)
        {
            VisualElement root = _document.rootVisualElement;

            float width = root.resolvedStyle.width;
            float height = root.resolvedStyle.height;

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
                case VendorTransactionStatus.RejectedNoChange:
                    return "The trader has nothing to pay you with.";
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

        private static VisualElement MakeColumn(string heading)
        {
            var column = new VisualElement();
            column.style.marginRight = 14f;
            column.style.maxWidth = 420f;
            column.Add(MakeHeading(heading));
            return column;
        }

        private static Label MakeHeading(string text)
        {
            var label = new Label { text = text };
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 14f;
            label.style.marginBottom = 4f;
            label.style.color = new Color(0.86f, 0.88f, 0.92f);
            return label;
        }

        private static Label MakeNote(string text)
        {
            var label = new Label { text = text };
            label.style.marginTop = 6f;
            label.style.fontSize = 11f;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.maxWidth = 320f;
            label.style.color = new Color(0.58f, 0.61f, 0.67f);
            return label;
        }

        private static Button MakeAction(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.style.marginTop = 4f;
            button.style.marginLeft = 0f;
            button.style.marginRight = 0f;
            return button;
        }

        private static VisualElement MakeStat(string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;

            var name = new Label { text = label };
            name.style.fontSize = 11f;
            name.style.color = new Color(0.62f, 0.65f, 0.70f);

            var amount = new Label { text = value };
            amount.style.fontSize = 11f;
            amount.style.color = new Color(0.90f, 0.92f, 0.96f);

            row.Add(name);
            row.Add(amount);
            return row;
        }

        private static void SetBorder(VisualElement element, Color colour)
        {
            element.style.borderLeftWidth = 1f;
            element.style.borderRightWidth = 1f;
            element.style.borderTopWidth = 1f;
            element.style.borderBottomWidth = 1f;
            element.style.borderLeftColor = colour;
            element.style.borderRightColor = colour;
            element.style.borderTopColor = colour;
            element.style.borderBottomColor = colour;
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
            _npcQuery.Dispose();
            _meterQuery.Dispose();
        }
    }
}
