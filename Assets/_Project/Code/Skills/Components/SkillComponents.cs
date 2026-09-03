using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Combat;

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
        ChainBolt = 3
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
        TriggerOnHit = 8
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
    /// One skill a character can cast, and when it is next available.
    ///
    /// Cooldown lives per slot rather than per skill definition, because two
    /// characters casting the same skill are on their own timers.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct SkillSlot : IBufferElementData
    {
        /// <summary>Index into the skill database, or Empty.</summary>
        public int SkillIndex;

        public float CooldownRemaining;

        public const int Empty = -1;

        public bool HasSkill => SkillIndex != Empty;
    }

    /// <summary>The loadout a character starts with, baked beside the skill database.</summary>
    [InternalBufferCapacity(4)]
    public struct DefaultSkillSlot : IBufferElementData
    {
        public int SkillIndex;
    }

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

        /// <summary>Skill to cast at the centre when this resolves, or -1.</summary>
        public int TriggerSkillIndex;

        public float TriggerDamageScale;
        public int TriggerDepth;
    }
}
