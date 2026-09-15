using Unity.Entities;
using TogetherWeFall.Player;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Gives a fresh character an empty skill bar.
    ///
    /// It hands out keys, not skills. Since skills live in gems, what a key
    /// casts is decided by socketing something and binding the key to it — and
    /// a bar handed out pre-filled would be a second source of truth about
    /// where a skill comes from.
    ///
    /// The number of keys is fixed and matches the four the input reader binds.
    /// Tying it to character progression is a decision for when there is any.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(TogetherWeFall.Player.Systems.PlayerCharacterRegistrySystem))]
    public partial struct SkillLoadoutSystem : ISystem
    {
        /// <summary>How many hotkeys there are. PlayerInputReader binds exactly these.</summary>
        private const int BarSlotCount = 4;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerCharacter>();
        }

        public void OnUpdate(ref SystemState state)
        {
            foreach (DynamicBuffer<SkillSlot> slots in
                     SystemAPI.Query<DynamicBuffer<SkillSlot>>().WithAll<PlayerCharacter>())
            {
                // A bar with no rows is the only signal that a character has not
                // been given one yet. Once it has, every row is a key that may
                // legitimately be bound to nothing.
                if (slots.Length > 0)
                    continue;

                for (int i = 0; i < BarSlotCount; i++)
                {
                    slots.Add(new SkillSlot
                    {
                        Gear = Entity.Null,
                        SocketIndex = 0,
                        CooldownRemaining = 0f
                    });
                }
            }
        }
    }
}
