using System.Text;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;
using TogetherWeFall.Skills;

namespace TogetherWeFall.UI
{
    /// <summary>
    /// What an item IS, in words, for the panel to draw beside the cursor.
    ///
    /// A static describer rather than part of InventoryUI, the same shape as
    /// GridFit and EquipmentSlots and for the same reason: the bag, the
    /// equipment slots, the sockets and the hotkeys all ask "what is this
    /// thing", and four answers would eventually be four different answers. It
    /// decides nothing and reads nothing but the two databases — hand it an id
    /// and it hands back text.
    ///
    /// Every number here is read off the SAME blob the cast folds, so the panel
    /// cannot advertise a skill the host does not have. The numbers shown for a
    /// gem are the authored ones, before supports and before the character's
    /// stats: what the gem is, not what this build makes of it. Showing the
    /// folded ones would mean folding a cast that is not happening, against a
    /// link group the gem may not even be in yet.
    /// </summary>
    public static class ItemTooltip
    {
        /// <summary>
        /// One tooltip, split into the parts the panel styles differently.
        /// Empty parts are simply not drawn.
        /// </summary>
        public struct Text
        {
            /// <summary>The item's name, drawn in its rarity colour.</summary>
            public string Title;

            public ItemRarity Rarity;

            /// <summary>What sort of thing it is: a slot, a kind of gem, money.</summary>
            public string Kind;

            /// <summary>The numbers, one per line.</summary>
            public string Stats;

            /// <summary>
            /// What changes if this replaces what is already worn, one stat per
            /// line. Empty when there is nothing to compare against.
            /// </summary>
            public string Compare;

            /// <summary>What it does, in sentences.</summary>
            public string Detail;

            /// <summary>How to use it. The line that answers "and now what".</summary>
            public string Hint;

            public bool IsEmpty => string.IsNullOrEmpty(Title);
        }

        /// <summary>
        /// Describes an item by id — gem, gear or coin alike.
        ///
        /// The worn id is what the player would be giving up to wear this, or
        /// zero for nothing. The panel picks it, because only the panel knows
        /// whose character is being looked at; what the swap is WORTH is
        /// answered here, so the bag and the vendor cannot answer it
        /// differently.
        /// </summary>
        public static bool TryDescribeItem(
            ItemDatabase items, SkillDatabase skills, int itemId, int wornItemId, out Text text)
        {
            text = default;

            int index = items.IndexOf(itemId);
            if (index < 0)
                return false;

            // By reference throughout: ItemBlob carries a BlobArray, and copying
            // the struct leaves the affixes pointing at nothing.
            ref ItemBlob item = ref items.Value.Value.Items[index];

            text.Title = item.Name.ToString();
            text.Rarity = item.Rarity;

            if (item.GemKind == GemKind.Active)
                DescribeActiveGem(ref item, skills, ref text);
            else if (item.GemKind == GemKind.Support)
                DescribeSupportGem(ref item, skills, ref text);
            else if (item.CurrencyValue > 0)
                DescribeCurrency(ref item, ref text);
            else
                DescribeGear(ref item, skills, ref text);

            text.Compare = CompareToWorn(items, ref item, wornItemId);

            return true;
        }

        /// <summary>
        /// What the player would gain and lose by wearing this instead of what
        /// they have on.
        ///
        /// Only for gear: a gem is authored with a slot like everything else, so
        /// without this guard hovering a support gem would helpfully explain
        /// what it is worth compared to a helmet.
        ///
        /// The stats are summed the way the sheet sums them — flat and increased
        /// kept apart, because they do not add up with each other — rather than
        /// resolved into final numbers. Resolving would need the whole character,
        /// and would answer a question nobody asked: what matters here is what
        /// the two ITEMS differ by.
        /// </summary>
        private static string CompareToWorn(ItemDatabase items, ref ItemBlob item, int wornItemId)
        {
            if (item.GemKind != GemKind.None || item.CurrencyValue > 0 || wornItemId == 0)
                return string.Empty;

            int wornIndex = items.IndexOf(wornItemId);
            if (wornIndex < 0)
                return string.Empty;

            ref ItemBlob worn = ref items.Value.Value.Items[wornIndex];

            StatBlock flat = StatBlock.Zero();
            StatBlock increased = StatBlock.Zero();

            Gather(ref item, ref flat, ref increased, 1f);
            Gather(ref worn, ref flat, ref increased, -1f);

            var lines = new StringBuilder();
            lines.Append($"Instead of {worn.Name}:");

            int before = lines.Length;

            for (int s = 0; s < StatBlock.StatCount; s++)
            {
                var stat = (StatKind)s;

                if (Meaningful(flat.Get(stat)))
                    Line(lines, $"{Signed(flat.Get(stat))} {stat}");

                if (Meaningful(increased.Get(stat)))
                    Line(lines, $"{Signed(increased.Get(stat))}% increased {stat}");
            }

            if (item.SocketCount != worn.SocketCount)
                Line(lines, $"{Signed(item.SocketCount - worn.SocketCount)} sockets");

            // Named rather than summed: a keystone is a rule, and losing one is
            // the sort of thing a player finds out three fights later.
            if (worn.Keystone != KeystoneEffect.None && item.Keystone != worn.Keystone)
                Line(lines, $"loses {worn.Keystone}");

            if (lines.Length == before)
                Line(lines, "the same numbers");

            return lines.ToString();
        }

