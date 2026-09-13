using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;

namespace TogetherWeFall.Skills
{
    /// <summary>
    /// What a skill fundamentally does. Supports change how much and how many;
    /// they never change this.
    /// </summary>
    public enum SkillEffectKind : byte
    {
        /// <summary>Travels, hits the first thing it touches.</summary>
        Projectile = 0,

        /// <summary>Instant, everything in an arc in front of the caster.</summary>
        MeleeArc = 1,

        /// <summary>Instant, everything within a radius of the aim point.</summary>
        AreaBurst = 2,

        /// <summary>Instant, the nearest target, then jumps onward.</summary>
        ChainBolt = 3,

        /// <summary>
        /// Stays where it was put, for seconds rather than an instant.
        ///
        /// The one effect that outlives its own cast. Everything above resolves
        /// and is gone; a wall of fire is still there when the next projectile
        /// crosses it, which is the entire point of it — a zone is a place where
        /// an element can be picked up as well as a place where damage happens.
        /// </summary>
        PersistentZone = 4,

        /// <summary>
        /// Steps behind a body, and behind the next one, and the next.
        ///
        /// Deals nothing of its own. Every blow it lands is whatever the primary
        /// key casts, cast from where the step ended — so a sword swings and a
        /// bow looses, each with its own supports, and this skill only decides
        /// WHERE. Its jumps are its chains, which is why the chain gem reads the
        /// same here as on a bolt: one more body, never one visited already.
        /// </summary>
        BlinkStrike = 5,

        /// <summary>
        /// A team spell on one ally: the one you face, else the nearest in
        /// reach, else yourself. Heals by its damage and applies its status.
        /// </summary>
        AllyTarget = 6,

        /// <summary>A team spell on every ally within its radius, the caster included.</summary>
        AllyAura = 7,

        // ─────────────────────────────────────────────────────────────────
        // Patterns. Each one is an effect above laid out in space or time —
        // Count copies, Interval apart — and resolves through the same queues,
        // so every support that reaches a projectile or a blast reaches these.
        // Appended, because the value is what an authored asset stores.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Count projectiles fanned evenly across ArcDegrees. Three hundred and
        /// sixty is a ring around the caster.
        /// </summary>
        Volley = 8,

        /// <summary>
        /// Count blasts marching out along the aim to Range, Interval apart,
        /// stopping at the first wall.
        /// </summary>
        Fissure = 9,

        /// <summary>
        /// Count blasts falling inside Scatter of the aim point, Interval apart.
        /// One with a long interval is a meteor.
        /// </summary>
        Rain = 10,

        /// <summary>
        /// The caster lands at the aim point, and a blast goes off where they
        /// land. Only a press moves the body; a triggered or echoed leap is the
        /// blast alone.
        /// </summary>
        LeapSlam = 11,

        /// <summary>
        /// Count full-circle blows around wherever the caster stands, Interval
        /// apart — they follow a moving caster.
        /// </summary>
        Cyclone = 12
    }

    /// <summary>
    /// The support gems, as a closed set.
    ///
    /// Each one is a number the cast fold reads, not a system of its own. See
    /// SkillDatabase.Resolve for why: a modifier that changes how many
    /// projectiles exist has nothing to say to a damage event, and a system per
    /// modifier would be a system that iterates nothing on almost every frame.
    /// </summary>
    public enum SkillModifierKind : byte
    {
        /// <summary>More damage, as a percentage, additive with other increases.</summary>
        IncreasedDamage = 0,

        /// <summary>Bigger radius, as a percentage.</summary>
        IncreasedArea = 1,

        /// <summary>Extra jumps to further targets after a hit.</summary>
        AddedChains = 2,

        /// <summary>A projectile splits into two more when it hits.</summary>
        Fork = 3,

        /// <summary>The whole skill is cast this many extra times.</summary>
        Multicast = 4,

        /// <summary>Damage becomes another element.</summary>
        ElementalConversion = 5,

        /// <summary>Anything killed by this skill bursts.</summary>
        ExplodeOnKill = 6,

        /// <summary>Faster projectiles, as a percentage.</summary>
        IncreasedProjectileSpeed = 7,

        /// <summary>
        /// When this skill lands, cast another one there.
        ///
        /// The one support that composes skills rather than tuning one. It fires
        /// once per effect instance — one projectile impact, one blast — and
        /// never once per body hit, or a burst that caught forty enemies would
        /// trigger forty times.
        /// </summary>
        TriggerOnHit = 8,

