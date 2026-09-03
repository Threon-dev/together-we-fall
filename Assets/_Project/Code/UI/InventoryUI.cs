using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;

namespace TogetherWeFall.UI
{
    /// <summary>
    /// The inventory and character sheet panel.
    ///
    /// It owns no game state whatsoever. Every time it draws, it reads the
    /// player character out of ECS — stats, slots, inventory — and renders what
    /// it found. Clicking a button does not equip anything either; it appends an
    /// EquipRequest and waits to be told what happened, exactly like a client
    /// would across a wire. The panel cannot show you wearing something the host
    /// refused, because it has nowhere to remember such a thing.
    ///
    /// Rebuilt only when the data behind it changes, tracked by the stat version
    /// and the shape of the inventory. A UI that rebuilds its element tree every
    /// frame is the cheapest way to make a profile unreadable.
    ///
    /// The visual tree is built in code rather than from a UXML asset. For a
    /// panel whose contents are entirely generated from a buffer, a layout file
    /// would describe nothing but an empty container.
    /// </summary>
    public sealed class InventoryUI : MonoBehaviour
    {
        [SerializeField] private UIDocument _document;

        [SerializeField] private int _panelWidth = 420;

        private PlayerInputReader _input;
        private EntityManager _entityManager;
        private EntityQuery _characterQuery;
        private EntityQuery _itemDatabaseQuery;

        private int _playerId;
        private bool _hasWorld;
        private bool _visible;
        private int _lastSignature;

        private VisualElement _panel;
        private VisualElement _statsList;
        private VisualElement _slotList;
        private VisualElement _inventoryList;

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
                ComponentType.ReadOnly<PlayerStats>());

