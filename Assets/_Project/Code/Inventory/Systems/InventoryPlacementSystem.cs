using Unity.Entities;
using TogetherWeFall.Audio;
using TogetherWeFall.Equipment;
using TogetherWeFall.Interaction;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;

namespace TogetherWeFall.Inventory.Systems
{
    /// <summary>
    /// The host half of moving items around a grid.
    ///
    /// One system for placing, auto-placing and removing rather than three.
    /// That is a deliberate departure from the shape the other pipelines in this
    /// project take, and the reason is that these three are not stages of one
    /// operation — they are one operation asked three ways. A move is a removal
    /// and a placement, and across two systems it is two frames, with a frame in
    /// between where the item is in no container at all. The failure that causes
    /// is not a dropped frame, it is an item that disappears when a placement is
    /// refused after its removal already committed.
    ///
    /// The same argument the conventions already make about skill modifiers:
    /// several systems that almost never have anything to iterate, and the
    /// answer to "why did it land there" spread across all of them.
    ///
    /// What it never does is decide. GridFit holds the rules; this reads a
    /// request, checks it is allowed to be asked, and writes down what happened.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TogetherWeFall.Loot.Systems.ItemPickupSystem))]
    public partial struct InventoryPlacementSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<PlayerCharacter>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();
            EntityManager entityManager = state.EntityManager;

            // Taken once for the whole frame rather than looked up per pickup.
            // Nothing below is a structural change, so it stays valid.
            DynamicBuffer<AudioEvent> audio = SystemAPI.GetSingletonBuffer<AudioEvent>();

