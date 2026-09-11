using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using TogetherWeFall.Combat;
using TogetherWeFall.Enemies;

namespace TogetherWeFall.Vfx.Systems
{
    /// <summary>
    /// Washes a body in the colour of whatever is on it.
    ///
    /// The cheapest readable feedback this project can afford, and the only one
    /// that scales: a wave of a hundred enemies where the rooted ones are grey
    /// is legible at a glance from a top-down camera, where a hundred little
    /// icons are not. Icons say what exactly; the colour says which bodies to
    /// look at, and in a crowd that is the question worth answering first.
    ///
    /// It writes the same per-instance colour override the death fade writes,
    /// and never at the same time as it: a fading body has already had its enemy
    /// tag switched off, so the two never see one entity. The rest colour comes
    /// from EnemySpawnDefaults, which the pool already keeps so a reused body
    /// knows what it looked like before anything happened to it — so restoring
    /// costs nothing and needs nothing new remembered.
    ///
    /// Training dummies are deliberately out. They paint their own colour when
    /// struck, and two writers of one field is the thing this project does not
    /// do; they have no spawn defaults, so the query excludes them by itself.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TogetherWeFall.Combat.Systems.StatusTickSystem))]
    public partial struct StatusTintSystem : ISystem
    {
        /// <summary>
        /// How much of the status colour shows through. Enough to read across a
        /// crowd, not so much that every enemy under a debuff becomes a
        /// silhouette the player cannot tell apart from another kind of enemy.
        /// </summary>
        private const float Blend = 0.65f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemyTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new TintJob().ScheduleParallel();
        }

        [BurstCompile]
        [WithAll(typeof(EnemyTag))]
        private partial struct TintJob : IJobEntity
        {
            private void Execute(
                ref URPMaterialPropertyBaseColor color,
                in StatusVisual visual,
                in EnemySpawnDefaults defaults)
            {
                // Written every frame, including the frame after the last status
                // ended. The alternative — writing only on a change — would mean
                // remembering whether the body is currently tinted, which is a
                // second copy of what the visual already says, and the failure
                // would be an enemy that stays blue forever.
                color.Value = visual.HasTint
                    ? math.lerp(defaults.BodyColor, visual.Tint, Blend)
                    : defaults.BodyColor;
            }
        }
    }
}
