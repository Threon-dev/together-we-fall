using Fusion;
using UnityEngine;

namespace _Project.Code.Player
{
    public class Enemy : NetworkBehaviour
    {
        [SerializeField] private int maxHealth = 100;
        
        [Networked] public int CurrentHealth { get; private set; }

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