        /// <summary>
        /// The actives linked to this cast themselves when the world says so,
        /// and stop answering the key.
        ///
        /// The mirror of TriggerOnHit and the reason both are worth having: that
        /// one names a second skill and fires it where the first landed; this
        /// one names no skill at all and fires the ones already beside it. So it
        /// needs no skill reference, and it REPLACES the manual cast rather than
        /// adding to it — a gem that both answers the key and fires on kill is a
        /// skill going off twice for reasons the player cannot see.
        ///
        /// Its cooldown and its chance are not tuning. Without them "cast on
        /// kill" during a wave is a cast every frame.
        /// </summary>
        TriggerOnCondition = 9,

        // ─────────────────────────────────────────────────────────────────
        // The second batch. Appended rather than inserted, because the value
        // is what an authored asset stores — reshuffling this enum would make
        // every gem on disk mean something else.
        //
        // What they have in common is that each one is a number the fold
        // already had somewhere and nobody could reach: the cost of a press,
        // the cooldown, how long a zone lasts, how wide a multicast fans out.
        // A build is only interesting where it can pay for something, and
        // until these there was nothing to pay WITH.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Changes what one press costs, as a percentage. Negative makes it
        /// cheaper.
        ///
        /// The other half of every gem that adds damage. Mana exists and is
        /// spent, so this is the first support whose number is a price rather
        /// than a reward — and the reason two-sided gems are worth authoring.
        /// </summary>
        IncreasedManaCost = 10,

        /// <summary>
        /// Shortens the cooldown, as a percentage. Negative lengthens it.
        ///
        /// Not the same lever as attack speed, which is a stat on the sheet and
        /// applies to everything the character casts. This one belongs to one
        /// link group, so making a single skill fast is a decision with a cost
        /// instead of a number that lifts the whole bar.
        /// </summary>
        ReducedCooldown = 11,

        /// <summary>
        /// How long a persistent zone stays on the ground, as a percentage.
        ///
        /// The number increased area could not reach: a wider wall of fire and
        /// a longer-lasting one are different purchases, and borrowing one for
        /// the other was the thing this project already refused to do quietly.
        /// </summary>
        IncreasedDuration = 12,

        /// <summary>
        /// Replaces the status the supported skill applies.
        ///
        /// Overwrites rather than adds, exactly as an elemental conversion
        /// does, and for the same reason: a blow carries one named status, and
        /// a second one would have to be carried through the projectile, the
        /// blast and the hit — three structs, for a rule nobody can read off
        /// the tooltip anyway.
        ///
        /// It is the support that makes control a build rather than a property
        /// of whichever skill happened to be authored with it: a spark that
        /// roots is a spark that sets up the chain gem beside it.
        /// </summary>
        StatusOverride = 13,

        /// <summary>
        /// Widens the fan a multicast comes out in, as a percentage of the
        /// default nine degrees. Negative tightens it.
        ///
        /// Nothing on its own — it needs extra casts to spread — and that is
        /// the point: it turns Multicast into a choice between a shotgun and a
        /// spear rather than a flat "more of it".
        /// </summary>
        IncreasedSpread = 14,

        /// <summary>
        /// The projectile carries on through what it hits, this many more
        /// times.
        ///
        /// The third member of the family this pipeline already had two of.
        /// Chain jumps to a new body, fork splits into two, and pierce simply
        /// does not stop — so a line of enemies is worth arranging yourself in
        /// front of.
        /// </summary>
        Pierce = 15,

        /// <summary>
        /// Anything left under this fraction of its life by the blow dies
        /// outright. The value is a percentage.
        ///
        /// Resolved where health is, which is the only place that can see the
        /// fraction. It is deliberately a kill rather than extra damage: what
        /// makes it worth socketing is that it turns a crowd of nearly-dead
        /// bodies into corpses on the same frame, which is what every on-kill
        /// support in the game is waiting for.
        /// </summary>
        CullingStrike = 16,

        /// <summary>
        /// Gives the caster this much mana for every body this skill kills.
        ///
        /// The answer to the cost supports above, and the reason they are not
        /// simply a tax: a build that kills keeps casting, and a build that
        /// misses runs dry. Kill phase, like the explosion beside it.
        /// </summary>
        ManaOnKill = 17,

