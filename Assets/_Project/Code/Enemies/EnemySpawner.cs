using Fusion;
using UnityEngine;

namespace _Project.Code.Enemies
{
    public class EnemySpawner : NetworkBehaviour
    {
        [SerializeField] private NetworkPrefabRef enemyPrefab;
        [SerializeField] private float spawnInterval = 2f;
        [SerializeField] private int enemiesPerWave = 5;

        [SerializeField] private float minSpawnRadius = 8f;
        [SerializeField] private float maxSpawnRadius = 15f;
        
        private TickTimer _spawnTimer;
        

        public override void Spawned()
        {
            _spawnTimer = TickTimer.CreateFromSeconds(Runner,spawnInterval);
        }

        public override void FixedUpdateNetwork()
        {
            if(!Object.HasStateAuthority) return;

            if (_spawnTimer.ExpiredOrNotRunning(Runner))
            {
                SpawnWave();
                _spawnTimer = TickTimer.CreateFromSeconds(Runner,spawnInterval);
            }
        }

        private void SpawnWave()
        {
            Debug.Log("Spawning enemies");
            var players = Runner.ActivePlayers;
            var playerList = new System.Collections.Generic.List<PlayerRef>(players);

            if (playerList.Count == 0)
            {
                Debug.Log("No players found");
                return;
            }
            
            for (int i = 0; i < enemiesPerWave; i++)
            {
                var targetPlayer = playerList[Random.Range(0,playerList.Count)];
                
                if(Runner.TryGetPlayerObject(targetPlayer, out var playerObject))
                {
                    Vector3 spawnPos = GetRandomPointAround(playerObject.transform.position);
                    Runner.Spawn(enemyPrefab,spawnPos,Quaternion.identity);
                }
                else
                {
                    Debug.Log("No player object");
                }
            } 
        }

        private Vector3 GetRandomPointAround(Vector3 center)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(minSpawnRadius, maxSpawnRadius);
             
            Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;

            return center + offset;
        }
    }
}