using Unity.Entities;
using Unity.NetCode;

namespace TogetherWeFall.Player
{
    // ─────────────────────────────────────────────────────────────────────
    // AUTHORITATIVE — what a character has left, decided on the host.
    //
    // Two pools and only one component, because health already had one. Enemies
    // have carried Combat.Health since the first wave, the damage resolver is
    // the single writer of it, and a second "PlayerHealth" beside that would be
    // a second answer to "how much life does this thing have" — with a second
    // place to apply vulnerability, a second clamp, and a second bug.
    //
    // So the player wears the same Health as everything else, and only mana is
    // new. Its maximum and its regeneration are not here either: they are stats,
    // read off the same fold that answers for damage and armour, so an item that
    // raises the pool is an ordinary affix rather than a feature.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// How much mana a character has left.
    ///
    /// Current alone. The maximum lives in StatKind.MaxMana and the refill rate
    /// in StatKind.ManaRegen, for the reason above — a copy of the maximum here
    /// would be a number somebody has to remember to update the moment a ring
    /// comes off, and the symptom would be a bar that is briefly more than full.
    ///
    /// Deliberately not merged into Health. They are clamped by different stats,
    /// one regenerates and the other does not, and only one of them is spent by
    /// pressing a button — a shared struct would be a pair of fields that never
    /// move together.
    /// </summary>
    public struct Mana : IComponentData
    {
        [GhostField]
        public float Current;
    }

    /// <summary>
    /// How much money a character is carrying.
    ///
    /// A number, and that is the change: money used to be an item. A coin was an
    /// ordinary ItemDefinition that came out of the item pool, occupied a cell,
    /// and was counted by walking the bag — which was a real answer to "what is
    /// money" and the wrong one to live with. Forty coins meant forty pool
    /// entities and forty squares of a sixty-square bag, so the reward for
    /// clearing a floor was a bag with no room for the loot.
    ///
    /// What is kept from the old model is the part worth keeping: a coin still
    /// exists as an item OUT IN THE WORLD. It drops from a chest, lies on the
    /// floor, and is walked over — and picking it up is the moment it stops
    /// being an item and becomes this number. Nothing between the chest and the
    /// purse had to change.
    ///
    /// Beside Mana rather than in the lobby, because it is the same kind of
    /// thing: a pool on the character that one system spends and another fills.
    /// The lobby is only where it is currently spent.
    /// </summary>
    public struct Wallet : IComponentData
    {
        [GhostField]
        public int Coin;
    }
}