        /// <summary>
        /// Increased chance for this skill's blows to be critical, as a
        /// percentage of the character's own chance.
        ///
        /// A multiplier of a sheet stat rather than a flat chance, which is the
        /// same shape as increased damage — a character with no crit chance at
        /// all still crits never, however many of these are socketed, and
        /// finding the first item that grants some is what makes them worth
        /// anything.
        /// </summary>
        IncreasedCritChance = 18,

        /// <summary>
        /// Presses the key can store, this many more. The cooldown refills them
        /// one at a time.
        ///
        /// A count on the slot rather than a second cooldown: the timer still
        /// means "until the next press comes back", and a key with charges in
        /// hand simply does not have to wait for it.
        /// </summary>
        AddedCharges = 19,

        // ─────────────────────────────────────────────────────────────────
        // The third batch: the levers the pattern effects opened, and three
        // numbers the hit stage already carried and nothing could raise.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>This many more projectiles, blasts or pulses in a pattern.</summary>
        AddedCount = 20,

        /// <summary>
        /// The whole cast goes off again this many more times, a beat apart, from
        /// wherever the caster stands by then. Multicast in time rather than in
        /// space — one cooldown and one bill.
        /// </summary>
        Echo = 21,

        /// <summary>
        /// A projectile bursts on impact, at least this many metres across.
        /// Turns a single-target shot into one that area supports can grow.
        /// </summary>
        ImpactBurst = 22,

        /// <summary>Adds to what a critical blow multiplies by, as a percentage.</summary>
        IncreasedCritMultiplier = 23,

        /// <summary>Chains, and a blink's steps, reach further, as a percentage.</summary>
        IncreasedChainRange = 24,

        /// <summary>
        /// Every blow also carries ConvertTo, as if the effect had flown through
        /// it — the element half of a reaction arrives with the skill.
        /// </summary>
        InfuseElement = 25,

        /// <summary>A pattern's elements come this many percent faster. Negative slows them.</summary>
        PatternTempo = 26
    }

    /// <summary>Marks the entity holding the skill queues.</summary>
    public struct SkillEventsSingleton : IComponentData
    {
    }

    /// <summary>
    /// A client asking to cast.
    ///
    /// Carries where the player was and where they were pointing — never what it
    /// expects to happen. Cooldowns, damage and how many projectiles come out
    /// are all the host's answer, exactly as with interaction and equipping.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct SkillCastRequest : IBufferElementData
    {
        public int PlayerId;
        public int SlotIndex;
        public float3 Origin;
        public float3 Direction;

        /// <summary>Where the pointer is on the ground. Where an area burst lands.</summary>
        public float3 AimPoint;
    }

    /// <summary>
    /// One hotkey, and the socket it casts from.
    ///
    /// This is the skill bar from the gem model: a slot names a piece of gear
    /// and a hole in it, never a skill. What it casts is whatever active gem is
    /// sitting in that hole right now, looked up at the moment of casting.
    ///
    /// Deliberately no cached skill index. A cached one would be a second
    /// answer to "what does this key do", and the instant a gem is pulled out
    /// the two would disagree until somebody remembered to invalidate it. An
    /// emptied socket simply casts nothing, and needs no repair.
    ///
    /// The cooldown lives here rather than on the gem, because two characters
    /// casting the same skill are on their own timers — and because a gem moved
    /// to another weapon should not carry a spent cooldown with it.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct SkillSlot : IBufferElementData
    {
        /// <summary>The gear holding the socket, or Entity.Null for an empty key.</summary>
        public Entity Gear;

        public int SocketIndex;

        /// <summary>Seconds until the next spent charge comes back. Zero when none are spent.</summary>
        public float CooldownRemaining;

        /// <summary>Presses used and not yet refilled. The key answers while this is under Charges.</summary>
        public int ChargesSpent;

        /// <summary>
        /// How many presses the key stores, as the last cast folded it. Zero on
        /// a key never pressed, which counts as one.
        ///
        /// A cache, and only a gate: the fold is the answer and overwrites this
        /// on every cast. It exists so a held button on an empty key does not
        /// fold its supports every frame just to be told no.
        /// </summary>
        public int Charges;

        /// <summary>The cooldown each spent charge refills over, as the last cast folded it.</summary>
        public float Recharge;

        /// <summary>
        /// The refill is frozen. Raised by a blink for as long as it is still
        /// stepping, so its cooldown starts when the last blow has landed.
        /// </summary>
        public bool Held;

        public bool HasBinding => Gear != Entity.Null;
    }

