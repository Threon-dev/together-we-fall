using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TogetherWeFall.Config;
using TogetherWeFall.Loot;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// The content the lobby needs that nothing else creates: money, and what a
    /// trader has on the shelf.
    ///
    /// Its own file beside the other factories, and additive like all of them:
    /// assets are filled in when they are created and never overwritten. These
    /// are edited by hand, and a generator that silently reverts that editing is
    /// worse than no generator.
    /// </summary>
    public static class LobbyContentFactory
    {
        private const string ItemFolder = "Assets/_Project/Data/Items";

        /// <summary>
        /// How many coins a new character starts with.
        ///
        /// Enough to buy two or three common things, so the shop can be tried
        /// the first time somebody walks into it. It is not balance — there is
        /// none — it is the difference between a feature you can use and one you
        /// have to grind to reach.
        /// </summary>
        private const int StarterCoinCount = 10;

        /// <summary>
        /// Money, as an item.
        ///
        /// One denomination worth one, which is what makes paying exact: the
        /// payment rule skips a coin worth more than what is still owed, so
        /// mixed denominations would need change, and change needs a reason
        /// first.
        ///
        /// It goes into the loot table because that is what "exists" means for
        /// an item — the item database is baked FROM the tables, so a coin
        /// outside them would be a coin no system had ever heard of. The
        /// side effect is the good kind: money drops out of chests.
        /// </summary>
        public static ItemDefinition CreateOrLoadCoin(LootTable table)
        {
            ItemDefinition coin = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                "GoldSliver", ItemFolder, out bool created);

            if (created)
            {
                var serialized = new SerializedObject(coin);
                serialized.FindProperty("_displayName").stringValue = "Gold Sliver";
                serialized.FindProperty("_rarity").enumValueIndex = (int)ItemRarity.Common;
                serialized.FindProperty("_gridWidth").intValue = 1;
                serialized.FindProperty("_gridHeight").intValue = 1;
                serialized.FindProperty("_currencyValue").intValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            SceneBuildUtility.EnsureInLootTable(table, coin);
            return coin;
        }

        /// <summary>
        /// Puts a handful of coins in the starting kit, once.
        ///
        /// Additive and idempotent: a kit that already mentions the coin is left
        /// alone, so rebuilding the scene twice does not make anybody rich. It
        /// writes to the shared CharacterConfig, which means a character in the
        /// arena starts with coins too — harmless, since a coin is an item like
        /// any other and there is nobody there to spend it on.
        /// </summary>
        public static void GrantStarterCoins(CharacterConfig config, ItemDefinition coin)
        {
            if (config == null || coin == null)
                return;

            var serialized = new SerializedObject(config);
            SerializedProperty kit = serialized.FindProperty("_starterItems");

            for (int i = 0; i < kit.arraySize; i++)
            {
                if (kit.GetArrayElementAtIndex(i).objectReferenceValue == coin)
                    return;
            }

            int start = kit.arraySize;
            kit.arraySize = start + StarterCoinCount;

            for (int i = 0; i < StarterCoinCount; i++)
                kit.GetArrayElementAtIndex(start + i).objectReferenceValue = coin;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// What the trader has out.
        ///
        /// Named assets rather than a folder scan, for the same reason the
        /// default attacks are named: which items a shop sells is a content
        /// decision, and a scan would quietly put the next thing somebody drops
        /// in the folder on the shelf. Anything not on disk yet is skipped —
        /// the sample items are created on the first build, and this runs
        /// beside them.
        /// </summary>
        public static ItemDefinition[] LoadVendorStock()
        {
            string[] names =
            {
                // Weapons, so the shop can sell something that changes what you
                // cast. A bought weapon rolls its own built-in skill exactly as
                // a dropped one does, because it comes out of the same pool by
                // the same call.
                "CrackedDagger",
                "HuntersBow",
                "FrostbiteBlade",

                // Armour and jewellery, so there is something to fill the other
                // slots with.
                "DentedBuckler",
                "RustedHelm",
                "RunedGauntlets",
                "BandofEmbers",
                "CoiloftheDeep",

                // Gems, which is the part a shop is actually for: a support you
                // did not find is the one your build wanted.
                "GemChain",
                "GemFork",
                "GemMulticast",
                "GemHoarfrost"
            };

            var stock = new List<ItemDefinition>(names.Length);

            for (int i = 0; i < names.Length; i++)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    $"{ItemFolder}/{names[i]}.asset");

                if (item != null)
                    stock.Add(item);
            }

            return stock.ToArray();
        }
    }
}
