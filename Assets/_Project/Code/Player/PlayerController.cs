using Fusion;
using UnityEngine;

namespace _Project.Code.Player
{
    public class PlayerController : NetworkBehaviour
    {
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private Transform shootingPoint;
        [SerializeField] private NetworkPrefabRef projectilePrefab;
        [SerializeField] private float fireRate = 0.3f;
        
        [Networked] public int CurrentHealth { get; set; }

        private TickTimer _fireCooldownTimer;

        public override void Spawned()
        {
            CurrentHealth = maxHealth;
        }

        public void TakeDamage(int damage)
        {
            if (!Object.HasStateAuthority) return;
            
            Debug.Log("Player taking damage");
            
            CurrentHealth -= damage;
            if (CurrentHealth <= 0)
            {
                Debug.Log("Player died");
            }
        }
        
        public override void FixedUpdateNetwork()
        {
            if (GetInput(out NetworkInputData data))
            {
                transform.position += data.Direction * 5f * Runner.DeltaTime;

                if (data.MouseButton0 && _fireCooldownTimer.ExpiredOrNotRunning(Runner))
                {
                    Runner.Spawn(projectilePrefab,shootingPoint.position,transform.rotation,Object.InputAuthority);
                    _fireCooldownTimer = TickTimer.CreateFromSeconds(Runner, fireRate);
                }
            }
        }
    }
}
