using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Skills
{
    /// <summary>
    /// A patch of ground that is on fire, or frozen, or poisoned.
    ///
    /// The only effect in the game that outlives the cast that made it, and the
    /// reason it exists: an element has to be somewhere for a projectile to fly
    /// through it. Everything else in the pipeline resolves and is gone within a
    /// frame or two, which leaves nothing to cross.
    ///
    /// It does two jobs and they are deliberately the same shape as everything
    /// else. It pulses damage into the ordinary area queue, so a zone has no
    /// privileged route to hurting anything and its pulses can be reacted with
    /// like any other blow; and it hands its element to projectiles that pass
    /// through, which is the whole of the second half of this feature.
    /// </summary>
    public struct ElementZone : IComponentData
    {
        public DamageType Element;

        public float Radius;

        public float RemainingDuration;

        /// <summary>Seconds between damage pulses. Zero for a zone that only tags.</summary>
        public float TickInterval;

        public float TickRemaining;

        /// <summary>Damage of one pulse, already folded through the supports that made it.</summary>
        public float Damage;

        public int SourcePlayerId;

        // ─────────────────────────────────────────────────────────────────
        // What the cast decided and the pulse has to carry.
        //
        // A zone used to keep four numbers and drop the rest of the fold on the
        // floor, which made a socket full of kill-phase and status gems do
        // nothing at all on the two zone skills — silently, which is the worst
        // way for a gem to fail. These are the same fields a projectile already
        // carried for the same reason: the thing that resolves the blow is a
        // long way from the cast that decided what the blow means.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>The status every pulse marks what it catches with, or None.</summary>
        public StatusEffectType AppliedStatus;

        public float ExplosionRadius;
        public float ExplosionDamage;

        /// <summary>Fraction of life below which a pulse finishes a body, or zero.</summary>
        public float CullThreshold;

        /// <summary>Mana the caster gets back per body a pulse kills.</summary>
        public float ManaOnKill;

        /// <summary>The chance each pulse's blow is critical, and its multiplier.</summary>
        public float CritChance;

        public float CritMultiplier;

        /// <summary>
        /// Skill this zone casts, or -1 — and it fires on the FIRST pulse only.
        ///
        /// A trigger goes off once per effect instance, and a zone is one
        /// effect that happens to last six seconds. Firing per pulse would make
        /// the number of triggered casts a function of how long the zone burns,
        /// which is the same property the rule refuses for a blast that catches
        /// forty bodies.
        /// </summary>
        public int TriggerSkillIndex;

        public float TriggerDamageScale;
        public int TriggerDepth;
    }

    /// <summary>
    /// "This zone is burning."
    ///
    /// The pool marker, the same shape as ProjectileActive and EnemyTag: an idle
    /// zone is one with this flag down, found by a query rather than kept in a
    /// list somebody has to keep in step.
    /// </summary>
    // The enabled bit is replicated: a client draws, counts and hides by it.
    [GhostEnabledBit]
    public struct ZoneActive : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// One zone waiting to be put on the ground.
    ///
    /// The same shape as ProjectileSpawn, because it is the same story: the cast
    /// describes what it wants, and the pool is asked for something to put it in.
    /// </summary>
    public struct ZoneSpawn
    {
        /// <summary>How far below the world an idle zone waits, out of the frustum.</summary>
        public const float ParkDepth = -1000f;

        public float3 Position;
        public ElementZone Zone;

        /// <summary>
        /// Puts as many zones on the ground as the pool can supply, and returns
        /// how many that was.
        ///
        /// Nothing is created. Running out is not an error — the pool size is the
        /// ceiling on zones burning at once, in the way the projectile pool caps
        /// projectiles, and a zone that does not fit is one nobody was going to
        /// notice under the ones that did.
        /// </summary>
        public static int ActivateAll(
            EntityManager entityManager,
            in NativeArray<Entity> free,
            NativeList<ZoneSpawn> spawns)
        {
            int count = math.min(free.Length, spawns.Length);

            for (int i = 0; i < count; i++)
            {
                Entity zone = free[i];

                LocalTransform transform = entityManager.GetComponentData<LocalTransform>(zone);
                transform.Position = spawns[i].Position;

                // The prefab is a disc one unit across, so the scale IS the
                // diameter. Uniform, because that is what LocalTransform carries;
                // the prefab is flattened by a baked non-uniform scale of its
                // own, which rides along untouched.
                transform.Scale = math.max(0.1f, spawns[i].Zone.Radius * 2f);

                entityManager.SetComponentData(zone, transform);
                entityManager.SetComponentData(zone, spawns[i].Zone);

                entityManager.SetComponentData(zone, new URPMaterialPropertyBaseColor
                {
                    Value = DamageTypePalette.For(spawns[i].Zone.Element)
                });

                entityManager.SetComponentEnabled<ZoneActive>(zone, true);
            }

            return count;
        }

        /// <summary>Takes burnt-out zones back into the pool. Not a structural change.</summary>
        public static void Release(EntityManager entityManager, in NativeArray<Entity> spent)
        {
            for (int i = 0; i < spent.Length; i++)
            {
                LocalTransform transform =
                    entityManager.GetComponentData<LocalTransform>(spent[i]);

                transform.Position = new float3(0f, ParkDepth, 0f);
                entityManager.SetComponentData(spent[i], transform);

                entityManager.SetComponentEnabled<ZoneActive>(spent[i], false);
            }
        }
    }
}
