using Unity.Entities;

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
        public float Current;
    }
}
