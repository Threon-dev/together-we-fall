using Unity.Entities;
using Unity.NetCode;

namespace TogetherWeFall.Network.Systems
{
    /// <summary>
    /// Makes every ghost instance live for the session instead of for the scene
    /// its prefab was baked in.
    ///
    /// EntityManager.Instantiate copies every component but Prefab — SceneTag
    /// included — so an instance of a SubScene prefab is destroyed with that
    /// SubScene. Walking from the lobby into the dungeon would take the host's
    /// avatars, characters, bags and pools down with the lobby (the pools are
    /// made once per session and would never come back), and on a client it
    /// deletes ghost copies behind Netcode's back, which it reports as "delete
    /// ghosts on the client" and resyncs from scratch.
    ///
    /// Ghosts only. Baked scene entities — NPCs, the portal, databases — are not
    /// ghosts, keep their tag and still leave with their scene, as they should.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct GhostSceneDetachSystem : ISystem
    {
        private EntityQuery _attachedQuery;

        public void OnCreate(ref SystemState state)
        {
            _attachedQuery = SystemAPI.QueryBuilder()
                .WithAll<GhostInstance, SceneTag>()
                .Build();

            state.RequireForUpdate(_attachedQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.RemoveComponent(
                _attachedQuery, new ComponentTypeSet(typeof(SceneTag), typeof(SceneSection)));
        }
    }
}
