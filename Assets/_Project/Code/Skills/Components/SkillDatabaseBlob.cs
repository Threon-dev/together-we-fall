using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;

namespace TogetherWeFall.Skills
{
    /// <summary>
    /// One support socketed into a skill.
    ///
    /// The fields are grouped by size rather than by meaning, on purpose: five
    /// bytes and six four-byte values pack into thirty-two, and this struct is
    /// gathered by value into a fixed list once per cast. Interleaving them
    /// would pad it to forty-four and cost a fifth of the list capacity for
    /// nothing.
    /// </summary>
    public struct SkillModifierBlob
    {
        public SkillModifierKind Kind;

        public DamageType ConvertTo;

        /// <summary>What makes a TriggerOnCondition support fire. Ignored by the rest.</summary>
        public TriggerConditionType TriggerCondition;

        /// <summary>
        /// What must be true for this support to count at all, or None.
        ///
        /// On every kind rather than on a separate sort of gem, which is the
        /// whole economy of the idea: "chain" and "chain into burning bodies"
        /// are one mechanism and one authored field apart, and every support
        /// that already exists gets the option for free.
        /// </summary>
        public ModifierConditionType Condition;

        /// <summary>Which element the condition is about. See ModifierConditionType.</summary>
        public DamageType RequiredElement;

        /// <summary>
        /// Which status the condition is about, for TargetHasStatusEffect.
        ///
        /// A sixth byte in a struct that had five and is padded to thirty-two
        /// either way, so it costs nothing here and nothing in the fixed list
        /// the fold gathers into.
        /// </summary>
        public StatusEffectType RequiredStatus;

        /// <summary>
        /// The status a StatusOverride support puts on the supported skill.
        /// Ignored by every other kind.
        ///
        /// A seventh byte in a struct that has room for eight before it pads,
        /// so it costs nothing here and nothing in the fixed list the fold
        /// gathers into — the same arithmetic RequiredStatus was added under.
        /// </summary>
        public StatusEffectType AppliedStatus;

        /// <summary>
        /// How many bodies a TargetCrowded condition wants. Ignored by the rest.
        ///
        /// The eighth byte, and the last one free. A ninth would push this
        /// struct to thirty-six and cost two slots out of the fixed list.
        /// </summary>
        public byte RequiredCount;

        public float Value;

        /// <summary>Second number, where one is not enough. See SkillModifier.</summary>
        public float SecondaryValue;

        /// <summary>
        /// Skill this support triggers, by stable id, or zero for none.
        ///
        /// An id rather than an index because a support can now arrive from a
        /// gem, and the item baker has no idea what order the skill database
        /// ended up in. Resolve turns it into an index once, at cast time.
        ///
        /// Only TriggerOnHit uses it. A condition trigger names no skill: it
        /// fires the actives already linked to it.
        /// </summary>
        public int TriggeredSkillId;

        /// <summary>
        /// How often a condition trigger actually goes off, from zero to one.
        ///
        /// Mandatory rather than a nicety. A trigger that is certain turns every
        /// kill in a wave into a cast, and the interesting version of "cast on
        /// kill" is the one you cannot count on.
        /// </summary>
        public float ProcChance;

        /// <summary>Seconds a condition trigger must wait between firings.</summary>
        public float TriggerCooldown;

        /// <summary>
        /// The number the condition compares against: a fraction of health for
        /// TargetLowHealth and for OnLowHealth. Ignored by conditions that
        /// compare nothing.
        /// </summary>
        public float Threshold;
    }

    /// <summary>
    /// One skill and everything socketed into it.
    ///
    /// NEVER copy by value: the modifier list is a BlobArray, whose offset is
    /// relative to its own address. Always take it as `ref SkillBlob`.
    /// </summary>
    public struct SkillBlob
    {
        public FixedString64Bytes Name;

        /// <summary>What an active gem names this skill by.</summary>
        public int SkillId;
        public SkillEffectKind Effect;
        public DamageType Type;

        public float BaseDamage;
        public float Cooldown;

