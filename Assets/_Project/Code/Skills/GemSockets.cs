using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;

namespace TogetherWeFall.Skills
{
    /// <summary>
    /// Everything that reads sockets and the gems in them.
    ///
    /// A struct of static methods, the same shape as GridFit and EquipmentSlots
    /// and for the same reason: the cast system needs these answers to fold a
    /// skill, the socket system needs them to check a request, and the panel
    /// will need them to draw a gem in a hole. Three callers, one set of rules.
    ///
    /// Nothing here caches. What skill a bar slot casts is derived from the gem
    /// currently in the socket it points at, every time — a cached copy would be
    /// a second answer to that question, and the moment a gem is pulled out the
    /// two would disagree until somebody remembered to invalidate it. Deriving
    /// costs a buffer read and a binary search, per cast, on the main thread.
    /// </summary>
    public struct GemSockets
    {
        /// <summary>
        /// The most supports one link group can feed a skill.
        ///
        /// Six sockets is the largest authored layout, and one of them holds the
        /// active, so five is the ceiling. The list is fixed-size because this
        /// is gathered per cast and a cast in this game happens in the middle of
        /// a chain reaction.
        /// </summary>
        public const int MaxSupportsPerGroup = 5;

        /// <summary>What a gem is, read out of the item database.</summary>
        public static bool TryDescribeGem(
            EntityManager entityManager,
            ItemDatabase items,
            Entity gem,
            out GemKind kind,
            out int skillId,
            out SkillModifierBlob support)
        {
            kind = GemKind.None;
            skillId = 0;
            support = default;

            if (gem == Entity.Null || !entityManager.Exists(gem) ||
                !entityManager.HasComponent<ItemInstance>(gem))
            {
                return false;
            }

            int itemId = entityManager.GetComponentData<ItemInstance>(gem).ItemId;

            int index = items.IndexOf(itemId);
            if (index < 0)
                return false;

            // By reference: ItemBlob carries a BlobArray of affixes, and copying
            // the struct leaves that array pointing at nothing useful. The three
            // values taken out of it are flat, so they may travel.
            ref ItemBlob item = ref items.Value.Value.Items[index];

            kind = item.GemKind;
            skillId = item.GemSkillId;
            support = item.GemSupport;

            return kind != GemKind.None;
        }

        /// <summary>
        /// The socket a bar slot points at, or false when the gear is gone, not
        /// socketed, or the index is past the end.
        /// </summary>
        public static bool TryReadSocket(
            EntityManager entityManager, Entity gear, int socketIndex, out GearSocket socket)
        {
            socket = default;

            if (gear == Entity.Null || !entityManager.Exists(gear) ||
                !entityManager.HasBuffer<GearSocket>(gear))
            {
                return false;
            }

            DynamicBuffer<GearSocket> sockets = entityManager.GetBuffer<GearSocket>(gear, true);

            if (socketIndex < 0 || socketIndex >= sockets.Length)
                return false;

            socket = sockets[socketIndex];
            return true;
        }

        /// <summary>
        /// The active skill a socket casts, if it holds an active gem at all.
        /// </summary>
        public static bool TryResolveActive(
            EntityManager entityManager,
            ItemDatabase items,
            SkillDatabase skills,
            Entity gear,
            int socketIndex,
            out int skillIndex,
            out int linkGroup)
        {
            skillIndex = -1;
            linkGroup = -1;

            if (!TryReadSocket(entityManager, gear, socketIndex, out GearSocket socket))
                return false;

            if (socket.IsEmpty)
                return false;

            if (!TryDescribeGem(entityManager, items, socket.InsertedGem,
                    out GemKind kind, out int skillId, out _))
            {
                return false;
            }

            if (kind != GemKind.Active)
                return false;

            skillIndex = skills.IndexOf(skillId);
            linkGroup = socket.LinkGroup;

            return skillIndex >= 0;
        }

        /// <summary>
        /// Every support gem linked to a group on one piece of gear.
        ///
        /// This is where a build comes from. The same active gem gathers a
        /// different list depending on what is sitting beside it, and the fold
        /// downstream does not know or care that the numbers arrived from
        /// sockets rather than from an asset.
        ///
        /// Every support in the group applies to every active in it, which is
        /// the PoE rule and is free here: the group is walked once and the
        /// actives are not consulted.
        /// </summary>
        public static FixedList128Bytes<SkillModifierBlob> GatherSupports(
            EntityManager entityManager, ItemDatabase items, Entity gear, int linkGroup)
        {
            var supports = new FixedList128Bytes<SkillModifierBlob>();

            if (linkGroup < 0 || gear == Entity.Null || !entityManager.Exists(gear) ||
                !entityManager.HasBuffer<GearSocket>(gear))
            {
                return supports;
            }

            DynamicBuffer<GearSocket> sockets = entityManager.GetBuffer<GearSocket>(gear, true);

            for (int i = 0; i < sockets.Length; i++)
            {
                if (sockets[i].LinkGroup != linkGroup || sockets[i].IsEmpty)
                    continue;

                if (!TryDescribeGem(entityManager, items, sockets[i].InsertedGem,
                        out GemKind kind, out _, out SkillModifierBlob support))
                {
                    continue;
                }

                if (kind != GemKind.Support)
                    continue;

                // Past capacity the extra supports are ignored rather than
                // allocating mid-cast. Five is more than any authored layout can
                // hold beside an active, so reaching this needs a layout nobody
                // has written yet.
                if (supports.Length >= MaxSupportsPerGroup)
                    break;

                supports.Add(support);
            }

            return supports;
        }

        /// <summary>
        /// Rebuilds an item's sockets from the layout its definition authored.
        ///
        /// Called when an item is handed out of the pool, which is the only
        /// moment its identity changes. Resetting on the way out rather than on
        /// the way in is the rule the enemy pool already follows: nobody reads a
        /// parked item, and everybody reads a live one.
        /// </summary>
        public static void Rebuild(EntityManager entityManager, ItemDatabase items, Entity item)
        {
            if (!entityManager.HasBuffer<GearSocket>(item))
                return;

            DynamicBuffer<GearSocket> sockets = entityManager.GetBuffer<GearSocket>(item);
            sockets.Clear();

            if (!entityManager.HasComponent<ItemInstance>(item))
                return;

            int index = items.IndexOf(entityManager.GetComponentData<ItemInstance>(item).ItemId);
            if (index < 0)
                return;

            ref ItemBlob blob = ref items.Value.Value.Items[index];

            for (int i = 0; i < blob.SocketCount && i < blob.LinkGroups.Length; i++)
            {
                sockets.Add(new GearSocket
                {
                    SocketIndex = i,
                    LinkGroup = blob.LinkGroups[i],
                    InsertedGem = Entity.Null
                });
            }
        }
    }
}
