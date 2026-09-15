using System;
using System.Threading.Tasks;
using TMPro;
using Unity.Entities;
using Unity.NetCode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using TogetherWeFall.UI;

namespace TogetherWeFall.Network
{
    /// <summary>
    /// The menu: host a session or join one, then walk into the lobby.
    ///
    /// Everything network-shaped that happens before the first gameplay scene
    /// lives here and nowhere else — signing in, creating or joining the Relay
    /// session, and pointing the GameObject side at the client world. After
    /// that it shrinks to a corner label with the join code and stays alive
    /// across scenes, because the host needs to read that code out to a friend
    /// while already standing in the lobby.
    ///
    /// The code is taken from and put on the clipboard rather than typed into a
    /// field: it arrives by messenger anyway, and it is six characters nobody
    /// should have to retype.
    /// </summary>
    public sealed class SessionLauncher : MonoBehaviour
    {
        [SerializeField] private string _firstScene = "Lobby";
        [SerializeField] private int _maxPlayers = 4;

        [Tooltip("Seconds a client waits for the connection before giving up.")]
        [SerializeField] private float _connectTimeout = 20f;

        private RectTransform _menu;
        private TextMeshProUGUI _status;
        private TextMeshProUGUI _codeLabel;
        private ISession _session;
        private bool _busy;

        private void Start()
        {
            BuildLayout();
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;

            _menu = Ugui.Node(root, "Menu");
            Ugui.Place(_menu, width: 420f, height: 220f);
            Ugui.Column(_menu, spacing: 14f).childAlignment = TextAnchor.UpperCenter;

            TextMeshProUGUI title = Ugui.Text(_menu, "Title", 28f, Color.white, bold: true);
            title.text = "Together We Fall";

            Ugui.Button(_menu, "Host", "Host a game", Host);
            Ugui.Button(_menu, "Join", "Join with the code from the clipboard", Join);

            _status = Ugui.Text(_menu, "Status", 14f, new Color(0.8f, 0.82f, 0.86f), wrap: true);

            _codeLabel = Ugui.Text(
                root, "SessionCode", 16f, Color.white, TextAlignmentOptions.TopRight, bold: true);
            Ugui.Place(_codeLabel.rectTransform, right: 16f, top: 12f, width: 420f, height: 30f);
            _codeLabel.gameObject.SetActive(false);
        }

        private async void Host()
        {
            if (!TryBegin())
                return;

            try
            {
                await SignIn();

                SetStatus("Creating a session...");
                var options = new SessionOptions { MaxPlayers = _maxPlayers, IsPrivate = true }
                    .WithRelayNetwork();

                _session = await MultiplayerService.Instance.CreateSessionAsync(options);

                GUIUtility.systemCopyBuffer = _session.Code;
                Debug.Log($"[{nameof(SessionLauncher)}] Hosting. Join code: {_session.Code}");

                await EnterGame($"Code: {_session.Code}  (copied)");
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private async void Join()
        {
            string code = GUIUtility.systemCopyBuffer?.Trim();

            if (string.IsNullOrEmpty(code))
            {
                SetStatus("Copy the host's code first, then press Join.");
                return;
            }

            if (!TryBegin())
                return;

            try
            {
                await SignIn();

                SetStatus($"Joining {code}...");
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);

                await EnterGame($"Code: {_session.Code}");
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        /// <summary>
        /// A profile per process, so two copies of the game on one machine sign
        /// in as two players. With the shared default profile the second copy
        /// is the same anonymous account, and Relay refuses to let a player join
        /// their own session.
        /// </summary>
        private static async Task SignIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions()
                    .SetProfile($"p{System.Diagnostics.Process.GetCurrentProcess().Id}");

                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        /// <summary>
        /// Waits for a NetworkId, points every bridge at the client world and
        /// loads the lobby.
        ///
        /// The client world on a host too: the host's own player is a client of
        /// its server like anybody else's, so the host sees the game through the
        /// same replicated data its friend does. The local world the menu ran in
        /// is disposed — its systems would otherwise go on simulating a second,
        /// empty game in the background.
        /// </summary>
        private async Task EnterGame(string codeText)
        {
            SetStatus("Connecting...");

            float deadline = Time.realtimeSinceStartup + _connectTimeout;

            while (!HasNetworkId(ClientServerBootstrap.ClientWorld))
            {
                if (Time.realtimeSinceStartup > deadline)
                    throw new TimeoutException("No connection to the host.");

                await Task.Yield();
            }

            World local = World.DefaultGameObjectInjectionWorld;
            World.DefaultGameObjectInjectionWorld = ClientServerBootstrap.ClientWorld;

            if (local != null && local.IsCreated &&
                local != ClientServerBootstrap.ClientWorld &&
                local != ClientServerBootstrap.ServerWorld)
            {
                local.Dispose();
            }

            _menu.gameObject.SetActive(false);
            _codeLabel.text = codeText;
            _codeLabel.gameObject.SetActive(true);
            DontDestroyOnLoad(transform.root.gameObject);

            SceneManager.LoadScene(_firstScene);
        }

        private static bool HasNetworkId(World world)
        {
            if (world == null || !world.IsCreated)
                return false;

            using EntityQuery query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<NetworkId>());

            return !query.IsEmptyIgnoreFilter;
        }

        private bool TryBegin()
        {
            if (_busy)
                return false;

            _busy = true;
            return true;
        }

        private void Fail(Exception exception)
        {
            Debug.LogException(exception);
            SetStatus($"Failed: {exception.Message}");
            _busy = false;
        }

        private void SetStatus(string text)
        {
            if (_status != null)
                _status.text = text;
        }

        private async void OnApplicationQuit()
        {
            if (_session != null)
                await _session.LeaveAsync();
        }
    }
}