    /// <summary>
    /// PRESENTATION — how many times this character's own buttons have produced
    /// a cast, and what kind the last one was.
    ///
    /// A counter rather than an event, for the animation that swings the arm:
    /// the VfxEvent queue is drained by the one presenter that draws it, and a
    /// second reader would be racing it. A number that only goes up can be read
    /// by anyone, late, and still show that something happened.
    ///
    /// Written where the cooldown is charged, so a press that did nothing plays
    /// nothing. Triggered casts do not count — nobody raised a hand for them.
    /// </summary>
    public struct CastCue : IComponentData
    {
        public uint Count;
        public SkillEffectKind Effect;

        /// <summary>
        /// Seconds until the same key can cast again — the cooldown that was
        /// just charged. What a swing has to fit inside, or a held button
        /// restarts it before the blade ever comes round.
        /// </summary>
        public float Interval;

        /// <summary>
        /// Seconds from the press to the blow landing, or zero for a skill that
        /// lands at once. The swing is paced so the blade connects then.
        /// </summary>
        public float StrikeDelay;

        /// <summary>
        /// Melee swings so far, the latest one included. Swings alternate the
        /// way they sweep, and this is the count they alternate on — so the
        /// host, the arm and the slash all read the same answer.
        /// </summary>
        public uint Swings;

        /// <summary>
        /// Whether a swing sweeps to the caster's right. Even swings do, odd
        /// swings come back. The controller files its swing clips by the same
        /// parity.
        /// </summary>
        public static bool SweepsRight(uint swing) => (swing & 1u) == 0u;
    }

    /// <summary>
    /// A swing that has been pressed and paid for but has not landed yet.
    ///
    /// A sword does not hit the instant the key goes down — the arm has to
    /// come round — and a blow that lands before the blade does reads as the
    /// animation lagging the game. So the host charges the cooldown and the
    /// mana at the press, as always, and holds the effect here for the wind-up.
    ///
    /// On the character rather than in a global queue, because it belongs to
    /// the one who swung: it lands where they stand when it lands, and it goes
    /// with them if they are ever removed.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct DelayedStrike : IBufferElementData
    {
        /// <summary>The skill as folded at the press. The build it was swung with is the build it lands with.</summary>
        public ResolvedSkill Skill;

        public int PlayerId;
        public float3 Origin;
        public float3 AimPoint;
        public float3 Direction;
        public KeystoneEffect Keystone;

        /// <summary>Which way the blade sweeps. See CastCue.SweepsRight.</summary>
        public bool SweepRight;

        /// <summary>
        /// How many triggers deep the cast behind this was. Zero for a press, and
        /// for every swing, pulse and echo a press queued. Carried so an echo of a
        /// triggered cast stays under the same depth rail.
        /// </summary>
        public int Depth;

        /// <summary>
        /// Lands where it was queued rather than following the caster. A press
        /// follows the one who pressed; a trigger stays where it was caused.
        /// </summary>
        public bool Anchored;

        /// <summary>Seconds until it lands.</summary>
        public float Remaining;
    }

    /// <summary>
    /// A blink strike still stepping from body to body.
    ///
    /// On the character and enableable, like the swing above it belongs to the
    /// one who pressed — and one at a time, because a body is only ever in one
    /// place. SkillCastSystem raises it and refuses every key while it is up;
    /// BlinkStrikeSystem steps, strikes and lowers it.
    /// </summary>
    public struct BlinkSequence : IComponentData, IEnableableComponent
    {
        public int PlayerId;

        /// <summary>The key that started it, whose refill is held until it ends.</summary>
        public int SlotIndex;

        /// <summary>The body to step behind next, when the cast already chose one. Null to search.</summary>
        public Entity Next;

        /// <summary>Where the character stands now: the press, then each landing.</summary>
        public float3 Position;

        public int JumpsRemaining;
        public int JumpsMade;

        /// <summary>How far from the last landing the next body may be.</summary>
        public float Reach;

        /// <summary>Seconds between steps.</summary>
        public float Interval;

        public float Timer;

