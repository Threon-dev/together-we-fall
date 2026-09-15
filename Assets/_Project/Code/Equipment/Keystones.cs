using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using TogetherWeFall.Player;

namespace TogetherWeFall.Equipment
{
    // ─────────────────────────────────────────────────────────────────────
    // AUTHORITATIVE — the rules an item is allowed to break.
    //
    // A keystone is not a stat and not a support. It is an affix that turns one
    // sentence of the combat rules into a different sentence, for everything the
    // character casts, at once. Every system it touches answers it with a single
    // branch at the top of the work it was going to do anyway — none of them
    // grows a second code path for keystones in general, only for the one thing
    // that particular keystone changes.
    //
    // That is the whole reason this is worth having: the interesting builds come
    // from a keystone meeting supports and triggers that have never heard of it.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Which rule an item inverts.
    ///
    /// A closed enum rather than authored data, and for the same reason StatKind
    /// is closed: a keystone only exists because a system was changed to answer
    /// it, so a keystone nobody implemented would be a value that silently does
    /// nothing. Adding one is an enum value AND a branch, together, or not at
    /// all.
    /// </summary>
    public enum KeystoneEffect : byte
    {
        /// <summary>Ordinary gear. Almost everything.</summary>
        None = 0,

        /// <summary>
        /// Area skills stop covering ground and start jumping between bodies.
        ///
        /// Answered by SkillCastSystem rather than by the area stage, which is
        /// not where the sketch put it. The area queue also carries corpse
        /// explosions, zone pulses and reaction blasts, none of which is an
        /// "area skill" — turning those into chains would mean a corpse that
        /// detonates into a chain of lightning because of a ring. The cast is
        /// the only place that still knows the blast is somebody's skill.
        /// </summary>
        AoeToChain = 1,

        /// <summary>
        /// A burn does all of its damage at once instead of over its duration.
        ///
        /// Answered by StatusTickSystem. Strictly more front-loaded damage and
        /// strictly less of it lingering, which is a real trade: nothing keeps
        /// burning long enough to be reacted with, and a support conditional on
        /// a burning target beside this one finds nothing to be conditional on.
        ///
        /// One sharp edge, named rather than quietly tuned away: a burn that
        /// resolves and disappears can be applied again by the very next blow,
        /// so a fast fire skill pays a full duration of burn damage per hit
        /// instead of per burn. That is what the mechanism means, and the
        /// numbers to fix it are the status damage and the skill rate rather
        /// than anything here. Balance is deferred throughout this prototype;
        /// this is the loudest place it will show.
        /// </summary>
        StatusInstantResolve = 2,

        /// <summary>
        /// The resource trade-off, as the shape it will take.
        ///
        /// There is no mana in this game, so today this is the downside without
        /// the upside: cooldowns double and nothing is given back. It is here
        /// because the seam is the interesting part — a keystone that touches
        /// the numbers of the cast itself rather than what the cast produces —
        /// and because the day casting costs something, that is one line in this
        /// same branch.
        ///
        /// Worn by Bloodwrought Band, which is the one keystone ring carrying a
        /// stat — the increased damage beside it is what makes an effect that is
        /// currently all cost worth picking up at all. The affix comes off the
        /// day casting costs something.
        /// </summary>
        NoManaCostDoubleCooldown = 3,

        /// <summary>
        /// Every reaction spends the status that fed it, whatever its rule says.
        ///
        /// Answered by ElementReactionSystem. It turns the reaction table from
        /// "some of these keep paying out" into "each mark is worth exactly one
        /// combination", which is a different game out of the same assets.
        /// </summary>
        ReactionsAlwaysConsume = 4,

        /// <summary>
        /// Everything you slow is also made vulnerable.
        ///
        /// Answered by ElementReactionSystem, in the one method every status
        /// application already passes through. It is deliberately hung on Slow
        /// rather than on a hard control: slow is the one control with no
        /// diminishing returns, so it is the one an item can build around
        /// without quietly becoming "your stun-lock also does more damage".
        ///
        /// The interesting half is that it needs no new mechanism at all.
        /// Vulnerability is already a status the damage resolver reads, and a
        /// slow is already something a frost build applies without choosing to —
        /// so a build that never went near control finds itself with a
        /// party-wide damage multiplier it did not author, which is what a
        /// keystone is for.
        /// </summary>
        SlowsAlsoWeaken = 5
    }

