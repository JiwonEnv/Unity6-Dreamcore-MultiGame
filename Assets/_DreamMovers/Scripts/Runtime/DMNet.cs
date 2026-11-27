using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>Network role shortcuts that also work with no network running (counts as the server).</summary>
    public static class DMNet
    {
        public static bool IsServer
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager == null || !manager.IsListening || manager.IsServer;
            }
        }

        /// <summary>Server clock in seconds, the same on every machine (estimated on clients).</summary>
        public static double ServerTime
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager != null && manager.IsListening ? manager.ServerTime.Time : Time.timeAsDouble;
            }
        }
    }
}
