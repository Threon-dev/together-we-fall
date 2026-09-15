using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Audio;
using TogetherWeFall.Equipment;
using TogetherWeFall.Inventory;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;

namespace TogetherWeFall.Lobby.Systems
{
    /// <summary>
    /// Buying and selling.
    ///
    /// One transaction per request, and every check happens before the first
    /// write — the same rule EquipmentSystem follows and for a sharper reason:
    /// a trade that fails halfway is a player who paid and got nothing, or got
    /// something and paid nothing. There is no undo here because there is
    /// nothing to undo.
    ///
    /// It moves cells itself rather than posting a placement request, for the
    /// same reason equipping does: a purchase is a removal from one grid, a
    /// placement in another and a payment, and through separate systems those
    /// are three frames with an item belonging to nobody in the middle of them.
    /// The rules still live in one place — this calls GridFit, it does not
    /// reimplement it.
    ///
    /// ponytail: room for the goods is checked BEFORE the coins leave the bag,
    /// so a purchase that would only fit once the money is gone is refused. That
    /// is conservative rather than wrong, and it keeps the ordering trivially
    /// safe: payment only frees cells, so a fit found before payment is still a
    /// fit after it.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(VendorStockSystem))]
    public partial struct VendorTransactionSystem : ISystem
    {
        // The free-item query used to live here, so a sale could mint the coins
        // it paid out. Money is a number on the character now and mints nothing,
        // so the query, the colour lookup and the "no change" refusal all went
        // with it.

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<VendorComponent>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();

            foreach ((DynamicBuffer<VendorTransactionRequest> requests,
                      DynamicBuffer<VendorTransactionResult> results,
                      RefRO<CarriedBag> bag,
                      Entity character) in
                     SystemAPI.Query<DynamicBuffer<VendorTransactionRequest>,
                         DynamicBuffer<VendorTransactionResult>,
                         RefRO<CarriedBag>>()
                         .WithAll<PlayerCharacter>()
                         .WithEntityAccess())
            {
                if (requests.Length == 0)
                    continue;

                // The character as well as the bag: the purse is a component on
                // it now, and the bag is only where the goods go.
                for (int i = 0; i < requests.Length; i++)
                    results.Add(Apply(ref state, items, requests[i], character, bag.ValueRO.Container));

                // One frame, like every other request queue. A refused trade
                // would be refused again with the same answer.
                requests.Clear();
            }
        }

        private VendorTransactionResult Apply(
            ref SystemState state,
            ItemDatabase items,
            in VendorTransactionRequest request,
            Entity character,
            Entity bag)
        {
            EntityManager entityManager = state.EntityManager;

            if (request.Vendor == Entity.Null ||
                !entityManager.HasComponent<VendorComponent>(request.Vendor))
            {
                return Reject(request, VendorTransactionStatus.RejectedNoVendor, 0);
            }

            VendorComponent vendor =
                entityManager.GetComponentData<VendorComponent>(request.Vendor);

            if (vendor.StockContainer == Entity.Null)
                return Reject(request, VendorTransactionStatus.RejectedNoVendor, 0);

            if (request.Item == Entity.Null ||
                !entityManager.Exists(request.Item) ||
                !entityManager.HasComponent<ItemInstance>(request.Item) ||
                !entityManager.HasComponent<ItemGridPlacement>(request.Item))
            {
                return Reject(request, VendorTransactionStatus.RejectedUnknownItem, 0);
            }

            int itemId = entityManager.GetComponentData<ItemInstance>(request.Item).ItemId;

            int index = items.IndexOf(itemId);
            if (index < 0)
                return Reject(request, VendorTransactionStatus.RejectedUnknownItem, 0);

            // By reference: ItemBlob carries a BlobArray of affixes, and a copy
            // leaves it pointing at nothing useful.
            ref ItemBlob blob = ref items.Value.Value.Items[index];

            return request.Kind == VendorTransactionKind.Buy
                ? Buy(ref state, items, request, vendor, character, bag, ref blob)
                : Sell(ref state, items, request, vendor, character, bag, ref blob);
        }

        private VendorTransactionResult Buy(
            ref SystemState state,
            ItemDatabase items,
            in VendorTransactionRequest request,
            in VendorComponent vendor,
            Entity character,
            Entity bag,
            ref ItemBlob blob)
        {
            EntityManager entityManager = state.EntityManager;

            ItemGridPlacement placement =
                entityManager.GetComponentData<ItemGridPlacement>(request.Item);

            if (placement.ContainerEntity != vendor.StockContainer)
                return Reject(request, VendorTransactionStatus.RejectedNotStocked, 0);

            int price = BuyPrice(vendor, blob.Rarity);

            if (Currency.Balance(entityManager, character) < price)
                return Reject(request, VendorTransactionStatus.RejectedTooPoor, price);

            // Where it will go, worked out before a coin moves. Rotation is not
            // offered: a shop click is not a drag, and the player can turn the
            // item once it is theirs.
            if (!GridFit.TryGetFootprint(items, blob.ItemId, false, out int width, out int height))
                return Reject(request, VendorTransactionStatus.RejectedUnknownItem, price);

            InventoryGridComponent bagGrid =
                entityManager.GetComponentData<InventoryGridComponent>(bag);

            if (!GridFit.FindFirstFit(
                    entityManager.GetBuffer<InventoryCell>(bag),
                    bagGrid, width, height, Entity.Null, out int x, out int y))
            {
                return Reject(request, VendorTransactionStatus.RejectedNoRoom, price);
            }

            // Everything above only looked. From here nothing can fail: payment
            // was already shown to be possible, and it only frees cells, so the
            // square found above is still free.
            if (!Currency.TryPay(entityManager, character, price))
                return Reject(request, VendorTransactionStatus.RejectedTooPoor, price);

            GridFit.Clear(
                entityManager.GetBuffer<InventoryCell>(vendor.StockContainer), request.Item);

            GridFit.Occupy(
                entityManager.GetBuffer<InventoryCell>(bag),
                bagGrid, x, y, width, height, request.Item);

            entityManager.SetComponentData(request.Item, new ItemGridPlacement
            {
                ContainerEntity = bag,
                OriginX = x,
                OriginY = y,
                IsRotated = false
            });

            Announce(ref state, AudioCue.ItemPickup);

            return new VendorTransactionResult
            {
                Item = request.Item,
                Status = VendorTransactionStatus.Bought,
                Price = price
            };
        }

