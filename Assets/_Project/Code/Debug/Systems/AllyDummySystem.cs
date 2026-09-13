using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using TogetherWeFall.Combat;
using TogetherWeFall.Player;
using TogetherWeFall.Player.Systems;
using TogetherWeFall.Shared;
using TogetherWeFall.Skills.Systems;

namespace TogetherWeFall.DebugTools.Systems
{
    /// <summary>
    /// Makes each ally dummy a player, keeps it wounded, and draws its
    /// character's heals and buffs on its body.
    ///
    /// Publishing the position is all it takes to become a player: the
    /// registry sees an id in the buffer and makes a character. Right after the
    /// registry, so the character is marked as kitted on the frame it is born —
    /// a dummy that took a starter kit would hold a dozen items out of the pool.
    ///
    /// It writes the character's health directly, which only a debug system may:
    /// the training dummy does the same in the other direction. A heal still goes
    /// through the resolver; this only takes it back down, slowly.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(PlayerCharacterRegistrySystem))]
    public partial struct AllyDummySystem : ISystem
    {
        /// <summary>How far a status tint pulls the body colour. The enemy tint's number.</summary>
        private const float TintBlend = 0.65f;

        private EntityQuery _dummyQuery;
        private EntityQuery _characterQuery;

        public void OnCreate(ref SystemState state)
        {
            _dummyQuery = SystemAPI.QueryBuilder()
                .WithAll<AllyDummy, LocalTransform>()
                .Build();

            _characterQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerCharacter, Health>()
                .Build();

            state.RequireForUpdate(_dummyQuery);
            state.RequireForUpdate<PlayerPositionsSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            EntityManager entityManager = state.EntityManager;

            using NativeArray<Entity> dummies = _dummyQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<Entity> characters = _characterQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PlayerCharacter> sheets =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);
            using var unkitted = new NativeList<Entity>(dummies.Length, Allocator.Temp);

            DynamicBuffer<PlayerPositionElement> players =
                SystemAPI.GetSingletonBuffer<PlayerPositionElement>();

            for (int i = 0; i < dummies.Length; i++)
            {
                Entity body = dummies[i];
                AllyDummy dummy = entityManager.GetComponentData<AllyDummy>(body);

                Publish(players, dummy.PlayerId, entityManager.GetComponentData<LocalTransform>(body).Position);

                Entity character = Find(characters, sheets, dummy.PlayerId);
                if (character == Entity.Null)
                    continue;

                if (!entityManager.HasComponent<StarterKitGranted>(character))
                    unkitted.Add(character);

                Wound(entityManager, character, ref dummy, deltaTime);
                MirrorFeedback(entityManager, character, body);
                Tint(entityManager, character, body, dummy);

                entityManager.SetComponentData(body, dummy);
            }

            // Structural, so last: nothing above survives it.
            for (int i = 0; i < unkitted.Length; i++)
                entityManager.AddComponent<StarterKitGranted>(unkitted[i]);
        }

        private static void Publish(DynamicBuffer<PlayerPositionElement> players, int playerId, float3 position)
        {
            var element = new PlayerPositionElement
            {
                PlayerId = playerId,
                Position = position,
                IsTargetable = true
            };

            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].PlayerId != playerId)
                    continue;

                players[i] = element;
                return;
            }

            players.Add(element);
        }

        private static Entity Find(NativeArray<Entity> characters, NativeArray<PlayerCharacter> sheets, int playerId)
        {
            for (int i = 0; i < sheets.Length; i++)
            {
                if (sheets[i].PlayerId == playerId)
                    return characters[i];
            }

            return Entity.Null;
        }

        private static void Wound(EntityManager entityManager, Entity character, ref AllyDummy dummy, float deltaTime)
        {
            Health health = entityManager.GetComponentData<Health>(character);

            // No sheet yet: PlayerResourceSystem has not sized the pool.
            if (health.Max <= 0f)
                return;

            float floor = health.Max * dummy.WoundedFraction;

            if (!dummy.Primed)
            {
                dummy.Primed = true;
                health.Current = math.min(health.Current, floor);
            }
            else if (health.Current > floor)
            {
                health.Current = math.max(floor, health.Current - health.Max * dummy.DrainPerSecond * deltaTime);
            }

            entityManager.SetComponentData(character, health);
        }

        /// <summary>
        /// Moves the character's raised feedback onto the body, where
        /// DamageNumberSystem can find a position to draw it at. One frame late,
        /// which nobody can see.
        /// </summary>
        private static void MirrorFeedback(EntityManager entityManager, Entity character, Entity body)
        {
            if (!entityManager.HasComponent<DamageFeedback>(character) ||
                !entityManager.IsComponentEnabled<DamageFeedback>(character))
            {
                return;
            }

            entityManager.SetComponentData(body, entityManager.GetComponentData<DamageFeedback>(character));
            entityManager.SetComponentEnabled<DamageFeedback>(body, true);
            entityManager.SetComponentEnabled<DamageFeedback>(character, false);
        }

        /// <summary>Washes the body in the loudest status on the character — gold while empowered.</summary>
        private static void Tint(EntityManager entityManager, Entity character, Entity body, in AllyDummy dummy)
        {
            if (!entityManager.HasComponent<StatusVisual>(character))
                return;

            StatusVisual visual = entityManager.GetComponentData<StatusVisual>(character);

            entityManager.SetComponentData(body, new URPMaterialPropertyBaseColor
            {
                Value = visual.HasTint
                    ? math.lerp(dummy.RestColor, visual.Tint, TintBlend)
                    : dummy.RestColor
            });
        }
    }
}
