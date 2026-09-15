using Unity.Entities;
using Unity.NetCode;
using UnityEngine.SceneManagement;

namespace TogetherWeFall.Network
{
    /// <summary>
    /// Decides which worlds exist before the first scene wakes up.
    ///
    /// Started from the menu, the game has no network yet: one local world and
    /// nothing else, because whether this process is a host or a client is
    /// what the player is about to choose. SessionLauncher creates the real
    /// worlds once they have.
    ///
    /// Started from any other scene — pressing Play with the lobby, the dungeon
    /// or the arena open — there is nobody to ask, so this process hosts itself:
    /// a server world and a client world joined in-process. That keeps the
    /// editor workflow of opening a scene and pressing Play working exactly as
    /// it did before there was a network, through the same host path a real
    /// session takes.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public sealed class NetworkBootstrap : ClientServerBootstrap
    {
        public const string MenuScene = "Menu";

        public override bool Initialize(string defaultWorldName)
        {
            string startScene = SceneManager.GetActiveScene().name;

            // Empty in a player build before the first scene is loaded, and the
            // first scene of a build is the menu.
            if (string.IsNullOrEmpty(startScene) || startScene == MenuScene)
            {
                CreateLocalWorld(defaultWorldName);
                return true;
            }

            AutoConnectPort = 7979;
            bool created = base.Initialize(defaultWorldName);

            // Every bridge reads the client world, on a host as much as on a
            // client — see SessionLauncher.EnterGame for the same line.
            if (ClientWorld != null)
                World.DefaultGameObjectInjectionWorld = ClientWorld;

            return created;
        }
    }
}
