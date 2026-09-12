using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;

namespace TogetherWeFall.Lobby
{
    /// <summary>
    /// Money: a number on the character, and an item only while it is on the
    /// floor.
    ///
    /// It used to be an item all the way through — a coin came out of the item
    /// pool, took a cell, and the balance was counted by walking the bag. That
    /// was a real answer to "what is money" and it cost the thing the bag is
    /// for: a floor's worth of payout was forty entities and forty of sixty
    /// squares, so the reward for clearing a room was having nowhere to put the
    /// loot. Half of this file was the machinery for that and is gone.
    ///
    /// What survives is the half worth keeping: a coin is still an ordinary
    /// ItemDefinition with a CurrencyValue, it still drops from chests and lies
    /// on the floor. Picking it up is where it stops being an item — the pickup
    /// stage adds its value to the purse and hands the entity straight back to
    /// the pool. Nothing between the chest and the purse knows the difference.
    ///
    /// A struct of static methods rather than a system, exactly like GridFit and
    /// for the same reason: several owners need the same answers and none of
    /// them calls the others. The vendor spends, the forge spends, the pickup
    /// stage fills, and the UI reads.
    /// </summary>
    public struct Currency
    {
        /// <summary>
        /// What an item is worth before a vendor markup, by rarity.
        ///
        /// Derived from rarity rather than authored per item, because there is
        /// no balance in this prototype to author against and a price field on
        /// forty assets is forty numbers nobody chose. The day an item needs to
        /// be worth more than its rarity says, this becomes a field and this
        /// method becomes its fallback.
        /// </summary>
        public static int BasePrice(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common: return 2;
                case ItemRarity.Uncommon: return 5;
                case ItemRarity.Rare: return 12;
                case ItemRarity.Epic: return 25;
                case ItemRarity.Legendary: return 50;
                case ItemRarity.Mythic: return 100;
                default: return 2;
            }
        }

        /// <summary>How much this item is worth as money. Zero for everything else.</summary>
        public static int ValueOf(ItemDatabase items, int itemId)
        {
            int index = items.IndexOf(itemId);
            if (index < 0)
                return 0;

            // By reference: ItemBlob carries a BlobArray and must never be
            // copied.
            ref ItemBlob item = ref items.Value.Value.Items[index];
            return item.CurrencyValue;
        }

        /// <summary>
        /// What this character is carrying, in coin.
        ///
        /// A component read rather than a walk of the bag. A character with no
        /// wallet has no money — which is every character in a scene built
        /// before wallets existed, and the honest answer for one.
        /// </summary>
        public static int Balance(EntityManager entityManager, Entity character)
        {
            return character != Entity.Null && entityManager.HasComponent<Wallet>(character)
                ? entityManager.GetComponentData<Wallet>(character).Coin
                : 0;
        }

        /// <summary>
        /// Takes an amount out of the purse, or takes nothing and says no.
        ///
        /// The two-pass dance this used to need is gone with the coins: there is
        /// one number, so "can they afford it" and "take it" cannot come apart
        /// half way. What is left is the rule that mattered — a payment either
        /// happens or does not.
        /// </summary>
        public static bool TryPay(EntityManager entityManager, Entity character, int amount)
        {
            if (amount <= 0)
                return true;

            if (character == Entity.Null || !entityManager.HasComponent<Wallet>(character))
                return false;

            Wallet wallet = entityManager.GetComponentData<Wallet>(character);

            if (wallet.Coin < amount)
                return false;

            wallet.Coin -= amount;
            entityManager.SetComponentData(character, wallet);

            return true;
        }

        /// <summary>
        /// Pays an amount into the purse.
        ///
        /// It cannot fail and it cannot be refused, which is the whole of what
        /// changed: a payout used to need free entities in the item pool and
        /// free squares in the bag, and a sale could be turned down because the
        /// seller had nowhere to put the change. Selling a breastplate for four
        /// coins now needs nothing at all.
        /// </summary>
        public static void Grant(EntityManager entityManager, Entity character, int amount)
        {
            if (amount <= 0 ||
                character == Entity.Null || !entityManager.HasComponent<Wallet>(character))
            {
                return;
            }

            Wallet wallet = entityManager.GetComponentData<Wallet>(character);
            wallet.Coin += amount;

            entityManager.SetComponentData(character, wallet);
        }

        /// <summary>
        /// Takes a coin off the floor and puts its value in the purse, if that
        /// is what this item is.
        ///
        /// The one place an item turns into money, so the pickup stage and the
        /// starter kit cannot disagree about what happens to a coin. The entity
        /// goes back to the pool: it was money for exactly as long as it was
        /// lying on the ground.
        /// </summary>
        public static bool TryCollect(
            EntityManager entityManager, ItemDatabase items, Entity character, Entity item)
        {
            if (item == Entity.Null || !entityManager.HasComponent<ItemInstance>(item))
                return false;

            int value = ValueOf(
                items, entityManager.GetComponentData<ItemInstance>(item).ItemId);

            if (value <= 0)
                return false;

            Grant(entityManager, character, value);
            LootItemPool.Release(entityManager, item);

            return true;
        }

        public static FixedString64Bytes NameOf(ItemDatabase items, int itemId)
        {
            int index = items.IndexOf(itemId);
            if (index < 0)
                return default;

            ref ItemBlob item = ref items.Value.Value.Items[index];
            return item.Name;
        }

    }
}
