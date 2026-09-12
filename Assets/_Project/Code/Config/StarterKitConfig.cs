using System;
using UnityEngine;

namespace TogetherWeFall.Config
{
    /// <summary>Where a kit entry ends up when the character is handed it.</summary>
    public enum StarterKitPlacement : byte
    {
        /// <summary>Laid out in the bag, wherever it fits. The default.</summary>
        Bag = 0,

        /// <summary>
        /// Worn, in the first free slot the item is allowed in.
        ///
        /// A per-entry choice rather than "the first entry is the gear", which
        /// is what this used to be: a test that needs four pieces of one set
        /// worn at once had no way to ask for it, and the answer cannot be
        /// derived from the item — a ring may be worn or kept, and both are
        /// things worth testing.
        /// </summary>
        Worn = 1
    }

    /// <summary>
    /// One line of the starting kit: what, where, and how many.
    /// </summary>
    [Serializable]
    public sealed class StarterKitEntry
    {
        [SerializeField] private ItemDefinition _item;

        [Tooltip("Worn goes into the first free slot the item is allowed in; " +
                 "Bag lays it out wherever it fits. An item that cannot be " +
                 "worn falls back to the bag and says so in the console.")]
        [SerializeField] private StarterKitPlacement _placement = StarterKitPlacement.Bag;

        [Tooltip("How many copies. Ten coins are one line with a count of ten, " +
                 "not ten lines — and every copy is a separate item out of the " +
                 "loot pool, so a large kit needs a pool to match.")]
        [SerializeField, Range(1, 99)] private int _count = 1;

        public ItemDefinition Item => _item;
        public StarterKitPlacement Placement => _placement;
        public int Count => Mathf.Clamp(_count, 1, 99);
    }

    /// <summary>
    /// What a new character is handed — the test loadout, as an asset.
    ///
    /// Its own asset rather than a field on the character sheet, which is where
    /// it lived: the sheet is balance that every scene shares, and this is a
    /// dial somebody turns twenty times an hour to look at a mechanic. Mixing
    /// them meant editing the numbers everybody plays with in order to try a
    /// gem, and it meant one kit for the whole project.
    ///
    /// The reference lives on CharacterStatsAuthoring, so a scene can point at
    /// its own kit: the arena is a test bench and the lobby is a game. Duplicate
    /// the asset, point a SubScene at the copy, and the two stop arguing.
    ///
    /// Order matters only for what is worn: entries are granted in order, so a
    /// two-handed weapon listed after a shield finds the off hand full.
    /// </summary>
    [CreateAssetMenu(
        fileName = "StarterKitConfig",
        menuName = "Together We Fall/Starter Kit Config")]
    public sealed class StarterKitConfig : ScriptableObject
    {
        [Tooltip("Everything a new character gets. Gems go in the bag — " +
                 "socketing them is the player's job, and a kit that did it " +
                 "would be choosing somebody's build.")]
        [SerializeField] private StarterKitEntry[] _entries = Array.Empty<StarterKitEntry>();

        public StarterKitEntry[] Entries => _entries ?? Array.Empty<StarterKitEntry>();
    }
}