    /// <summary>
    /// The keystone a character is currently under.
    ///
    /// Derived from equipment, exactly like PlayerStats, by exactly the same
    /// system on exactly the same dirty flag — because it IS a stat in every way
    /// that matters to the code: authored on items, gathered over what is worn,
    /// recomputed when that changes, and never written by anything else.
    ///
    /// One at a time. Two keystones that both rewrite what an area skill does
    /// have no correct answer and four of them have twenty-four, so the rule is
    /// the cheapest one that can be explained: the first slot in enum order
    /// wins, and the panel says so. Merging them is a design conversation, not a
    /// missing feature.
    /// </summary>
    public struct KeystoneComponent : IComponentData
    {
        [GhostField]
        public KeystoneEffect Effect;

        /// <summary>
        /// How many keystone items are worn beyond the one in force.
        ///
        /// Kept so the panel can say "two worn, the first applies" rather than
        /// quietly ignoring the second. Counting is free here — the stat pass
        /// walks every slot anyway — and finding out later would mean walking
        /// them all again from the UI.
        /// </summary>
        [GhostField]
        public int Ignored;
    }

    /// <summary>
    /// Which player is under which keystone, small enough to hand to a job.
    ///
    /// The awkwardness this exists for: a keystone belongs to a character
    /// entity, and the jobs that have to obey one — the status tick, the
    /// reaction pass — are walking ENEMIES and hold nothing but a player id.
    /// A ComponentLookup needs an entity, and there is no entity to be had.
    ///
    /// So the answer is the one the enemy jobs already use for player positions:
    /// a tiny flat list, built on the main thread, read by whoever needs it. It
    /// is derived every frame from the components rather than stored, so there
    /// is nothing here to invalidate — only something to rebuild, and rebuilding
    /// is walking at most four characters.
    /// </summary>
    public struct KeystoneSet
    {
        /// <summary>One player and the rule they are breaking.</summary>
        public struct Entry
        {
            public int PlayerId;
            public KeystoneEffect Effect;
        }

        /// <summary>
        /// Room for seven players in sixty-four bytes, which is more than a
        /// four-player co-op needs and small enough to be a job field.
        /// </summary>
        public FixedList64Bytes<Entry> Entries;

        public void Add(int playerId, KeystoneEffect effect)
        {
            // Only players actually under a keystone go in, so the list is
            // usually empty and the lookup below usually answers immediately.
            if (effect == KeystoneEffect.None || Entries.Length >= Entries.Capacity)
                return;

            Entries.Add(new Entry { PlayerId = playerId, Effect = effect });
        }

        /// <summary>The keystone this player is under, or None.</summary>
        public KeystoneEffect Of(int playerId)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].PlayerId == playerId)
                    return Entries[i].Effect;
            }

            return KeystoneEffect.None;
        }

        public bool Has(int playerId, KeystoneEffect effect) => Of(playerId) == effect;

        /// <summary>Whether anybody is under a keystone at all. The frame's early out.</summary>
        public bool IsEmpty => Entries.Length == 0;

        /// <summary>
        /// Reads the current set off the characters.
        ///
        /// A query rather than a singleton buffer somebody publishes into: the
        /// components ARE the answer, and a published copy would be a second one
        /// that has to be kept in step. Walking four entities costs less than
        /// the bug.
        /// </summary>
        public static KeystoneSet Gather(
            in NativeArray<PlayerCharacter> characters,
            in NativeArray<KeystoneComponent> keystones)
        {
            var set = new KeystoneSet();

            for (int i = 0; i < characters.Length && i < keystones.Length; i++)
                set.Add(characters[i].PlayerId, keystones[i].Effect);

            return set;
        }
    }
}
