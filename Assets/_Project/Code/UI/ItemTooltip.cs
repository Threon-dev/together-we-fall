using System.Text;
using Unity.Collections;
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
        /// <param name="supported">
        /// The skill this gem is currently supporting, when it is sitting in a
        /// socket beside one, and default when it is loose in the bag. It is
        /// what lets a support gem say "this does nothing HERE" — the same gem
        /// in another weapon may be the best one in the build, so the sentence
        /// only exists when there is a skill to say it about.
        /// </param>
        /// <param name="companions">
        /// The other supports in the same link group, for the one question that
        /// is about the group rather than the skill: a spread gem with no
        /// multicast beside it has no gap to widen.
        /// </param>
        /// <param name="passive">
        /// Whether this active gem is one the trigger owns rather than the key.
        /// The panel's answer, because only it knows which socket the gem is
        /// sitting in — a group with a trigger still has one active that keeps
        /// the hotkey.
        /// </param>
        /// <param name="sets">
        /// The set database, when the panel has one. Without it the item is
        /// described exactly as it was before sets existed, which is what a
        /// scene with nothing baked should read like.
        /// </param>
        /// <param name="equippedFromSet">
        /// How many pieces of this item's set the player is currently wearing.
        /// The panel's answer, because only it knows whose character is being
        /// looked at — counted by ItemSets so the tooltip and the character
        /// sheet cannot disagree.
        /// </param>
        public static bool TryDescribeItem(
            ItemDatabase items,
            SkillDatabase skills,
            int itemId,
            int wornItemId,
            out Text text,
            SkillShape supported = default,
            FixedList512Bytes<SkillModifierBlob> companions = default,
            bool passive = false,
            bool groupHasPassive = true,
            ItemSetDatabase sets = default,
            int equippedFromSet = 0)
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
                DescribeActiveGem(ref item, skills, companions, passive, ref text);
            else if (item.GemKind == GemKind.Support)
                DescribeSupportGem(
                    ref item, skills, supported, companions, groupHasPassive, ref text);
            else if (item.CurrencyValue > 0)
                DescribeCurrency(ref item, ref text);
            else
                DescribeGear(ref item, skills, ref text);

            text.Compare = CompareToWorn(items, ref item, wornItemId);

            // Appended rather than folded into the branches above, because it is
            // the one thing on the tooltip that is not about the item alone: the
            // same helmet says something different depending on what else is
            // worn, and every branch would otherwise have to ask.
            Append(ref text.Detail, SetLines(sets, skills, itemId, equippedFromSet));

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

        /// <param name="companions">
        /// The supports in the same link group, or empty for a gem in the bag.
        /// A trigger among them is what turns this gem from something on a key
        /// into something the world casts.
        /// </param>
        private static void DescribeActiveGem(
            ref ItemBlob item,
            SkillDatabase skills,
            in FixedList512Bytes<SkillModifierBlob> companions,
            bool passive,
            ref Text text)
        {
            bool triggered = GemSockets.TryGetTrigger(companions, out SkillModifierBlob trigger);

            text.Kind = "Active gem";

            // Whether THIS gem is the passive one is the panel's answer, not a
            // thing to guess from the group: a group with a trigger in it still
            // has one active that keeps the key, and telling the player their
            // hotkey gem is passive would be worse than saying nothing.
            if (passive)
            {
                text.Hint = $"Cast on {trigger.TriggerCondition} rather than by a key — " +
                            "it cannot go on the bar while the trigger is linked to it.";
            }
            else if (triggered)
            {
                text.Hint = "This one keeps the hotkey. The actives behind it in the group " +
                            $"are cast on {trigger.TriggerCondition} instead.";
            }
            else
            {
                text.Hint = "Put it in a socket, then drag that socket onto a hotkey.";
            }

            int skillIndex = skills.IndexOf(item.GemSkillId);

            if (!skills.IsValidIndex(skillIndex))
            {
                text.Detail = "Casts a skill this build has never heard of.";
                return;
            }

            ref SkillBlob skill = ref skills.Value.Value.Skills[skillIndex];

            text.Kind = passive
                ? $"Passive gem · {skill.Name} on {trigger.TriggerCondition}"
                : $"Active gem · casts {skill.Name}";

            text.Stats = SkillStats(ref skill);
            text.Detail = SkillDetail(ref skill, skills);
        }

        private static void DescribeSupportGem(
            ref ItemBlob item,
            SkillDatabase skills,
            in SkillShape supported,
            in FixedList512Bytes<SkillModifierBlob> companions,
            bool groupHasPassive,
            ref Text text)
        {
            SkillModifierPhase phase = SkillModifiers.PhaseOf(item.GemSupport.Kind);

            text.Kind = $"Support gem · {SkillModifiers.Describe(phase)}";

            var stats = new StringBuilder();
            stats.Append(DescribeSupport(item.GemSupport, skills));

            // The second half of a two-sided gem, on its own line and without a
            // word saying which is the price. What the numbers do says it: a
            // player reading "+80% damage" over "+60% mana cost" does not need
            // to be told which one hurts.
            if (item.HasSupportSecond)
            {
                Line(stats, DescribeSupport(item.GemSupportSecond, skills));

                string secondCondition = SkillConditions.Describe(item.GemSupportSecond);
                if (secondCondition.Length > 0)
                    Line(stats, $"— {secondCondition}");
            }

            text.Stats = stats.ToString();
            text.Hint = "Socket it beside an active gem, in the same link group.";

            var detail = new StringBuilder();

            // What this gem is doing right where it sits, said before anything
            // general about what supports are. A gem that cannot act here is
            // the one thing the player needs to read first — the socket looks
            // exactly as full either way.
            string inert = InertLine(ref item, supported, companions, groupHasPassive);

            if (inert.Length > 0)
            {
                detail.Append(inert);
                Line(detail, "Move it to a socket where it has something to do.");
            }
            else
            {
                detail.Append(
                    "Changes every active gem linked to it. On its own it casts nothing.");
            }

            string condition = SkillConditions.Describe(item.GemSupport);
            if (condition.Length > 0)
                Line(detail, $"Counts {condition}.");

            if (item.GemSupport.Kind == SkillModifierKind.TriggerOnCondition)
            {
                Line(detail,
                    "The first active in the group keeps its hotkey. Every active behind " +
                    "it becomes a passive this gem casts, and stops answering keys.");

                if (!SkillModifiers.HasSource(item.GemSupport.TriggerCondition))
                {
                    Line(detail,
                        $"Nothing in the game raises {item.GemSupport.TriggerCondition} yet, " +
                        "so this one never fires.");
                }
            }

            text.Detail = detail.ToString();
        }

        /// <summary>
        /// The sentence a support gem gets when it cannot do anything where it
        /// is, or empty.
        ///
        /// Two reasons, and they are genuinely different questions. One is
        /// about the skill — a swing has no projectile to split — and is
        /// answered by the same table the socket cell dims the gem with. The
        /// other is about the group: a spread gem with nothing to spread is
        /// fine on any skill and useless in this hole.
        ///
        /// Both halves of a two-sided gem are asked, because a gem whose
        /// benefit lands and whose price does not is not inert, and saying it
        /// is would be worse than saying nothing.
        /// </summary>
        private static string InertLine(
            ref ItemBlob item,
            in SkillShape supported,
            in FixedList512Bytes<SkillModifierBlob> companions,
            bool groupHasPassive)
        {
            if (!supported.Exists)
                return string.Empty;

            bool first = SkillModifiers.AppliesTo(item.GemSupport.Kind, supported);
            bool second = !item.HasSupportSecond ||
                          SkillModifiers.AppliesTo(item.GemSupportSecond.Kind, supported);

            if (!first && !second)
            {
                return $"Does nothing for {supported.Name}: " +
                       $"{SkillModifiers.WhyInert(item.GemSupport.Kind, supported)}.";
            }

            if (!first || !second)
            {
                SkillModifierKind dead = first ? item.GemSupportSecond.Kind : item.GemSupport.Kind;

                return $"Half of it does nothing for {supported.Name}: " +
                       $"{SkillModifiers.WhyInert(dead, supported)}.";
            }

            // A trigger needs something to cast, and what it casts is an active
            // gem socketed BEHIND the one on the key. On its own it fires into
            // an empty group.
            if (SkillModifiers.IsAutomatic(item.GemSupport.Kind) && !groupHasPassive)
            {
                return "Nothing to cast: put a second active gem in this group. The first " +
                       "one keeps the hotkey, and this fires the ones behind it.";
            }

            // Nothing wrong with the skill. The group is the other place a gem
            // can be wasted, and only one kind is asked about it.
            if (!SkillModifiers.HasCompanion(item.GemSupport.Kind))
                return string.Empty;

            SkillModifierKind needed = SkillModifiers.NeedsCompanion(item.GemSupport.Kind);

            return GemSockets.GroupHas(companions, needed)
                ? string.Empty
                : $"Does nothing until a {needed} support is linked beside it.";
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

                case SkillModifierKind.IncreasedManaCost:
                    lines.Append($"{Signed(support.Value)}% mana cost");
                    break;

                case SkillModifierKind.ReducedCooldown:
                    lines.Append($"{Signed(support.Value)}% reduced cooldown");
                    break;

                case SkillModifierKind.IncreasedDuration:
                    lines.Append($"{Signed(support.Value)}% increased zone duration");
                    break;

                case SkillModifierKind.IncreasedSpread:
                    lines.Append($"{Signed(support.Value)}% wider spread between casts");
                    break;

                case SkillModifierKind.IncreasedCritChance:
                    lines.Append($"{Signed(support.Value)}% increased critical chance");
                    break;

                case SkillModifierKind.Pierce:
                    lines.Append($"projectiles pass through {(int)support.Value} more enemies");
                    break;

                case SkillModifierKind.StatusOverride:
                    lines.Append($"everything it hits is {support.AppliedStatus} instead");
                    break;

                case SkillModifierKind.CullingStrike:
                    lines.Append($"kills outright below {support.Value:0}% life");
                    break;

                case SkillModifierKind.ManaOnKill:
                    lines.Append($"+{support.Value:0.#} mana for every kill");
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
            text.Detail = "It never goes in the bag. Picking it up puts its value straight " +
                          "into your purse.";
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

        // ─────────────────────────────────────────────────────────────────
        // Item sets
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Which set this item belongs to, how much of it is worn, and what
        /// every step is worth — reached or not.
        ///
        /// The unreached steps are shown on purpose. A set bonus the player
        /// cannot see the shape of is a reason to keep wearing four pieces of
        /// something worse for no reason they could name, and "what do I get
        /// for one more piece" is the only question this whole feature asks.
        ///
        /// Empty for an item in no set, which is almost every item.
        /// </summary>
        private static string SetLines(
            ItemSetDatabase sets, SkillDatabase skills, int itemId, int equipped)
        {
            int setIndex = ItemSets.SetIndexOf(sets, itemId);
            if (setIndex < 0)
                return string.Empty;

            // By reference: ItemSetBlob carries two blob arrays, and a copy of
            // it would point them at whatever sits near the copy.
            ref ItemSetBlob set = ref sets.Value.Value.Sets[setIndex];

            var lines = new StringBuilder();
            Line(lines, $"Set: {set.SetName} — {equipped}/{set.MemberItemIds.Length} worn");

            // Which step is in force, asked of the same helper the host asks, so
            // the highlight cannot promise a bonus the stat maths is not paying.
            ItemSets.TryHighestReached(ref set, equipped, out int active);

            for (int t = 0; t < set.Thresholds.Length; t++)
            {
                // Flat data with no blob array inside, so this copy is safe in a
                // way copying the set never is.
                SetThresholdBlob step = set.Thresholds[t];

                string rewards = DescribeThreshold(step, skills);

                // Three states, not two. A step below the one in force is
                // REPLACED rather than missing, and saying so is the whole of
                // explaining that these do not stack — a player who reads
                // "2 pieces" beside "4 pieces (active)" with no note would
                // reasonably assume they have both.
                string state = t == active
                    ? " (active)"
                    : step.RequiredPieceCount <= equipped
                        ? " (replaced)"
                        : $" (needs {step.RequiredPieceCount - equipped} more)";

                Line(lines, $"{step.RequiredPieceCount} pieces: {rewards}{state}");
            }

            return lines.ToString();
        }

        /// <summary>What one step of a set gives, in one line.</summary>
        private static string DescribeThreshold(
            in SetThresholdBlob step, SkillDatabase skills)
        {
            var rewards = new StringBuilder();

            for (int s = 0; s < StatBlock.StatCount; s++)
            {
                var stat = (StatKind)s;

                if (Meaningful(step.FlatBonuses.Get(stat)))
                    Comma(rewards, $"{Signed(step.FlatBonuses.Get(stat))} {stat}");

                if (Meaningful(step.IncreasedBonuses.Get(stat)))
                {
                    Comma(rewards,
                        $"{Signed(step.IncreasedBonuses.Get(stat))}% increased {stat}");
                }
            }

            // The same sentence a support gem gets, because it IS one — it
            // simply acts on every skill instead of on one link group, which is
            // what the note beside it says.
            if (step.HasBonusSupport)
                Comma(rewards, $"{DescribeSupport(step.BonusSupport, skills)} (all skills)");

            if (step.BonusKeystone != KeystoneEffect.None)
                Comma(rewards, DescribeKeystone(step.BonusKeystone));

            return rewards.Length > 0 ? rewards.ToString() : "nothing yet";
        }

        /// <summary>Appends within a line, comma separated.</summary>
        private static void Comma(StringBuilder builder, string text)
        {
            if (builder.Length > 0)
                builder.Append(", ");

            builder.Append(text);
        }

        /// <summary>Appends a block to a part of the tooltip that may be empty.</summary>
        private static void Append(ref string part, string block)
        {
            if (string.IsNullOrEmpty(block))
                return;

            part = string.IsNullOrEmpty(part) ? block : part + "\n" + block;
        }

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
