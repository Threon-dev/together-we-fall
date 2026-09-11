using Unity.Entities;
using TogetherWeFall.Inventory;
using TogetherWeFall.Inventory.Systems;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Equipment.Systems
{
    /// <summary>
    /// Puts gems into sockets, takes them out, and points the skill bar at them.
    ///
    /// Four kinds of request in one system, the same shape as the equipment
    /// queue and for the same reason: inserting a gem is a removal from the grid
    /// AND a write to a socket, and across two systems there is a frame in which
    /// the gem is in neither, plus a refusal halfway that loses it. Everything
    /// that can fail is checked before anything is written.
    ///
    /// Socketing works whether the gear is worn or sitting in the bag, and
    /// whether the player is in a dungeon or not. It is not a combat action —
    /// PoE lets you rearrange gems standing in a boss room and so does this. The
    /// only thing being worn changes is whether the socket can feed the bar.
    ///
    /// A bar slot pointing at a socket whose gem was pulled out needs no repair.
    /// The skill is derived from the socket at cast time, so an emptied socket
    /// simply casts nothing — there is no cached skill to go stale.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(InventoryPlacementSystem))]
    public partial struct SocketSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<SkillDatabase>();
            state.RequireForUpdate<PlayerCharacter>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();
            SkillDatabase skills = SystemAPI.GetSingleton<SkillDatabase>();
            EntityManager entityManager = state.EntityManager;

            foreach ((DynamicBuffer<SocketRequest> requests,
                      DynamicBuffer<SocketResult> results,
                      DynamicBuffer<SkillSlot> bar,
                      DynamicBuffer<EquippedItem> worn,
                      RefRO<CarriedBag> bag,
                      RefRO<PlayerCharacter> character) in
                     SystemAPI.Query<DynamicBuffer<SocketRequest>,
                         DynamicBuffer<SocketResult>,
                         DynamicBuffer<SkillSlot>,
                         DynamicBuffer<EquippedItem>,
                         RefRO<CarriedBag>,
                         RefRO<PlayerCharacter>>())
            {
                if (requests.Length == 0)
                    continue;

                for (int i = 0; i < requests.Length; i++)
                {
                    results.Add(Apply(
                        entityManager, items, skills, requests[i], bar, worn,
                        bag.ValueRO.Container, character.ValueRO.PlayerId));
                }

                // Requests live for one frame. A refused one is not retried: the
                // answer would be the same, and a queue that never drains is a
                // queue that grows.
                requests.Clear();
            }
        }

        private static SocketResult Apply(
            EntityManager entityManager,
            ItemDatabase items,
            SkillDatabase skills,
            in SocketRequest request,
            DynamicBuffer<SkillSlot> bar,
            DynamicBuffer<EquippedItem> worn,
            Entity bag,
            int playerId)
        {
            switch (request.Kind)
            {
                case SocketRequestKind.Insert:
                    return Insert(entityManager, items, request, worn, bag, playerId);

                case SocketRequestKind.Remove:
                    return Remove(entityManager, items, request, worn, bag, playerId);

                case SocketRequestKind.BindBar:
                    return BindBar(entityManager, items, skills, request, bar);

                default:
                    return ClearBar(request, bar);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Gems in and out
        // ─────────────────────────────────────────────────────────────────

        private static SocketResult Insert(
            EntityManager entityManager,
            ItemDatabase items,
            in SocketRequest request,
            DynamicBuffer<EquippedItem> worn,
            Entity bag,
            int playerId)
        {
            if (!Owns(entityManager, request.Gear, worn, bag))
                return Reject(request, SocketStatus.RejectedNotCarried);

            // The gem has to be loose in the bag. One already in a socket moves
            // by coming out first, which is a second request and a second
            // chance for the bag to be full.
            if (!IsInBag(entityManager, request.Gem, bag))
                return Reject(request, SocketStatus.RejectedNotCarried);

            if (!GemSockets.TryDescribeGem(entityManager, items, request.Gem, out _, out _, out _))
                return Reject(request, SocketStatus.RejectedNotAGem);

            if (!entityManager.HasBuffer<GearSocket>(request.Gear))
                return Reject(request, SocketStatus.RejectedNoSuchSocket);

            DynamicBuffer<GearSocket> sockets =
                entityManager.GetBuffer<GearSocket>(request.Gear);

            if (request.SocketIndex < 0 || request.SocketIndex >= sockets.Length)
                return Reject(request, SocketStatus.RejectedNoSuchSocket);

            // Welded first, so the refusal says which of the two it is. "Full"
            // invites the player to take the thing out and try again, and this
            // is the one socket where that will never work.
            if (sockets[request.SocketIndex].IsWelded)
                return Reject(request, SocketStatus.RejectedWelded);

            if (!sockets[request.SocketIndex].IsEmpty)
                return Reject(request, SocketStatus.RejectedSocketFull);

            // Nothing above this line changed anything, so everything below is
            // safe to do in order.
            GridFit.Clear(entityManager.GetBuffer<InventoryCell>(bag), request.Gem);

            entityManager.SetComponentData(request.Gem, new ItemGridPlacement
            {
                ContainerEntity = Entity.Null,
                OriginX = 0,
                OriginY = 0,
                IsRotated = false
            });

            GearSocket socket = sockets[request.SocketIndex];
            socket.InsertedGem = request.Gem;
            sockets[request.SocketIndex] = socket;

            UnityEngine.Debug.Log(
                $"[SocketSystem] Player {playerId} socketed a gem into slot {request.SocketIndex}.");

            return new SocketResult
            {
                Gear = request.Gear,
                Gem = request.Gem,
                SocketIndex = request.SocketIndex,
                Status = SocketStatus.Inserted
            };
        }

        private static SocketResult Remove(
            EntityManager entityManager,
            ItemDatabase items,
            in SocketRequest request,
            DynamicBuffer<EquippedItem> worn,
            Entity bag,
            int playerId)
        {
            if (!Owns(entityManager, request.Gear, worn, bag))
                return Reject(request, SocketStatus.RejectedNotCarried);

            if (!entityManager.HasBuffer<GearSocket>(request.Gear))
                return Reject(request, SocketStatus.RejectedNoSuchSocket);

            DynamicBuffer<GearSocket> sockets =
                entityManager.GetBuffer<GearSocket>(request.Gear);

            if (request.SocketIndex < 0 || request.SocketIndex >= sockets.Length)
                return Reject(request, SocketStatus.RejectedNoSuchSocket);

            GearSocket socket = sockets[request.SocketIndex];

            // The weapon's own attack. There is no gem entity to hand back — the
            // forge put an id there, not an item — so this is not a refusal that
            // could go the other way on a better day.
            if (socket.IsWelded)
                return Reject(request, SocketStatus.RejectedWelded);

            if (socket.IsEmpty)
                return Reject(request, SocketStatus.RejectedSocketEmpty);

            if (bag == Entity.Null || !entityManager.HasComponent<InventoryCell>(bag))
                return Reject(request, SocketStatus.RejectedBagFull);

            // Checked before the socket is cleared, never after. A gem out of
            // its socket and not yet in the bag exists nowhere — the same rule
            // that makes unequipping refuse rather than lose a breastplate.
            if (!TryReturnToBag(entityManager, items, bag, socket.InsertedGem))
            {
                UnityEngine.Debug.LogWarning(
                    $"[SocketSystem] Player {playerId} has no room for the gem — refused.");
                return Reject(request, SocketStatus.RejectedBagFull);
            }

            Entity gem = socket.InsertedGem;

            socket.InsertedGem = Entity.Null;
            sockets[request.SocketIndex] = socket;

            return new SocketResult
            {
                Gear = request.Gear,
                Gem = gem,
                SocketIndex = request.SocketIndex,
                Status = SocketStatus.Removed
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // The bar
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Points a hotkey at a socket.
        ///
        /// The socket must hold an ACTIVE gem. A support gem never casts on its
        /// own — that is what makes it a support — and letting one onto the bar
        /// would produce a hotkey that silently does nothing.
        /// </summary>
        private static SocketResult BindBar(
            EntityManager entityManager,
            ItemDatabase items,
            SkillDatabase skills,
            in SocketRequest request,
            DynamicBuffer<SkillSlot> bar)
        {
            if (request.BarSlotIndex < 0 || request.BarSlotIndex >= bar.Length)
                return Reject(request, SocketStatus.RejectedNoSuchBarSlot);

            if (!GemSockets.TryResolveActive(
                    entityManager, items, skills, request.Gear, request.SocketIndex,
                    out _, out _))
            {
                return Reject(request, SocketStatus.RejectedNotActive);
            }

            SkillSlot slot = bar[request.BarSlotIndex];
            slot.Gear = request.Gear;
            slot.SocketIndex = request.SocketIndex;

            // The cooldown is deliberately NOT reset. Swapping a hotkey onto a
            // fresh socket to skip a cooldown is the first thing anybody tries.
            bar[request.BarSlotIndex] = slot;

            return new SocketResult
            {
                Gear = request.Gear,
                SocketIndex = request.SocketIndex,
                Status = SocketStatus.Bound
            };
        }

        private static SocketResult ClearBar(
            in SocketRequest request, DynamicBuffer<SkillSlot> bar)
        {
            if (request.BarSlotIndex < 0 || request.BarSlotIndex >= bar.Length)
                return Reject(request, SocketStatus.RejectedNoSuchBarSlot);

            SkillSlot slot = bar[request.BarSlotIndex];
            slot.Gear = Entity.Null;
            slot.SocketIndex = 0;
            bar[request.BarSlotIndex] = slot;

            return new SocketResult
            {
                SocketIndex = request.SocketIndex,
                Status = SocketStatus.Cleared
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // Ownership and the bag
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether this player owns this gear, worn or carried.
        ///
        /// Both, because socketing does not care which. A client naming a piece
        /// of gear it does not own is the thing being stopped here, not a client
        /// tinkering with something in its own bag.
        /// </summary>
        private static bool Owns(
            EntityManager entityManager,
            Entity gear,
            DynamicBuffer<EquippedItem> worn,
            Entity bag)
        {
            if (gear == Entity.Null || !entityManager.Exists(gear))
                return false;

            for (int i = 0; i < worn.Length; i++)
            {
                if (worn[i].Item == gear)
                    return true;
            }

            return IsInBag(entityManager, gear, bag);
        }

        private static bool IsInBag(EntityManager entityManager, Entity item, Entity bag)
        {
            if (item == Entity.Null || !entityManager.Exists(item) ||
                !entityManager.HasComponent<ItemGridPlacement>(item))
            {
                return false;
            }

            return entityManager.GetComponentData<ItemGridPlacement>(item).ContainerEntity == bag;
        }

        /// <summary>
        /// Finds room for a gem and writes it into the grid. Gems are small, so
        /// rotation never comes into it — but the fit is still asked of GridFit
        /// rather than assumed.
        /// </summary>
        private static bool TryReturnToBag(
            EntityManager entityManager, ItemDatabase items, Entity bag, Entity gem)
        {
            int itemId = entityManager.GetComponentData<ItemInstance>(gem).ItemId;

            if (!GridFit.TryGetFootprint(items, itemId, false, out int width, out int height))
                return false;

            InventoryGridComponent grid =
                entityManager.GetComponentData<InventoryGridComponent>(bag);
            DynamicBuffer<InventoryCell> cells = entityManager.GetBuffer<InventoryCell>(bag);

            if (!GridFit.FindFirstFit(cells, grid, width, height, gem, out int x, out int y))
                return false;

            GridFit.Occupy(cells, grid, x, y, width, height, gem);

            entityManager.SetComponentData(gem, new ItemGridPlacement
            {
                ContainerEntity = bag,
                OriginX = x,
                OriginY = y,
                IsRotated = false
            });

            return true;
        }

        private static SocketResult Reject(in SocketRequest request, SocketStatus status)
        {
            return new SocketResult
            {
                Gear = request.Gear,
                Gem = request.Gem,
                SocketIndex = request.SocketIndex,
                Status = status
            };
        }
    }
}