        /// <summary>
        /// Bodies already struck. The same small list a chain carries, with the
        /// same ceiling: past seven a long sequence may revisit.
        /// </summary>
        public FixedList64Bytes<Entity> Visited;
    }

    // The starting loadout used to be a DefaultSkillSlot buffer here. Skills
    // live in gems now, so what a character can cast is decided by what is
    // socketed — and a second list saying otherwise would be a second answer to
    // the same question. What a character STARTS with is a list of items, and
    // that is StarterItem, beside the character sheet.

    /// <summary>
    /// A skill that something other than a player asked for.
    ///
    /// The queue a trigger support writes into. Deliberately a different type
    /// from SkillCastRequest even though the fields nearly match: a request
    /// comes from a client and is checked against a cooldown, while this comes
    /// from the host having already decided something happened. Merging them
    /// would mean one of the two paths carries a flag saying which it really is.
    ///
    /// Depth is the safety rail. A skill that triggers a skill that triggers the
    /// first one is a perfectly reasonable thing to author by accident, and
    /// without a budget it would fill the frame.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct PendingCast : IBufferElementData
    {
        public int SkillIndex;
        public int PlayerId;

        public float3 Origin;
        public float3 Direction;

        /// <summary>Fraction of the triggered skill's own damage. One is full.</summary>
        public float DamageScale;

        /// <summary>
        /// The body that caused this, when there was exactly one.
        ///
        /// A projectile knows precisely what it hit, and "the one I hit" is a
        /// different thing from "whatever is nearest to where it landed" — in a
        /// crowd those come apart, and a crowd is where this gets used. Null
        /// when the cause had no single target, such as a blast.
        /// </summary>
        public Entity PreferredTarget;

        /// <summary>How many triggers deep this already is. Zero is a player press.</summary>
        public int Depth;

        /// <summary>
        /// The gear and hole this cast came out of, or Null.
        ///
        /// Filled only by a condition trigger, which is the one cause that still
        /// knows: it decided to fire BECAUSE of what is in a particular socket,
        /// so the link group is right there. A projectile cannot fill this and
        /// deliberately does not try — it has been in the air for a second and
        /// remembering the weapon would mean the weapon could not be swapped
        /// while it flew.
        ///
        /// When it is here the fold gathers the group's supports, so an
        /// automatic cast is the same skill the key would have cast. When it is
        /// not, the triggered skill folds its innate supports alone, exactly as
        /// before.
        /// </summary>
        public Entity Gear;

        public int SocketIndex;
    }

    /// <summary>How far triggering is allowed to go. Baked beside the database.</summary>
    public struct SkillTriggerSettings : IComponentData
    {
        public int MaxDepth;
    }

    /// <summary>Prefabs the skill systems draw their pool from.</summary>
    public struct SkillPrefabs : IComponentData
    {
        public Entity Projectile;

        /// <summary>The patch of ground a persistent zone is made of.</summary>
        public Entity Zone;

        /// <summary>
        /// How many zones exist, ever. Far smaller than the projectile pool —
        /// zones last seconds and nobody casts dozens — but pooled for the same
        /// reason: nothing that happens in a fight should be a structural change.
        /// </summary>
        public int ZonePoolSize;

        /// <summary>
        /// How many projectiles exist, ever. Created once and reused; this is
        /// therefore also the hard ceiling on how many can be in the air at
        /// once, in the same way MaxAlive caps enemies.
        /// </summary>
        public int ProjectilePoolSize;
    }

    /// <summary>
    /// "This projectile is in the air."
    ///
    /// The pool marker. Projectiles are created once at startup and never
    /// destroyed: firing one raises this flag and retiring one lowers it, and
    /// neither is a structural change. Instantiate and Destroy are batched
    /// already, but each batch is still a sync point in a frame that has
    /// nothing else to sync for — and in a fight that is most frames.
    ///
    /// Every query that acts on projectiles filters by it, so an idle one is
    /// invisible to the simulation without anything asking what a pool is.
    /// </summary>
    public struct ProjectileActive : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// How much of the combat pipeline may be resolved in one frame.
    ///
    /// The same lever as MaxRequestsPerFrame in pathfinding, and for the same
    /// reason: several players emptying area skills into one crowd should cost
    /// more frames, not one very long one. Anything over budget stays in its
    /// queue and goes off next frame.
    /// </summary>
    public struct SkillBudgetSettings : IComponentData
    {
        public int MaxAreasPerFrame;
        public int MaxHitsPerFrame;
    }