        /// <summary>
        /// Mana one press costs. Zero is free.
        ///
        /// Charged per REQUEST, not per effect: multicast is one cooldown and
        /// several projectiles, and the cost follows the cooldown for the same
        /// reason — a support that fires three shots must not also triple the
        /// bill, or "more projectiles" would quietly also mean "a third of the
        /// casts".
        /// </summary>
        public float ManaCost;

        /// <summary>How far the skill reaches, and how far a projectile travels.</summary>
        public float Range;

        /// <summary>Blast radius, arc radius, or impact radius. Zero means single target.</summary>
        public float Radius;

        /// <summary>Full width of a melee arc. Three hundred and sixty is a circle.</summary>
        public float ArcDegrees;

        public float ProjectileSpeed;

        /// <summary>How long a persistent zone lasts. Ignored by every other effect.</summary>
        public float ZoneDuration;

        /// <summary>Seconds between a zone's damage pulses.</summary>
        public float ZoneTickInterval;

        public int BaseChains;
        public float ChainRange;
        public float ChainDelay;

        /// <summary>
        /// Which visual set draws this skill, or zero for none.
        ///
        /// An id rather than an index, like every other cross-database
        /// reference here: the presenter's list of sets is filled by the scene
        /// build and this blob by the skill authoring, and the two share no
        /// ordering. Zero draws what the game drew before sets existed.
        /// </summary>
        public int VfxId;

        /// <summary>
        /// A status this skill applies to everything it hits, or None.
        ///
        /// Named by type, because the status table is baked by a different
        /// authoring object than this database and an index would mean whichever
        /// order that one happened to produce. A type nothing answers to applies
        /// nothing, which is the same inert failure a gem naming a missing skill
        /// already has.
        ///
        /// Deliberately no application chance. A chance would be one more field
        /// and a random number in a job that has none — and chance is balance,
        /// which this prototype does not have anywhere else either. Making it
        /// probabilistic later is one field and one draw.
        /// </summary>
        public StatusEffectType AppliedStatus;

        /// <summary>
        /// Supports authored onto the skill itself.
        ///
        /// These are now INNATE behaviour rather than the build: a skill that is
        /// meant to chain by its nature says so here. Everything a player
        /// chooses arrives from gems in the same link group and is folded on top
        /// of these. Emptying this array on an asset is all it takes to make a
        /// skill purely what its gems say it is.
        /// </summary>
        public BlobArray<SkillModifierBlob> Modifiers;
    }

    /// <summary>Every skill in the game, addressed by index.</summary>
    public struct SkillDatabaseBlob
    {
        public BlobArray<SkillBlob> Skills;
    }

    /// <summary>
    /// The few facts about a skill that decide whether a support has anything
    /// to act on.
    ///
    /// A flat, copyable struct — no BlobArray — so the panel can hold one while
    /// it draws a socket and hand it to the tooltip. It exists because "this
    /// gem does nothing here" is a question two parts of the UI ask about the
    /// same gem, and an answer written twice is an answer that drifts.
    ///
    /// Deliberately not the whole SkillBlob. What a support needs to know is
    /// what kind of effect this is, whether it has an area at all, and whether
    /// it costs anything — everything else about a skill is a number a support
    /// changes rather than a reason it cannot.
    /// </summary>
    public struct SkillShape
    {
        public FixedString64Bytes Name;
        public SkillEffectKind Effect;

        /// <summary>Zero on a projectile means it hits one body and nothing around it.</summary>
        public float Radius;

        /// <summary>Zero is free, which a weapon's built-in attack always is.</summary>
        public float ManaCost;

        /// <summary>Whether this shape names a real skill at all.</summary>
        public bool Exists;
    }

    /// <summary>
    /// A skill after its supports and the caster have been folded in. What the
    /// cast actually uses.
    /// </summary>
    public struct ResolvedSkill
    {
        public SkillEffectKind Effect;
        public DamageType Type;

        public float Damage;
        public float Cooldown;

        /// <summary>What this press will cost, after the fold. Zero is free.</summary>
        public float ManaCost;

        public float Range;
        public float Radius;
        public float ArcCosine;
        public float ProjectileSpeed;

        public float ZoneDuration;
        public float ZoneTickInterval;

        /// <summary>How many times the whole skill goes off. Multicast raises it.</summary>
        public int Casts;

