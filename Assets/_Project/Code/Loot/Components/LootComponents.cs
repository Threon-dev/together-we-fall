using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace TogetherWeFall.Loot
{
    // ─────────────────────────────────────────────────────────────────────
    // AUTHORITATIVE — decided by the host and never re-decided by a client.
    //
    // Loot is the sharpest case of the request/result split in the project. A
    // client may ask to open a chest; what falls out of it is rolled here and
    // travels back as a result. A client that rolled its own loot would be a
    // client that could roll again until it liked the answer.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Item quality, Diablo/PoE style. The order is the order of the weights in
    /// a loot table, so it must not be reshuffled — Mythic staying last is what
    /// lets a table say "and 0.2% of the time, this".
    /// </summary>
    public enum ItemRarity : byte
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4,
        Mythic = 5
    }

    /// <summary>
    /// Whether an item survives its owner dying.
    ///
    /// Fixed by the project conventions ahead of the systems that will enforce
    /// it: everything carried into a run is AtRisk, whether it was found here or
    /// brought from the lobby, and only what sits in the stash is Safe. Items on
    /// the dungeon floor are born AtRisk, so picking one up changes nothing
    /// about its risk — which is the point of the rule.
    /// </summary>
    public enum ItemRiskState : byte
    {
        Safe = 0,
        AtRisk = 1
    }

    /// <summary>How far along opening a chest is.</summary>
    public enum ChestPhase : byte
    {
        Closed = 0,
        Opening = 1,
        Opened = 2
    }

    /// <summary>Chest marker. Needed for queries and the debug readout.</summary>
    public struct ChestTag : IComponentData
    {
    }

    /// <summary>
    /// A chest, mid-open.
    ///
    /// OpenedByPlayerId is written once and never cleared: "who got here first"
    /// is the answer to a race, and a race resolved twice is not resolved. The
    /// opening delay is not decoration either — it is the window in which a
    /// second player can see they lost the race.
    /// </summary>
    public struct ChestState : IComponentData
    {
        public ChestPhase Phase;
        public float OpenTimer;
        public float OpenDuration;

        /// <summary>-1 until somebody claims it.</summary>
        public int OpenedByPlayerId;
    }

    /// <summary>Which table this chest rolls on. An index into LootDatabase.</summary>
    public struct LootTableId : IComponentData
    {
        public int Value;
    }

    /// <summary>
    /// "This chest still owes its drops."
    ///
    /// Enableable rather than a bool inside ChestState so the rolling system can
    /// query only the chests that owe something, instead of walking every chest
    /// on the floor to find the one that just finished opening.
    /// </summary>
    public struct LootRollRequest : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// An item lying in the world.
    ///
    /// Items drop as entities rather than straight into an inventory on purpose.
    /// In coop that is the difference between "the game decided who got it" and
    /// "we both saw it fall and one of us walked over" — the second needs no
    /// arbitration rules at all, because the world already arbitrates.
    /// </summary>
    public struct ItemInstance : IComponentData
    {
        [GhostField]
        public int ItemId;
        [GhostField]
        public ItemRarity Rarity;
        [GhostField]
        public ItemRiskState RiskState;
    }

    /// <summary>
    /// PRESENTATION — what the item is called.
    ///
    /// Split off from ItemInstance because a name is something a client draws,
    /// never something a system decides with. The id is the identity; this is
    /// the label on it.
    /// </summary>
    public struct ItemDisplayName : IComponentData
    {
        [GhostField]
        public FixedString64Bytes Value;
    }

    /// <summary>
    /// Prefabs the loot systems instantiate. Baked, not loaded: entity prefabs
    /// come from a Baker, and Addressables is for the SubScene around them.
    /// </summary>
    public struct LootPrefabs : IComponentData
    {
        public Entity Chest;
        public Entity Item;
    }

    /// <summary>The baked loot tables.</summary>
    public struct LootDatabase : IComponentData
    {
        public BlobAssetReference<LootDatabaseBlob> Value;
    }

    /// <summary>Placement and drop tuning, mirrored from LootConfig.</summary>
    public struct LootSettings : IComponentData
    {
        public int ChestsPerTreasureRoom;
        public int TreasureChestTableId;
        public float DropScatterRadius;

        /// <summary>
        /// Items created once and reused. Also the ceiling on how many can lie
        /// on the floor, in the same way the enemy pool is the living cap.
        /// </summary>
        public int ItemPoolSize;
    }

    /// <summary>
    /// The host's loot dice.
    ///
    /// A component rather than a fresh Random per roll so the sequence is one
    /// reproducible stream. Re-seeded from the run seed when a floor is built,
    /// which means a strange drop can be reproduced from a single number.
    /// </summary>
    public struct LootRandom : IComponentData
    {
        public Random Value;
    }

    /// <summary>Colour per rarity, indexed by ItemRarity.</summary>
    [InternalBufferCapacity(6)]
    public struct RarityColor : IBufferElementData
    {
        public float4 Value;
    }
}