        /// <summary>
        /// Adds one item's stats into the running difference, or subtracts them.
        ///
        /// Flat and increased stay in separate blocks because they are not the
        /// same kind of number: the sheet adds every increase together and
        /// applies it once, so folding "+4 Armour" and "+10% Armour" into one
        /// total here would print a figure no system anywhere agrees with.
        /// </summary>
        private static void Gather(
            ref ItemBlob item, ref StatBlock flat, ref StatBlock increased, float sign)
        {
            for (int s = 0; s < StatBlock.StatCount; s++)
            {
                var stat = (StatKind)s;
                flat.Add(stat, item.BaseStats.Get(stat) * sign);
            }

            for (int a = 0; a < item.Affixes.Length; a++)
            {
                AffixBlob affix = item.Affixes[a];

                if (affix.Kind == ModifierKind.Increased)
                    increased.Add(affix.Stat, affix.Value * sign);
                else
                    flat.Add(affix.Stat, affix.Value * sign);
            }
        }

        /// <summary>
        /// Describes a skill that sits in a socket with no item to hold it: a
        /// weapon's welded attack, and whatever a hotkey currently points at.
        /// </summary>
        public static bool TryDescribeSkill(SkillDatabase skills, int skillIndex, out Text text)
        {
            text = default;

            if (!skills.IsValidIndex(skillIndex))
                return false;

            ref SkillBlob skill = ref skills.Value.Value.Skills[skillIndex];

            text.Title = skill.Name.ToString();
            text.Rarity = ItemRarity.Common;
            text.Kind = "Skill";
            text.Stats = SkillStats(ref skill);
            text.Detail = SkillDetail(ref skill, skills);

            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // Gems
        // ─────────────────────────────────────────────────────────────────

        private static void DescribeActiveGem(
            ref ItemBlob item, SkillDatabase skills, ref Text text)
        {
            text.Kind = "Active gem";
            text.Hint = "Put it in a socket, then drag that socket onto a hotkey.";

            int skillIndex = skills.IndexOf(item.GemSkillId);

            if (!skills.IsValidIndex(skillIndex))
            {
                text.Detail = "Casts a skill this build has never heard of.";
                return;
            }

            ref SkillBlob skill = ref skills.Value.Value.Skills[skillIndex];

            text.Kind = $"Active gem · casts {skill.Name}";
            text.Stats = SkillStats(ref skill);
            text.Detail = SkillDetail(ref skill, skills);
        }

        private static void DescribeSupportGem(
            ref ItemBlob item, SkillDatabase skills, ref Text text)
        {
            SkillModifierPhase phase = SkillModifiers.PhaseOf(item.GemSupport.Kind);

            text.Kind = $"Support gem · {SkillModifiers.Describe(phase)}";
            text.Stats = DescribeSupport(item.GemSupport, skills);
            text.Hint = "Socket it beside an active gem, in the same link group.";

            var detail = new StringBuilder();
            detail.Append("Changes every active gem linked to it. On its own it casts nothing.");

            string condition = SkillConditions.Describe(item.GemSupport);
            if (condition.Length > 0)
                Line(detail, $"Counts {condition}.");

            if (item.GemSupport.Kind == SkillModifierKind.TriggerOnCondition)
            {
                Line(detail,
                    "While it is linked, the hotkey stops answering — the skills beside " +
                    "it cast themselves instead.");

                if (!SkillModifiers.HasSource(item.GemSupport.TriggerCondition))
                {
                    Line(detail,
                        $"Nothing in the game raises {item.GemSupport.TriggerCondition} yet, " +
                        "so this one never fires.");
                }
            }

            text.Detail = detail.ToString();
        }

        /// <summary>The line that says what a support actually does.</summary>
        private static string DescribeSupport(in SkillModifierBlob support, SkillDatabase skills)
        {
            var lines = new StringBuilder();

            switch (support.Kind)
            {
                case SkillModifierKind.IncreasedDamage:
                    lines.Append($"{Signed(support.Value)}% increased damage");
                    break;

                case SkillModifierKind.IncreasedArea:
                    lines.Append($"{Signed(support.Value)}% increased area of effect");
                    break;

                case SkillModifierKind.IncreasedProjectileSpeed:
                    lines.Append($"{Signed(support.Value)}% increased projectile speed");
                    break;

                case SkillModifierKind.AddedChains:
                    lines.Append($"+{(int)support.Value} chain to a further enemy");
                    break;

                case SkillModifierKind.Fork:
                    lines.Append($"projectiles split into {(int)support.Value + 1} on impact");
                    break;

                case SkillModifierKind.Multicast:
                    lines.Append($"the whole skill goes off {(int)support.Value} extra times");
                    break;

                case SkillModifierKind.ElementalConversion:
                    lines.Append($"damage becomes {support.ConvertTo}");
                    break;

                case SkillModifierKind.ExplodeOnKill:
                    lines.Append(
                        $"anything it kills bursts for {support.SecondaryValue:0}% of the " +
                        $"damage, {support.Value:0.#} m across");
                    break;

                case SkillModifierKind.TriggerOnHit:
                    lines.Append(
                        $"casts {SkillName(skills, support.TriggeredSkillId)} where it lands, " +
                        $"at {support.Value:0}% damage");
                    break;

                case SkillModifierKind.TriggerOnCondition:
                    lines.Append($"casts the linked skills on {support.TriggerCondition}");
                    Line(lines, $"{support.ProcChance * 100f:0}% of the time");

                    if (support.TriggerCooldown > 0f)
                        Line(lines, $"at most once every {support.TriggerCooldown:0.##} s");

                    break;
            }

            return lines.ToString();
        }

        // ─────────────────────────────────────────────────────────────────
        // Gear and money
        // ─────────────────────────────────────────────────────────────────

        private static void DescribeGear(ref ItemBlob item, SkillDatabase skills, ref Text text)
        {
            var kind = new StringBuilder();
            kind.Append(item.Rarity).Append(' ').Append(item.Slot);

            if (item.IsTwoHanded)
                kind.Append(" · both hands");

            text.Kind = kind.ToString();
            text.Stats = StatLines(ref item);
            text.Hint = "Double-click to wear it.";

            var detail = new StringBuilder();

            if (item.SocketCount > 0)
                Line(detail, $"{item.SocketCount} sockets, linked {LinkGroups(ref item)}.");

            for (int i = 0; i < item.ActiveSkillCount && i < item.InnateSkillIds.Length; i++)
            {
                Line(detail,
                    $"Comes with {SkillName(skills, item.InnateSkillIds[i])}, " +
                    "welded into a socket.");
            }

            if (item.Keystone != KeystoneEffect.None)
                Line(detail, DescribeKeystone(item.Keystone));

            text.Detail = detail.ToString();
        }

        private static void DescribeCurrency(ref ItemBlob item, ref Text text)
        {
            text.Kind = "Currency";
            text.Stats = $"Worth {item.CurrencyValue}";
            text.Detail = "Carried like anything else, and lost with the rest of the bag.";
            text.Hint = "Spend it at the vendor.";
        }

        /// <summary>
        /// What a keystone changes, in one sentence each.
        ///
        /// Written out rather than printed as an enum name, because the enum
        /// name is the one thing a player cannot read: "StatusInstantResolve" on
        /// a ring says nothing about burns landing all at once.
        /// </summary>
        private static string DescribeKeystone(KeystoneEffect keystone)
        {
            switch (keystone)
            {
                case KeystoneEffect.AoeToChain:
                    return "Keystone: area skills stop covering ground and jump between bodies.";

                case KeystoneEffect.StatusInstantResolve:
                    return "Keystone: burns and poisons do all their damage at once.";

                case KeystoneEffect.NoManaCostDoubleCooldown:
                    return "Keystone: skills cost no mana, and every cooldown is doubled.";

                case KeystoneEffect.ReactionsAlwaysConsume:
                    return "Keystone: every elemental reaction spends the status that fed it.";

                default:
                    return $"Keystone: {keystone}.";
            }
        }

        /// <summary>The base values and the affixes, one per line.</summary>
        private static string StatLines(ref ItemBlob item)
        {
            var lines = new StringBuilder();

            for (int s = 0; s < StatBlock.StatCount; s++)
            {
                var stat = (StatKind)s;
                float value = item.BaseStats.Get(stat);

                if (Meaningful(value))
                    Line(lines, $"{Signed(value)} {stat}");
            }

            for (int a = 0; a < item.Affixes.Length; a++)
            {
                // A flat struct with no blob inside, so this copy is safe in a
                // way copying the item never is.
                AffixBlob affix = item.Affixes[a];

                Line(lines, affix.Kind == ModifierKind.Increased
                    ? $"{Signed(affix.Value)}% increased {affix.Stat}"
                    : $"{Signed(affix.Value)} {affix.Stat}");
            }

            return lines.ToString();
        }

        /// <summary>"1-2, 3" — which sockets share a group, in socket order.</summary>
        private static string LinkGroups(ref ItemBlob item)
        {
            var groups = new StringBuilder();

            for (int i = 0; i < item.LinkGroups.Length; i++)
            {
                bool joined = i > 0 && item.LinkGroups[i - 1] == item.LinkGroups[i];
                groups.Append(i == 0 ? string.Empty : joined ? "-" : ", ").Append(i + 1);
            }

            return groups.ToString();
        }

        // ─────────────────────────────────────────────────────────────────
        // Skills
        // ─────────────────────────────────────────────────────────────────

        /// <summary>The authored numbers of one skill, before any fold.</summary>
        private static string SkillStats(ref SkillBlob skill)
        {
            var lines = new StringBuilder();

            Line(lines, $"{skill.BaseDamage:0.#} {skill.Type} damage");
            Line(lines, $"{skill.Cooldown:0.##} s cooldown");

            if (skill.ManaCost > 0f)
                Line(lines, $"{skill.ManaCost:0.#} mana");

            if (skill.Range > 0f)
                Line(lines, $"{skill.Range:0.#} m range");

            if (skill.Radius > 0f)
                Line(lines, $"{skill.Radius:0.#} m radius");

            if (skill.Effect == SkillEffectKind.MeleeArc && skill.ArcDegrees > 0f)
                Line(lines, $"{skill.ArcDegrees:0} degree arc");

            if (skill.BaseChains > 0)
                Line(lines, $"chains {skill.BaseChains} times");

            if (skill.ZoneDuration > 0f)
                Line(lines, $"lasts {skill.ZoneDuration:0.#} s");

            return lines.ToString();
        }

        /// <summary>What the skill does, and what it leaves on whatever it hits.</summary>
        private static string SkillDetail(ref SkillBlob skill, SkillDatabase skills)
        {
            var detail = new StringBuilder();
            detail.Append(DescribeEffect(skill.Effect));

            if (skill.AppliedStatus != StatusEffectType.None)
                Line(detail, $"Everything it hits is left {skill.AppliedStatus}.");

            // The supports the skill was authored with. Not the build — those
            // arrive from the sockets beside it and depend on where the gem ends
            // up — but its own behaviour, which a player has no other way of
            // finding out.
            for (int m = 0; m < skill.Modifiers.Length; m++)
                Line(detail, $"Innately: {DescribeSupport(skill.Modifiers[m], skills)}.");

            return detail.ToString();
        }

        private static string DescribeEffect(SkillEffectKind effect)
        {
            switch (effect)
            {
                case SkillEffectKind.Projectile:
                    return "Fires a projectile where you point.";

                case SkillEffectKind.MeleeArc:
                    return "Swings through everything in front of you.";

                case SkillEffectKind.AreaBurst:
                    return "Bursts where you point.";

                case SkillEffectKind.ChainBolt:
                    return "A bolt that jumps from body to body.";

                case SkillEffectKind.PersistentZone:
                    return "Leaves ground that keeps hurting whatever stands in it.";

                default:
                    return effect.ToString();
            }
        }

        private static string SkillName(SkillDatabase skills, int skillId)
        {
            int index = skills.IndexOf(skillId);
            return skills.IsValidIndex(index) ? skills.NameOf(index).ToString() : "something";
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>Appends a line, with no leading blank one on the first.</summary>
        private static void Line(StringBuilder builder, string text)
        {
            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(text);
        }

        /// <summary>
        /// Whether a number is worth a line of its own.
        ///
        /// A difference of a thousandth is float arithmetic, not a stat, and it
        /// would print as "+0" — which reads as a bug in the item rather than in
        /// the comparison.
        /// </summary>
        private static bool Meaningful(float value) => value > 0.005f || value < -0.005f;

        private static string Signed(float value)
            => value >= 0f ? $"+{value:0.##}" : $"{value:0.##}";
    }
}