        /// <summary>
        /// How far apart those casts fan out, in degrees. The default is what
        /// the cast system used to hold as a constant.
        /// </summary>
        public float SpreadDegrees;

        /// <summary>Bodies a projectile passes through before it stops.</summary>
        public int Pierces;

        /// <summary>Fraction of life below which a blow finishes the target, or zero.</summary>
        public float CullThreshold;

        /// <summary>Mana returned per body this skill kills.</summary>
        public float ManaOnKill;

        /// <summary>
        /// Chance that this cast lands as a critical blow, from zero to one.
        ///
        /// Folded here rather than rolled here: the fold is a pure function of
        /// the build, and a random number in it would make the same gems answer
        /// differently to the panel and to the host.
        /// </summary>
        public float CritChance;

        /// <summary>What a critical blow multiplies the damage by.</summary>
        public float CritMultiplier;

        public int Forks;
        public int Chains;
        public float ChainRange;
        public float ChainDelay;

        public float ExplosionRadius;
        public float ExplosionDamage;

        /// <summary>Skill to cast where this one lands, or -1.</summary>
        public int TriggerSkillIndex;

        /// <summary>Fraction of the triggered skill's own damage. One is full.</summary>
        public float TriggerDamageScale;

        /// <summary>The status everything this cast touches is marked with, or None.</summary>
        public StatusEffectType AppliedStatus;

        /// <summary>
        /// Which visual set draws this, or zero.
        ///
        /// It rides the fold rather than being read off the blob at the far end
        /// for one reason: everything downstream of a cast already carries what
        /// it needs to resolve itself and has deliberately forgotten which skill
        /// it came from. A projectile in flight knows its damage and its element
        /// and nothing else — so if it is to have a trail, the trail has to
        /// travel with it.
        ///
        /// No support changes it. It is the only field in here that is copied
        /// rather than folded, and that is the point: a gem may turn a fire bolt
        /// blue, and the fold already says so through the damage type the
        /// presenter tints with.
        /// </summary>
        public int VfxId;
    }

    /// <summary>
    /// Handle to the skill database, and the place supports are applied.
    ///
    /// The fold happens here, once per cast, rather than in a system per
    /// modifier. That is a deliberate departure from "each modifier is its own
    /// system": most supports change how many things exist or how big they are,
    /// which is a decision to make BEFORE anything exists — a system reading a
    /// damage event has already missed the moment. The result would be a dozen
    /// systems that iterate nothing on almost every frame, and the one place to
    /// look to answer "why is my damage this number" would be spread across all
    /// of them.
    ///
    /// What genuinely does belong downstream is here too, just not as a fold:
    /// chains happen at hit time, forks at impact, explosions at death. Those
    /// are behaviours, and they live in the systems that own those moments.
    ///
    /// Resolve lives on this component rather than on the blob struct because a
    /// method on the blob would take `this` by value, and copying a struct that
    /// contains a BlobArray quietly breaks it.
    /// </summary>
    public struct SkillDatabase : IComponentData
    {
        /// <summary>
        /// How far apart the copies of a multicast fan out, before any support.
        ///
        /// It lived in the cast system as a constant until a gem wanted to
        /// change it. Here rather than there because the fold is where every
        /// other number a support touches is decided, and a value the fold
        /// could not see would be one the tooltip could not explain.
        /// </summary>
        public const float DefaultSpreadDegrees = 9f;

        public BlobAssetReference<SkillDatabaseBlob> Value;

        public bool IsValidIndex(int index)
            => Value.IsCreated && index >= 0 && index < Value.Value.Skills.Length;

        /// <summary>
        /// Index of a skill by its stable id, or -1.
        ///
        /// A scan rather than a binary search: there are four skills, and
        /// sorting the database by id would scramble the order the loadout and
        /// the content factory both address it by.
        /// </summary>
        public int IndexOf(int skillId)
        {
            if (!Value.IsCreated || skillId == 0)
                return -1;

            ref SkillDatabaseBlob blob = ref Value.Value;

            for (int i = 0; i < blob.Skills.Length; i++)
            {
                if (blob.Skills[i].SkillId == skillId)
                    return i;
            }

            return -1;
        }

        public FixedString64Bytes NameOf(int index)
            => IsValidIndex(index) ? Value.Value.Skills[index].Name : default;