    /// <summary>
    /// A projectile in flight.
    ///
    /// It carries everything needed to resolve its own impact, so nothing has to
    /// look back at the skill that fired it — which is what lets the skill be
    /// re-cast, changed or removed while its projectiles are still travelling.
    /// </summary>
    public struct SkillProjectile : IComponentData
    {
        public float3 Velocity;
        public float Damage;
        public DamageType Type;
        public int SourcePlayerId;

        /// <summary>Seconds left before it gives up.</summary>
        public float Lifetime;

        /// <summary>How close counts as a hit.</summary>
        public float HitRadius;

        /// <summary>Blast radius on impact. Zero means it hits one target.</summary>
        public float ImpactRadius;

        /// <summary>Splits left. Each split makes two projectiles with one fewer.</summary>
        public int ForksRemaining;

        /// <summary>
        /// The chance each body this strikes takes a critical blow, and what a
        /// critical blow multiplies by.
        ///
        /// Carried rather than spent at the cast, because a crit is a property
        /// of a BLOW and a projectile is not one blow: it forks, it pierces, it
        /// bursts. Rolling once at the press meant three forks all crit or all
        /// did not, which is a coin flip wearing a build as a costume.
        /// </summary>
        public float CritChance;

        public float CritMultiplier;

        /// <summary>
        /// Bodies this projectile may pass through before it stops.
        ///
        /// A count rather than a flag, so "pierces once" and "pierces the whole
        /// room" are the same support with a different number. Inherited by a
        /// fork for free, like everything else in this struct — a split that
        /// forgot how to pierce would be a fork gem quietly cancelling a pierce
        /// gem two holes away.
        ///
        /// It carries the same sharp edge LastHitTarget names, and for the same
        /// reason: only the body just passed through is remembered, so a
        /// projectile threading a dense pack can strike a neighbour and then
        /// that first body again. The fix is a list of everything pierced —
        /// which is the per-projectile FixedList the fork path already decided
        /// against.
        /// </summary>
        public int PiercesRemaining;

        /// <summary>
        /// The body this projectile last landed on, and the one its forks must
        /// not land on again.
        ///
        /// A fork is born at the impact point, which is by definition inside the
        /// hit radius of whatever was just hit — so without this it strikes the
        /// same enemy on its first frame, spends its last split and vanishes
        /// before travelling anywhere. Two forks appearing and dying on one
        /// frame at one spot is indistinguishable from a fork support that does
        /// nothing.
        ///
        /// Never cleared. A projectile travels in a straight line, so a body it
        /// has already passed through is not one it should meet again.
        /// </summary>
        public Entity LastHitTarget;

        /// <summary>Skill to cast where this lands, or -1. Set by a trigger support.</summary>
        public int TriggerSkillIndex;

        public float TriggerDamageScale;

        /// <summary>How many triggers deep the skill that fired this already was.</summary>
        public int TriggerDepth;

        /// <summary>
        /// Whether it landed on something or simply ran out of range. Kept here
        /// rather than on the spent flag so the job that retires a projectile
        /// touches one component instead of asking for the same one twice — once
        /// for its data and once for its enabled state.
        /// </summary>
        public bool HitSomething;

        public int ChainsRemaining;
        public float ChainRange;
        public float ChainDelay;

        public float ExplosionRadius;
        public float ExplosionDamage;

        /// <summary>
        /// Below this fraction of life, whatever this lands on simply dies.
        /// Zero for almost everything.
        /// </summary>
        public float CullThreshold;

        /// <summary>Mana the caster gets back for each body this kills.</summary>
        public float ManaOnKill;

        /// <summary>
        /// Elements picked up on the way here — a lightning bolt that crossed a
        /// wall of fire is carrying fire.
        ///
        /// A field on the projectile rather than a buffer of tags, and the fork
        /// is why: a fork copies this struct, so a split projectile inherits what
        /// its parent gathered for nothing. A buffer would have to be copied by
        /// hand at the one point on this path that has to stay cheap.
        ///
        /// Never cleared in flight. What a projectile picked up it carries to
        /// whatever it lands on; the pool clears it when the projectile is fired
        /// again, because activation writes this whole struct.
        /// </summary>
        public byte CarriedElements;

