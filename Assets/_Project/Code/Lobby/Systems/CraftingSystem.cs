using Unity.Entities;
using TogetherWeFall.Config;
using TogetherWeFall.Equipment;
using TogetherWeFall.Inventory;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Lobby.Systems
{
    /// <summary>
    /// The forge: three things a player can pay to change about one item.
    ///
    /// All three touch the socket buffer, and that is not a coincidence — it is
    /// the whole of what is per-instance about an item today. Affixes are
    /// authored on the definition, so two copies of a sword are the same sword
    /// and there is nothing on an instance to reroll; the sockets, their link
    /// groups and the skills welded into them are rolled per item when it comes
    /// out of the pool, and those are exactly what crafting is allowed to touch.
    /// Rerolling affixes is one field on ItemInstance away and it is a different
    /// feature.
    ///
    /// Same transaction rule as trading: everything that can refuse does so
    /// before the first write, because a craft that took the money and did
    /// nothing is worse than one that never happened.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CraftingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<CraftingStation>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();

            foreach ((DynamicBuffer<CraftRequest> requests,
                      DynamicBuffer<CraftResult> results,
                      DynamicBuffer<EquippedItem> worn,
                      RefRO<CarriedBag> bag,
                      Entity character) in
                     SystemAPI.Query<DynamicBuffer<CraftRequest>,
                         DynamicBuffer<CraftResult>,
                         DynamicBuffer<EquippedItem>,
                         RefRO<CarriedBag>>()
                         .WithAll<PlayerCharacter>()
                         .WithEntityAccess())
            {
                if (requests.Length == 0)
                    continue;

                // The character carries the purse; the bag only says what the
                // forge is allowed to touch.
                for (int i = 0; i < requests.Length; i++)
                {
                    results.Add(Apply(
                        ref state, items, requests[i], worn, character, bag.ValueRO.Container));
                }

                requests.Clear();
            }
        }

        private CraftResult Apply(
            ref SystemState state,
            ItemDatabase items,
            in CraftRequest request,
            DynamicBuffer<EquippedItem> worn,
            Entity character,
            Entity bag)
        {
            EntityManager entityManager = state.EntityManager;

            if (request.Station == Entity.Null ||
                !entityManager.HasComponent<CraftingStation>(request.Station))
            {
                return Reject(request, CraftStatus.RejectedNoStation, 0);
            }

            CraftingStation station =
                entityManager.GetComponentData<CraftingStation>(request.Station);

            int price = station.CostOf(request.Operation);

            if (!IsOwnedBy(entityManager, worn, bag, request.Item))
                return Reject(request, CraftStatus.RejectedNotCarried, price);

            // The dry run. Nothing below it writes anything, and nothing above
            // it refuses — which is what makes charging safe.
            CraftStatus check = Validate(entityManager, items, request);
            if (check != CraftStatus.Crafted)
                return Reject(request, check, price);

            if (!Currency.TryPay(entityManager, character, price))
                return Reject(request, CraftStatus.RejectedTooPoor, price);

            Perform(ref state, items, request);

            return new CraftResult
            {
                Item = request.Item,
                Operation = request.Operation,
                Status = CraftStatus.Crafted,
                Price = price
            };
        }

        /// <summary>
        /// Whether this character may craft on this item: it has to be in their
        /// own bag or on their own body.
        ///
        /// Worn gear is allowed deliberately. Every one of these operations is
        /// about holes rather than about stats, the skill bar points at sockets
        /// rather than at skills, and making the player take a weapon off to add
        /// a socket to it would be a rule with nothing behind it.
        /// </summary>
        private static bool IsOwnedBy(
            EntityManager entityManager,
            DynamicBuffer<EquippedItem> worn,
            Entity bag,
            Entity item)
        {
            if (item == Entity.Null ||
                !entityManager.Exists(item) ||
                !entityManager.HasComponent<ItemGridPlacement>(item))
            {
                return false;
            }

            if (entityManager.GetComponentData<ItemGridPlacement>(item).ContainerEntity == bag)
                return true;

            // The same check the cast path, the auto-bind and the panel all ask.
            // Three copies of this loop would eventually disagree.
            return GemSockets.IsWorn(worn, item);
        }

        private static CraftStatus Validate(
            EntityManager entityManager, ItemDatabase items, in CraftRequest request)
        {
            if (!entityManager.HasBuffer<GearSocket>(request.Item))
                return CraftStatus.RejectedNoSuchSocket;

            DynamicBuffer<GearSocket> sockets =
                entityManager.GetBuffer<GearSocket>(request.Item);

            switch (request.Operation)
            {
                case CraftOperation.AddSocket:
                    // The same ceiling the item database bakes against. A
                    // seventeenth hole would be a socket no layout can describe
                    // and no panel has room to draw.
                    return sockets.Length >= ItemDefinition.MaxSockets
                        ? CraftStatus.RejectedSocketLimit
                        : CraftStatus.Crafted;

                case CraftOperation.LinkSocket:
                    if (request.SocketIndex <= 0 || request.SocketIndex >= sockets.Length)
                        return CraftStatus.RejectedNoSuchSocket;

                    return sockets[request.SocketIndex].LinkGroup ==
                           sockets[request.SocketIndex - 1].LinkGroup
                        ? CraftStatus.RejectedAlreadyLinked
                        : CraftStatus.Crafted;

                case CraftOperation.RerollSkills:
                    return HasWeld(sockets) && HasCandidates(items, entityManager, request.Item)
                        ? CraftStatus.Crafted
                        : CraftStatus.RejectedNothingToReroll;

                default:
                    return CraftStatus.RejectedNoSuchSocket;
            }
        }

        private void Perform(
            ref SystemState state, ItemDatabase items, in CraftRequest request)
        {
            EntityManager entityManager = state.EntityManager;

            switch (request.Operation)
            {
                case CraftOperation.AddSocket:
                {
                    DynamicBuffer<GearSocket> sockets =
                        entityManager.GetBuffer<GearSocket>(request.Item);

                    // Into the last hole group rather than a new one. A socket
                    // added on its own island supports nothing and is worth
                    // nothing; joining the end is the useful default, and
                    // LinkSocket is how a player moves it if they wanted
                    // otherwise.
                    int group = sockets.Length > 0 ? sockets[sockets.Length - 1].LinkGroup : 0;

                    sockets.Add(new GearSocket
                    {
                        SocketIndex = sockets.Length,
                        LinkGroup = group,
                        InsertedGem = Entity.Null,
                        WeldedSkillId = 0
                    });

                    break;
                }

                case CraftOperation.LinkSocket:
                {
                    DynamicBuffer<GearSocket> sockets =
                        entityManager.GetBuffer<GearSocket>(request.Item);

                    GearSocket socket = sockets[request.SocketIndex];
                    socket.LinkGroup = sockets[request.SocketIndex - 1].LinkGroup;
                    sockets[request.SocketIndex] = socket;

                    break;
                }

                case CraftOperation.RerollSkills:
                {
                    // The loot dice, not a fresh Random: one reproducible stream
                    // answers for everything about how an item turned out, and a
                    // reroll is one more way it turns out.
                    RefRW<LootRandom> random = SystemAPI.GetSingletonRW<LootRandom>();

                    GemSockets.RerollWelded(
                        entityManager, items, request.Item, ref random.ValueRW.Value);

                    break;
                }
            }
        }

        private static bool HasWeld(DynamicBuffer<GearSocket> sockets)
        {
            for (int i = 0; i < sockets.Length; i++)
            {
                if (sockets[i].IsWelded)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether this item has more than one attack to choose between.
        ///
        /// A weapon with a single authored candidate would roll the same skill
        /// back every time, and charging for that is charging for nothing.
        /// </summary>
        private static bool HasCandidates(
            ItemDatabase items, EntityManager entityManager, Entity item)
        {
            int index = items.IndexOf(
                entityManager.GetComponentData<ItemInstance>(item).ItemId);

            if (index < 0)
                return false;

            ref ItemBlob blob = ref items.Value.Value.Items[index];
            return blob.InnateSkillIds.Length > blob.ActiveSkillCount;
        }

        private static CraftResult Reject(
            in CraftRequest request, CraftStatus status, int price)
        {
            return new CraftResult
            {
                Item = request.Item,
                Operation = request.Operation,
                Status = status,
                Price = price
            };
        }
    }
}