        /// <summary>
        /// How far a skill reaches, before the fold.
        ///
        /// Needed by the one caller that has to look at the world BEFORE it can
        /// fold: a conditional support asks about the target, and finding the
        /// target needs a range. Range is not scaled by any support, so asking
        /// early and asking late give the same answer — which is what makes this
        /// safe rather than a second source of truth.
        /// </summary>
        public float RangeOf(int index)
            => IsValidIndex(index) ? Value.Value.Skills[index].Range : 0f;

        // ManaCostOf used to sit here: the authored cost, for a caller that
        // wanted one field and not the fold. Its own comment said it would
        // become wrong the day a support scaled the cost and that the compiler
        // would not say so — IncreasedManaCost is that day, so it is gone
        // rather than left to quietly disagree with the host. The one caller,
        // the skill bar, folds properly now; it was already holding the
        // supports.

        /// <summary>
        /// Everything the supports of one cast add up to, before the maths.
        ///
        /// Its own struct so that innate supports and gem supports run through
        /// exactly the same code. Two loops with the same body is how the two
        /// sources start disagreeing about what Multicast means.
        /// </summary>
        private struct Fold
        {
            public float IncreasedDamage;
            public float IncreasedArea;
            public float IncreasedSpeed;
            public float IncreasedCost;
            public float ReducedCooldown;
            public float IncreasedDuration;
            public float IncreasedSpread;
            public float IncreasedCrit;

            public int Chains;
            public int Forks;
            public int Casts;
            public int Pierces;

            public DamageType Type;

            /// <summary>The status the skill ends up applying, after any override.</summary>
            public StatusEffectType Status;

            public float ExplosionRadius;
            public float ExplosionShare;

            /// <summary>The deepest cull any support asked for. Fractions, not percent.</summary>
            public float CullThreshold;

            public float ManaOnKill;

            public int TriggerId;
            public float TriggerShare;
        }

        /// <summary>
        /// What this skill will actually deal, before anything else is folded.
        ///
        /// Innate conversions first, then gem ones, so a gem beats an authored
        /// conversion — the same precedence the main fold applies, arrived at
        /// the same way. Its own pass rather than a value read out of the main
        /// one, because the main fold needs this answer before it starts.
        /// </summary>
        private static DamageType FinalElement(
            ref SkillBlob skill, in FixedList512Bytes<SkillModifierBlob> gemSupports)
        {
            DamageType type = skill.Type;

            for (int m = 0; m < skill.Modifiers.Length; m++)
            {
                if (skill.Modifiers[m].Kind == SkillModifierKind.ElementalConversion)
                    type = skill.Modifiers[m].ConvertTo;
            }

            for (int g = 0; g < gemSupports.Length; g++)
            {
                if (gemSupports[g].Kind == SkillModifierKind.ElementalConversion)
                    type = gemSupports[g].ConvertTo;
            }

            return type;
        }