            _itemDatabaseQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ItemDatabase>());

            _hasWorld = true;
        }

        private void Update()
        {
            if (!_hasWorld || _input == null || !EnsureTree())
                return;

            if (_input.WasInventoryTogglePressed())
            {
                _visible = !_visible;
                _panel.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;

                // Force a rebuild on the next visible frame: the data may well
                // have moved on while the panel was closed.
                _lastSignature = int.MinValue;
            }

            if (_visible)
                RefreshIfChanged();
        }

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

            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.top = 16f;
            _panel.style.right = 16f;
            _panel.style.width = _panelWidth;
            _panel.style.paddingLeft = 12f;
            _panel.style.paddingRight = 12f;
            _panel.style.paddingTop = 10f;
            _panel.style.paddingBottom = 12f;
            _panel.style.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 0.92f);
            _panel.style.display = DisplayStyle.None;

            _panel.Add(MakeHeading("Character"));
            _statsList = MakeSection(_panel);

            _panel.Add(MakeHeading("Equipped"));
            _slotList = MakeSection(_panel);

            _panel.Add(MakeHeading("Inventory"));
            _inventoryList = MakeSection(_panel);

            root.Add(_panel);
            return true;
        }

        private void RefreshIfChanged()
        {
            if (!TryGetCharacter(out Entity character))
            {
                ShowMessage("No character yet.");
                return;
            }

            PlayerStats stats = _entityManager.GetComponentData<PlayerStats>(character);
            DynamicBuffer<EquippedItem> slots =
                _entityManager.GetBuffer<EquippedItem>(character, isReadOnly: true);
            DynamicBuffer<InventoryItem> inventory =
                _entityManager.GetBuffer<InventoryItem>(character, isReadOnly: true);

            int signature = Signature(stats, slots, inventory);
            if (signature == _lastSignature)
                return;

            _lastSignature = signature;

            if (_itemDatabaseQuery.IsEmptyIgnoreFilter)
            {
                ShowMessage("No item database baked.");
                return;
            }

            ItemDatabase items = _itemDatabaseQuery.GetSingleton<ItemDatabase>();

            RebuildStats(stats);
            RebuildSlots(slots, items);
            RebuildInventory(inventory, slots, items);
        }

        private void RebuildStats(in PlayerStats stats)
        {
            _statsList.Clear();

            for (int s = 0; s < StatBlock.StatCount; s++)
            {
                var stat = (StatKind)s;
                _statsList.Add(MakeRow(stat.ToString(), $"{stats.Final.Get(stat):0.##}", null));
            }
        }

        private void RebuildSlots(DynamicBuffer<EquippedItem> slots, ItemDatabase items)
        {
            _slotList.Clear();

            for (int i = 0; i < slots.Length; i++)
            {
                EquippedItem slot = slots[i];

                if (!slot.HasItem)
                {
                    _slotList.Add(MakeRow(slot.Slot.ToString(), "empty", null));
                    continue;
                }

                EquipmentSlot capturedSlot = slot.Slot;
                Button unequip = MakeButton("Unequip",
                    () => SendRequest(EquippedItem.Empty, capturedSlot, equip: false));

                _slotList.Add(MakeRow(slot.Slot.ToString(), NameOf(items, slot.ItemId), unequip));
            }
        }

        private void RebuildInventory(
            DynamicBuffer<InventoryItem> inventory,
            DynamicBuffer<EquippedItem> slots,
            ItemDatabase items)
        {
            _inventoryList.Clear();

            if (inventory.Length == 0)
            {
                _inventoryList.Add(MakeRow("Nothing carried", string.Empty, null));
                return;
            }

            for (int i = 0; i < inventory.Length; i++)
            {
                InventoryItem entry = inventory[i];
                int capturedId = entry.ItemId;

                bool equipped = IsEquipped(slots, capturedId);
                Button action = equipped
                    ? null
                    : MakeButton("Equip", () => SendRequest(capturedId, EquipmentSlot.Weapon, equip: true));

                string label = NameOf(items, capturedId);
                string detail = equipped ? $"{entry.Rarity} · equipped" : entry.Rarity.ToString();

                VisualElement row = MakeRow(label, detail, action);
                row.Q<Label>().style.color = RarityColour(entry.Rarity);

                _inventoryList.Add(row);
            }
        }

        /// <summary>
        /// Appends a request and returns. The slot travels with it only so an
        /// unequip knows what to clear — for an equip the host reads the slot off
        /// the item definition and ignores whatever is passed here.
        /// </summary>
        private void SendRequest(int itemId, EquipmentSlot slot, bool equip)
        {
            if (!TryGetCharacter(out Entity character))
                return;

            _entityManager.GetBuffer<EquipRequest>(character).Add(new EquipRequest
            {
                ItemId = itemId,
                Slot = slot,
                Equip = equip
            });

            // Nothing is drawn differently yet. The next refresh will show
            // whatever the host decided, which may be nothing at all.
            _lastSignature = int.MinValue;
        }

        private bool TryGetCharacter(out Entity character)
        {
            character = Entity.Null;

            if (_characterQuery.IsEmptyIgnoreFilter)
                return false;

            using NativeArray<Entity> entities = _characterQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PlayerCharacter> characters =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);

            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i].PlayerId != _playerId)
                    continue;

                character = entities[i];
                return true;
            }

            return false;
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

        private static bool IsEquipped(DynamicBuffer<EquippedItem> slots, int itemId)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].ItemId == itemId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// What the panel is showing, in one number. Version covers every stat
        /// and slot change, because equipping is the only thing that moves them;
        /// the inventory count and the ids cover pickups.
        /// </summary>
        private static int Signature(
            in PlayerStats stats,
            DynamicBuffer<EquippedItem> slots,
            DynamicBuffer<InventoryItem> inventory)
        {
            unchecked
            {
                int hash = stats.Version * 31 + inventory.Length;

                for (int i = 0; i < slots.Length; i++)
                    hash = hash * 31 + slots[i].ItemId;

                for (int i = 0; i < inventory.Length; i++)
                    hash = hash * 31 + inventory[i].ItemId;

                return hash;
            }
        }

        private void ShowMessage(string message)
        {
            _statsList.Clear();
            _slotList.Clear();
            _inventoryList.Clear();
            _statsList.Add(MakeRow(message, string.Empty, null));
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

        private static Button MakeButton(string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.fontSize = 12;
            button.style.paddingLeft = 6f;
            button.style.paddingRight = 6f;
            button.style.marginLeft = 0f;
            button.style.marginRight = 0f;
            return button;
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

        private void OnDestroy()
        {
            if (!_hasWorld)
                return;

            _characterQuery.Dispose();
            _itemDatabaseQuery.Dispose();
        }
    }
}
