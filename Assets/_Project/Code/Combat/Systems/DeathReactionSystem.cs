using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using TogetherWeFall.Enemies;
using TogetherWeFall.Skills;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.Combat.Systems
{
    /// <summary>
    /// Decides what a death causes, then starts the body on its way out.
    ///
    /// This is the reason the damage pipeline is split the way it is. An
    /// explosion on kill is not a special case wired into the skill that fired:
    /// the killing blow recorded a radius, this reads it, and the burst goes
    /// into the same area queue everything else uses.
    ///
    /// Except that a blast does NOT cascade, on purpose: an explosion is queued
    /// with its own explosion radius cleared. Without that one line, a single
    /// kill with the support socketed would detonate the entire floor and never
    /// stop.
    ///
    /// The body is not destroyed here. It stops being an enemy — the tag goes
    /// down, so nothing chases, targets or counts it any more — and then fades
    /// out over a moment. What kills a wave should look like a wave dying.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DamageResolutionSystem))]
    public partial struct DeathReactionSystem : ISystem
    {
        /// <summary>
        /// A beat between the kill and the burst. Simultaneous reads as one
        /// event; a fraction of a second reads as a consequence.
        /// </summary>
        private const float ExplosionDelay = 0.06f;

        private EntityQuery _deadQuery;

        public void OnCreate(ref SystemState state)
        {
            // Dead is enableable, so this holds the ones that died. WithDisabled
            // on the fade is what makes it hold the ones that died THIS frame:
            // a body already fading has had its reaction and must not have
            // another.
            _deadQuery = SystemAPI.QueryBuilder()
                .WithAll<Dead, LocalTransform, URPMaterialPropertyBaseColor>()
                .WithDisabled<DeathFade>()
                .Build();

            state.RequireForUpdate<SkillEventsSingleton>();
            state.RequireForUpdate(_deadQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            using NativeArray<Entity> dead = _deadQuery.ToEntityArray(Allocator.Temp);
            if (dead.Length == 0)
                return;

            using NativeArray<Dead> info = _deadQuery.ToComponentDataArray<Dead>(Allocator.Temp);
            using NativeArray<LocalTransform> transforms =
                _deadQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using NativeArray<URPMaterialPropertyBaseColor> colors =
                _deadQuery.ToComponentDataArray<URPMaterialPropertyBaseColor>(Allocator.Temp);

            QueueExplosions(ref state, info, transforms);
            AnnounceDeaths(ref state, transforms, colors);

            RefRW<CombatTally> tally = SystemAPI.GetSingletonRW<CombatTally>();
            tally.ValueRW.Kills += dead.Length;

            BeginFades(ref state, dead, transforms, colors);
        }

        private void QueueExplosions(
            ref SystemState state,
            in NativeArray<Dead> info,
            in NativeArray<LocalTransform> transforms)
        {
            DynamicBuffer<PendingArea> areas = SystemAPI.GetSingletonBuffer<PendingArea>();

            for (int i = 0; i < info.Length; i++)
            {
                if (info[i].ExplosionRadius <= 0f)
                    continue;

                areas.Add(new PendingArea
                {
                    Position = transforms[i].Position,
                    Direction = new float3(0f, 0f, 1f),
                    Radius = info[i].ExplosionRadius,

                    // A full circle: a corpse has no facing worth respecting.
                    ArcCosine = -1f,
                    Damage = info[i].ExplosionDamage,
                    Type = info[i].Type,
                    SourcePlayerId = info[i].KilledByPlayerId,
                    Delay = ExplosionDelay,

                    // The line that stops the floor from detonating itself.
                    // Anything this blast kills dies quietly.
                    ExplosionRadius = 0f,
                    ExplosionDamage = 0f,

                    // And triggers nothing. Zero is a real skill index, so this
                    // has to be said rather than left at its default — otherwise
                    // every corpse would cast the first skill in the database.
                    TriggerSkillIndex = -1
                });
            }
        }

        /// <summary>
        /// Tells the presentation layer that bodies fell, in one event carrying
        /// how many.
        ///
        /// Two hundred deaths in a frame were two hundred events until this was
        /// a count. Nothing downstream wanted them separately — the camera adds
        /// a knock per body and the overlay counts them for the mass-kill window,
        /// and both do arithmetic that a number does just as well. The bodies
        /// themselves are already being drawn, fading, one per corpse.
        ///
        /// What that looks like is none of this system's business — it does not
        /// know whether anything is even drawing.
        /// </summary>
        private void AnnounceDeaths(
            ref SystemState state,
            in NativeArray<LocalTransform> transforms,
            in NativeArray<URPMaterialPropertyBaseColor> colors)
        {
            if (transforms.Length == 0)
                return;

            DynamicBuffer<VfxEvent> events = SystemAPI.GetSingletonBuffer<VfxEvent>();

            events.Add(new VfxEvent
            {
                Kind = VfxEventKind.Death,

                // The first body stands for the group. Nothing reads it yet;
                // when something does, a crowd wiped out at once has no one
                // position anyway.
                Position = transforms[0].Position,
                Color = colors[0].Value,
                Magnitude = transforms.Length
            });
        }

        /// <summary>
        /// Retires the body: no longer an enemy, and fading from whatever it
        /// looked like at the moment it died.
        ///
        /// None of this is a structural change, so a hundred deaths in one frame
        /// cost a hundred component writes rather than a hundred archetype moves.
        /// </summary>
        private void BeginFades(
            ref SystemState state,
            in NativeArray<Entity> dead,
            in NativeArray<LocalTransform> transforms,
            in NativeArray<URPMaterialPropertyBaseColor> colors)
        {
            for (int i = 0; i < dead.Length; i++)
            {
                DeathFade fade = state.EntityManager.GetComponentData<DeathFade>(dead[i]);

                fade.Remaining = fade.Duration;
                fade.StartScale = transforms[i].Scale;
                fade.StartColor = colors[i].Value;

                state.EntityManager.SetComponentData(dead[i], fade);
                state.EntityManager.SetComponentEnabled<DeathFade>(dead[i], true);

                // Stops being an enemy here. Everything that chases, targets,
                // counts or waits on enemies filters by this tag, so one write
                // takes the body out of all of it.
                state.EntityManager.SetComponentEnabled<EnemyTag>(dead[i], false);
            }
        }
    }
}
