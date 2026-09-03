using System;
using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace TogetherWeFall.Dungeon
{
    /// <summary>
    /// Bakes the navmesh over the freshly built dungeon geometry, off the main
    /// thread.
    ///
    /// The surface must collect its children only. Collecting the whole scene
    /// would pull in the player capsule and every enemy collider, carving holes
    /// into the navmesh around whatever happened to be standing there when the
    /// bake ran.
    ///
    /// A note on the API: com.unity.ai.navigation 2.0 has no BuildNavMeshAsync.
    /// The asynchronous path is UpdateNavMesh, which returns an AsyncOperation
    /// and needs an existing NavMeshData to write into — so an empty one is
    /// created and registered first, and the bake fills it. BuildNavMesh, the
    /// method that does create data on its own, is fully synchronous and would
    /// freeze the frame for as long as the bake takes.
    /// </summary>
    public sealed class DungeonNavMeshBaker
    {
        /// <summary>
        /// Runs the bake. Reports success rather than throwing, because the
        /// caller has something useful to do on failure: leave the run in its
        /// generating phase so no enemy system starts on a floor with no
        /// navmesh under it.
        /// </summary>
        public IEnumerator BakeAsync(NavMeshSurface surface, Action<bool> onCompleted)
        {
            if (surface == null)
            {
                Debug.LogError(
                    $"[{nameof(DungeonNavMeshBaker)}] No NavMeshSurface assigned — " +
                    "enemies would have no surface to walk on.");
                onCompleted?.Invoke(false);
                yield break;
            }

            if (surface.navMeshData == null)
            {
                surface.navMeshData = new NavMeshData(surface.agentTypeID);

                // Register the (still empty) data with the navigation system
                // before filling it. Doing it afterwards would leave a window in
                // which the bake is finished but nothing queryable exists.
                surface.AddData();
            }

            AsyncOperation operation = surface.UpdateNavMesh(surface.navMeshData);
            if (operation == null)
            {
                Debug.LogError(
                    $"[{nameof(DungeonNavMeshBaker)}] The navmesh bake did not start. " +
                    "Check that the surface has geometry among its children and that " +
                    "Use Geometry is set to Physics Colliders.");
                onCompleted?.Invoke(false);
                yield break;
            }

            yield return operation;

            onCompleted?.Invoke(true);
        }
    }
}
