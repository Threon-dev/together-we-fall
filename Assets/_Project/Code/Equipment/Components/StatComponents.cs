using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace TogetherWeFall.Equipment
{
    // ─────────────────────────────────────────────────────────────────────
    // AUTHORITATIVE — the numbers a host fights with.
    //
    // Stats are derived, never authored on a character: base values plus what
    // the equipment adds. Nothing writes PlayerStats except the one system that
    // recomputes it, which is what makes "the client showed me 240 damage" a
    // display bug rather than a balance exploit.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every stat in the game.
    ///
    /// A closed enum rather than authored stat assets: the set of stats is
    /// something the combat code has to know about at compile time anyway, and
    /// making it data would buy flexibility nobody can use — a stat no system
    /// reads does nothing.
    ///
    /// The order is the storage order in StatBlock, so values may be appended
    /// but never reshuffled.
    /// </summary>
    public enum StatKind : byte
    {
        Damage = 0,
        AttackSpeed = 1,
        Armour = 2,
        MaxHealth = 3,
        MoveSpeed = 4,
        FireResistance = 5,
        ColdResistance = 6,
        LightningResistance = 7,

        /// <summary>
        /// The mana pool, as a stat rather than a number of its own.
        ///
        /// Appended here instead of given its own component for the reason the
        /// enum's header already gives: the fold that turns base plus equipment
        /// into a final number exists and works, and a second pool with its own
        /// maths would be a second place "+20% maximum mana" has to be taught
        /// about. A ring that raises it is an ordinary affix.
        /// </summary>
        MaxMana = 8,

        /// <summary>Mana returned per second. Flat, so a regen affix is a flat affix.</summary>
        ManaRegen = 9,

        /// <summary>
        /// How often a cast lands as a critical blow, in percent.
        ///
        /// A stat rather than a field on the skill, because it is the thing
        /// every skill shares and the thing equipment is supposed to change. A
        /// character sheet that never heard of it answers zero, which is a
        /// character who never crits — the safe default, and the one every
        /// enemy is on.
        /// </summary>
        CritChance = 10,

        /// <summary>
        /// What a critical blow multiplies the damage by. 1.5 is half again.
        ///
        /// Kept separate from the chance because they are two different
        /// purchases: a build that crits often and one that crits enormously
        /// want different items, and a single "crit" number would make that one
        /// decision instead of two.
        /// </summary>
        CritMultiplier = 11
    }

    /// <summary>
    /// How a modifier applies, in the PoE sense.
    ///
    /// Flat is added to the base; Increased is a percentage that stacks
    /// ADDITIVELY with every other increase and is applied once, to the sum.
    /// Two 50% increases give 2x, not 2.25x — which is the whole reason the
    /// distinction is modelled instead of just multiplying things together.
    /// </summary>
    public enum ModifierKind : byte
    {
        Flat = 0,
        Increased = 1
    }

    /// <summary>
    /// One value per stat.
    ///
    /// A fixed list indexed by StatKind rather than named fields: the UI wants
    /// to walk every stat, the stat maths wants to walk every stat, and adding a
    /// ninth stat should not mean editing four switch statements that each
    /// silently keep compiling when one case is forgotten.
    ///
    /// FixedList64Bytes holds fifteen floats, so there is room to grow without
    /// changing the size of anything that stores this.
    /// </summary>
    public struct StatBlock
    {
        public const int StatCount = (int)StatKind.CritMultiplier + 1;

        public FixedList64Bytes<float> Values;

        /// <summary>
        /// A block of zeroes at full length. Always build through this rather
        /// than default(StatBlock): a default block has length zero, and reading
        /// a stat off it would answer zero for a while and then start throwing
        /// the moment something writes to it.
        /// </summary>
        public static StatBlock Zero()
        {
            var block = new StatBlock();
            block.Values.Length = StatCount;

            for (int i = 0; i < StatCount; i++)
                block.Values[i] = 0f;

            return block;
        }

        public float Get(StatKind stat)
        {
            int index = (int)stat;
            return index < Values.Length ? Values[index] : 0f;
        }

        public void Set(StatKind stat, float value)
        {
            int index = (int)stat;
            if (index < Values.Length)
                Values[index] = value;
        }

        public void Add(StatKind stat, float value)
        {
            int index = (int)stat;
            if (index < Values.Length)
                Values[index] += value;
        }
    }

    /// <summary>
    /// What a character is worth before any equipment. Baked from CharacterConfig.
    ///
    /// A singleton rather than a copy per player: in this game every player
    /// starts from the same sheet, and the difference between two characters is
    /// entirely what they are wearing.
    /// </summary>
    public struct CharacterBaseStats : IComponentData
    {
        public StatBlock Value;
    }

    /// <summary>
    /// The cached result of the stat maths. Read by everything, written by
    /// PlayerStatsSystem alone.
    /// </summary>
    public struct PlayerStats : IComponentData
    {
        [GhostField]
        public StatBlock Final;

        /// <summary>
        /// Bumped on every recompute. The UI compares it instead of diffing the
        /// numbers, so a panel rebuild happens when something actually changed.
        /// </summary>
        [GhostField]
        public int Version;
    }

    /// <summary>
    /// "This character's stats no longer match its equipment."
    ///
    /// The whole point of the caching requirement: stats are recomputed when
    /// something changes them, not on a frame boundary. Enableable so raising it
    /// costs no structural change — the same reason NeedsRepath is enableable.
    /// </summary>
    public struct StatsDirty : IComponentData, IEnableableComponent
    {
    }
}