        private VendorTransactionResult Sell(
            ref SystemState state,
            ItemDatabase items,
            in VendorTransactionRequest request,
            in VendorComponent vendor,
            Entity character,
            Entity bag,
            ref ItemBlob blob)
        {
            EntityManager entityManager = state.EntityManager;

            ItemGridPlacement placement =
                entityManager.GetComponentData<ItemGridPlacement>(request.Item);

            // Only out of the seller own bag. An equipped item has no container
            // at all, so this also refuses selling the sword off your back —
            // take it off first, the same as in every game that has a shop.
            if (placement.ContainerEntity != bag)
                return Reject(request, VendorTransactionStatus.RejectedNotCarried, 0);

            if (blob.CurrencyValue > 0)
                return Reject(request, VendorTransactionStatus.RejectedNotForSale, 0);

            int price = SellPrice(vendor, blob.Rarity);

            if (!GridFit.TryGetFootprint(
                    items, blob.ItemId, placement.IsRotated, out int width, out int height))
            {
                return Reject(request, VendorTransactionStatus.RejectedUnknownItem, price);
            }

            InventoryGridComponent shelfGrid =
                entityManager.GetComponentData<InventoryGridComponent>(vendor.StockContainer);

            if (!GridFit.FindFirstFit(
                    entityManager.GetBuffer<InventoryCell>(vendor.StockContainer),
                    shelfGrid, width, height, Entity.Null, out int shelfX, out int shelfY))
            {
                return Reject(request, VendorTransactionStatus.RejectedNoRoom, price);
            }

            // The payout used to need free entities in the item pool and free
            // squares in the bag, and a sale could be refused because the seller
            // had nowhere to put the change — for money they were being handed.
            // A purse is a number, so there is nothing left to check here.
            //
            // Nothing below can fail.
            GridFit.Clear(entityManager.GetBuffer<InventoryCell>(bag), request.Item);

            GridFit.Occupy(
                entityManager.GetBuffer<InventoryCell>(vendor.StockContainer),
                shelfGrid, shelfX, shelfY, width, height, request.Item);

            entityManager.SetComponentData(request.Item, new ItemGridPlacement
            {
                ContainerEntity = vendor.StockContainer,
                OriginX = shelfX,
                OriginY = shelfY,
                IsRotated = placement.IsRotated
            });

            Currency.Grant(entityManager, character, price);

            Announce(ref state, AudioCue.ItemPickup);

            return new VendorTransactionResult
            {
                Item = request.Item,
                Status = VendorTransactionStatus.Sold,
                Price = price
            };
        }

        /// <summary>
        /// Rounded up, so a markup never makes something cheaper: 2 at a 1.5
        /// multiplier is 3, not 2.
        /// </summary>
        private static int BuyPrice(in VendorComponent vendor, ItemRarity rarity) =>
            math.max(1, (int)math.ceil(
                Currency.BasePrice(rarity) * math.max(0f, vendor.BuyPriceMultiplier)));

        /// <summary>
        /// Rounded down, and never less than one: an item worth nothing is an
        /// item the player cannot get rid of, which is worse than a bad price.
        /// </summary>
        private static int SellPrice(in VendorComponent vendor, ItemRarity rarity) =>
            math.max(1, (int)math.floor(
                Currency.BasePrice(rarity) * math.max(0f, vendor.SellPriceMultiplier)));

        /// <summary>
        /// The same seam every other system announces through. There is no
        /// dedicated trade cue yet and inventing one that no clip answers to
        /// would be a row in a table nobody filled.
        /// </summary>
        private void Announce(ref SystemState state, AudioCue cue)
        {
            if (!SystemAPI.TryGetSingletonBuffer(out DynamicBuffer<AudioEvent> audio))
                return;

            audio.Add(new AudioEvent { Cue = cue, Volume = 1f });
        }

        private static VendorTransactionResult Reject(
            in VendorTransactionRequest request, VendorTransactionStatus status, int price)
        {
            return new VendorTransactionResult
            {
                Item = request.Item,
                Status = status,
                Price = price
            };
        }
    }
}