        /// <summary>
        /// Whether this skill's own supports ask about anything.
        ///
        /// The counterpart of SkillConditions.AnyConditional, which can only
        /// see the gems: a skill authored with a conditional support of its own
        /// needs the context gathered even when nothing is socketed beside it.
        /// </summary>
        public bool AnyConditional(int index)
        {
            if (!IsValidIndex(index))
                return false;

            ref SkillBlob skill = ref Value.Value.Skills[index];

            for (int m = 0; m < skill.Modifiers.Length; m++)
            {
                if (skill.Modifiers[m].Condition != ModifierConditionType.None)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// What kind of effect a skill is, for a caller that wants to know
        /// whether a support has anything to act on.
        ///
        /// Beside RangeOf and safe for the same reason: no support changes what
        /// a skill fundamentally does, so asking before the fold and after it
        /// give the same answer.
        /// </summary>
        public SkillEffectKind EffectOf(int index)
            => IsValidIndex(index) ? Value.Value.Skills[index].Effect : SkillEffectKind.Projectile;

        /// <summary>
        /// The handful of facts that decide whether a support can act on this
        /// skill at all. Safe to copy, unlike the blob it is read from.
        /// </summary>
        public SkillShape ShapeOf(int index)
        {
            if (!IsValidIndex(index))
                return default;

            ref SkillBlob skill = ref Value.Value.Skills[index];

            return new SkillShape
            {
                Name = skill.Name,
                Effect = skill.Effect,
                Radius = skill.Radius,
                ManaCost = skill.ManaCost,
                Exists = true
            };
        }

        /// <summary>
        /// Folds one support in, unless its condition says otherwise.
        ///
        /// The condition check sits here rather than in the two loops below so
        /// that innate supports and gem supports go through it identically —
        /// the same reason this method exists at all. It is one comparison for
        /// every support authored before conditions existed, because None is
        /// the first case.
        /// </summary>
        private static void Accumulate(
            in SkillModifierBlob modifier, ref Fold fold, in CastConditions conditions)
        {
            if (!SkillConditions.IsMet(modifier, conditions))
                return;

            switch (modifier.Kind)
            {
                case SkillModifierKind.IncreasedDamage:
                    fold.IncreasedDamage += modifier.Value;
                    break;

                case SkillModifierKind.IncreasedArea:
                    fold.IncreasedArea += modifier.Value;
                    break;

                case SkillModifierKind.IncreasedProjectileSpeed:
                    fold.IncreasedSpeed += modifier.Value;
                    break;

                case SkillModifierKind.AddedChains:
                    fold.Chains += (int)modifier.Value;
                    break;

                case SkillModifierKind.Fork:
                    fold.Forks += (int)math.max(1f, modifier.Value);
                    break;

                case SkillModifierKind.Multicast:
                    fold.Casts += (int)math.max(1f, modifier.Value);
                    break;

                case SkillModifierKind.ElementalConversion:
                    fold.Type = modifier.ConvertTo;
                    break;

                case SkillModifierKind.ExplodeOnKill:
                    fold.ExplosionRadius = math.max(fold.ExplosionRadius, modifier.Value);
                    fold.ExplosionShare = math.max(fold.ExplosionShare, modifier.SecondaryValue);
                    break;

                case SkillModifierKind.TriggerOnHit:
                    // Two trigger supports in one group: the last one folded
                    // wins. Firing both would double every effect downstream of
                    // it, which is not what anyone means by socketing two.
                    fold.TriggerId = modifier.TriggeredSkillId;
                    fold.TriggerShare = modifier.Value;
                    break;

                case SkillModifierKind.TriggerOnCondition:
                    // Nothing to fold. It changes who casts, not what the cast
                    // is — and by the time anything gets here, that decision has
                    // already been made by TriggerEvaluationSystem. Listed
                    // explicitly rather than left to the default so that the one
                    // support with no numbers is visibly deliberate.
                    break;

                case SkillModifierKind.IncreasedManaCost:
                    fold.IncreasedCost += modifier.Value;
                    break;

                case SkillModifierKind.ReducedCooldown:
                    fold.ReducedCooldown += modifier.Value;
                    break;

                case SkillModifierKind.IncreasedDuration:
                    fold.IncreasedDuration += modifier.Value;
                    break;

                case SkillModifierKind.IncreasedSpread:
                    fold.IncreasedSpread += modifier.Value;
                    break;

                case SkillModifierKind.IncreasedCritChance:
                    fold.IncreasedCrit += modifier.Value;
                    break;

                case SkillModifierKind.Pierce:
                    fold.Pierces += (int)math.max(1f, modifier.Value);
                    break;

                // Overwrites, exactly as an elemental conversion does, and the
                // last one folded wins for the same reason: a blow carries one
                // named status, so two override gems is a question with no
                // second answer rather than a sum.
                case SkillModifierKind.StatusOverride:
                    fold.Status = modifier.AppliedStatus;
                    break;

                // The deepest threshold wins rather than the sum, the same rule
                // the explosion radius beside it follows: two culling supports
                // is the better of the two, not a percentage nobody authored.
                case SkillModifierKind.CullingStrike:
                    fold.CullThreshold =
                        math.max(fold.CullThreshold, math.saturate(modifier.Value * 0.01f));
                    break;

                case SkillModifierKind.ManaOnKill:
                    fold.ManaOnKill += math.max(0f, modifier.Value);
                    break;
            }
        }

        /// <summary>
        /// Folds supports and the caster's stats into the numbers one cast uses.

        /// <para>
        /// The supports arrive as an argument rather than being read off the
        /// skill, and that is the whole gem model: the same active gem folds a
        /// different list depending on what is socketed beside it. Nothing below
        /// this line knows the numbers came from sockets.
        /// </para>
        ///
        /// Increases stack additively before being applied once, the same rule
        /// the equipment stats follow — two supports each adding fifty percent
        /// give double, not two and a quarter times.
        ///
        /// <para>
        /// The conditions arrive as an argument for the same reason the supports
        /// do: whether a support counts is a fact about this cast, not about the
        /// gem. A caller with nothing to say passes CastConditions.Unknown and
        /// every conditional support simply sits out.
        /// </para>
        /// </summary>
        public ResolvedSkill Resolve(
            int index,
            in StatBlock stats,
            in FixedList512Bytes<SkillModifierBlob> gemSupports,
            in CastConditions conditions)
        {
            ref SkillBlob skill = ref Value.Value.Skills[index];

            // The element a condition asks about is what the skill will
            // ACTUALLY deal, worked out in a pass of its own before anything
            // else is folded.
            //
            // It used to be the authored one, to keep the answer from depending
            // on where a conversion gem happened to sit. The cost of that was a
            // player socketing Galvanic Focus beside a lance that comes out
            // yellow and getting nothing, with nothing anywhere saying why —
            // and the order-independence was bought at the price of the gem
            // being wrong about the game the player can see.
            //
            // The pre-pass buys it back: conversions are gathered first and
            // separately, so every element condition in the group sees the same
            // final element no matter which hole it sits in. Two conversion
            // gems still resolve last-wins, exactly as the damage type itself
            // always has — and a conversion's own condition is not consulted
            // here, because "what element is this skill" cannot depend on an
            // answer that depends on it.
            CastConditions asked = conditions;
            asked.SkillElement = FinalElement(ref skill, gemSupports);

            var fold = new Fold
            {
                Chains = skill.BaseChains,
                Casts = 1,
                Type = skill.Type,

                // What the skill applies unless a support says otherwise. In
                // the fold rather than read straight off the blob at the bottom,
                // because that is the only way an override can lose to the
                // authored one when nobody socketed a gem for it.
                Status = skill.AppliedStatus,
                TriggerShare = 100f
            };

            // Innate first, then whatever the player linked. The order only
            // matters for the two modifiers that overwrite rather than add — a
            // gem conversion beats an authored one, which is the way round
            // anybody would expect.
            for (int m = 0; m < skill.Modifiers.Length; m++)
            {
                // A plain struct with no blob array inside, so copying is safe
                // here in a way that copying the skill never is.
                Accumulate(skill.Modifiers[m], ref fold, asked);
            }

            for (int g = 0; g < gemSupports.Length; g++)
                Accumulate(gemSupports[g], ref fold, asked);

            float increasedDamage = fold.IncreasedDamage;
            float increasedArea = fold.IncreasedArea;
            float increasedSpeed = fold.IncreasedSpeed;
            float duration = fold.IncreasedDuration;
            float spread = fold.IncreasedSpread;

            int chains = fold.Chains;
            int forks = fold.Forks;
            int casts = fold.Casts;
            int pierces = fold.Pierces;

            DamageType type = fold.Type;
            float explosionRadius = fold.ExplosionRadius;
            float explosionShare = fold.ExplosionShare;

            // Turned into an index exactly once, here, so nothing downstream
            // ever holds an id it would have to resolve again.
            int triggerIndex = IndexOf(fold.TriggerId);
            float triggerShare = fold.TriggerShare;

            // The character's damage stat adds flat to every skill, so a weapon
            // makes every skill hit harder without any skill knowing weapons
            // exist. Supports then scale the sum.
            float damage = (skill.BaseDamage + stats.Get(StatKind.Damage))
                           * (1f + increasedDamage * 0.01f);

            // Attack speed shortens cooldowns, which is the other half of making
            // equipment felt rather than merely displayed.
            //
            // Zero means the sheet has not been computed yet — a character in
            // its first frame, or a scene with no base stats baked. Treating
            // that as "no attack speed" would make every skill ten times slower
            // and look like a bug in the cooldowns.
            float attackSpeed = stats.Get(StatKind.AttackSpeed);
            if (attackSpeed <= 0.01f)
                attackSpeed = 1f;

            // Floored well above zero. A hundred percent reduced cooldown is a
            // skill that goes off every frame, which is not a fast build, it is
            // a frame spent casting — the same rail the trigger cooldown has.
            float cooldownScale = math.max(0.2f, 1f - fold.ReducedCooldown * 0.01f);

            // Floored at nothing rather than at the base: a support that makes
            // a skill free is a legitimate thing to author, and a negative bill
            // would refund mana for pressing a button.
            float costScale = math.max(0f, 1f + fold.IncreasedCost * 0.01f);

            // The sheet's chance, scaled by the supports, as a fraction. A
            // character with no crit chance stays at zero however many supports
            // are socketed — which is what makes the first item that grants
            // some worth finding.
            float critChance = math.saturate(
                stats.Get(StatKind.CritChance) * 0.01f * (1f + fold.IncreasedCrit * 0.01f));

            // A sheet that has never heard of crit multipliers would otherwise
            // make every critical blow do less damage than an ordinary one.
            float critMultiplier = math.max(1f, stats.Get(StatKind.CritMultiplier));

            return new ResolvedSkill
            {
                Effect = skill.Effect,
                Type = type,
                Damage = damage,
                Cooldown = skill.Cooldown / attackSpeed * cooldownScale,

                // Scaled by its own modifier kind, which is what the note that
                // used to sit here asked for: a support that changes the price
                // has a fold entry of its own rather than borrowing a number
                // from somewhere else. Everything else about it is unchanged —
                // the panel and the host still read this one figure, because a
                // bar that advertises a price the cast does not charge is worse
                // than no price at all. ManaCostOf, which used to answer that
                // question off the asset, is gone for exactly this reason.
                ManaCost = skill.ManaCost * costScale,
                Range = skill.Range,
                Radius = skill.Radius * (1f + increasedArea * 0.01f),
                ArcCosine = math.cos(math.radians(math.clamp(skill.ArcDegrees, 0f, 360f) * 0.5f)),
                ProjectileSpeed = skill.ProjectileSpeed * (1f + increasedSpeed * 0.01f),

                // Its own modifier kind, as the note that used to sit here
                // insisted: increased area makes a zone cover more ground
                // through Radius above, and how long it burns is a different
                // purchase rather than a number borrowed from that one.
                //
                // The tick interval is deliberately NOT scaled with it. A zone
                // that lasts twice as long should pulse twice as many times,
                // not twice as fast — scaling both would leave the total damage
                // where it started and make the support do nothing.
                ZoneDuration = skill.ZoneDuration * math.max(0.1f, 1f + duration * 0.01f),
                ZoneTickInterval = skill.ZoneTickInterval,
                Casts = math.max(1, casts),

                // Clamped away from zero and from a full circle: at zero every
                // copy of a multicast flies down the same line and the support
                // reads as doing nothing, and past a semicircle half of them
                // fly backwards.
                SpreadDegrees = math.clamp(
                    DefaultSpreadDegrees * (1f + spread * 0.01f), 1f, 180f),

                Pierces = math.max(0, pierces),
                CullThreshold = fold.CullThreshold,
                ManaOnKill = fold.ManaOnKill,
                CritChance = critChance,
                CritMultiplier = critMultiplier,
                Forks = math.max(0, forks),
                Chains = math.max(0, chains),
                ChainRange = skill.ChainRange,
                ChainDelay = skill.ChainDelay,
                ExplosionRadius = explosionRadius,
                ExplosionDamage = damage * explosionShare * 0.01f,
                TriggerSkillIndex = triggerIndex,
                TriggerDamageScale = math.max(0f, triggerShare) * 0.01f,

                // Out of the fold now rather than off the blob: StatusOverride
                // is that new modifier kind, and it starts from the authored
                // status, so a skill with no override gem resolves to exactly
                // what it always did.
                AppliedStatus = fold.Status,

                // Copied, not folded. See the field.
                VfxId = skill.VfxId
            };
        }
    }
}
