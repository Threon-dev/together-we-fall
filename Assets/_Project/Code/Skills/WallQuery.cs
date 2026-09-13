using Unity.Mathematics;
using UnityEngine;

namespace TogetherWeFall.Skills
{
    /// <summary>
    /// What stops a projectile: the level's colliders.
    ///
    /// PhysX rather than a grid of our own, because the levels already are
    /// colliders — a dungeon wall, an arena obstacle, anything placed by hand is
    /// a box with one, and the navmesh is baked from the very same boxes. So
    /// "is there a wall between here and there" is answered by the geometry
    /// everything else already agrees on, in every scene, with nothing to
    /// rebuild when a floor is generated.
    ///
    /// Main thread only, like all of PhysX. Both callers are on it: the flight
    /// job is Run after the walls are found, and a cast is main-thread work.
    ///
    /// Nothing that moves has a collider that should stop a bolt. Enemies are
    /// entities with none, the NPCs and dummies lose theirs at build time, and
    /// the player's CharacterController is on the Character layer the mask
    /// leaves out.
    /// </summary>
    public static class WallQuery
    {
        /// <summary>SceneBuildUtility.CharacterLayer; the editor assembly is out of reach from here.</summary>
        private const string CharacterLayer = "Character";

        /// <summary>
        /// How far short of the surface a stopped projectile is left, so a ray
        /// from there next frame still starts outside the wall and finds it.
        /// </summary>
        private const float StandOff = 0.05f;

        /// <summary>
        /// A hit facing up more than this is a floor, not a wall. A projectile
        /// born at ground height — triggered from a burst on the floor — skims
        /// the seams between floor boxes, and treating those as walls would
        /// kill it on its first frame.
        /// </summary>
        private const float FloorNormalY = 0.7f;

        // Main thread only, so one buffer serves every call.
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        public static int Mask()
        {
            int character = LayerMask.NameToLayer(CharacterLayer);

            return character < 0
                ? Physics.DefaultRaycastLayers
                : Physics.DefaultRaycastLayers & ~(1 << character);
        }

        /// <summary>
        /// Whether a wall lies between two points, and where to stop short of
        /// it. Stop is the far point when there is none.
        /// </summary>
        public static bool Cast(float3 from, float3 to, int mask, out float3 stop)
        {
            stop = to;

            float3 offset = to - from;
            float length = math.length(offset);

            if (length < 1e-5f)
                return false;

            float3 direction = offset / length;

            int count = Physics.RaycastNonAlloc(
                from, direction, Hits, length, mask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;

            // Unordered, and the first may be a floor seam with the real wall
            // behind it — so the nearest one that is not a floor.
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].normal.y > FloorNormalY || Hits[i].distance >= nearest)
                    continue;

                nearest = Hits[i].distance;
            }

            if (nearest == float.MaxValue)
                return false;

            stop = from + direction * math.max(0f, nearest - StandOff);
            return true;
        }
    }
}
