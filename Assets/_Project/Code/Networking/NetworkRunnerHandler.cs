using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace _Project.Code.Player
{
    public class NetworkRunnerHandler : MonoBehaviour, INetworkRunnerCallbacks
    {
        [SerializeField] private NetworkPrefabRef playerPrefab;

        private NetworkRunner _runner;
        private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();

        async void StartGame(GameMode mode)
        {
            if(_runner == null) _runner = transform.GetComponent<NetworkRunner>();
            _runner.ProvideInput = true;

            await _runner.StartGame(new StartGameArgs()
            {
                GameMode = mode,
                SessionName = "TestRoom",
                Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
                SceneManager = _runner.GetComponent<NetworkSceneManagerDefault>()
            });

            _runner.AddCallbacks(this);
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner.IsServer)
            {
                Vector3 spawnPosition = new Vector3(0, 1, 0);
                NetworkObject playerObject = runner.Spawn(playerPrefab, spawnPosition, Quaternion.identity, player);
                _spawnedPlayers.Add(player, playerObject);
            }
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (_spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
            {
                runner.Despawn(playerObject);
                _spawnedPlayers.Remove(player);
            }
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            var data = new NetworkInputData();
            if (Input.GetKey(KeyCode.W)) data.Direction += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) data.Direction += Vector3.back;
            if (Input.GetKey(KeyCode.A)) data.Direction += Vector3.left;
            if (Input.GetKey(KeyCode.D)) data.Direction += Vector3.right;
            
            data.MouseButton0 = Input.GetMouseButton(0);
            input.Set(data);
        }

        public void OnConnectedToServer(NetworkRunner runner)
        {
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
        }

        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request,
            byte[] token)
        {
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
        }

        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
        }

        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key,
            ReadOnlySpan<byte> data)
        {
        }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
        }

        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
        {
        }

        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        {
        }

        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key,
            ArraySegment<byte> data)
        {
        }

        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
        {
        }

        public void OnSceneLoadDone(NetworkRunner runner)
        {
        }

        public void OnSceneLoadStart(NetworkRunner runner)
        {
        }

        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }

        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }

        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
        {
        }

        void OnGUI()
        {
            if (_runner == null)
            {
                if (GUI.Button(new Rect(0, 0, 200, 40), "Host"))
                    StartGame(GameMode.Host);
                if (GUI.Button(new Rect(0, 40, 200, 40), "Join"))
                    StartGame(GameMode.Client);
            }
        }
    }
}