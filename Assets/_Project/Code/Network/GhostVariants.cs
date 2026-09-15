using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Transforms;

namespace TogetherWeFall.Network
{
    /// <summary>
    /// Replicates the per-instance colour every body in the game is drawn with.
    ///
    /// The colour carries more than looks here: a status tint, a darkening
    /// corpse, a loot drop's rarity and a projectile's element are all this one
    /// component, written on the host. Without it a client sees every enemy in
    /// its prefab colour and every drop white.
    /// </summary>
    [GhostComponentVariation(typeof(URPMaterialPropertyBaseColor), "Base Colour")]
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct BaseColourGhostVariant
    {
        [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)]
        public float4 Value;
    }

    /// <summary>
    /// Replicates LocalTransform the way NetCode's own default does, except that
    /// a jump no body makes is drawn as a jump.
    ///
    /// Every pool here parks its members a kilometre under the floor and fires
    /// one by moving it to a muzzle. Interpolated across that move, a fireball
    /// climbed out of the ground; extrapolated past the last snapshot — a late
    /// packet or a slow frame is enough — it flew a kilometre up first and came
    /// down onto its own muzzle, trail and all. Past MaxSmoothingDistance between
    /// two snapshots NetCode stops blending and takes the value.
    /// </summary>
    [GhostComponentVariation(typeof(LocalTransform), "Transform - Snap Teleports")]
    [GhostComponent(PrefabType = GhostPrefabType.All, SendTypeOptimization = GhostSendType.AllClients)]
    public struct TeleportingTransformGhostVariant
    {
        /// <summary>
        /// Ten metres between two snapshots is a teleport for everything that
        /// moves in this game: the fastest projectile covers a third of that in
        /// the longest gap snapshots leave, and a blink should snap anyway.
        /// </summary>
        [GhostField(Quantization = 1000, Smoothing = SmoothingAction.InterpolateAndExtrapolate, MaxSmoothingDistance = 10f)]
        public float3 Position;

        [GhostField(Quantization = 1000, Smoothing = SmoothingAction.InterpolateAndExtrapolate)]
        public float Scale;

        [GhostField(Quantization = 1000, Smoothing = SmoothingAction.InterpolateAndExtrapolate)]
        public quaternion Rotation;
    }

    /// <summary>
    /// Makes both variants above the default for every ghost, so no prefab has
    /// to opt in one by one.
    /// </summary>
    public sealed partial class GhostVariantDefaultsSystem : DefaultVariantSystemBase
    {
        protected override void RegisterDefaultVariants(Dictionary<ComponentType, Rule> defaultVariants)
        {
            defaultVariants.Add(typeof(URPMaterialPropertyBaseColor), Rule.ForAll(typeof(BaseColourGhostVariant)));
            defaultVariants.Add(typeof(LocalTransform), Rule.ForAll(typeof(TeleportingTransformGhostVariant)));
        }
    }
}
