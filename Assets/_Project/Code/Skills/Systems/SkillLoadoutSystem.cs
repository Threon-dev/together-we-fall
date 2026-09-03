using Unity.Entities;
using TogetherWeFall.Player;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Gives a fresh character the starting loadout.
    ///
    /// Its own pass rather than part of character creation because the skill
    /// database arrives in a SubScene, and SubScenes load asynchronously — a
    /// character can easily exist a few frames before there is a loadout to give
    /// it. Filling empty loadouts every frame costs one query that matches
    /// nothing once everyone is armed.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(TogetherWeFall.Player.Systems.PlayerCharacterRegistrySystem))]
    public partial struct SkillLoadoutSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DefaultSkillSlot>();
            state.RequireForUpdate<PlayerCharacter>();
        }

        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<DefaultSkillSlot> defaults =
                SystemAPI.GetSingletonBuffer<DefaultSkillSlot>(isReadOnly: true);

            if (defaults.Length == 0)
                return;

            foreach (DynamicBuffer<SkillSlot> slots in
                     SystemAPI.Query<DynamicBuffer<SkillSlot>>().WithAll<PlayerCharacter>())
            {
                // An empty loadout is the only signal that a character has not
                // been armed yet. A character who deliberately unequips every
                // skill will need a flag; nothing can do that today.
                if (slots.Length > 0)
                    continue;

                for (int i = 0; i < defaults.Length; i++)
                {
                    slots.Add(new SkillSlot
                    {
                        SkillIndex = defaults[i].SkillIndex,
                        CooldownRemaining = 0f
                    });
                }
            }
        }
    }
}
