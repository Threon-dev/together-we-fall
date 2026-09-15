using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Player
{
    /// <summary>
    /// Puts what the character did on the model: walking, casting, being hurt,
    /// dying, and the weapon they are wearing.
    ///
    /// Locomotion comes from the controller's velocity, not the input: a
    /// character pushing into a wall should stand, not run on the spot. The
    /// velocity goes into the character's own space, because facing follows aim
    /// and walking follows the keys — backing away from a crowd is MoveZ below
    /// zero, and that is what picks the backward run out of the blend tree.
    ///
    /// Everything else is read off the character entity as state, never as a
    /// queue: a cast counter that moved, health that went down, Dead raised,
    /// what is in the main hand. State cannot be drained by someone else first,
    /// and a missed frame costs nothing because the next one still shows the
    /// difference.
    ///
    /// Drawn or sheathed is this class's own business and nobody else's. The
    /// host decides what is worn and what gets cast; whether the weapon is on the
    /// back or in the hand while it happens changes no number anywhere.
    ///
    /// Presentation only: nothing reads any of this back.
    /// </summary>
    public sealed class PlayerAnimationPresenter : MonoBehaviour
    {
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
        private static readonly int WeaponBlendHash = Animator.StringToHash("WeaponBlend");
        private static readonly int WeaponHash = Animator.StringToHash("Weapon");
        private static readonly int GripHash = Animator.StringToHash("Grip");
        private static readonly int DrawHash = Animator.StringToHash("Draw");
        private static readonly int SheathHash = Animator.StringToHash("Sheath");
        private static readonly int AimingHash = Animator.StringToHash("Aiming");
        private static readonly int CastHash = Animator.StringToHash("Cast");
        private static readonly int CastKindHash = Animator.StringToHash("CastKind");
        private static readonly int AttackIndexHash = Animator.StringToHash("AttackIndex");
        private static readonly int SwingRateHash = Animator.StringToHash("SwingRate");
        private static readonly int HitHash = Animator.StringToHash("Hit");
        private static readonly int DeadHash = Animator.StringToHash("Dead");

        /// <summary>Casts, swings, shots, flinches, draw and sheath. See CharacterContentFactory.</summary>
        private const int UpperBodyLayer = 1;

        /// <summary>
        /// Swings per stance in the controller: two each way, even indices
        /// sweeping right — the parity CastCue.SweepsRight gives the slash.
        /// </summary>
        private const int SwingCount = 4;

        /// <summary>
        /// The stance a bow is filed under. Nothing, a sword in one hand and a
        /// sword in two are 0, 1 and 2 — the numbers CharacterContentFactory
        /// files its stances by and SceneBuildUtility writes into WeaponModel.
        /// </summary>
        private const int BowStance = 3;

        /// <summary>Standing easy — the Relax set, below every stance on WeaponBlend's line.</summary>
        private const int RelaxedStance = -1;

        /// <summary>
        /// Id and prefabs rather than the ItemDefinition itself, for the reason
        /// the inventory's icons give: a definition drags its skills and their
        /// effect prefabs into the scene with it.
        /// </summary>
        [Serializable]
        public struct WeaponModel
        {
            public int ItemId;
            public GameObject Prefab;

            /// <summary>Worn on the back for as long as the item is — a quiver. Null for most.</summary>
            public GameObject Back;

            public int Stance;
        }

        [SerializeField] private Animator _animator;

        [Header("Locomotion")]
        [Tooltip("Speed that maps to the edge of the blend tree — the run clips. " +
                 "Keep it equal to the motor's move speed.")]
        [SerializeField] private float _runSpeed = 6f;

        [Tooltip("Seconds the parameters take to settle. Zero snaps between " +
                 "directions; too much and the feet slide on a sharp turn.")]
        [SerializeField] private float _damping = 0.08f;

        [Tooltip("Seconds after the last cast or blow taken before the character " +
                 "drops their guard and stands easy. A drawn weapon keeps the " +
                 "guard up however long it has been.")]
        [SerializeField] private float _relaxDelay = 5f;

        [Tooltip("Seconds to ease between neighbouring stances — easy and guard, " +
                 "bare hands and a sword. Stances further apart snap instead, " +
                 "under the draw or sheath clip that separates them.")]
        [SerializeField] private float _stanceBlendSeconds = 0.35f;

        [Header("Reactions")]
        [Tooltip("Shortest gap between two flinches. A burn ticks many times a " +
                 "second, and a character flinching at every tick is never seen " +
                 "doing anything else.")]
        [SerializeField] private float _hitInterval = 0.5f;

        [Header("Swings")]
        [Tooltip("Share of the cooldown a swing takes. Under one, so a held " +
                 "button lets the blade come all the way round before the next.")]
        [SerializeField, Range(0.5f, 1f)] private float _swingShare = 0.9f;

        [Tooltip("Longest a swing takes, however long the cooldown. A slow " +
                 "skill should not turn into a slow-motion swing.")]
        [SerializeField] private float _longestSwing = 0.8f;

        [Tooltip("How far through a swing, from where it starts playing, the " +
                 "blade connects. The swing is paced so that moment lands when " +
                 "the host lands the blow (CastCue.StrikeDelay).")]
        [SerializeField, Range(0.05f, 0.95f)] private float _swingImpact = 0.35f;

        [Header("Weapons")]
        [Tooltip("Weapon bodies by item id. Written by the scene build from every " +
                 "item that has a model; an item missing here is worn invisibly.")]
        [SerializeField] private WeaponModel[] _weaponModels = Array.Empty<WeaponModel>();

        [Tooltip("Seconds without casting before the weapon goes back on the back.")]
        [SerializeField] private float _sheathDelay = 5f;

        [Tooltip("Seconds into the draw at which the hand reaches the grip and " +
                 "the weapon moves into it — and the swing that asked for the " +
                 "draw starts. Timed against CharacterContentFactory.DrawSpeed.")]
        [SerializeField] private float _drawSwapDelay = 0.16f;

        [Tooltip("Seconds into the sheath at which the weapon leaves the hand.")]
        [SerializeField] private float _sheathSwapDelay = 0.24f;

        [Tooltip("Where along the palm, from wrist to knuckles, the grip closes.")]
        [SerializeField, Range(0f, 1.5f)] private float _palmGrip = 0.6f;

        [Tooltip("Where a sword's grip sits on the back, from the chest bone, in " +
                 "the character's space: x right, y up, z forward.")]
        [SerializeField] private Vector3 _backGrip = new Vector3(0.15f, 0.2f, -0.2f);

        [Tooltip("How far a sword's blade leans towards the left hip. Zero hangs it straight down.")]
        [SerializeField] private float _backLean = 0.6f;

        [Header("Bows")]
        [Tooltip("Seconds after a shot the string stays drawn, ready for the next.")]
        [SerializeField] private float _aimHold = 1.2f;

        [Tooltip("Where a bow's grip sits on the back, from the chest bone, in " +
                 "the character's space. Further back than the quiver, so the " +
                 "two do not pass through each other.")]
        [SerializeField] private Vector3 _bowBackGrip = new Vector3(0f, 0.05f, -0.3f);

        [Tooltip("Where the quiver's middle sits on the back, from the chest bone.")]
        [SerializeField] private Vector3 _quiverGrip = new Vector3(0.12f, 0.05f, -0.2f);

        [Tooltip("How far the quiver's mouth leans towards the right shoulder, " +
                 "and the bow's upper limb with it.")]
        [SerializeField] private float _quiverLean = 0.35f;

        private Vector3 _lastPosition;

        private EntityManager _entityManager;
        private EntityQuery _characterQuery;
        private bool _hasWorld;
        private int _playerId;

        private bool _primed;
        private uint _seenCasts;
        private float _lastHealth;
        private float _lastMaxHealth;
        private float _nextHitTime;
        private float _lastCombatTime = float.NegativeInfinity;
        private float _blend = RelaxedStance;

        // Instances by prefab, weapons and quivers alike: taken off is hidden,
        // not destroyed, and put back on is shown again.
        private readonly Dictionary<GameObject, GameObject> _spawned = new Dictionary<GameObject, GameObject>();

        private int _wornId;
        private GameObject _worn;
        private GameObject _wornBack;
        private int _grip;
        private int _held;
        private bool _drawn;
        private float _lastCastTime = float.NegativeInfinity;

        /// <summary>
        /// What the last cast was, so a beam's next pulse is read as the same
        /// channel rather than a fresh cast.
        /// </summary>
        private SkillEffectKind _lastCastEffect;

        /// <summary>Longer than a beam's pulse and a late snapshot together.</summary>
        private const float ChannelGap = 0.3f;
        private float _swapAt = -1f;
        private bool _swapToHand;

        // The cast that found the weapon on the back, played once it is in hand.
        private bool _swingAfterDraw;
        private SkillEffectKind _drawnFor;
        private uint _drawnSwing;

        /// <summary>
        /// Told which character is this body, like the HUD: in coop there are
        /// several, and only one of them is standing here.
        /// </summary>
        public void Initialize(int playerId)
        {
            _playerId = playerId;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            _entityManager = world.EntityManager;
            _characterQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerCharacter>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<CastCue>(),
                ComponentType.ReadOnly<EquippedItem>());
            _hasWorld = true;
        }

        private void Awake() => _lastPosition = transform.position;

        // Late, so the motor has already moved the body this frame.
        private void LateUpdate()
        {
            if (_animator == null)
                return;

            // Measured from the transform rather than read off a CharacterController,
            // so the same presenter animates a remote player's body, which has none.
            Vector3 position = transform.position;
            Vector3 velocity = Time.deltaTime > 0f ? (position - _lastPosition) / Time.deltaTime : Vector3.zero;
            _lastPosition = position;
            velocity.y = 0f;

            Vector3 local = transform.InverseTransformDirection(velocity) / _runSpeed;

            _animator.SetFloat(MoveXHash, local.x, _damping, Time.deltaTime);
            _animator.SetFloat(MoveZHash, local.z, _damping, Time.deltaTime);

            // Easy out of a fight, the held weapon's stance in one. A weapon in
            // the hand, or one on its way in or out, is a fight whatever the
            // clock says.
            bool relaxed = !_drawn && _swapAt < 0f && Time.time - _lastCombatTime > _relaxDelay;
            float stance = relaxed ? RelaxedStance : _held;

            // Eased between neighbours, snapped across further. The stances sit
            // one after another on a line, so easing from bare hands to a bow
            // would walk the arms through both sword stances — and a jump that
            // far only happens at a swap, under a clip that covers the arms.
            _blend = Mathf.Abs(stance - _blend) > 1.01f
                ? stance
                : Mathf.MoveTowards(_blend, stance, Time.deltaTime / Mathf.Max(0.01f, _stanceBlendSeconds));

            _animator.SetFloat(WeaponBlendHash, _blend);

            _animator.SetBool(AimingHash, _held == BowStance && Time.time - _lastCastTime < _aimHold);

            AdvanceSwap();

            if (_hasWorld && TryGetCharacter(out Entity character))
                ReadCharacter(character);
        }

        private void ReadCharacter(Entity character)
        {
            // Present on every character from birth, so this is a read of the
            // flag rather than a question of whether it exists.
            bool dead = _entityManager.IsComponentEnabled<Dead>(character);
            Health health = _entityManager.GetComponentData<Health>(character);
            CastCue cue = _entityManager.GetComponentData<CastCue>(character);

            DynamicBuffer<EquippedItem> equipped =
                _entityManager.GetBuffer<EquippedItem>(character, isReadOnly: true);

            int mainHand = (int)EquipmentSlot.MainHand < equipped.Length
                ? equipped[(int)EquipmentSlot.MainHand].ItemId
                : 0;

            if (mainHand != _wornId)
                Wear(mainHand);

            _animator.SetBool(DeadHash, dead);

            // A corpse does not finish its cast from the waist up.
            _animator.SetLayerWeight(UpperBodyLayer, dead ? 0f : 1f);

            // The first frame only learns where things stand. Otherwise a scene
            // that loads with casts already counted plays one at once, and the
            // pool filling from zero reads as nothing, which is right.
            if (_primed && !dead)
            {
                if (cue.Count != _seenCasts)
                {
                    // A held beam casts a pulse several times a second. The arm
                    // raises once for the channel; each pulse after that only
                    // keeps the guard up — retriggering the clip is a spasm.
                    bool channelling = cue.Effect == SkillEffectKind.Beam &&
                                       _lastCastEffect == SkillEffectKind.Beam &&
                                       Time.time - _lastCastTime < ChannelGap;

                    _lastCastTime = Time.time;
                    _lastCombatTime = Time.time;
                    _lastCastEffect = cue.Effect;

                    // Paced so the blade connects when the blow lands, and never
                    // longer than the cooldown leaves before the next one.
                    float longest = Mathf.Max(0.1f, Mathf.Min(cue.Interval * _swingShare, _longestSwing));
                    float swing = cue.StrikeDelay > 0f ? cue.StrikeDelay / _swingImpact : longest;
                    _animator.SetFloat(SwingRateHash, 1f / Mathf.Clamp(swing, 0.1f, longest));

                    // The first blow out of a sheath draws first and swings the
                    // moment the grip is in the hand. The skill has already gone
                    // off — the host did not wait — so this is as close as the
                    // weapon can get to it.
                    if (_worn != null && !_drawn)
                    {
                        Draw();
                        _swingAfterDraw = true;
                        _drawnFor = cue.Effect;
                        _drawnSwing = cue.Swings;
                    }
                    else if (!channelling)
                    {
                        Swing(cue.Effect, cue.Swings);
                    }
                }
                else if (_drawn && Time.time - _lastCastTime > _sheathDelay)
                {
                    Sheathe();
                }

                // Only a drop under an unchanged ceiling is a blow. Taking off
                // an amulet of life lowers both, and that is not a flinch.
                if (health.Max == _lastMaxHealth && health.Current < _lastHealth)
                {
                    // Being hurt raises the guard even when the flinch itself
                    // is rate-limited away.
                    _lastCombatTime = Time.time;

                    if (Time.time >= _nextHitTime)
                    {
                        _animator.SetTrigger(HitHash);
                        _nextHitTime = Time.time + _hitInterval;
                    }
                }
            }

            _primed = true;
            _seenCasts = cue.Count;
            _lastHealth = health.Current;
            _lastMaxHealth = health.Max;
        }

        /// <summary>
        /// The swing is the host's count, not one kept here: the host already
        /// decided which way this blow sweeps, and the slash it sent follows
        /// that — a count of our own would drift from it the first time a
        /// press was dropped.
        /// </summary>
        private void Swing(SkillEffectKind effect, uint swings)
        {
            _animator.SetInteger(CastKindHash, (int)SkillModifiers.AnimatesAs(effect));
            _animator.SetInteger(AttackIndexHash, (int)(swings % SwingCount));
            _animator.SetTrigger(CastHash);
        }

        // ─────────────────────────────────────────────────────────────────
        // Weapons
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// What is in the main hand changed. Whatever it is goes on the back,
        /// with its quiver if it has one; the next cast draws it.
        /// </summary>
        private void Wear(int itemId)
        {
            if (_worn != null)
                _worn.SetActive(false);

            if (_wornBack != null)
                _wornBack.SetActive(false);

            _wornId = itemId;
            _worn = null;
            _wornBack = null;
            _grip = 0;
            _held = 0;
            _drawn = false;
            _swapAt = -1f;
            _swingAfterDraw = false;

            foreach (WeaponModel model in _weaponModels)
            {
                if (model.ItemId != itemId || model.Prefab == null)
                    continue;

                _worn = Spawn(model.Prefab);
                _wornBack = model.Back != null ? Spawn(model.Back) : null;
                _grip = model.Stance;
                break;
            }

            _animator.SetInteger(GripHash, _grip);
            _animator.SetInteger(WeaponHash, _held);

            if (_worn != null)
                Attach(toHand: false);

            if (_wornBack != null)
                AttachQuiver(_wornBack.transform);
        }

        private void Draw()
        {
            _drawn = true;
            _animator.SetTrigger(DrawHash);
            _swapToHand = true;
            _swapAt = Time.time + _drawSwapDelay;
        }

        private void Sheathe()
        {
            _drawn = false;
            _swingAfterDraw = false;
            _animator.SetTrigger(SheathHash);
            _swapToHand = false;
            _swapAt = Time.time + _sheathSwapDelay;
        }

        /// <summary>
        /// The weapon changes bone partway through the clip, when the hand is at
        /// the shoulder, and only then does the stance change — so the arms are
        /// not holding a weapon that is still on the back.
        /// </summary>
        private void AdvanceSwap()
        {
            if (_swapAt < 0f || Time.time < _swapAt || _worn == null)
                return;

            _swapAt = -1f;
            Attach(_swapToHand);

            _held = _swapToHand ? _grip : 0;
            _animator.SetInteger(WeaponHash, _held);

            // Weapon is already the new stance, so the swing picked is the
            // weapon's and not the bare hand's.
            if (_swapToHand && _swingAfterDraw)
            {
                _swingAfterDraw = false;
                Swing(_drawnFor, _drawnSwing);
            }
        }

        private GameObject Spawn(GameObject prefab)
        {
            if (!_spawned.TryGetValue(prefab, out GameObject instance))
            {
                instance = Instantiate(prefab);

                // The portrait camera draws the character's layer and nothing
                // else; a sword left on Default would vanish from the panel.
                foreach (Transform part in instance.GetComponentsInChildren<Transform>(includeInactive: true))
                    part.gameObject.layer = gameObject.layer;

                _spawned.Add(prefab, instance);
            }

            instance.SetActive(true);
            return instance;
        }

        /// <summary>
        /// Places the grip prefab — origin at the grip, long axis along +Z,
        /// width along +Y — and parents it to a bone, keeping where it was put.
        ///
        /// The hand is read off the skeleton rather than a hand-tuned offset per
        /// rig: the long axis runs from the little finger's knuckle through the
        /// index finger's, which is what a closed fist does to a handle, and +Y
        /// leads the way the knuckles point — a sword's edge, a bow's riser. The
        /// knuckles do not move relative to the hand, so the offset holds in any
        /// pose.
        ///
        /// A bow goes in the left hand. The pack's archer holds it there and
        /// draws the string with the right.
        /// </summary>
        private void Attach(bool toHand)
        {
            Transform weapon = _worn.transform;

            if (toHand)
            {
                bool left = _grip == BowStance;

                Transform hand = Bone(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (hand == null)
                    return;

                Transform index = Bone(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                Transform little = Bone(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
                Transform middle = Bone(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                Transform forearm = Bone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);

                Vector3 knuckles = middle != null
                    ? middle.position - hand.position
                    : hand.position - forearm.position;

                Vector3 along = index != null && little != null
                    ? index.position - little.position
                    : transform.up;

                weapon.SetPositionAndRotation(
                    hand.position + knuckles * _palmGrip,
                    Quaternion.LookRotation(along, Vector3.ProjectOnPlane(knuckles, along)));

                weapon.SetParent(hand, worldPositionStays: true);
                return;
            }

            Transform chest = Chest();
            if (chest == null)
                return;

            if (_grip == BowStance)
            {
                // Across the back, riser out and string against it, the upper
                // limb towards the right shoulder the draw clip reaches over.
                Vector3 limb = transform.up + transform.right * _quiverLean;

                weapon.SetPositionAndRotation(
                    chest.position + transform.rotation * _bowBackGrip,
                    Quaternion.LookRotation(limb, -transform.forward));
            }
            else
            {
                // Handle over the right shoulder, blade down towards the left
                // hip, flat against the back — where the draw clip reaches for it.
                Vector3 down = -transform.up - transform.right * _backLean;

                weapon.SetPositionAndRotation(
                    chest.position + transform.rotation * _backGrip,
                    Quaternion.LookRotation(down, Vector3.Cross(down, transform.forward)));
            }

            weapon.SetParent(chest, worldPositionStays: true);
        }

        /// <summary>
        /// A quiver: on the back, mouth over the right shoulder where the right
        /// hand would reach for an arrow. It never moves — drawing the bow does
        /// not draw the quiver.
        /// </summary>
        private void AttachQuiver(Transform quiver)
        {
            Transform chest = Chest();
            if (chest == null)
                return;

            Vector3 mouth = transform.up + transform.right * _quiverLean;

            quiver.SetPositionAndRotation(
                chest.position + transform.rotation * _quiverGrip,
                Quaternion.LookRotation(mouth, -transform.forward));

            quiver.SetParent(chest, worldPositionStays: true);
        }

        private Transform Chest()
        {
            Transform chest = Bone(HumanBodyBones.UpperChest);
            if (chest == null)
                chest = Bone(HumanBodyBones.Chest);
            if (chest == null)
                chest = Bone(HumanBodyBones.Spine);

            return chest;
        }

        private Transform Bone(HumanBodyBones bone) => _animator.GetBoneTransform(bone);

        private bool TryGetCharacter(out Entity character)
        {
            character = Entity.Null;

            if (_characterQuery.IsEmptyIgnoreFilter)
                return false;

            using NativeArray<Entity> entities = _characterQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PlayerCharacter> characters =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);

            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i].PlayerId != _playerId)
                    continue;

                character = entities[i];
                return true;
            }

            return false;
        }

        private void OnDestroy()
        {
            // The world goes down before scene objects on exit; a query that
            // outlived it is already gone.
            if (_hasWorld && World.DefaultGameObjectInjectionWorld is { IsCreated: true })
                _characterQuery.Dispose();
        }
    }
}