        /// <summary>
        /// A status this projectile applies to whatever it lands on, or None.
        ///
        /// Carried like everything else it needs to resolve its own impact, and
        /// inherited by a fork for free because a fork copies this struct — the
        /// same property that makes CarriedElements a field rather than a buffer.
        /// </summary>
        public StatusEffectType AppliedStatus;

        /// <summary>
        /// Which visual set follows this in the air, or zero.
        ///
        /// The one field in here that no system reads. It is carried because a
        /// projectile in flight is deliberately the only thing that knows where
        /// it is — the cast that fired it is over and may be over twice, since
        /// the weapon can be swapped mid-flight — so a trail that belongs to
        /// this projectile has to be findable from this projectile.
        ///
        /// Inherited by a fork for free, which is the right answer: a split
        /// fireball is still a fireball.
        /// </summary>
        public int VfxId;
    }

    /// <summary>
    /// "This projectile is finished." Enableable, so the job that detects
    /// impacts can retire one without a structural change mid-flight.
    ///
    /// A pure tag: where it stopped is its LocalTransform, and why it stopped is
    /// on SkillProjectile. Nothing here to read means the retiring job never
    /// needs both the value and the enabled state of the same component.
    /// </summary>
    public struct ProjectileSpent : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// One resolved hit, waiting to become damage.
    ///
    /// The stage exists because a hit is more than a number: it knows how many
    /// jumps are left and how long to wait before making them. Chain delay is
    /// not decoration — jumps that all land on the same frame read as one
    /// flash, and the player never sees the chain happen.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct PendingHit : IBufferElementData
    {
        public Entity Target;

        /// <summary>
        /// Which visual set draws the impact, or zero.
        ///
        /// Carried to the hit rather than looked up from the skill, for the
        /// reason the whole struct exists: by the time a hit lands, the cast is
        /// a frame or more gone and a chain's fourth jump has no idea what fired
        /// it. Copied by a jump, so a chain plays its impact once per body.
        /// </summary>
        public int VfxId;

        /// <summary>
        /// Where this hit lands. Carried rather than read off the target,
        /// because a chain whose next victim died during the jump delay still
        /// has to know where to look from.
        /// </summary>
        public float3 Origin;

        public float Damage;
        public DamageType Type;
        public int SourcePlayerId;

        public int ChainsRemaining;
        public float ChainRange;
        public float ChainDelay;

        /// <summary>
        /// Targets this chain has already jumped to, so it walks outward instead
        /// of bouncing between the same two enemies. Deliberately small: past
        /// its capacity a long chain may revisit, which is a better failure than
        /// allocating per hit.
        /// </summary>
        public FixedList64Bytes<Entity> Visited;

        /// <summary>Seconds until this hit lands.</summary>
        public float Delay;

        public float ExplosionRadius;
        public float ExplosionDamage;

        /// <summary>
        /// The fraction of life below which this blow finishes the target
        /// outright, or zero.
        ///
        /// Carried the same way the explosion is, and for the same reason: the
        /// stage that can answer "how much life is left" is the resolver, and
        /// by then the skill that decided this is long gone.
        /// </summary>
        public float CullThreshold;

        /// <summary>Mana the caster gets back if this hit kills.</summary>
        public float ManaOnKill;

        /// <summary>
        /// The chance THIS blow is critical, and what it multiplies by.
        ///
        /// Rolled in the hit stage, which is the only place that knows a blow
        /// is about to land on a particular body. A chain jump copies the whole
        /// hit, so every jump rolls its own — which is what makes a chain build
        /// and a crit build the same build rather than two.
        /// </summary>
        public float CritChance;

        public float CritMultiplier;

        /// <summary>Elements the effect that caused this hit was carrying.</summary>
        public byte CarriedElements;

        /// <summary>The status the effect that caused this hit applies, or None.</summary>
        public StatusEffectType AppliedStatus;

        /// <summary>
        /// Whether this hit is the work of a status or a reaction rather than a
        /// fresh blow. Carried into the damage event, where it stops the
        /// reaction from feeding itself.
        /// </summary>
        public bool FromReaction;

        /// <summary>A team spell landing on an ally. See DamageEvent.Supportive.</summary>
        public bool Supportive;

        /// <summary>
        /// Whether the cast behind this hit was fired by a trigger. Read by
        /// nothing in combat; carried so the damage meter can split a build into
        /// what the player pressed and what the build did on its own.
        /// </summary>
        public bool FromTrigger;
    }

