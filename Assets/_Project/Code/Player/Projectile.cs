using Fusion;
using UnityEngine;

namespace _Project.Code.Player
{
    public class Projectile : NetworkBehaviour
    {
        [SerializeField] private float speed = 15f;
        [SerializeField] private float lifetime = 3f;

        private TickTimer _lifeTimer;
        private float _spawnTime;

        public override void Spawned()
        {
            _lifeTimer = TickTimer.CreateFromSeconds(Runner,lifetime);
        }

        public override void FixedUpdateNetwork()
        {
            transform.position += transform.forward * speed * Runner.DeltaTime;

            if (_lifeTimer.Expired(Runner))
            {
                Runner.Despawn(Object);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if(!Object.HasStateAuthority) return;

            if (other.TryGetComponent(out Enemy enemy))
            {
                enemy.TakeDamage(10);
                Runner.Despawn(Object);
            }
        }
    }
}