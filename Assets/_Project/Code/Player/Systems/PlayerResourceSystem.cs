using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;
using TogetherWeFall.Equipment.Systems;

namespace TogetherWeFall.Player.Systems
{
    /// <summary>
    /// Keeps the two pools in step with the sheet, and refills the one that
    /// refills.
    ///
    /// One system for both, rather than one each, because they are the same
    /// three lines of work: read the ceiling off the stats, clamp what is left
    /// to it, and — for mana — put a little back. Two systems would be two
    /// places to notice that the ceiling moved.
    ///
    /// It does NOT write health except to clamp it. The damage resolver is the
    /// only thing that subtracts health anywhere in this project, and that stays
    /// true for the player: this system moves the ceiling and lets Current
    /// follow it, which is a different operation from taking damage and must
    /// stay a different one.
    ///
    /// After PlayerStatsSystem, because the ceiling it reads is what that system
    /// has just recomputed. A frame's lag would show as a bar that is briefly
    /// the wrong length every time a ring is swapped, which is exactly the kind
    /// of thing nobody can reproduce on purpose.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerStatsSystem))]
    public partial struct PlayerResourceSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new FollowStatsJob { DeltaTime = SystemAPI.Time.DeltaTime }.ScheduleParallel();
        }

        /// <summary>
        /// Every frame rather than on the dirty flag, and that is deliberate.
        ///
        /// The stats behind it are cached precisely because recomputing them is
        /// expensive; this is two clamps and an add, over at most four
        /// characters. Hanging it off StatsDirty would mean mana regenerated
        /// only on the frames somebody changed a ring.
        /// </summary>
        [BurstCompile]
        private partial struct FollowStatsJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                in PlayerStats stats, ref Health health, ref Mana mana)
            {
                float maxHealth = stats.Final.Get(StatKind.MaxHealth);
                float maxMana = stats.Final.Get(StatKind.MaxMana);

                // A character in its first frame has an uncomputed sheet, and a
                // ceiling of zero would empty both pools before anything had a
                // chance to fill them. Leaving the pools alone until the sheet
                // is real is the honest answer — the alternative is a bar that
                // flashes empty on spawn for one frame.
                if (maxHealth <= 0f)
                    return;

                bool firstSheet = health.Max <= 0f;
                health.Max = maxHealth;

                // Born full, and topped up only then. A player who picks up a
                // chestplate mid-fight gets a bigger bar, not a free heal —
                // that is a healing effect, and healing is not a thing this
                // project has yet.
                health.Current = firstSheet
                    ? maxHealth
                    : math.min(health.Current, maxHealth);

                if (maxMana <= 0f)
                {
                    mana.Current = 0f;
                    return;
                }

                float regen = stats.Final.Get(StatKind.ManaRegen);

                mana.Current = firstSheet
                    ? maxMana
                    : math.min(mana.Current + regen * DeltaTime, maxMana);
            }
        }
    }
}
