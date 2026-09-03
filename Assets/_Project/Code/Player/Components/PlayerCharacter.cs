using Unity.Entities;

namespace TogetherWeFall.Player
{
    /// <summary>
    /// The player, as an entity.
    ///
    /// Up to now a player existed in ECS only as a position in a buffer, which
    /// was enough while the only question anyone asked was "who do I chase".
    /// Equipment asks a different question — "what is this player wearing" —
    /// and that needs something to hang state on.
    ///
    /// The two are deliberately not merged. PlayerPositionElement is a flat,
    /// cheap targeting index that enemy jobs read every frame; this is the
    /// character sheet. Merging them would put an inventory buffer in the hot
    /// path of every enemy on the floor.
    ///
    /// Nothing creates these by hand: PlayerCharacterRegistrySystem derives them
    /// from the position buffer, so a player joining in coop gets a character
    /// without anybody having to remember to make one.
    /// </summary>
    public struct PlayerCharacter : IComponentData
    {
        public int PlayerId;
    }
}
