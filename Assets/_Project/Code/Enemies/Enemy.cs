using Fusion;
using UnityEngine;

namespace _Project.Code.Enemies
{
    public class Enemy : NetworkBehaviour
    {
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private float moveSpeed = 3f;
        
        [Networked] public int CurrentHealth { get; private set; }


        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            var players = Runner.ActivePlayers;
            Transform closestPlayer = null;
            float closestDist = float.MaxValue;

            foreach (var player in players)
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

            if (closestPlayer != null)
            {
                Vector3 dir =  (closestPlayer.position - transform.position).normalized;
                transform.position += dir * moveSpeed * Runner.DeltaTime;
            }
        }

        public override void Spawned()
        {
            CurrentHealth = maxHealth;
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