    /// <summary>
    /// One area effect waiting to go off: an area burst, a melee swing, a
    /// projectile impact, or a corpse exploding.
    ///
    /// All four are the same thing — damage to everything inside a shape — so
    /// they share one queue and one system rather than four that would drift
    /// apart.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct PendingArea : IBufferElementData
    {
        public float3 Position;

        /// <summary>
        /// Which visual set draws the blows this blast produces, or zero.
        ///
        /// Only the impacts. The blast's own effect was announced by the cast,
        /// once, at the point it went off — a queue that also carries corpse
        /// explosions and zone pulses has no business announcing those as
        /// somebody's skill.
        /// </summary>
        public int VfxId;

        /// <summary>Facing, for arcs. Ignored when the arc is a full circle.</summary>
        public float3 Direction;

        public float Radius;

        /// <summary>
        /// Cosine of the half-angle. Minus one is a full circle, which is what
        /// every effect except a melee swing uses.
        /// </summary>
        public float ArcCosine;

        public float Damage;
        public DamageType Type;
        public int SourcePlayerId;

        public float Delay;

        public float ExplosionRadius;
        public float ExplosionDamage;

        /// <summary>
        /// Jumps handed to ONE body caught in this blast — the one nearest the
        /// centre — or zero.
        ///
        /// An area effect used to end every chain that reached it: a swing, a
        /// burst and a projectile that lands with an impact radius all produced
        /// hits with no jumps left, so a chain gem in the same group was a
        /// wasted hole that still drew a letter in the socket. Handing the
        /// chain to one victim rather than to all of them is what keeps that
        /// from turning a blast on a crowd into forty chains: one effect, one
        /// chain, however many bodies it touched.
        /// </summary>
        public int ChainsRemaining;

        public float ChainRange;
        public float ChainDelay;

        /// <summary>The fraction of life below which this blast finishes a body, or zero.</summary>
        public float CullThreshold;

        /// <summary>Mana the caster gets back per body this blast kills.</summary>
        public float ManaOnKill;

        /// <summary>
        /// Handed to every body the blast catches, and each of them rolls its
        /// own.
        ///
        /// One roll for the whole blast would make a crit build's damage swing
        /// between nothing and everything depending on a single number, which is
        /// the opposite of what a chance is for.
        /// </summary>
        public float CritChance;

        public float CritMultiplier;

        /// <summary>Skill to cast at the centre when this resolves, or -1.</summary>
        public int TriggerSkillIndex;

        public float TriggerDamageScale;
        public int TriggerDepth;

        /// <summary>Elements the effect that caused this blast was carrying.</summary>
        public byte CarriedElements;

        /// <summary>
        /// The status every body caught in this blast is marked with, or None.
        ///
        /// Passed through to each hit rather than applied here, so one blast
        /// that stuns forty enemies is forty ordinary applications — each one
        /// checked against that body's own immunity, which is the only place
        /// that check can be right.
        /// </summary>
        public StatusEffectType AppliedStatus;

        /// <summary>
        /// Whether this blast is itself a reaction. Passed on to every hit it
        /// produces, so a reaction cannot walk across a crowd.
        /// </summary>
        public bool FromReaction;

        /// <summary>
        /// Whether to skip announcing this one to the presentation layer.
        ///
        /// For the pulses of a persistent zone, which are the only area effects
        /// that repeat on a timer. An explosion is announced with a ring, a
        /// camera shake and a hit-stop, which is right for one blast and wrong
        /// for something that happens twice a second for six seconds — the
        /// camera would never settle and time would never come back to one. And
        /// there is nothing to draw anyway: the zone is a disc already on the
        /// screen, burning, for the whole of its life.
        ///
        /// The default is to announce, because everything that existed before
        /// zones did should keep announcing without being told to.
        /// </summary>
        public bool Silent;

        /// <summary>
        /// Whether this blast plays its skill's cast effect where and when it
        /// goes off.
        ///
        /// For the elements of a pattern and the landing of a leap: the cast
        /// announcement happens once, at the press, and a meteor that lands half
        /// a second later somewhere else has to be seen landing there.
        /// </summary>
        public bool CastVfx;
    }
}
