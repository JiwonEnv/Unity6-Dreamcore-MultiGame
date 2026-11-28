using ProcLevelGen;
using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Keeps this machine's generated map in step with the server. The server's <see cref="DMGameMode"/> builds the
    /// layout and publishes the seed in <see cref="DMGameState"/>; clients build the same seed here (every machine builds
    /// the graybox locally, only the seed goes over the network). Dolls wait for <see cref="Ready"/> before they fall.
    /// Sits next to the generator, which stays disabled in the scene until a network role is known.
    /// </summary>
    [RequireComponent(typeof(PLGGenerator))]
    public class DMLevelSync : MonoBehaviour
    {
        static DMLevelSync instance;

        PLGGenerator generator;
        int builtBuild = -1;
        bool serverReady;

        /// <summary>This machine has floors to stand on (a generated map built here, or a scene without a generator).</summary>
        public static bool Ready => instance == null || instance.IsReady;

        bool IsReady => serverReady || (builtBuild >= 0 && generator.IsGenerated);

        void Awake()
        {
            instance = this;
            generator = GetComponent<PLGGenerator>();
        }

        void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>Server: its own generation finished (successful or not).</summary>
        public void MarkServerBuilt(bool built) => serverReady = built;

        void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || manager.IsServer)
            {
                return;
            }
            generator.IsAuthority = false;
            DMGameState state = DMGameState.Instance;
            if (state == null || state.LevelSeed == 0 || state.LevelBuild == builtBuild)
            {
                return;
            }
            builtBuild = state.LevelBuild;
            generator.ApplyServerSeed(state.LevelSeed);
            Debug.Log($"[DreamMovers] client built seed {state.LevelSeed} (build {builtBuild}), layout hash {generator.LayoutHash:x8}");
        }
    }
}
