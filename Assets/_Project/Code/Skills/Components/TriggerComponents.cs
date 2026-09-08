using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Skills
{
    // ─────────────────────────────────────────────────────────────────────
    // AUTHORITATIVE — what makes a skill go off without anybody pressing a key.
    //
    // A trigger gem is a support like any other: it lies in the bag, it goes in
    // a hole, and it changes every active gem linked to it. What it changes is
    // not a number but WHO casts — the key stops working and the condition
    // starts working instead.
    //
    // The shape is the one this pipeline already uses everywhere: something
    // that knows an event happened announces it into a queue, and one system
    // decides what it means. Nothing polls. A frame in which nothing died and
    // nothing was set alight costs a length check.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What makes a trigger gem fire.
    ///
    /// Two of these have writers today — a body dying and an element being
    /// applied — because those are the two things the simulation already knows
    /// about. The other four are about the player being hurt, and the player
    /// cannot be hurt yet: there is no player health, no crit, and no block. The
    /// values exist so the day health lands the work is a single Announce call
    /// next to the code that already knows, rather than an enum everybody has to
    /// agree to extend.
    ///
    /// A gem authored against one of the four is not broken, it is early: it
    /// sits in its socket, costs nothing, and starts working when the game grows
    /// the event. The bake warns about it rather than refusing it.
    /// </summary>
    public enum TriggerConditionType : byte
    {
        /// <summary>Something this player killed died. Raised by DeathReactionSystem.</summary>
        OnKill = 0,

        /// <summary>A blow of this player's crit. Nothing rolls crits yet.</summary>
        OnCrit = 1,

        /// <summary>This player took damage. Players cannot be damaged yet.</summary>
        OnTakingDamage = 2,

        /// <summary>
        /// This player is below a fraction of their health. A STATE, not an
        /// event: it is asked every frame rather than announced, which is why it
        /// is the one condition that needs no writer — only a Health component
        /// on the character, which does not exist yet either.
        /// </summary>
        OnLowHealth = 3,

        /// <summary>This player blocked or dodged. Neither exists yet.</summary>
        OnBlockOrDodge = 4,

        /// <summary>An elemental status was applied by this player. Raised by ElementReactionSystem.</summary>
        OnStatusApplied = 5
    }

    /// <summary>
    /// "This happened to, or because of, this player."
    ///
    /// A queue on the events singleton, drained once a frame by
    /// TriggerEvaluationSystem. Deliberately not a component on the player: the
    /// systems that raise these are Burst jobs walking enemies, and the thing
    /// they have in hand is a player id, not a character entity.
    ///
    /// It carries a position and a body because a triggered cast has to happen
    /// somewhere and prefers to happen ON something — the same reason PendingCast
    /// carries a preferred target rather than letting the bolt search again.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct TriggerEvent : IBufferElementData
    {
        public TriggerConditionType Condition;

        /// <summary>Whose doing this was. Not an Entity: the caster may be gone.</summary>
        public int PlayerId;

        /// <summary>Where it happened. Where the triggered skill goes off.</summary>
        public float3 Position;

        /// <summary>The body it happened to, or Null.</summary>
        public Entity Target;
    }

    /// <summary>
    /// The rules every announcer of a trigger event shares.
    ///
    /// A struct of static methods, the same shape as GridFit and GemSockets and
    /// for the same reason: two systems raise these events and both must obey
    /// the same ceiling, and a ceiling written twice is a ceiling that drifts.
    /// </summary>
    public struct TriggerEvents
    {
        /// <summary>
        /// How many of these one frame may hold.
        ///
        /// Three hundred enemies dying together is three hundred kills and, for
        /// a trigger gem, one cast: the cooldown swallows the rest whatever the
        /// queue says. So the cap costs nothing real and stops a chain reaction
        /// from turning into a four-figure buffer that is cleared unread.
        ///
        /// The same lever as MaxAreasPerFrame and MaxHitsPerFrame, and the same
        /// reasoning — except that here the surplus is genuinely worthless
        /// rather than merely deferred, so it is dropped rather than held over.
        /// </summary>
        public const int MaxPerFrame = 32;

        /// <summary>
        /// Adds an event unless the frame is already full. Returns whether it
        /// went in, which nobody has to check — every caller is announcing, not
        /// asking.
        /// </summary>
        public static bool Announce(DynamicBuffer<TriggerEvent> queue, in TriggerEvent announced)
        {
            if (queue.Length >= MaxPerFrame)
                return false;

            queue.Add(announced);
            return true;
        }
    }

    /// <summary>
    /// How long until one socket may fire again.
    ///
    /// Keyed by the gear and the hole rather than by the skill, exactly as the
    /// hotkey cooldown is: the same active gem in two weapons is two skills, and
    /// a gem moved to another weapon must not carry a spent cooldown with it.
    ///
    /// Entries appear when a trigger fires and are removed when they run out, so
    /// the buffer is as long as the number of triggers that went off recently
    /// and empty the rest of the time. A character with no trigger gems never
    /// grows one.
    ///
    /// The cooldown is not optional and not decoration. A trigger gem without
    /// one fires every frame the condition holds, which for "on kill" during a
    /// wave is every frame — the balance is not merely wrong, the frame is gone.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct TriggerCooldown : IBufferElementData
    {
        public Entity Gear;
        public int SocketIndex;
        public float Remaining;
    }
}
