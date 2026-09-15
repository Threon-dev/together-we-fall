using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Config;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;
using TogetherWeFall.Skills;
using Object = UnityEngine.Object;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// The arsenal: skills built on the pattern effects, the small skills their
    /// supports trigger, and a hundred support gems.
    ///
    /// Its own file beside BuildLibraryFactory for the reason that one sits
    /// beside SkillContentFactory: this content answers one question — what
    /// clearing a crowd looks like — and should be readable, or deletable, as
    /// one thing.
    ///
    /// Assets are filled in only when created, like every factory here.
    /// EVERY NUMBER IS PROVISIONAL (TUNE): chosen so the effect is visible within
    /// a press, not balanced against anything.
    /// </summary>
    public static class ArsenalContentFactory
    {
        private const string Pack = SkillVfxContentFactory.Pack;
        private const string GemSheet = "Assets/Gem Pack Complete/128 Full Content/Pack-ElementsGEMS.png";

        /// <summary>How many sprites of the sheet a support gem may be drawn with.</summary>
        private const int SupportIconCount = 90;

        // The sprites the hand-authored gems of each element already use, so a new
        // fire gem reads as a fire gem in the bag.
        private static readonly int[] FireIcons = { 3, 32, 73 };
        private static readonly int[] ColdIcons = { 66, 23, 63 };
        private static readonly int[] LightningIcons = { 13, 28, 31 };
        private static readonly int[] ChaosIcons = { 12, 55 };
        private static readonly int[] PhysicalIcons = { 30, 9, 11 };

        // ─────────────────────────────────────────────────────────────────
        // Skills
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every arsenal skill, helpers included, for the skill database. A
        /// helper with no place in this list is a trigger that casts nothing.
        /// </summary>
        public static SkillDefinition[] CreateSkills()
        {
            var all = new List<SkillDefinition>();

            // The helpers first: the skills below and the trigger gems hold
            // references to them. None has a gem — each exists to be cast by
            // something landing.
            SkillDefinition emberGround = Skill("EmberGround", "Ember Ground", s =>
            {
                s.Effect = SkillEffectKind.PersistentZone;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 7f;
                s.Cooldown = 1f;
                s.Range = 2f;
                s.Radius = 2.4f;
                s.ZoneDuration = 3f;
                s.ZoneTickInterval = 0.5f;
            }, "Environment/Fire/Field/FireField", null, "Combat/Explosions/BasicTiny/BasicTinyExplosionRed", scale: 2.4f);

            SkillDefinition petalStorm = Skill("PetalStorm", "Petal Storm", s =>
            {
                s.Effect = SkillEffectKind.Volley;
                s.DamageType = DamageType.Physical;
                s.BaseDamage = 8f;
                s.Cooldown = 1f;
                s.Range = 10f;
                s.ProjectileSpeed = 22f;
                s.Count = 12;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Bleed);
            }, "Combat/Explosions/- Misc/PoofCloudStars", "Combat/Missiles/Ring/RingMissileRed", "Combat/Sword/SwordHit/SwordHitRed");

            SkillDefinition iceRing = Skill("IceRing", "Ice Ring", s =>
            {
                s.Effect = SkillEffectKind.Volley;
                s.DamageType = DamageType.Cold;
                s.BaseDamage = 7f;
                s.Cooldown = 1f;
                s.Range = 10f;
                s.ProjectileSpeed = 20f;
                s.Count = 12;
            }, "Combat/Nova/Basic/NovaBlue", "Combat/Missiles/Frost/FrostMissile", "Combat/Sword/SwordHit/SwordHitBlue");

            SkillDefinition aftershock = Skill("Aftershock", "Aftershock", s =>
            {
                s.Effect = SkillEffectKind.Fissure;
                s.DamageType = DamageType.Physical;
                s.BaseDamage = 12f;
                s.Cooldown = 1f;
                s.Range = 9f;
                s.Radius = 1.8f;
                s.Count = 5;
                s.Interval = 0.06f;
            }, "Combat/Explosions/Earth/EarthExplosion", null, "Combat/Sword/SwordHit/SwordHitRed", scale: 1.4f);

            SkillDefinition fallingStar = Skill("FallingStar", "Falling Star", s =>
            {
                s.Effect = SkillEffectKind.Rain;
                s.DamageType = DamageType.Lightning;
                s.BaseDamage = 28f;
                s.Cooldown = 1f;
                s.Range = 4f;
                s.Radius = 2.8f;
                s.Count = 1;
                s.Interval = 0.3f;
                s.Scatter = 0f;
            }, "Combat/Explosions/Sparkle/SparkleExplosionYellow", null, "Combat/Sword/SwordHit/SwordHitYellow", scale: 2.2f, lift: 0.4f);

            SkillDefinition staticBurst = Skill("StaticBurst", "Static Burst", s =>
            {
                s.Effect = SkillEffectKind.AreaBurst;
                s.DamageType = DamageType.Lightning;
                s.BaseDamage = 14f;
                s.Cooldown = 1f;
                s.Range = 2f;
                s.Radius = 3f;
                s.BaseChains = 2;
                s.ChainRange = 7f;
            }, "Combat/Magic/Nova/MagicNovaYellow", null, "Combat/Sword/SwordHit/SwordHitYellow", scale: 3f,
                castSound: "Explosion/retro_explosion_lightning");

            SkillDefinition frostCrater = Skill("FrostCrater", "Frost Crater", s =>
            {
                s.Effect = SkillEffectKind.PersistentZone;
                s.DamageType = DamageType.Cold;
                s.BaseDamage = 5f;
                s.Cooldown = 1f;
                s.Range = 2f;
                s.Radius = 2.6f;
                s.ZoneDuration = 3f;
                s.ZoneTickInterval = 0.6f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Slow);
            }, "Interactive/Zone/Round/RoundZoneBlue", null, "Combat/Sword/SwordHit/SwordHitBlue", scale: 2.6f,
                castSound: "Explosion/retro_explosion_ice");

            SkillDefinition venomBurst = Skill("VenomBurst", "Venom Burst", s =>
            {
                s.Effect = SkillEffectKind.AreaBurst;
                s.DamageType = DamageType.Chaos;
                s.BaseDamage = 12f;
                s.Cooldown = 1f;
                s.Range = 2f;
                s.Radius = 3.2f;
            }, "Combat/Explosions/Poison/PoisonExplosionGreen", null, "Combat/Sword/SwordHit/SwordHitGreen", scale: 2.6f, lift: 0.4f);

            SkillDefinition emberMeteor = Skill("EmberMeteor", "Ember Meteor", s =>
            {
                s.Effect = SkillEffectKind.Rain;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 26f;
                s.Cooldown = 1f;
                s.Range = 4f;
                s.Radius = 2.6f;
                s.Count = 1;
                s.Interval = 0.35f;
                s.Scatter = 0f;
            }, "Combat/Explosions/Fire/FireExplosionYellow", null, "Combat/Explosions/BasicTiny/BasicTinyExplosionRed", scale: 2f, lift: 0.4f);

            SkillDefinition bladeWhirl = Skill("BladeWhirl", "Blade Whirl", s =>
            {
                s.Effect = SkillEffectKind.Cyclone;
                s.DamageType = DamageType.Physical;
                s.BaseDamage = 9f;
                s.Cooldown = 1f;
                s.Range = 3f;
                s.Radius = 2.8f;
                s.Count = 3;
                s.Interval = 0.12f;
            }, "Combat/Sword/SlashRound/SlashRoundYellow", null, "Combat/Sword/SwordHit/SwordHitRed", scale: 2.2f, lift: 0.9f,
                castSound: "Combat/retro_sword_slash");

            SkillDefinition singularity = Skill("Singularity", "Singularity", s =>
            {
                s.Effect = SkillEffectKind.PersistentZone;
                s.DamageType = DamageType.Chaos;
                s.BaseDamage = 6f;
                s.Cooldown = 1f;
                s.Range = 2f;
                s.Radius = 3f;
                s.ZoneDuration = 3f;
                s.ZoneTickInterval = 0.4f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Slow);
            }, "Interactive/Zone/Magic/MagicZonePurple", null, "Combat/Sword/SwordHit/SwordHitPurple", scale: 3f,
                castSound: "Explosion/retro_explosion_blackhole3");

            all.AddRange(new[]
            {
                emberGround, petalStorm, iceRing, aftershock, fallingStar,
                staticBurst, frostCrater, venomBurst, emberMeteor, bladeWhirl,
                singularity
            });

            // ── Rain: the sky does the work ──────────────────────────────

            all.Add(Skill("Meteor", "Meteor", s =>
            {
                s.Effect = SkillEffectKind.Rain;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 70f;
                s.Cooldown = 2.2f;
                s.ManaCost = 28f;
                s.Range = 18f;
                s.Radius = 4.5f;
                s.Count = 1;
                s.Interval = 0.6f;
                s.Scatter = 0f;

                // The crater keeps burning after the rock is gone.
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportMeteorEmbers", SkillModifierKind.TriggerOnHit, 60f,
                        triggered: emberGround)
                };
            }, "Combat/Explosions/FireBig/FireBigExplosion", null, "Combat/Explosions/BasicTiny/BasicTinyExplosionRed", scale: 3f, lift: 0.4f));

            all.Add(Skill("MeteorShower", "Meteor Shower", s =>
            {
                s.Effect = SkillEffectKind.Rain;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 20f;
                s.Cooldown = 4f;
                s.ManaCost = 40f;
                s.Range = 18f;
                s.Radius = 2.2f;
                s.Count = 10;
                s.Interval = 0.1f;
                s.Scatter = 5.5f;
            }, "Combat/Explosions/Fire/FireExplosionRed", null, "Combat/Explosions/BasicTiny/BasicTinyExplosionRed", scale: 1.8f, lift: 0.4f));

            all.Add(Skill("Starfall", "Starfall", s =>
            {
                s.Effect = SkillEffectKind.Rain;
                s.DamageType = DamageType.Lightning;
                s.BaseDamage = 11f;
                s.Cooldown = 3.5f;
                s.ManaCost = 34f;
                s.Range = 18f;
                s.Radius = 1.8f;
                s.Count = 14;
                s.Interval = 0.07f;
                s.Scatter = 6f;
            }, "Combat/Explosions/Sparkle/SparkleExplosionPurple", null, "Combat/Sword/SwordHit/SwordHitYellow", scale: 1.5f, lift: 0.4f));

            all.Add(Skill("Blizzard", "Blizzard", s =>
            {
                s.Effect = SkillEffectKind.Rain;
                s.DamageType = DamageType.Cold;
                s.BaseDamage = 8f;
                s.Cooldown = 5f;
                s.ManaCost = 38f;
                s.Range = 16f;
                s.Radius = 1.7f;
                s.Count = 18;
                s.Interval = 0.09f;
                s.Scatter = 5f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Slow);
            }, "Combat/Explosions/Frost/FrostExplosion", null, "Combat/Sword/SwordHit/SwordHitBlue", scale: 1.4f, lift: 0.4f));

            all.Add(Skill("Thunderstorm", "Thunderstorm", s =>
            {
                s.Effect = SkillEffectKind.Rain;
                s.DamageType = DamageType.Lightning;
                s.BaseDamage = 18f;
                s.Cooldown = 4f;
                s.ManaCost = 32f;
                s.Range = 18f;
                s.Radius = 2.4f;
                s.Count = 6;
                s.Interval = 0.22f;
                s.Scatter = 4.5f;

                // Every bolt from the sky jumps on through the crowd.
                s.BaseChains = 2;
                s.ChainRange = 7f;
                s.ChainDelay = 0.06f;
            }, "Environment/Lightning/Strike/LightningStrikeYellow", null, "Combat/Sword/SwordHit/SwordHitYellow", scale: 2f));

            all.Add(Skill("HammerOfJudgement", "Hammer of Judgement", s =>
            {
                s.Effect = SkillEffectKind.Rain;
                s.DamageType = DamageType.Lightning;
                s.BaseDamage = 36f;
                s.Cooldown = 3f;
                s.ManaCost = 30f;
                s.Range = 16f;
                s.Radius = 3.5f;
                s.Count = 1;
                s.Interval = 0.25f;
                s.Scatter = 0f;

                // Falls four times: an echo is authored here, not socketed.
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportJudgementEcho", SkillModifierKind.Echo, 3f)
                };
            }, "Combat/Nova/Sparkle/SparkleNovaRainbow", null, "Combat/Sword/SwordHit/SwordHitYellow", scale: 3.5f,
                castSound: "Explosion/retro_explosion_holy02"));

            // ── Fissure: the ground tears open along the aim ─────────────

            all.Add(Skill("Earthshatter", "Earthshatter", s =>
            {
                s.Effect = SkillEffectKind.Fissure;
                s.DamageType = DamageType.Physical;
                s.BaseDamage = 26f;
                s.Cooldown = 1.6f;
                s.ManaCost = 16f;
                s.Range = 14f;
                s.Radius = 2.2f;
                s.Count = 7;
                s.Interval = 0.06f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Stun);
            }, "Combat/Explosions/Earth/EarthExplosion", null, "Combat/Sword/SwordHit/SwordHitRed", scale: 1.8f));

            all.Add(Skill("FlameWave", "Flame Wave", s =>
            {
                s.Effect = SkillEffectKind.Fissure;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 17f;
                s.Cooldown = 1.4f;
                s.ManaCost = 18f;
                s.Range = 16f;
                s.Radius = 2f;
                s.Count = 9;
                s.Interval = 0.05f;
            }, "Combat/Explosions/Fire/FireExplosion", null, "Combat/Explosions/BasicTiny/BasicTinyExplosionRed", scale: 1.6f, lift: 0.4f));

            all.Add(Skill("GlacialCascade", "Glacial Cascade", s =>
            {
                s.Effect = SkillEffectKind.Fissure;
                s.DamageType = DamageType.Cold;
                s.BaseDamage = 20f;
                s.Cooldown = 1.5f;
                s.ManaCost = 16f;
                s.Range = 13f;
                s.Radius = 2.4f;
                s.Count = 6;
                s.Interval = 0.08f;

                // The last spike shatters into a ring of shards.
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportCascadeShatter", SkillModifierKind.TriggerOnHit, 60f,
                        triggered: iceRing)
                };
            }, "Combat/Magic/Pillarblast/MagicPillarBlastBlue", null, "Combat/Sword/SwordHit/SwordHitBlue", scale: 2.4f,
                castSound: "Explosion/retro_explosion_ice"));

            all.Add(Skill("VoltaicRift", "Voltaic Rift", s =>
            {
                s.Effect = SkillEffectKind.Fissure;
                s.DamageType = DamageType.Lightning;
                s.BaseDamage = 14f;
                s.Cooldown = 1.3f;
                s.ManaCost = 15f;
                s.Range = 18f;
                s.Radius = 1.8f;
                s.Count = 8;
                s.Interval = 0.045f;
                s.BaseChains = 1;
                s.ChainRange = 6f;
            }, "Combat/Explosions/Lightning/LightningExplosionBlue", null, "Combat/Sword/SwordHit/SwordHitYellow", scale: 1.4f, lift: 0.4f));

            // ── Volley: fans and rings of projectiles ────────────────────

            all.Add(Skill("BladeFan", "Blade Fan", s =>
            {
                s.Effect = SkillEffectKind.Volley;
                s.DamageType = DamageType.Physical;
                s.BaseDamage = 11f;
                s.Cooldown = 0.7f;
                s.ManaCost = 8f;
                s.Range = 16f;
                s.ProjectileSpeed = 30f;
                s.ArcDegrees = 70f;
                s.Count = 7;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Bleed);
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportBladeFanPierce", SkillModifierKind.Pierce, 1f)
                };
            }, "Combat/Muzzleflash/Bullet/BulletMuzzleYellow", "Combat/Missiles/Ring/RingMissileYellow", "Combat/Sword/SwordHit/SwordHitRed", scale: 1.2f,
                castSound: "Combat/retro_combat_slash"));

            all.Add(Skill("FrostShards", "Frost Shards", s =>
            {
                s.Effect = SkillEffectKind.Volley;
                s.DamageType = DamageType.Cold;
                s.BaseDamage = 9f;
                s.Cooldown = 1.2f;
                s.ManaCost = 18f;
                s.Range = 12f;
                s.ProjectileSpeed = 22f;
                s.ArcDegrees = 360f;
                s.Count = 16;
            }, "Combat/Nova/Basic/NovaBlue", "Combat/Missiles/Frost/FrostMissile", "Combat/Sword/SwordHit/SwordHitBlue", scale: 1.2f));

            all.Add(Skill("SparkRing", "Spark Ring", s =>
            {
                s.Effect = SkillEffectKind.Volley;
                s.DamageType = DamageType.Lightning;
                s.BaseDamage = 7f;
                s.Cooldown = 1f;
                s.ManaCost = 14f;
                s.Range = 14f;
                s.ProjectileSpeed = 26f;
                s.ArcDegrees = 360f;
                s.Count = 10;
                s.BaseChains = 1;
                s.ChainRange = 6f;
            }, "Combat/Nova/Sparkle/SparkleNovaYellow", "Combat/Missiles/Sparkle/SparkleMissileYellow", "Combat/Sword/SwordHit/SwordHitYellow", scale: 1.2f));

            all.Add(Skill("HellfireBarrage", "Hellfire Barrage", s =>
            {
                s.Effect = SkillEffectKind.Volley;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 14f;
                s.Cooldown = 0.9f;
                s.ManaCost = 14f;
                s.Range = 20f;
                s.ProjectileSpeed = 28f;
                s.ArcDegrees = 30f;
                s.Count = 5;
                s.Radius = 2f;
            }, "Combat/Muzzleflash/FireBig/FireBigMuzzle", "Combat/Missiles/Fireball/FireballMissileRed", "Combat/Explosions/Fire/FireExplosionRed", scale: 1.2f));

            all.Add(Skill("Comet", "Comet", s =>
            {
                s.Effect = SkillEffectKind.Projectile;
                s.DamageType = DamageType.Cold;
                s.BaseDamage = 38f;
                s.Cooldown = 1.8f;
                s.ManaCost = 22f;
                s.Range = 18f;
                s.ProjectileSpeed = 12f;
                s.Radius = 3.5f;

                // Slow and heavy, and it bursts into a ring of shards on impact.
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportCometShards", SkillModifierKind.TriggerOnHit, 70f,
                        triggered: iceRing)
                };
            }, "Combat/Muzzleflash/Frost/FrostMuzzle", "Combat/Missiles/FireballBig/FireballBigMissileBlue", "Combat/Explosions/FireBig/FireBigExplosionBlue", scale: 1.8f));

            // ── Leap: the body goes where the blow lands ─────────────────

            all.Add(Skill("LeapSlam", "Leap Slam", s =>
            {
                s.Effect = SkillEffectKind.LeapSlam;
                s.DamageType = DamageType.Physical;
                s.BaseDamage = 40f;
                s.Cooldown = 2.5f;
                s.ManaCost = 12f;
                s.Range = 12f;
                s.Radius = 4f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Stun);
            }, "Combat/Fighting/GroundSlam", null, "Combat/Sword/SwordHit/SwordHitRed"));

            all.Add(Skill("StormDive", "Storm Dive", s =>
            {
                s.Effect = SkillEffectKind.LeapSlam;
                s.DamageType = DamageType.Lightning;
                s.BaseDamage = 30f;
                s.Cooldown = 3f;
                s.ManaCost = 16f;
                s.Range = 14f;
                s.Radius = 4.5f;
                s.BaseChains = 3;
                s.ChainRange = 8f;
            }, "Environment/Lightning/Strike/LightningStrikeBlue", null, "Combat/Sword/SwordHit/SwordHitYellow", scale: 3f));

            all.Add(Skill("CataclysmLeap", "Cataclysm Leap", s =>
            {
                s.Effect = SkillEffectKind.LeapSlam;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 45f;
                s.Cooldown = 4f;
                s.ManaCost = 24f;
                s.Range = 12f;
                s.Radius = 5f;
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportCataclysmEmbers", SkillModifierKind.TriggerOnHit, 80f,
                        triggered: emberGround)
                };
            }, "Combat/Explosions/Nuke/NukeExplosionRed", null, "Combat/Explosions/BasicTiny/BasicTinyExplosionRed", scale: 1.5f));

            // ── Cyclone and melee: the caster is the storm ───────────────

            all.Add(Skill("WhirlingBlades", "Whirling Blades", s =>
            {
                s.Effect = SkillEffectKind.Cyclone;
                s.DamageType = DamageType.Physical;
                s.BaseDamage = 13f;
                s.Cooldown = 2f;
                s.ManaCost = 14f;
                s.Range = 3f;
                s.Radius = 3.4f;
                s.Count = 6;
                s.Interval = 0.18f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Bleed);
            }, "Combat/Sword/SlashSpherical/SlashSphericalYellow", null, "Combat/Sword/SwordHit/SwordHitRed", scale: 2.6f, lift: 0.9f,
                castSound: "Combat/retro_sword_slash_blood"));

            all.Add(Skill("InfernoSpin", "Inferno Spin", s =>
            {
                s.Effect = SkillEffectKind.Cyclone;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 9f;
                s.Cooldown = 2.4f;
                s.ManaCost = 18f;
                s.Range = 3f;
                s.Radius = 3f;
                s.Count = 8;
                s.Interval = 0.14f;
            }, "Combat/Sword/SlashRound/SlashRoundRed", null, "Combat/Explosions/BasicTiny/BasicTinyExplosionRed", scale: 2.4f, lift: 0.9f,
                castSound: "Combat/retro_sword_slash"));

            all.Add(Skill("FrostVortex", "Frost Vortex", s =>
            {
                s.Effect = SkillEffectKind.Cyclone;
                s.DamageType = DamageType.Cold;
                s.BaseDamage = 8f;
                s.Cooldown = 2.6f;
                s.ManaCost = 16f;
                s.Range = 3f;
                s.Radius = 3.6f;
                s.Count = 7;
                s.Interval = 0.16f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Slow);
            }, "Combat/Sword/SlashRound/SlashRoundBlue", null, "Combat/Sword/SwordHit/SwordHitBlue", scale: 2.8f, lift: 0.9f,
                castSound: "Combat/retro_sword_slash"));

            all.Add(Skill("TectonicSlam", "Tectonic Slam", s =>
            {
                s.Effect = SkillEffectKind.MeleeArc;
                s.DamageType = DamageType.Physical;
                s.BaseDamage = 30f;
                s.Cooldown = 1.2f;
                s.ManaCost = 10f;
                s.Range = 4f;
                s.Radius = 4f;
                s.ArcDegrees = 160f;

                // The ground answers the blow twice more.
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportTectonicEcho", SkillModifierKind.Echo, 2f)
                };
            }, "Combat/Fighting/BodySlam", null, "Combat/Sword/SwordHit/SwordHitRed", scale: 1.2f));

            // ── Bursts and bolts that spread ─────────────────────────────

            all.Add(Skill("DeathBlossom", "Death Blossom", s =>
            {
                s.Effect = SkillEffectKind.AreaBurst;
                s.DamageType = DamageType.Physical;
                s.BaseDamage = 24f;
                s.Cooldown = 2f;
                s.ManaCost = 20f;
                s.Range = 16f;
                s.Radius = 3.5f;
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportBlossomPetals", SkillModifierKind.TriggerOnHit, 80f,
                        triggered: petalStorm)
                };
            }, "Combat/Sword/SlashSpherical/SlashSphericalRed", null, "Combat/Sword/SwordHit/SwordHitRed", scale: 2f, lift: 0.9f,
                castSound: "Combat/retro_sword_slash"));

            all.Add(Skill("PlagueBurst", "Plague Burst", s =>
            {
                s.Effect = SkillEffectKind.AreaBurst;
                s.DamageType = DamageType.Chaos;
                s.BaseDamage = 16f;
                s.Cooldown = 2.5f;
                s.ManaCost = 26f;
                s.Range = 16f;
                s.Radius = 5f;

                // What it kills bursts in turn — the plague spreads itself.
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportPlagueSpread", SkillModifierKind.ExplodeOnKill, 3.5f,
                        secondary: 70f)
                };
            }, "Combat/Explosions/- Misc/SmokeGrenadeExplosion", null, "Combat/Sword/SwordHit/SwordHitGreen", scale: 3.5f, lift: 0.4f));

            all.Add(Skill("SoulRend", "Soul Rend", s =>
            {
                s.Effect = SkillEffectKind.ChainBolt;
                s.DamageType = DamageType.Chaos;
                s.BaseDamage = 12f;
                s.Cooldown = 1.1f;
                s.ManaCost = 14f;
                s.Range = 18f;
                s.BaseChains = 7;
                s.ChainRange = 8f;
                s.ChainDelay = 0.05f;
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportSoulRendCull", SkillModifierKind.CullingStrike, 10f),
                    SkillContentFactory.Modifier("SupportSoulRendMana", SkillModifierKind.ManaOnKill, 2f)
                };
            }, "Combat/Muzzleflash/Symbol/SymbolMuzzlePurple", null, "Combat/Death/Soul/DeathSoulPurple", scale: 1.2f, lift: 0.9f,
                castSound: "Shoot/retro_shoot_soul", hitSound: "Explosion/retro_explosion_soul"));

            // ── Orbs and bombs: the pack's missiles, each with its own burst ─

            all.Add(Skill("VoidOrb", "Void Orb", s =>
            {
                s.Effect = SkillEffectKind.Projectile;
                s.DamageType = DamageType.Chaos;
                s.BaseDamage = 30f;
                s.Cooldown = 2.4f;
                s.ManaCost = 24f;
                s.Range = 18f;
                s.ProjectileSpeed = 11f;
                s.Radius = 2.5f;

                // Where it bursts, a well opens and holds the crowd in place.
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportVoidOrbWell", SkillModifierKind.TriggerOnHit, 60f,
                        triggered: singularity)
                };
            }, "Combat/Muzzleflash/BlackHole/BlackHoleMuzzlePurple", "Combat/Missiles/BlackHole/BlackHoleMissilePurple",
                "Combat/Explosions/BlackHole/BlackHoleExplosionPurple", scale: 1.6f));

            all.Add(Skill("DoomOrb", "Doom Orb", s =>
            {
                s.Effect = SkillEffectKind.Projectile;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 90f;
                s.Cooldown = 6f;
                s.ManaCost = 45f;
                s.Range = 20f;
                s.ProjectileSpeed = 8f;
                s.Radius = 6f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Ignite);
            }, "Combat/Muzzleflash/Rocket - Nuke/RocketMuzzle", "Combat/Missiles/Nuke/NukeMissile",
                "Combat/Explosions/Nuke/NukeExplosion", scale: 1.6f));

            all.Add(Skill("LavaBomb", "Lava Bomb", s =>
            {
                s.Effect = SkillEffectKind.Projectile;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 28f;
                s.Cooldown = 1.6f;
                s.ManaCost = 18f;
                s.Range = 16f;
                s.ProjectileSpeed = 16f;
                s.Radius = 2.8f;

                // The splash keeps burning where it landed.
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportLavaBombPool", SkillModifierKind.TriggerOnHit, 50f,
                        triggered: emberGround)
                };
            }, "Combat/Muzzleflash/Liquids/LavaMuzzle", "Combat/Missiles/Liquids/LavaMissile",
                "Combat/Explosions/Liquids/LavaExplosion", scale: 1.4f));

            all.Add(Skill("AcidFlask", "Acid Flask", s =>
            {
                s.Effect = SkillEffectKind.Projectile;
                s.DamageType = DamageType.Chaos;
                s.BaseDamage = 14f;
                s.Cooldown = 0.9f;
                s.ManaCost = 10f;
                s.Range = 14f;
                s.ProjectileSpeed = 18f;
                s.Radius = 2.4f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Poison);
            }, "Combat/Muzzleflash/Liquids/AcidMuzzle", "Combat/Missiles/Liquids/AcidMissile",
                "Combat/Explosions/Liquids/AcidExplosion", scale: 1.4f));

            all.Add(Skill("PlasmaBolt", "Plasma Bolt", s =>
            {
                s.Effect = SkillEffectKind.Projectile;
                s.DamageType = DamageType.Lightning;
                s.BaseDamage = 20f;
                s.Cooldown = 0.8f;
                s.ManaCost = 12f;
                s.Range = 22f;
                s.ProjectileSpeed = 32f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Shock);

                // Splits on the first body, and its halves split again.
                s.Modifiers = new[]
                {
                    SkillContentFactory.Modifier("SupportPlasmaBoltFork", SkillModifierKind.Fork, 2f)
                };
            }, "Combat/Muzzleflash/Plasma/PlasmaMuzzleBlue", "Combat/Missiles/Plasma/PlasmaMissileBlue",
                "Combat/Explosions/Plasma/PlasmaExplosionBlue", scale: 1.2f));

            all.Add(Skill("ShadowDaggers", "Shadow Daggers", s =>
            {
                s.Effect = SkillEffectKind.Volley;
                s.DamageType = DamageType.Chaos;
                s.BaseDamage = 10f;
                s.Cooldown = 0.6f;
                s.ManaCost = 9f;
                s.Range = 16f;
                s.ProjectileSpeed = 34f;
                s.ArcDegrees = 40f;
                s.Count = 5;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Vulnerable);
            }, "Combat/Muzzleflash/Shadow/ShadowMuzzlePurple", "Combat/Missiles/Shadow/ShadowMissilePurple",
                "Combat/Explosions/Shadow/ShadowExplosionPurple", scale: 1.1f));

            // ── Close and wide: a cone of fire and a flurry of claws ─────

            all.Add(Skill("DragonsBreath", "Dragon's Breath", s =>
            {
                s.Effect = SkillEffectKind.MeleeArc;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 22f;
                s.Cooldown = 0.9f;
                s.ManaCost = 12f;
                s.Range = 6f;
                s.Radius = 6f;
                s.ArcDegrees = 50f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Ignite);

                // The pack's flamethrower loops, and a looping prefab plays for
                // the presenter's whole one-shot ceiling — four seconds of flame
                // for a press under one. Its big fire muzzle is the same cone once.
            }, "Combat/Muzzleflash/FireBig/FireBigMuzzleRed", null, "Combat/Explosions/BasicTiny/BasicTinyExplosionRed",
                scale: 2.5f, lift: 0.9f, castSound: "Shoot/retro_shoot_fireball3"));

            all.Add(Skill("RendingClaws", "Rending Claws", s =>
            {
                s.Effect = SkillEffectKind.MeleeArc;
                s.DamageType = DamageType.Physical;
                s.BaseDamage = 14f;
                s.Cooldown = 0.45f;
                s.ManaCost = 4f;
                s.Range = 3f;
                s.Radius = 3f;
                s.ArcDegrees = 100f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Bleed);
            }, "Combat/Fighting/Claw/ClawBlood", null, "Combat/Sword/SwordHit/SwordHitRed", scale: 2f, lift: 0.9f));

            all.Add(Skill("PillarsOfDawn", "Pillars of Dawn", s =>
            {
                s.Effect = SkillEffectKind.Fissure;
                s.DamageType = DamageType.Lightning;
                s.BaseDamage = 18f;
                s.Cooldown = 1.5f;
                s.ManaCost = 18f;
                s.Range = 15f;
                s.Radius = 2f;
                s.Count = 6;
                s.Interval = 0.09f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Shock);
            }, "Combat/Magic/Pillarblast/MagicPillarBlastYellow", null, "Combat/Sword/SwordHit/SwordHitYellow",
                scale: 2f, castSound: "Explosion/retro_explosion_holy"));

            // ── Tornadoes: a zone you can see moving ─────────────────────
            //
            // Four seconds each, which is also the presenter's ceiling on a
            // looping prefab: a longer zone would outlive its own tornado.

            all.Add(Skill("FireTornado", "Fire Tornado", s =>
            {
                s.Effect = SkillEffectKind.PersistentZone;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 10f;
                s.Cooldown = 3.5f;
                s.ManaCost = 28f;
                s.Range = 14f;
                s.Radius = 2.6f;
                s.ZoneDuration = 4f;
                s.ZoneTickInterval = 0.4f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Ignite);
            }, "Interactive/Tornado/v2/TornadoFire", null, "Combat/Explosions/BasicTiny/BasicTinyExplosionRed",
                scale: 1.2f, castSound: "Explosion/retro_explosion_incendiary"));

            all.Add(Skill("Tempest", "Tempest", s =>
            {
                s.Effect = SkillEffectKind.PersistentZone;
                s.DamageType = DamageType.Lightning;
                s.BaseDamage = 8f;
                s.Cooldown = 3.5f;
                s.ManaCost = 28f;
                s.Range = 14f;
                s.Radius = 2.8f;
                s.ZoneDuration = 4f;
                s.ZoneTickInterval = 0.5f;
                s.BaseChains = 1;
                s.ChainRange = 6f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Shock);
            }, "Interactive/Tornado/v2/TornadoLightning", null, "Combat/Sword/SwordHit/SwordHitYellow",
                scale: 1.2f, castSound: "Explosion/retro_explosion_storm"));

            // ── Channel: held, it burns; released, it stops ──────────────
            //
            // The cooldown is one pulse and the cost is per pulse: the host
            // already casts while a key is held, so this pulses about eight
            // times a second for about twelve mana a second.

            all.Add(Skill("SearingRay", "Searing Ray", s =>
            {
                s.Effect = SkillEffectKind.Beam;
                s.DamageType = DamageType.Fire;
                s.BaseDamage = 6f;
                s.Cooldown = 0.12f;
                s.ManaCost = 1.5f;
                s.Range = 14f;
                s.Radius = 0.8f;
                s.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Ignite);
            }, "Combat/Beams/Laser/Setup/Beam/LaserBeamRed", null, "Combat/Explosions/Laser/LaserExplosionRed",
                scale: 1.5f, lift: 0.9f, castSound: "Beam/retro_beam_laser"));

            return all.ToArray();
        }

        /// <summary>
        /// One skill plus its look. The set is create-or-load like the skill, and
        /// written onto the skill only when it names none — a set somebody has
        /// since pointed elsewhere by hand is left alone.
        /// </summary>
        private static SkillDefinition Skill(
            string asset,
            string display,
            Action<SkillContentFactory.SkillFields> fill,
            string cast,
            string projectile,
            string hit,
            float scale = 1f,
            float lift = 0f,
            float yaw = 0f,
            string castSound = null,
            string hitSound = null)
        {
            SkillDefinition skill = SkillContentFactory.Skill(asset, display, fill);

            SkillVfxSet set = SkillVfxContentFactory.Set(
                "Vfx" + asset, Fx(cast), Fx(projectile), Fx(hit), scale, lift, yaw,
                Snd(castSound), Snd(hitSound));

            var serialized = new SerializedObject(skill);
            SerializedProperty field = serialized.FindProperty("_vfx");

            if (field.objectReferenceValue == null && set != null)
            {
                field.objectReferenceValue = set;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return skill;
        }

        private static string Fx(string path)
            => string.IsNullOrEmpty(path) ? null : $"{Pack}/{path}.prefab";

        /// <summary>
        /// A clip under the pack's Sound folder — only for a set whose prefabs
        /// carry none: the pack's missiles and explosions bring their own.
        /// </summary>
        private static string Snd(string path)
            => string.IsNullOrEmpty(path) ? null : $"{SkillVfxContentFactory.Sounds}/{path}.wav";

        // ─────────────────────────────────────────────────────────────────
        // Gems
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// A gem for every arsenal skill that is not a helper, a hundred supports,
        /// and a rod with seven empty holes to try them in. All of it goes into the
        /// chest table: a gem that cannot be found is combined with nothing.
        /// </summary>
        public static void CreateGems(LootTable table)
        {
            CreateSkills();

            var shelf = new Shelf(table);

            ActiveGems(shelf);
            PatternGems(shelf);
            EchoGems(shelf);
            ImpactGems(shelf);
            CritAndReachGems(shelf);
            InfusionGems(shelf);
            TriggerGems(shelf);
            ShapeGems(shelf);
            KillGems(shelf);
            StatusGems(shelf);
            LeverGems(shelf);

            // One group of seven: a skill and six supports, nothing welded.
            BuildLibraryFactory.Testbed(
                "ArsenalRod", "Arsenal Rod", EquipmentSlot.MainHand, "Wands",
                new[] { 0, 0, 0, 0, 0, 0, 0 }, table);
        }

        private static void ActiveGems(Shelf shelf)
        {
            shelf.Active("Meteor", "Meteor", ItemRarity.Epic, DamageType.Fire);
            shelf.Active("MeteorShower", "Meteor Shower", ItemRarity.Epic, DamageType.Fire);
            shelf.Active("Starfall", "Starfall", ItemRarity.Rare, DamageType.Lightning);
            shelf.Active("Blizzard", "Blizzard", ItemRarity.Rare, DamageType.Cold);
            shelf.Active("Thunderstorm", "Thunderstorm", ItemRarity.Epic, DamageType.Lightning);
            shelf.Active("HammerOfJudgement", "Hammer of Judgement", ItemRarity.Legendary, DamageType.Lightning);
            shelf.Active("Earthshatter", "Earthshatter", ItemRarity.Rare, DamageType.Physical);
            shelf.Active("FlameWave", "Flame Wave", ItemRarity.Uncommon, DamageType.Fire);
            shelf.Active("GlacialCascade", "Glacial Cascade", ItemRarity.Rare, DamageType.Cold);
            shelf.Active("VoltaicRift", "Voltaic Rift", ItemRarity.Uncommon, DamageType.Lightning);
            shelf.Active("BladeFan", "Blade Fan", ItemRarity.Uncommon, DamageType.Physical);
            shelf.Active("FrostShards", "Frost Shards", ItemRarity.Uncommon, DamageType.Cold);
            shelf.Active("SparkRing", "Spark Ring", ItemRarity.Uncommon, DamageType.Lightning);
            shelf.Active("HellfireBarrage", "Hellfire Barrage", ItemRarity.Rare, DamageType.Fire);
            shelf.Active("Comet", "Comet", ItemRarity.Rare, DamageType.Cold);
            shelf.Active("LeapSlam", "Leap Slam", ItemRarity.Uncommon, DamageType.Physical);
            shelf.Active("StormDive", "Storm Dive", ItemRarity.Rare, DamageType.Lightning);
            shelf.Active("CataclysmLeap", "Cataclysm Leap", ItemRarity.Epic, DamageType.Fire);
            shelf.Active("WhirlingBlades", "Whirling Blades", ItemRarity.Uncommon, DamageType.Physical);
            shelf.Active("InfernoSpin", "Inferno Spin", ItemRarity.Rare, DamageType.Fire);
            shelf.Active("FrostVortex", "Frost Vortex", ItemRarity.Rare, DamageType.Cold);
            shelf.Active("TectonicSlam", "Tectonic Slam", ItemRarity.Epic, DamageType.Physical);
            shelf.Active("DeathBlossom", "Death Blossom", ItemRarity.Epic, DamageType.Physical);
            shelf.Active("PlagueBurst", "Plague Burst", ItemRarity.Rare, DamageType.Chaos);
            shelf.Active("SoulRend", "Soul Rend", ItemRarity.Rare, DamageType.Chaos);

            shelf.Active("VoidOrb", "Void Orb", ItemRarity.Epic, DamageType.Chaos);
            shelf.Active("DoomOrb", "Doom Orb", ItemRarity.Legendary, DamageType.Fire);
            shelf.Active("LavaBomb", "Lava Bomb", ItemRarity.Rare, DamageType.Fire);
            shelf.Active("AcidFlask", "Acid Flask", ItemRarity.Uncommon, DamageType.Chaos);
            shelf.Active("PlasmaBolt", "Plasma Bolt", ItemRarity.Rare, DamageType.Lightning);
            shelf.Active("ShadowDaggers", "Shadow Daggers", ItemRarity.Uncommon, DamageType.Chaos);
            shelf.Active("DragonsBreath", "Dragon's Breath", ItemRarity.Rare, DamageType.Fire);
            shelf.Active("RendingClaws", "Rending Claws", ItemRarity.Uncommon, DamageType.Physical);
            shelf.Active("PillarsOfDawn", "Pillars of Dawn", ItemRarity.Rare, DamageType.Lightning);
            shelf.Active("FireTornado", "Fire Tornado", ItemRarity.Epic, DamageType.Fire);
            shelf.Active("Tempest", "Tempest", ItemRarity.Epic, DamageType.Lightning);
            shelf.Active("SearingRay", "Searing Ray", ItemRarity.Rare, DamageType.Fire);
        }

        /// <summary>How many elements a pattern has, and how fast they come.</summary>
        private static void PatternGems(Shelf shelf)
        {
            shelf.Support("Barrage", "Barrage", ItemRarity.Uncommon,
                Mod("Barrage", SkillModifierKind.AddedCount, 3f));

            shelf.Support("Deluge", "Deluge", ItemRarity.Rare,
                Mod("Deluge", SkillModifierKind.AddedCount, 6f),
                Mod("DelugeWeak", SkillModifierKind.IncreasedDamage, -25f));

            shelf.Support("Torrent", "Torrent", ItemRarity.Epic,
                Mod("Torrent", SkillModifierKind.AddedCount, 10f),
                Mod("TorrentCost", SkillModifierKind.IncreasedManaCost, 60f));

            shelf.Support("Swarm", "Swarm", ItemRarity.Rare,
                When("Swarm", SkillModifierKind.AddedCount, 4f, ModifierConditionType.TargetCrowded, count: 4));

            shelf.Support("Splinterstorm", "Splinterstorm", ItemRarity.Rare,
                Mod("Splinterstorm", SkillModifierKind.AddedCount, 2f),
                Mod("SplinterstormTempo", SkillModifierKind.PatternTempo, 25f));

            shelf.Support("ConcentratedFire", "Concentrated Fire", ItemRarity.Rare,
                Mod("ConcentratedFire", SkillModifierKind.IncreasedDamage, 60f),
                Mod("ConcentratedFireFewer", SkillModifierKind.AddedCount, -2f));

            shelf.Support("Hailstorm", "Hailstorm", ItemRarity.Rare,
                When("Hailstorm", SkillModifierKind.AddedCount, 5f,
                    ModifierConditionType.TargetHasStatusEffect, status: StatusEffectType.Slow));

            shelf.Support("Wildfire", "Wildfire", ItemRarity.Rare,
                When("Wildfire", SkillModifierKind.AddedCount, 5f,
                    ModifierConditionType.TargetHasStatus, element: DamageType.Fire));

            shelf.Support("RapidSequence", "Rapid Sequence", ItemRarity.Uncommon,
                Mod("RapidSequence", SkillModifierKind.PatternTempo, 50f));

            shelf.Support("SlowBurn", "Slow Burn", ItemRarity.Rare,
                Mod("SlowBurn", SkillModifierKind.PatternTempo, -40f),
                Mod("SlowBurnDamage", SkillModifierKind.IncreasedDamage, 45f));

            shelf.Support("Blitz", "Blitz", ItemRarity.Rare,
                Mod("Blitz", SkillModifierKind.PatternTempo, 100f),
                Mod("BlitzCost", SkillModifierKind.IncreasedManaCost, 40f));

            shelf.Support("Frenzy", "Frenzy", ItemRarity.Rare,
                When("Frenzy", SkillModifierKind.PatternTempo, 60f, ModifierConditionType.TargetCrowded, count: 4));

            shelf.Support("MeasuredCadence", "Measured Cadence", ItemRarity.Rare,
                Mod("MeasuredCadence", SkillModifierKind.AddedCount, 3f),
                Mod("MeasuredCadenceTempo", SkillModifierKind.PatternTempo, -25f));
        }

        /// <summary>The whole cast, again.</summary>
        private static void EchoGems(Shelf shelf)
        {
            shelf.Support("Echo", "Echo", ItemRarity.Rare,
                Mod("Echo", SkillModifierKind.Echo, 1f));

            shelf.Support("Resonance", "Resonance", ItemRarity.Epic,
                Mod("Resonance", SkillModifierKind.Echo, 2f),
                Mod("ResonanceWeak", SkillModifierKind.IncreasedDamage, -30f));

            shelf.Support("Reverberation", "Reverberation", ItemRarity.Rare,
                When("Reverberation", SkillModifierKind.Echo, 1f, ModifierConditionType.TargetCrowded, count: 5));

            shelf.Support("EndlessEcho", "Endless Echo", ItemRarity.Epic,
                Mod("EndlessEcho", SkillModifierKind.Echo, 3f),
                Mod("EndlessEchoSlow", SkillModifierKind.ReducedCooldown, -80f));

            shelf.Support("Thunderclap", "Thunderclap", ItemRarity.Rare,
                When("Thunderclap", SkillModifierKind.Echo, 1f,
                    ModifierConditionType.TargetHasStatusEffect, status: StatusEffectType.Shock));

            shelf.Support("Ripple", "Ripple", ItemRarity.Epic,
                Mod("Ripple", SkillModifierKind.Echo, 1f),
                Mod("RippleArea", SkillModifierKind.IncreasedArea, 30f));

            shelf.Support("DeathKnell", "Death Knell", ItemRarity.Epic,
                When("DeathKnell", SkillModifierKind.Echo, 2f, ModifierConditionType.TargetLowHealth, threshold: 0.35f));

            shelf.Support("OpeningSalvo", "Opening Salvo", ItemRarity.Uncommon,
                When("OpeningSalvo", SkillModifierKind.Echo, 1f, ModifierConditionType.TargetHighHealth, threshold: 0.9f));
        }

        /// <summary>Projectiles that burst where they land.</summary>
        private static void ImpactGems(Shelf shelf)
        {
            shelf.Support("ConcussiveRounds", "Concussive Rounds", ItemRarity.Uncommon,
                Mod("ConcussiveRounds", SkillModifierKind.ImpactBurst, 2f));

            shelf.Support("DetonatingShells", "Detonating Shells", ItemRarity.Rare,
                Mod("DetonatingShells", SkillModifierKind.ImpactBurst, 3.5f),
                Mod("DetonatingShellsWeak", SkillModifierKind.IncreasedDamage, -20f));

            shelf.Support("ClusterCharge", "Cluster Charge", ItemRarity.Epic,
                Mod("ClusterCharge", SkillModifierKind.ImpactBurst, 2.5f),
                Mod("ClusterChargeFork", SkillModifierKind.Fork, 1f));

            shelf.Support("Shrapnel", "Shrapnel", ItemRarity.Rare,
                Mod("Shrapnel", SkillModifierKind.ImpactBurst, 1.5f),
                Mod("ShrapnelPierce", SkillModifierKind.Pierce, 1f));

            shelf.Support("SiegePayload", "Siege Payload", ItemRarity.Epic,
                Mod("SiegePayload", SkillModifierKind.ImpactBurst, 4.5f),
                Mod("SiegePayloadSlow", SkillModifierKind.IncreasedProjectileSpeed, -40f));

            shelf.Support("CrowdBurster", "Crowd Burster", ItemRarity.Rare,
                When("CrowdBurster", SkillModifierKind.ImpactBurst, 3f, ModifierConditionType.TargetCrowded, count: 3));
        }

        private static void CritAndReachGems(Shelf shelf)
        {
            shelf.Support("BrutalCriticals", "Brutal Criticals", ItemRarity.Rare,
                Mod("BrutalCriticals", SkillModifierKind.IncreasedCritMultiplier, 60f));

            shelf.Support("AssassinsMark", "Assassins Mark", ItemRarity.Epic,
                When("AssassinsMark", SkillModifierKind.IncreasedCritMultiplier, 120f,
                    ModifierConditionType.TargetLowHealth, threshold: 0.5f));

            shelf.Support("Headhunter", "Headhunter", ItemRarity.Epic,
                Mod("Headhunter", SkillModifierKind.IncreasedCritChance, 80f),
                Mod("HeadhunterMultiplier", SkillModifierKind.IncreasedCritMultiplier, 40f));

            shelf.Support("GlassEdge", "Glass Edge", ItemRarity.Epic,
                Mod("GlassEdge", SkillModifierKind.IncreasedCritMultiplier, 150f),
                Mod("GlassEdgeWeak", SkillModifierKind.IncreasedDamage, -30f));

            shelf.Support("CoupDeGrace", "Coup de Grace", ItemRarity.Rare,
                When("CoupDeGrace", SkillModifierKind.IncreasedCritMultiplier, 100f,
                    ModifierConditionType.TargetHasStatusEffect, status: StatusEffectType.Stun));

            shelf.Support("LongReach", "Long Reach", ItemRarity.Uncommon,
                Mod("LongReach", SkillModifierKind.IncreasedChainRange, 60f));

            shelf.Support("ArcLattice", "Arc Lattice", ItemRarity.Rare,
                Mod("ArcLattice", SkillModifierKind.AddedChains, 1f),
                Mod("ArcLatticeReach", SkillModifierKind.IncreasedChainRange, 40f));

            shelf.Support("TightCircuit", "Tight Circuit", ItemRarity.Rare,
                Mod("TightCircuit", SkillModifierKind.AddedChains, 4f),
                Mod("TightCircuitReach", SkillModifierKind.IncreasedChainRange, -30f));

            shelf.Support("Stormweb", "Stormweb", ItemRarity.Rare,
                When("Stormweb", SkillModifierKind.AddedChains, 2f, ModifierConditionType.TargetCrowded, count: 4));
        }

        /// <summary>
        /// The element half of a reaction, carried by the skill. Fire reacts with
        /// the most — cold, physical and lightning blows all have a rule for it —
        /// so it is the plain one; the weaves carry two at once.
        /// </summary>
        private static void InfusionGems(Shelf shelf)
        {
            shelf.Support("FireInfusion", "Fire Infusion", ItemRarity.Uncommon, Infuse("FireInfusion", DamageType.Fire));
            shelf.Support("FrostInfusion", "Frost Infusion", ItemRarity.Uncommon, Infuse("FrostInfusion", DamageType.Cold));
            shelf.Support("StormInfusion", "Storm Infusion", ItemRarity.Uncommon, Infuse("StormInfusion", DamageType.Lightning));
            shelf.Support("VenomInfusion", "Venom Infusion", ItemRarity.Uncommon, Infuse("VenomInfusion", DamageType.Chaos));

            shelf.Support("Pyroclasm", "Pyroclasm", ItemRarity.Epic,
                Infuse("Pyroclasm", DamageType.Fire),
                SkillContentFactory.Modifier("SupportPyroclasmBurst", SkillModifierKind.ExplodeOnKill, 3f, secondary: 50f));

            shelf.Support("PermafrostCore", "Permafrost Core", ItemRarity.Rare,
                Infuse("PermafrostCore", DamageType.Cold),
                StatusOverride("PermafrostCoreSlow", StatusEffectType.Slow));

            shelf.Support("OverloadCoil", "Overload Coil", ItemRarity.Rare,
                Infuse("OverloadCoil", DamageType.Lightning),
                Mod("OverloadCoilChain", SkillModifierKind.AddedChains, 1f));

            shelf.Support("BlightSeed", "Blight Seed", ItemRarity.Rare,
                Infuse("BlightSeed", DamageType.Chaos),
                Mod("BlightSeedCull", SkillModifierKind.CullingStrike, 8f));

            shelf.Support("ElementalWeave", "Elemental Weave", ItemRarity.Epic,
                Infuse("ElementalWeave", DamageType.Fire),
                Infuse("ElementalWeaveStorm", DamageType.Lightning));

            shelf.Support("TempestWeave", "Tempest Weave", ItemRarity.Epic,
                Infuse("TempestWeave", DamageType.Cold),
                Infuse("TempestWeaveStorm", DamageType.Lightning));
        }

        /// <summary>
        /// Skills that go off where this one lands. The loudest gems in the game,
        /// and the depth rail is what keeps them from feeding each other forever.
        /// </summary>
        private static void TriggerGems(Shelf shelf)
        {
            shelf.Support("EmberTrail", "Ember Trail", ItemRarity.Rare, Trigger("EmberTrail", "EmberGround", 70f));
            shelf.Support("Petalstorm", "Petalstorm", ItemRarity.Epic, Trigger("Petalstorm", "PetalStorm", 50f));
            shelf.Support("ShatterRing", "Shatter Ring", ItemRarity.Epic, Trigger("ShatterRing", "IceRing", 50f));
            shelf.Support("Aftershock", "Aftershock", ItemRarity.Epic, Trigger("Aftershock", "Aftershock", 60f));
            shelf.Support("Starcall", "Starcall", ItemRarity.Epic, Trigger("Starcall", "FallingStar", 45f));
            shelf.Support("StaticDischarge", "Static Discharge", ItemRarity.Rare, Trigger("StaticDischarge", "StaticBurst", 50f));
            shelf.Support("Rimefall", "Rimefall", ItemRarity.Rare, Trigger("Rimefall", "FrostCrater", 70f));
            shelf.Support("VenomousImpact", "Venomous Impact", ItemRarity.Rare, Trigger("VenomousImpact", "VenomBurst", 60f));
            shelf.Support("Meteoric", "Meteoric", ItemRarity.Epic, Trigger("Meteoric", "EmberMeteor", 50f));
            shelf.Support("BladeTempest", "Blade Tempest", ItemRarity.Epic, Trigger("BladeTempest", "BladeWhirl", 60f));
            shelf.Support("Hellrain", "Hellrain", ItemRarity.Legendary, Trigger("Hellrain", "MeteorShower", 30f));
            shelf.Support("StormCall", "Storm Call", ItemRarity.Rare, Trigger("StormCall", "StormLance", 50f));

            shelf.Support("CarnageEngine", "Carnage Engine", ItemRarity.Epic,
                SkillContentFactory.Modifier("SupportCarnageEngine", SkillModifierKind.TriggerOnCondition, 0f,
                    triggerCondition: TriggerConditionType.OnKill, triggerCooldown: 0.6f, procChance: 0.25f));

            shelf.Support("Catalyst", "Catalyst", ItemRarity.Epic,
                SkillContentFactory.Modifier("SupportCatalyst", SkillModifierKind.TriggerOnCondition, 0f,
                    triggerCondition: TriggerConditionType.OnStatusApplied, triggerCooldown: 1f, procChance: 0.3f));

            shelf.Support("CriticalCascade", "Critical Cascade", ItemRarity.Epic,
                SkillContentFactory.Modifier("SupportCriticalCascade", SkillModifierKind.TriggerOnCondition, 0f,
                    triggerCondition: TriggerConditionType.OnCrit, triggerCooldown: 0.5f, procChance: 0.4f));
        }

        /// <summary>Forks, pierces, copies and areas, bought with something.</summary>
        private static void ShapeGems(Shelf shelf)
        {
            shelf.Support("Trifurcation", "Trifurcation", ItemRarity.Epic,
                Mod("Trifurcation", SkillModifierKind.Fork, 2f),
                Mod("TrifurcationWeak", SkillModifierKind.IncreasedDamage, -20f));

            shelf.Support("GalvanicSplit", "Galvanic Split", ItemRarity.Rare,
                When("GalvanicSplit", SkillModifierKind.Fork, 1f,
                    ModifierConditionType.TargetHasStatusEffect, status: StatusEffectType.Shock));

            shelf.Support("Impaler", "Impaler", ItemRarity.Rare,
                Mod("Impaler", SkillModifierKind.Pierce, 4f),
                Mod("ImpalerSpeed", SkillModifierKind.IncreasedProjectileSpeed, 30f));

            shelf.Support("Skewer", "Skewer", ItemRarity.Rare,
                When("Skewer", SkillModifierKind.Pierce, 3f,
                    ModifierConditionType.TargetHasStatusEffect, status: StatusEffectType.Slow));

            shelf.Support("TwinnedFury", "Twinned Fury", ItemRarity.Epic,
                Mod("TwinnedFury", SkillModifierKind.Multicast, 2f),
                Mod("TwinnedFuryCost", SkillModifierKind.IncreasedManaCost, 80f));

            shelf.Support("MobJustice", "Mob Justice", ItemRarity.Rare,
                When("MobJustice", SkillModifierKind.Multicast, 1f, ModifierConditionType.TargetCrowded, count: 5));

            shelf.Support("Scattershot", "Scattershot", ItemRarity.Epic,
                Mod("Scattershot", SkillModifierKind.Multicast, 2f),
                Mod("ScattershotSpread", SkillModifierKind.IncreasedSpread, 250f));

            shelf.Support("TitanicReach", "Titanic Reach", ItemRarity.Rare,
                Mod("TitanicReach", SkillModifierKind.IncreasedArea, 60f),
                Mod("TitanicReachCost", SkillModifierKind.IncreasedManaCost, 35f));

            shelf.Support("Colossal", "Colossal", ItemRarity.Epic,
                Mod("Colossal", SkillModifierKind.IncreasedArea, 120f),
                Mod("ColossalSlow", SkillModifierKind.ReducedCooldown, -50f));

            shelf.Support("Pinpoint", "Pinpoint", ItemRarity.Rare,
                Mod("Pinpoint", SkillModifierKind.IncreasedDamage, 70f),
                Mod("PinpointNarrow", SkillModifierKind.IncreasedArea, -40f));

            shelf.Support("Momentum", "Momentum", ItemRarity.Rare,
                Mod("Momentum", SkillModifierKind.IncreasedProjectileSpeed, 100f),
                Mod("MomentumDamage", SkillModifierKind.IncreasedDamage, 20f));

            shelf.Support("HeavyShot", "Heavy Shot", ItemRarity.Rare,
                Mod("HeavyShot", SkillModifierKind.IncreasedDamage, 55f),
                Mod("HeavyShotSlow", SkillModifierKind.IncreasedProjectileSpeed, -50f));

            shelf.Support("TidalForce", "Tidal Force", ItemRarity.Rare,
                When("TidalForce", SkillModifierKind.IncreasedDamage, 60f, ModifierConditionType.TargetCrowded, count: 6));

            shelf.Support("Bully", "Bully", ItemRarity.Rare,
                When("Bully", SkillModifierKind.IncreasedDamage, 80f,
                    ModifierConditionType.TargetHasStatusEffect, status: StatusEffectType.Stun));
        }

        /// <summary>What a death is worth: bursts, culls and mana.</summary>
        private static void KillGems(Shelf shelf)
        {
            shelf.Support("CorpseDetonation", "Corpse Detonation", ItemRarity.Epic,
                SkillContentFactory.Modifier("SupportCorpseDetonation", SkillModifierKind.ExplodeOnKill, 4.5f, secondary: 90f),
                Mod("CorpseDetonationWeak", SkillModifierKind.IncreasedDamage, -20f));

            shelf.Support("Frostburst", "Frostburst", ItemRarity.Rare,
                When("Frostburst", SkillModifierKind.ExplodeOnKill, 3.5f,
                    ModifierConditionType.TargetHasStatus, element: DamageType.Cold, secondary: 70f));

            shelf.Support("Shockburst", "Shockburst", ItemRarity.Rare,
                When("Shockburst", SkillModifierKind.ExplodeOnKill, 3.5f,
                    ModifierConditionType.TargetHasStatusEffect, status: StatusEffectType.Shock, secondary: 70f));

            shelf.Support("Plaguebearer", "Plaguebearer", ItemRarity.Rare,
                When("Plaguebearer", SkillModifierKind.ExplodeOnKill, 4f,
                    ModifierConditionType.TargetHasStatus, element: DamageType.Chaos, secondary: 60f));

            shelf.Support("Bloodburst", "Bloodburst", ItemRarity.Rare,
                When("Bloodburst", SkillModifierKind.ExplodeOnKill, 3.5f,
                    ModifierConditionType.TargetHasStatusEffect, status: StatusEffectType.Bleed, secondary: 80f));

            shelf.Support("ChainReaction", "Chain Reaction", ItemRarity.Epic,
                SkillContentFactory.Modifier("SupportChainReaction", SkillModifierKind.ExplodeOnKill, 3f, secondary: 60f),
                Mod("ChainReactionJump", SkillModifierKind.AddedChains, 1f));

            shelf.Support("Reaper", "Reaper", ItemRarity.Epic,
                Mod("Reaper", SkillModifierKind.CullingStrike, 15f),
                Mod("ReaperMana", SkillModifierKind.ManaOnKill, 3f));

            shelf.Support("MercyKill", "Mercy Kill", ItemRarity.Rare,
                When("MercyKill", SkillModifierKind.CullingStrike, 25f,
                    ModifierConditionType.TargetHasStatusEffect, status: StatusEffectType.Stun));

            shelf.Support("SoulSiphon", "Soul Siphon", ItemRarity.Rare,
                Mod("SoulSiphon", SkillModifierKind.ManaOnKill, 8f),
                Mod("SoulSiphonWeak", SkillModifierKind.IncreasedDamage, -15f));

            shelf.Support("ManaTide", "Mana Tide", ItemRarity.Uncommon,
                When("ManaTide", SkillModifierKind.ManaOnKill, 3f, ModifierConditionType.TargetCrowded, count: 4));
        }

        /// <summary>What the blow leaves behind, and what the skill deals.</summary>
        private static void StatusGems(Shelf shelf)
        {
            shelf.Support("ViciousWounds", "Vicious Wounds", ItemRarity.Uncommon, StatusOverride("ViciousWounds", StatusEffectType.Bleed));
            shelf.Support("ToxicPayload", "Toxic Payload", ItemRarity.Uncommon, StatusOverride("ToxicPayload", StatusEffectType.Poison));
            shelf.Support("Kindling", "Kindling", ItemRarity.Uncommon, StatusOverride("Kindling", StatusEffectType.Ignite));
            shelf.Support("Exposure", "Exposure", ItemRarity.Rare, StatusOverride("Exposure", StatusEffectType.Vulnerable));
            shelf.Support("Enfeeble", "Enfeeble", ItemRarity.Rare, StatusOverride("Enfeeble", StatusEffectType.Weaken));
            shelf.Support("Terror", "Terror", ItemRarity.Epic, StatusOverride("Terror", StatusEffectType.Fear));
            shelf.Support("ScorchingPayload", "Scorching Payload", ItemRarity.Rare, StatusOverride("ScorchingPayload", StatusEffectType.Scorch));
            shelf.Support("Rimebrand", "Rimebrand", ItemRarity.Uncommon, StatusOverride("Rimebrand", StatusEffectType.Chill));
            shelf.Support("StaticBrand", "Static Brand", ItemRarity.Uncommon, StatusOverride("StaticBrand", StatusEffectType.Shock));
            shelf.Support("Crippling", "Crippling", ItemRarity.Uncommon, StatusOverride("Crippling", StatusEffectType.Slow));

            shelf.Support("Emberbrand", "Emberbrand", ItemRarity.Uncommon, Conversion("Emberbrand", DamageType.Fire));
            shelf.Support("Stormbrand", "Stormbrand", ItemRarity.Uncommon, Conversion("Stormbrand", DamageType.Lightning));
            shelf.Support("Voidbrand", "Voidbrand", ItemRarity.Uncommon, Conversion("Voidbrand", DamageType.Chaos));
        }

        /// <summary>The price of a press, the cooldown and the charges.</summary>
        private static void LeverGems(Shelf shelf)
        {
            shelf.Support("ArcaneFlow", "Arcane Flow", ItemRarity.Rare,
                Mod("ArcaneFlow", SkillModifierKind.ReducedCooldown, 20f),
                Mod("ArcaneFlowCost", SkillModifierKind.IncreasedManaCost, -20f));

            shelf.Support("BattleRhythm", "Battle Rhythm", ItemRarity.Rare,
                When("BattleRhythm", SkillModifierKind.ReducedCooldown, 50f, ModifierConditionType.TargetCrowded, count: 5));

            shelf.Support("DeepReserves", "Deep Reserves", ItemRarity.Epic,
                Mod("DeepReserves", SkillModifierKind.AddedCharges, 2f),
                Mod("DeepReservesSlow", SkillModifierKind.ReducedCooldown, -30f));

            shelf.Support("ReserveCharge", "Reserve Charge", ItemRarity.Rare,
                Mod("ReserveCharge", SkillModifierKind.AddedCharges, 1f),
                Mod("ReserveChargeCost", SkillModifierKind.IncreasedManaCost, 25f));

            shelf.Support("LingeringBlight", "Lingering Blight", ItemRarity.Rare,
                Mod("LingeringBlight", SkillModifierKind.IncreasedDuration, 50f),
                Mod("LingeringBlightArea", SkillModifierKind.IncreasedArea, 20f));
        }

        // ─────────────────────────────────────────────────────────────────
        // Modifier shorthands. Every asset is "Support" + name, create-or-load.
        // ─────────────────────────────────────────────────────────────────

        private static SkillModifier Mod(string name, SkillModifierKind kind, float value)
            => SkillContentFactory.Modifier("Support" + name, kind, value);

        private static SkillModifier When(
            string name,
            SkillModifierKind kind,
            float value,
            ModifierConditionType condition,
            DamageType element = DamageType.Fire,
            StatusEffectType status = StatusEffectType.Stun,
            float threshold = 0.35f,
            int count = 3,
            float secondary = 60f)
            => SkillContentFactory.Modifier(
                "Support" + name, kind, value,
                secondary: secondary,
                condition: condition,
                requiredElement: element,
                requiredStatus: status,
                threshold: threshold,
                requiredCount: count);

        private static SkillModifier Infuse(string name, DamageType element)
            => SkillContentFactory.Modifier("Support" + name, SkillModifierKind.InfuseElement, 0f, convertTo: element);

        private static SkillModifier Conversion(string name, DamageType element)
            => SkillContentFactory.Modifier("Support" + name, SkillModifierKind.ElementalConversion, 0f, convertTo: element);

        private static SkillModifier StatusOverride(string name, StatusEffectType status)
            => SkillContentFactory.Modifier("Support" + name, SkillModifierKind.StatusOverride, 0f, appliedStatus: status);

        /// <summary>A trigger-on-hit at this share of the named skill's damage, or null when that skill is missing.</summary>
        private static SkillModifier Trigger(string name, string skillAsset, float share)
        {
            var skill = SceneBuildUtility.LoadConfig<SkillDefinition>(skillAsset);

            if (skill == null)
            {
                Debug.LogWarning(
                    $"[{nameof(ArsenalContentFactory)}] No skill '{skillAsset}' for the '{name}' trigger gem — skipped.");
                return null;
            }

            return SkillContentFactory.Modifier("Support" + name, SkillModifierKind.TriggerOnHit, share, triggered: skill);
        }

        /// <summary>Stable pick out of a count, by the FNV hash items already use.</summary>
        private static int Pick(string text, int count)
            => (int)((uint)ItemDefinition.ComputeId(text) % (uint)Mathf.Max(1, count));

        /// <summary>
        /// Puts gems into the chest table with an icon off the gem sheet. The
        /// icon is written only when the gem has none, so a hand-picked one stays.
        /// </summary>
        private sealed class Shelf
        {
            private readonly LootTable _table;
            private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

            public Shelf(LootTable table)
            {
                _table = table;

                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(GemSheet))
                {
                    if (asset is Sprite sprite)
                        _sprites[sprite.name] = sprite;
                }
            }

            public void Active(string skillAsset, string display, ItemRarity rarity, DamageType element)
            {
                var skill = SceneBuildUtility.LoadConfig<SkillDefinition>(skillAsset);

                if (skill == null)
                {
                    Debug.LogWarning($"[{nameof(ArsenalContentFactory)}] No skill '{skillAsset}' for its gem — skipped.");
                    return;
                }

                ItemDefinition gem = SkillContentFactory.ActiveGem(
                    "Gem" + skillAsset, display + " Gem", skill, rarity, _table);

                int[] family = IconsOf(element);
                Icon(gem, family[Pick(display, family.Length)]);
            }

            public void Support(
                string name, string display, ItemRarity rarity, SkillModifier first, SkillModifier second = null)
            {
                // A trigger whose skill is missing has already said so.
                if (first == null)
                    return;

                ItemDefinition gem = SkillContentFactory.SupportGem(
                    "Gem" + name, display + " Support", first, rarity, _table, second);

                Icon(gem, Pick(display, SupportIconCount));
            }

            private void Icon(ItemDefinition gem, int index)
            {
                if (gem == null || !_sprites.TryGetValue($"Pack-ElementsGEMS_{index}", out Sprite sprite))
                    return;

                var serialized = new SerializedObject(gem);
                SerializedProperty icon = serialized.FindProperty("_icon");

                if (icon.objectReferenceValue != null)
                    return;

                icon.objectReferenceValue = sprite;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            private static int[] IconsOf(DamageType element)
            {
                switch (element)
                {
                    case DamageType.Fire: return FireIcons;
                    case DamageType.Cold: return ColdIcons;
                    case DamageType.Lightning: return LightningIcons;
                    case DamageType.Chaos: return ChaosIcons;
                    default: return PhysicalIcons;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Showcase kit
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Puts the rod, every arsenal skill gem and a handful of the supports
        /// that change them most into the starting kit. Additive, like the
        /// library kit: listed items are left alone and nothing is removed.
        /// </summary>
        [MenuItem("Tools/Together We Fall/Grant Arsenal Showcase Kit")]
        public static void GrantShowcaseKit()
        {
            LootTable table = ItemContentFactory.CreateOrLoadTreasureTable();
            CreateGems(table);

            var wanted = new[]
            {
                "ArsenalRod",

                "GemMeteor", "GemMeteorShower", "GemStarfall", "GemBlizzard", "GemThunderstorm",
                "GemHammerOfJudgement", "GemEarthshatter", "GemFlameWave", "GemGlacialCascade",
                "GemVoltaicRift", "GemBladeFan", "GemFrostShards", "GemSparkRing", "GemHellfireBarrage",
                "GemComet", "GemLeapSlam", "GemStormDive", "GemCataclysmLeap", "GemWhirlingBlades",
                "GemInfernoSpin", "GemFrostVortex", "GemTectonicSlam", "GemDeathBlossom",
                "GemPlagueBurst", "GemSoulRend",

                "GemVoidOrb", "GemDoomOrb", "GemLavaBomb", "GemAcidFlask", "GemPlasmaBolt",
                "GemShadowDaggers", "GemDragonsBreath", "GemRendingClaws", "GemPillarsOfDawn",
                "GemFireTornado", "GemTempest", "GemSearingRay",

                "GemEcho", "GemBarrage", "GemConcussiveRounds", "GemFireInfusion", "GemStormInfusion",
                "GemRapidSequence", "GemShatterRing", "GemAftershock", "GemResonance", "GemCorpseDetonation"
            };

            var kit = SceneBuildUtility.CreateOrLoadConfig<StarterKitConfig>("StarterKitConfig");
            var serialized = new SerializedObject(kit);
            SerializedProperty entries = serialized.FindProperty("_entries");
            int added = 0;

            foreach (string name in wanted)
            {
                var item = SceneBuildUtility.LoadConfig<ItemDefinition>(name);

                if (item == null || Lists(entries, item))
                    continue;

                SceneBuildUtility.AppendKitEntry(entries, item, worn: false, count: 1);
                added++;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[{nameof(ArsenalContentFactory)}] Showcase kit: {added} item(s) added to {kit.name}. " +
                "Rebuild the scene so the skill database and the effect presenter know the arsenal.");
        }

        internal static bool Lists(SerializedProperty entries, Object item)
        {
            for (int i = 0; i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("_item").objectReferenceValue == item)
                    return true;
            }

            return false;
        }
    }
}
