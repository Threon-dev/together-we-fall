using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;
using Random = Unity.Mathematics.Random;

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
        /// Six, because that is what a weapon's skill is authored with: a group
        /// is one welded skill and six holes to change it with. It was five
        /// while a group was one active plus what was left of a six-socket
        /// weapon, and the number moved when the layout did — it has never been
        /// a balance figure, only a count of what fits.
        ///
        /// The list it goes into is a FixedList512Bytes rather than the 128 it
        /// once was, and that is arithmetic rather than generosity: a support
        /// grew from twenty bytes to thirty-two when conditions and triggers
        /// arrived, and 124 bytes of payload holds three of those. Six fits in
        /// 512 with room to spare; three would have meant supports silently
        /// vanishing out of a fully socketed weapon.
        /// </summary>
        public const int MaxSupportsPerGroup = 6;

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

            // The weapon's own attack. Answered before the gem is looked at
            // because a welded socket never holds one — there is no entity, only
            // the id the forge put there — and from here down nothing else in
            // the pipeline can tell the two apart.
            if (socket.IsWelded)
            {
                skillIndex = skills.IndexOf(socket.WeldedSkillId);
                linkGroup = socket.LinkGroup;

                return skillIndex >= 0;
            }

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
        public static FixedList512Bytes<SkillModifierBlob> GatherSupports(
            EntityManager entityManager, ItemDatabase items, Entity gear, int linkGroup)
        {
            var supports = new FixedList512Bytes<SkillModifierBlob>();

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
        /// The trigger support in a gathered group, if there is one.
        ///
        /// Asked by three callers who must agree: the cast system, to refuse the
        /// key; the evaluation system, to know what fires and how often; and the
        /// panel, to say that the key is automatic now. A trigger gem the cast
        /// system refuses but the evaluator never fires would be a hotkey that
        /// simply stopped working.
        ///
        /// The first one wins when two are socketed together. Firing both would
        /// mean one condition quietly doubling the other's cooldown, which is
        /// not what anybody means by socketing two.
        /// </summary>
        public static bool TryGetTrigger(
            in FixedList512Bytes<SkillModifierBlob> supports, out SkillModifierBlob trigger)
        {
            for (int i = 0; i < supports.Length; i++)
            {
                if (!SkillModifiers.IsAutomatic(supports[i].Kind))
                    continue;

                trigger = supports[i];
                return true;
            }

            trigger = default;
            return false;
        }

        /// <summary>
        /// Points the hotkeys at the skills a worn weapon came with.
        ///
        /// Without this, equipping a weapon changes nothing a player can see:
        /// the skills exist, in sockets, and no key casts them. Binding is a
        /// CHOICE rather than a fact, which is why giving it a default breaks
        /// nothing — the bar still names a socket and the skill still comes from
        /// one. What would break the model is a bar carrying a skill of its own,
        /// and this does not.
        ///
        /// Which key a weapon answers is decided by the HAND it is in: the main
        /// hand speaks with the first key, the off hand with the second. So two
        /// one-handed weapons are two skills on two buttons, and a two-handed
        /// weapon — which has two welded skills because it takes both hands —
        /// fills both itself. The key is a property of where the weapon is worn
        /// rather than of the order things were picked up, which is the whole
        /// reason it is predictable: a player who swaps hands swaps buttons.
        ///
        /// It never takes a key away. A key is free if it is unbound, or if it
        /// points at gear the character is no longer wearing — which is exactly
        /// what a weapon swap leaves behind, and the alternative there is a
        /// hotkey still casting the sword now sitting in the bag. A skill whose
        /// key is taken simply goes unbound rather than stealing the next one:
        /// its neighbour's key belongs to its neighbour.
        ///
        /// Called from the two places gear is put on: the equip system, and the
        /// starter kit, which dresses a new character without sending itself a
        /// request. Both on the frame it happens and never every frame — a
        /// version that ran continuously would put the binding back a frame
        /// after a player deliberately cleared it.
        /// </summary>
        public static void ArmDefaultAttack(
            EntityManager entityManager,
            ItemDatabase items,
            SkillDatabase skills,
            DynamicBuffer<EquippedItem> worn,
            DynamicBuffer<SkillSlot> bar)
        {
            for (int i = 0; i < worn.Length; i++)
            {
                Entity gear = worn[i].Item;

                if (gear == Entity.Null || !entityManager.HasBuffer<GearSocket>(gear))
                    continue;

                int key = FirstKeyOf(worn[i].Slot);

                // Nine of the ten slots are not hands, and a ring with a welded
                // skill is not a thing that exists.
                if (key < 0)
                    continue;

                DynamicBuffer<GearSocket> sockets =
                    entityManager.GetBuffer<GearSocket>(gear, true);

                for (int socket = 0; socket < sockets.Length; socket++)
                {
                    if (!sockets[socket].IsWelded)
                        continue;

                    // Resolved rather than assumed: a welded id naming a skill
                    // the database has never heard of would bind a key to
                    // nothing, and a key that does nothing is the bug this
                    // exists to remove.
                    if (!TryResolveActive(
                            entityManager, items, skills, gear, socket, out _, out _))
                    {
                        continue;
                    }

                    if (key < bar.Length && IsBarSlotFree(entityManager, worn, bar[key]))
                    {
                        SkillSlot bound = bar[key];
                        bound.Gear = gear;
                        bound.SocketIndex = socket;
                        bar[key] = bound;
                    }

                    // Advanced whether or not it was bound, so the second skill
                    // of a two-handed weapon still aims at the second key when
                    // the first one is already spoken for.
                    key++;
                }
            }
        }

        /// <summary>
        /// The key a hand speaks with, or -1 for a slot that is not a hand.
        /// </summary>
        private static int FirstKeyOf(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.MainHand:
                    return 0;

                case EquipmentSlot.OffHand:
                    return 1;

                default:
                    return -1;
            }
        }

        /// <summary>
        /// Whether a hotkey is the game's to fill: empty, or pointing at
        /// something the character is not wearing any more.
        /// </summary>
        private static bool IsBarSlotFree(
            EntityManager entityManager, DynamicBuffer<EquippedItem> worn, in SkillSlot slot)
            => !slot.HasBinding || !entityManager.Exists(slot.Gear) || !IsWorn(worn, slot.Gear);

        /// <summary>
        /// Whether this gear is actually on the character.
        ///
        /// Public and here rather than a loop in each caller, because three of
        /// them need the same answer and must agree: the cast system, which
        /// refuses a hotkey pointing at a sword in the bag; the arming above,
        /// which treats such a key as free; and the panel, which must not name a
        /// skill the host will not cast.
        /// </summary>
        public static bool IsWorn(DynamicBuffer<EquippedItem> worn, Entity gear)
        {
            if (gear == Entity.Null)
                return false;

            for (int i = 0; i < worn.Length; i++)
            {
                if (worn[i].Item == gear)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Rebuilds an item's sockets from the layout its definition authored,
        /// and rolls the skills it comes with.
        ///
        /// Called when an item is handed out of the pool, which is the only
        /// moment its identity changes — and therefore the only moment a roll
        /// may happen. Resetting on the way out rather than on the way in is the
        /// rule the enemy pool already follows: nobody reads a parked item, and
        /// everybody reads a live one.
        ///
        /// The roll needs nowhere to live. A welded id is already per-instance
        /// state sitting in this item's own socket buffer, so "which skills did
        /// this staff come with" is answered by the same read that answers "what
        /// is in this hole" — no second component, and nothing to keep in step.
        ///
        /// The random comes from the caller rather than from here, and it is the
        /// same LootRandom that rolls rarities: one source the host owns, so the
        /// day items are rolled on a server and described to clients there is
        /// one thing to send rather than two.
        /// </summary>
        public static void Rebuild(
            EntityManager entityManager, ItemDatabase items, Entity item, ref Random random)
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

            Weld(sockets, ref blob, ref random);
        }

        /// <summary>
        /// Rolls the weapon's own skills and puts one at the head of each link
        /// group.
        ///
        /// They take authored sockets rather than being added beside them: a
        /// built-in attack that cost nothing would be strictly better than one
        /// the player chose, and the whole model rests on holes being the scarce
        /// thing. A weapon with no authored sockets at all still gets one, so
        /// that "this weapon has an attack" never depends on a layout somebody
        /// forgot to write.
        ///
        /// The FIRST socket of each group, in the order the groups appear, and
        /// that is what makes a two-handed weapon two skills rather than one
        /// skill with twelve supports: a group is a skill and the holes that
        /// change it, so the head of a group is where a skill lives. It is also
        /// why nothing downstream had to learn anything — the second skill is
        /// found by the same walk that already finds the first.
        ///
        /// Distinct, because a staff that rolled the same skill into both groups
        /// would be a staff with one skill and a wasted half.
        /// </summary>
        /// <summary>
        /// Rolls this weapon built-in attacks again, leaving everything else
        /// exactly where it is.
        ///
        /// Deliberately NOT a call to Rebuild. Rebuild clears the socket buffer
        /// and lays it out from the definition again, which is right when an
        /// item changes identity coming out of the pool and catastrophic on an
        /// item somebody owns: every gem in it would be forgotten by the socket
        /// that held it while still counting as stored, and the pool would leak
        /// one item per gem. So this writes only the field it is about.
        ///
        /// Returns false when there is nothing welded in, which is every item
        /// that is not a weapon. That is a refusal, not a silent no-op: a forge
        /// that charged for rerolling a helmet attack would be charging for
        /// nothing.
        /// </summary>
        public static bool RerollWelded(
            EntityManager entityManager, ItemDatabase items, Entity item, ref Random random)
        {
            if (!entityManager.HasBuffer<GearSocket>(item) ||
                !entityManager.HasComponent<ItemInstance>(item))
            {
                return false;
            }

            int index = items.IndexOf(entityManager.GetComponentData<ItemInstance>(item).ItemId);
            if (index < 0)
                return false;

            ref ItemBlob blob = ref items.Value.Value.Items[index];

            if (blob.InnateSkillIds.Length == 0)
                return false;

            DynamicBuffer<GearSocket> sockets = entityManager.GetBuffer<GearSocket>(item);

            // A copy on purpose, same as in Weld: candidates are drawn out of it
            // as they are picked, and the blob behind it is shared by every item
            // of this kind.
            FixedList64Bytes<int> candidates = blob.InnateSkillIds;

            bool rerolled = false;

            for (int i = 0; i < sockets.Length && candidates.Length > 0; i++)
            {
                if (!sockets[i].IsWelded)
                    continue;

                int pick = random.NextInt(0, candidates.Length);

                GearSocket socket = sockets[i];
                socket.WeldedSkillId = candidates[pick];
                sockets[i] = socket;

                // Drawn out, so a two-handed weapon cannot roll the same attack
                // into both of its groups — the same rule Weld follows, for the
                // same reason.
                candidates.RemoveAtSwapBack(pick);
                rerolled = true;
            }

            return rerolled;
        }

        private static void Weld(
            DynamicBuffer<GearSocket> sockets, ref ItemBlob blob, ref Random random)
        {
            int count = blob.ActiveSkillCount;

            if (count <= 0 || blob.InnateSkillIds.Length == 0)
                return;

            // A copy on purpose: candidates are drawn out of it as they are
            // picked, and the blob behind it is shared by every item of this
            // kind. It is a FixedList of ints, so the copy is safe — the rule
            // about never copying a blob struct is about the arrays inside one.
            FixedList64Bytes<int> candidates = blob.InnateSkillIds;

            if (sockets.Length == 0)
                sockets.Add(new GearSocket { SocketIndex = 0, LinkGroup = 0 });

            var filled = new FixedList32Bytes<int>();

            for (int i = 0; i < sockets.Length; i++)
            {
                if (filled.Length >= count || candidates.Length == 0)
                    break;

                int group = sockets[i].LinkGroup;

                if (HasGroup(filled, group))
                    continue;

                filled.Add(group);

                int pick = random.NextInt(0, candidates.Length);

                GearSocket socket = sockets[i];
                socket.WeldedSkillId = candidates[pick];
                sockets[i] = socket;

                candidates.RemoveAtSwapBack(pick);
            }
        }

        /// <summary>Whether a group has already been given its skill.</summary>
        private static bool HasGroup(in FixedList32Bytes<int> groups, int group)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] == group)
                    return true;
            }

            return false;
        }
    }
}
