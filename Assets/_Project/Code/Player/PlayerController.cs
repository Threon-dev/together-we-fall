using Fusion;
using UnityEngine;

namespace _Project.Code.Player
{
    public class PlayerController : NetworkBehaviour
    {
        [SerializeField] private Transform shootingPoint;
        [SerializeField] private NetworkPrefabRef projectilePrefab;
        [SerializeField] private float fireRate = 0.3f;

        private TickTimer _fireCooldownTimer;
        
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