            foreach ((DynamicBuffer<InventoryPlacementRequest> requests,
                      DynamicBuffer<InventoryPlacementResult> results,
                      RefRO<PlayerCharacter> character) in
                     SystemAPI.Query<DynamicBuffer<InventoryPlacementRequest>,
                         DynamicBuffer<InventoryPlacementResult>,
                         RefRO<PlayerCharacter>>())
            {
                if (requests.Length == 0)
                    continue;

                for (int i = 0; i < requests.Length; i++)
                {
                    results.Add(Apply(
                        entityManager, items, audio, requests[i], character.ValueRO.PlayerId));
                }

                // Requests live for one frame. A refused one is not retried: the
                // answer would be the same, and a queue that never drains is a
                // queue that grows.
                requests.Clear();
            }
        }

        /// <summary>
        /// Nothing here is a structural change — every write is SetComponentData
        /// or SetComponentEnabled — so the buffers taken above stay valid for the
        /// whole loop, and a request can see what the request before it did.
        /// </summary>
        private static InventoryPlacementResult Apply(
            EntityManager entityManager,
            ItemDatabase items,
            DynamicBuffer<AudioEvent> audio,
            in InventoryPlacementRequest request,
            int playerId)
        {
            if (request.Item == Entity.Null ||
                !entityManager.Exists(request.Item) ||
                !entityManager.HasComponent<ItemInstance>(request.Item) ||
                !entityManager.HasComponent<ItemGridPlacement>(request.Item))
            {
                return Reject(request, InventoryPlacementStatus.RejectedUnknownItem);
            }

            return request.Mode == InventoryPlacementMode.Remove
                ? Remove(entityManager, request)
                : Place(entityManager, items, audio, request, playerId);
        }

        private static InventoryPlacementResult Place(
            EntityManager entityManager,
            ItemDatabase items,
            DynamicBuffer<AudioEvent> audio,
            in InventoryPlacementRequest request,
            int playerId)
        {
            if (!IsOwnedContainer(entityManager, request.Container, playerId))
                return Reject(request, InventoryPlacementStatus.RejectedNoContainer);

            ItemInstance instance = entityManager.GetComponentData<ItemInstance>(request.Item);

            // The client asked to rotate. Whether it may is the item's business,
            // and refusing is better than quietly placing it unrotated somewhere
            // the player did not point at.
            if (request.Rotated && !GridFit.CanRotate(items, instance.ItemId))
                return Reject(request, InventoryPlacementStatus.RejectedCannotRotate);

            if (!GridFit.TryGetFootprint(
                    items, instance.ItemId, request.Rotated, out int width, out int height))
            {
                return Reject(request, InventoryPlacementStatus.RejectedUnknownItem);
            }

            InventoryGridComponent grid =
                entityManager.GetComponentData<InventoryGridComponent>(request.Container);
            DynamicBuffer<InventoryCell> cells =
                entityManager.GetBuffer<InventoryCell>(request.Container);

            ItemGridPlacement placement =
                entityManager.GetComponentData<ItemGridPlacement>(request.Item);

            // Cleared from wherever it is now BEFORE the fit is tested, so that
            // an item never collides with itself, and so that a move between two
            // containers is one step rather than two frames apart.
            DynamicBuffer<InventoryCell> sourceCells = cells;
            bool movedContainer = placement.ContainerEntity != Entity.Null &&
                                  placement.ContainerEntity != request.Container;

            if (movedContainer &&
                entityManager.HasComponent<InventoryCell>(placement.ContainerEntity))
            {
                sourceCells = entityManager.GetBuffer<InventoryCell>(placement.ContainerEntity);
            }

            int clearedCells = GridFit.Clear(sourceCells, request.Item);

            int originX = request.OriginX;
            int originY = request.OriginY;
            bool fits;

            if (request.Mode == InventoryPlacementMode.Auto)
            {
                fits = GridFit.FindFirstFit(
                    cells, grid, width, height, request.Item, out originX, out originY);
            }
            else
            {
                fits = GridFit.Fits(
                    cells, grid, originX, originY, width, height, request.Item);
            }

            if (!fits)
            {
                // Put it back exactly where it was. The client is told no, and
                // nothing about the world changed — which is the only honest
                // meaning of a refused request.
                if (clearedCells > 0)
                {
                    RestorePrevious(
                        entityManager, items, sourceCells, placement, request.Item, instance.ItemId);
                }

                return Reject(request, FailureFor(request, grid, originX, originY, width, height));
            }

            GridFit.Occupy(cells, grid, originX, originY, width, height, request.Item);

            entityManager.SetComponentData(request.Item, new ItemGridPlacement
            {
                ContainerEntity = request.Container,
                OriginX = originX,
                OriginY = originY,
                IsRotated = request.Rotated
            });

            // Off the floor and into ownership. Only reached once the cells are
            // written, so an item is never both invisible and un-owned.
            if (!entityManager.IsComponentEnabled<ItemStored>(request.Item))
            {
                LootItemPool.Store(entityManager, request.Item);

                // Only for an item that was actually picked up. Shuffling
                // something around inside the bag is not a pickup, and hearing
                // it as one is how tidying up starts sounding like a haul.
                audio.Add(new AudioEvent { Cue = AudioCue.ItemPickup, Volume = 1f });
            }

            return new InventoryPlacementResult
            {
                Item = request.Item,
                Status = InventoryPlacementStatus.Placed,
                OriginX = originX,
                OriginY = originY,
                Rotated = request.Rotated
            };
        }

        /// <summary>
        /// Takes an item out of its container without giving it a new one.
        ///
        /// It stays owned — ItemStored is untouched — because the only caller
        /// that wants this is equipping, and an equipped item is very much still
        /// the player's. An item that should cease to exist goes back to the
        /// pool through LootItemPool.Release instead.
        /// </summary>
        private static InventoryPlacementResult Remove(
            EntityManager entityManager, in InventoryPlacementRequest request)
        {
            ItemGridPlacement placement =
                entityManager.GetComponentData<ItemGridPlacement>(request.Item);

            if (placement.ContainerEntity != Entity.Null &&
                entityManager.HasComponent<InventoryCell>(placement.ContainerEntity))
            {
                GridFit.Clear(
                    entityManager.GetBuffer<InventoryCell>(placement.ContainerEntity),
                    request.Item);
            }

            entityManager.SetComponentData(request.Item, new ItemGridPlacement
            {
                ContainerEntity = Entity.Null,
                OriginX = 0,
                OriginY = 0,
                IsRotated = false
            });

            return new InventoryPlacementResult
            {
                Item = request.Item,
                Status = InventoryPlacementStatus.Removed
            };
        }

        /// <summary>
        /// Undoes the speculative clear when a placement turns out not to fit.
        ///
        /// The footprint is recomputed from the placement that was recorded
        /// rather than remembered from the clear, because the clear counted
        /// cells and cells do not say which way round the item was lying.
        /// </summary>
        private static void RestorePrevious(
            EntityManager entityManager,
            ItemDatabase items,
            DynamicBuffer<InventoryCell> cells,
            in ItemGridPlacement placement,
            Entity item,
            int itemId)
        {
            if (placement.ContainerEntity == Entity.Null)
                return;

            if (!GridFit.TryGetFootprint(
                    items, itemId, placement.IsRotated, out int width, out int height))
            {
                return;
            }

            InventoryGridComponent grid =
                entityManager.GetComponentData<InventoryGridComponent>(placement.ContainerEntity);

            GridFit.Occupy(
                cells, grid, placement.OriginX, placement.OriginY, width, height, item);
        }

        /// <summary>
        /// Which flavour of "no" this was.
        ///
        /// Not cosmetic: out of bounds means the player dragged past the edge and
        /// the panel should say nothing, occupied means something is in the way
        /// and the square should have been red, and no room means the bag is full
        /// and that is worth telling them about.
        /// </summary>
        private static InventoryPlacementStatus FailureFor(
            in InventoryPlacementRequest request,
            in InventoryGridComponent grid,
            int originX,
            int originY,
            int width,
            int height)
        {
            if (request.Mode == InventoryPlacementMode.Auto)
                return InventoryPlacementStatus.RejectedNoRoom;

            bool inBounds = originX >= 0 && originY >= 0 &&
                            originX + width <= grid.Width &&
                            originY + height <= grid.Height;

            return inBounds
                ? InventoryPlacementStatus.RejectedOccupied
                : InventoryPlacementStatus.RejectedOutOfBounds;
        }

        /// <summary>
        /// A container only counts as somewhere this player may put things when
        /// it is a container and it is theirs. Both halves matter: the first
        /// stops a request naming any entity at all, the second stops it naming
        /// another player's bag.
        /// </summary>
        private static bool IsOwnedContainer(
            EntityManager entityManager, Entity container, int playerId)
        {
            if (container == Entity.Null || !entityManager.Exists(container))
                return false;

            if (!entityManager.HasComponent<InventoryGridComponent>(container) ||
                !entityManager.HasComponent<InventoryCell>(container))
            {
                return false;
            }

            // No owner means a shared container, and there are none yet. When a
            // shared stash arrives this is where the conversation about who may
            // write to it happens.
            if (!entityManager.HasComponent<ContainerOwner>(container))
                return false;

            return entityManager.GetComponentData<ContainerOwner>(container).PlayerId == playerId;
        }

        private static InventoryPlacementResult Reject(
            in InventoryPlacementRequest request, InventoryPlacementStatus status)
        {
            return new InventoryPlacementResult
            {
                Item = request.Item,
                Status = status
            };
        }
    }
}
