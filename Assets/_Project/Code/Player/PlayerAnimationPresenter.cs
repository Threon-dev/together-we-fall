using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Player
{
    /// <summary>
    /// Puts what the character did on the model: walking, casting, being hurt,
    /// dying.
    ///
    /// Locomotion comes from the controller's velocity, not the input: a
    /// character pushing into a wall should stand, not run on the spot. The
    /// velocity goes into the character's own space, because facing follows aim
    /// and walking follows the keys — backing away from a crowd is MoveZ below
    /// zero, and that is what picks the backward run out of the blend tree.
    ///
    /// Everything else is read off the character entity as state, never as a
    /// queue: a cast counter that moved, health that went down, Dead raised.
    /// State cannot be drained by someone else first, and a missed frame costs
    /// nothing because the next one still shows the difference.
    ///
    /// Presentation only: nothing reads these parameters back.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerAnimationPresenter : MonoBehaviour
    {
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
        private static readonly int CastHash = Animator.StringToHash("Cast");
        private static readonly int CastKindHash = Animator.StringToHash("CastKind");
        private static readonly int HitHash = Animator.StringToHash("Hit");
        private static readonly int DeadHash = Animator.StringToHash("Dead");

        /// <summary>Casts and flinches, masked to the upper body. See CharacterContentFactory.</summary>
        private const int UpperBodyLayer = 1;

        [SerializeField] private Animator _animator;

        [Tooltip("Speed that maps to the edge of the blend tree — the run clips. " +
                 "Keep it equal to the motor's move speed.")]
        [SerializeField] private float _runSpeed = 6f;

        [Tooltip("Seconds the parameters take to settle. Zero snaps between " +
                 "directions; too much and the feet slide on a sharp turn.")]
        [SerializeField] private float _damping = 0.08f;

        [Tooltip("Shortest gap between two flinches. A burn ticks many times a " +
                 "second, and a character flinching at every tick is never seen " +
                 "doing anything else.")]
        [SerializeField] private float _hitInterval = 0.5f;

        private CharacterController _controller;

        private EntityManager _entityManager;
        private EntityQuery _characterQuery;
        private bool _hasWorld;
        private int _playerId;

        private bool _primed;
        private uint _seenCasts;
        private float _lastHealth;
        private float _lastMaxHealth;
        private float _nextHitTime;

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
                ComponentType.ReadOnly<CastCue>());
            _hasWorld = true;
        }

        private void Awake() => _controller = GetComponent<CharacterController>();

        // Late, so the motor has already moved the body this frame.
        private void LateUpdate()
        {
            if (_animator == null)
                return;

            Vector3 velocity = _controller.velocity;
            velocity.y = 0f;

            Vector3 local = transform.InverseTransformDirection(velocity) / _runSpeed;

            _animator.SetFloat(MoveXHash, local.x, _damping, Time.deltaTime);
            _animator.SetFloat(MoveZHash, local.z, _damping, Time.deltaTime);

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
                    _animator.SetInteger(CastKindHash, (int)cue.Effect);
                    _animator.SetTrigger(CastHash);
                }

                // Only a drop under an unchanged ceiling is a blow. Taking off
                // an amulet of life lowers both, and that is not a flinch.
                if (health.Max == _lastMaxHealth && health.Current < _lastHealth &&
                    Time.time >= _nextHitTime)
                {
                    _animator.SetTrigger(HitHash);
                    _nextHitTime = Time.time + _hitInterval;
                }
            }

            _primed = true;
            _seenCasts = cue.Count;
            _lastHealth = health.Current;
            _lastMaxHealth = health.Max;
        }

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
