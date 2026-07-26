using _Project.Code.Player;
using Fusion;
using UnityEngine;

namespace _Project.Code.Enemies
{
    public class Enemy : NetworkBehaviour
    {
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private float moveSpeed = 3f;

        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float windupTime = 0.5f;
        [SerializeField] private float attackDuration = 0.2f;
        [SerializeField] private float attackCooldown = 1.5f;
        [SerializeField] private int attackDamage = 15;
        
        [Networked] public int CurrentHealth { get; private set; }
        [Networked] private EnemyState State { get; set; }
        private TickTimer _stateTimer;
        private enum EnemyState { Chasing,Winding,Attacking,Cooldown }

        
        public override void Spawned()
        {
            CurrentHealth = maxHealth;
            State = EnemyState.Chasing;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            Transform target = GetClosestPlayer();
            if (target == null) return;

            float distance = Vector3.Distance(transform.position, target.position);
            
            switch (State)
            {
                case EnemyState.Chasing:
                    if (distance <= attackRange)
                    {
                        State = EnemyState.Winding;
                        _stateTimer = TickTimer.CreateFromSeconds(Runner, windupTime);
                    }
                    else
                    {
                        Vector3 dir = (target.position - transform.position).normalized;
                        transform.position += dir * moveSpeed * Runner.DeltaTime;
                    }
                    break;

                case EnemyState.Winding:
                    // тут можно проигрывать анимацию замаха — игрок видит и успевает увернуться
                    if (_stateTimer.Expired(Runner))
                    {
                        State = EnemyState.Attacking;
                        _stateTimer = TickTimer.CreateFromSeconds(Runner, attackDuration);
                        DoAttackHit(target);
                    }
                    break;

                case EnemyState.Attacking:
                    if (_stateTimer.Expired(Runner))
                    {
                        State = EnemyState.Cooldown;
                        _stateTimer = TickTimer.CreateFromSeconds(Runner, attackCooldown);
                    }
                    break;

                case EnemyState.Cooldown:
                    if (_stateTimer.Expired(Runner))
                    {
                        State = EnemyState.Chasing;
                    }
                    break;
            }
        }
        
        private void DoAttackHit(Transform target)
        {
            float distance = Vector3.Distance(transform.position, target.position);
            if (distance <= attackRange + 0.5f)
            {
                PlayerController player = target.GetComponent<PlayerController>();
                if (player != null)
                {
                    player.TakeDamage(attackDamage);
                }
            }
        }
        
        private Transform GetClosestPlayer()
        {
            Transform closestPlayer = null;
            float closestDist = float.MaxValue;

            foreach (var player in Runner.ActivePlayers)
            {
                if (Runner.TryGetPlayerObject(player, out var playerObject))
                {
                    float dist = Vector3.Distance(playerObject.transform.position, transform.position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestPlayer = playerObject.transform;
                    }
                }
            }
            
            return closestPlayer;
        }
        
        public void TakeDamage(int damage)
        {
            if (!Object.HasStateAuthority) return;
            
            CurrentHealth -= damage;

            if (CurrentHealth <= 0)
            {
                Runner.Despawn(Object);
            }
        }
    }
}