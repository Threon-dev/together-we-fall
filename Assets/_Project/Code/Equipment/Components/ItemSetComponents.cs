using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Equipment
{
    // ─────────────────────────────────────────────────────────────────────
    // AUTHORITATIVE — what wearing several pieces of one family is worth.
    //
    // A set adds no new kind of reward. Its steps carry the stats an affix
    // carries, the support modifier a gem carries and the keystone a unique
    // carries, so every system downstream reads a set bonus with the code it
    // already had: the stat fold sums it, the cast fold folds it, the keystone
    // branch branches on it.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One step of a set, as the simulation sees it.
    ///
    /// The authored affix list is pre-folded into two blocks at bake time,
    /// exactly the split the stat maths needs — flat added to the pile,
    /// increased summed and applied once. A blob array of affixes would have
    /// been the same numbers in a shape every reader has to walk, plus a blob
    /// pointer inside a struct that is copied all over this file.
    ///
    /// Flat throughout, therefore, and safe to copy — unlike ItemBlob.
    /// </summary>
    public struct SetThresholdBlob
    {
        public int RequiredPieceCount;

        public StatBlock FlatBonuses;
        public StatBlock IncreasedBonuses;

        /// <summary>
        /// A support that applies to EVERY skill the wearer casts.
        ///
        /// The one thing about a set bonus that is not just "another source of
        /// the same numbers": an ordinary support acts on the link group it sits
        /// in, and this one has no socket to sit in at all.
        /// </summary>
        public SkillModifierBlob BonusSupport;

        /// <summary>
        /// Whether that support is real, for the reason ItemBlob needs the same
        /// flag: a default modifier is a valid "increased damage by zero", which
        /// folds to nothing but still takes a place in the group's list.
        /// </summary>
        public bool HasBonusSupport;

        public KeystoneEffect BonusKeystone;
    }

    /// <summary>
    /// One set: who belongs to it and what each step is worth.
    ///
    /// NEVER copy this by value — two BlobArrays inside, whose offsets are
    /// relative to their own address. Always `ref ItemSetBlob`.
    /// </summary>
    public struct ItemSetBlob
    {
        public FixedString64Bytes SetId;
        public FixedString64Bytes SetName;

        /// <summary>Members by stable item id, sorted so a lookup can stop early.</summary>
        public BlobArray<int> MemberItemIds;

        /// <summary>Steps, ascending by required piece count.</summary>
        public BlobArray<SetThresholdBlob> Thresholds;
    }

    /// <summary>Every set in the game.</summary>
    public struct ItemSetDatabaseBlob
    {
        public BlobArray<ItemSetBlob> Sets;
    }

    /// <summary>
    /// Handle to the set database, baked beside the item database — the two are
    /// one bake on purpose, so a set can never name an item the stat maths has
    /// never heard of.
    ///
    /// The lookup lives on the component rather than on the blob for the reason
    /// ItemDatabase gives: a method on the blob would take `this` by value, and
    /// copying a struct with a BlobArray in it quietly breaks it.
    /// </summary>
    public struct ItemSetDatabase : IComponentData
    {
        public BlobAssetReference<ItemSetDatabaseBlob> Value;

        public bool IsCreated => Value.IsCreated;

        public int SetCount => Value.IsCreated ? Value.Value.Sets.Length : 0;
    }

    /// <summary>
    /// How much of each set a character is currently wearing, and what that is
    /// worth right now.
    ///
    /// Derived, exactly like PlayerStats and KeystoneComponent, by a system on
    /// the same StatsDirty flag — so there is nothing here to invalidate, only
    /// something to rebuild. It is rebuilt whole rather than patched, the same
    /// choice StatusGate made and for the same reason: an accumulated count
    /// would be a second copy of what the equipment already says.
    ///
    /// The resolved bonus rides along rather than being looked up again by each
    /// reader. Three of them read it — the stat fold, the cast fold and the
    /// panel — and two of those would otherwise have to be handed a database
    /// they need for nothing else.
    /// </summary>
    public struct ActiveSetBonusStatus : IBufferElementData
    {
        /// <summary>The set, by authored id. What a save or a packet would name.</summary>
        public FixedString64Bytes SetId;

        /// <summary>Its index in the database, for everything that is not a save.</summary>
        public int SetIndex;

        public int CurrentEquippedCount;

        /// <summary>
        /// The required piece count of the step in force, or zero when the
        /// wearer has pieces but not yet enough of them.
        ///
        /// Highest reached REPLACES the lower ones rather than stacking with
        /// them: it is the ARPG rule players already know, it keeps one number
        /// per set to balance instead of a sum of them, and "4-piece bonus" is
        /// a sentence rather than a derivation.
        /// </summary>
        public int HighestActiveThreshold;

        public StatBlock FlatBonuses;
        public StatBlock IncreasedBonuses;

        public SkillModifierBlob BonusSupport;
        public bool HasBonusSupport;
        public KeystoneEffect BonusKeystone;

        public bool IsActive => HighestActiveThreshold > 0;
    }
}
