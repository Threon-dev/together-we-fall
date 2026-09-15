using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TogetherWeFall.Skills
{
    /// <summary>
    /// A snapshot of every enemy on the floor, and the target searches the skill
    /// systems run against it.
    ///
    /// A value type holding two arrays rather than a static helper: casting,
    /// projectile impact and chain jumps all ask the same question, and each of
    /// them already has the arrays in hand. Passing this around means the search
    /// is written once without anything owning global state.
    ///
    /// The searches are linear. With tens of projectiles against hundreds of
    /// enemies that is a few thousand distance checks on one Burst thread, which
    /// is nothing next to what the crowd systems already do. If projectile
    /// counts ever reach the enemy counts, the upgrade is the spatial hash
    /// SeparationSystem already builds — not a different search here.
    /// </summary>
    public struct EnemyTargets
    {
        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<LocalTransform> Transforms;

        public int Length => Entities.Length;

        public float3 PositionOf(int index) => Transforms[index].Position;

        /// <summary>
        /// Where a specific body sits in this snapshot, or -1 if it is not in it.
        ///
        /// Absence is the answer as often as presence: the array comes from the
        /// enemy query, which filters by the enabled tag, so anything that died
        /// since it was named has already dropped out. Asking for it is how a
        /// delayed effect finds out its target is gone.
        /// </summary>
        public int IndexOf(Entity entity)
        {
            for (int i = 0; i < Entities.Length; i++)
            {
                if (Entities[i] == entity)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Nearest enemy to a point, or -1. Distances are measured on the ground
        /// plane: an enemy is a capsule and a projectile flies at chest height,
        /// so counting the vertical gap would make every shot miss slightly.
        /// </summary>
        public int FindNearest(float3 position, float maxDistance)
        {
            int best = -1;
            float bestDistanceSq = maxDistance * maxDistance;

            for (int i = 0; i < Entities.Length; i++)
            {
                float distanceSq = math.distancesq(position.xz, Transforms[i].Position.xz);
                if (distanceSq >= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// Nearest enemy that this chain has not already jumped to.
        ///
        /// The visited list is small on purpose. Past its capacity a chain may
        /// revisit a target, which is a far better failure than a chain that
        /// stops early or a per-hit heap allocation.
        /// </summary>
        /// <remarks>
        /// The visited list is taken by value, not by `in`: Contains is an
        /// extension that needs the list by ref, and a readonly reference cannot
        /// be handed to it. Sixty-four bytes copied once per jump is not a cost
        /// worth writing a manual loop to avoid.
        /// </remarks>
        public int FindNearestUnvisited(
            float3 position, float maxDistance, FixedList64Bytes<Entity> visited)
        {
            int best = -1;
            float bestDistanceSq = maxDistance * maxDistance;

            for (int i = 0; i < Entities.Length; i++)
            {
                if (visited.Contains(Entities[i]))
                    continue;

                float distanceSq = math.distancesq(position.xz, Transforms[i].Position.xz);
                if (distanceSq >= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// Nearest enemy inside a cone. A full circle is an arc cosine of minus
        /// one, so an area burst and a melee swing go through the same test.
        /// </summary>
        public int FindNearestInArc(
            float3 position, float3 direction, float arcCosine, float maxDistance)
        {
            int best = -1;
            float bestDistanceSq = maxDistance * maxDistance;

            for (int i = 0; i < Entities.Length; i++)
            {
                float3 offset = Transforms[i].Position - position;
                offset.y = 0f;

                float distanceSq = math.lengthsq(offset);
                if (distanceSq >= bestDistanceSq)
                    continue;

                if (!IsInsideArc(offset, distanceSq, direction, arcCosine))
                    continue;

                bestDistanceSq = distanceSq;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// The first enemy a body moving along a line would run into, or -1.
        /// Only what is ahead counts, and only within halfWidth of the line;
        /// along is how far down the line that body stands, or maxDistance.
        /// </summary>
        public int FindFirstAlong(
            float3 origin, float3 direction, float maxDistance, float halfWidth, out float along)
        {
            int best = -1;
            along = maxDistance;

            for (int i = 0; i < Entities.Length; i++)
            {
                float3 offset = Transforms[i].Position - origin;
                offset.y = 0f;

                float forward = math.dot(offset, direction);
                if (forward <= 0f || forward >= along)
                    continue;

                if (math.lengthsq(offset) - forward * forward > halfWidth * halfWidth)
                    continue;

                along = forward;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// Whether an offset from the centre falls inside the arc. Something
        /// standing exactly on the centre counts as inside — there is no
        /// direction to compare, and being on top of a swing should not save you.
        /// </summary>
        public static bool IsInsideArc(
            float3 offset, float distanceSq, float3 direction, float arcCosine)
        {
            if (arcCosine <= -1f || distanceSq < 1e-4f)
                return true;

            float3 toTarget = offset / math.sqrt(distanceSq);
            return math.dot(toTarget, direction) >= arcCosine;
        }
    }
}
